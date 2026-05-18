using System.Collections.Generic;
using RimWorld;
using StoryMaker.Action;
using StoryMaker.Core;
using StoryMaker.Response;
using Verse;

namespace StoryMaker.Dialogue;

// 对话状态机 + TTS 钩子 + 即时事件触发
// 与调度通道独立——对话的锁、超时、错误处理不影响调度正常运行
public static class DialogueHandler
{
    // 状态
    private static bool isWaitingForResponse;  // 等待 LLM 回复中
    private static string pendingPlayerText;   // 等待回复期间的玩家消息文本
    private static float requestSentRealtime;
    private static int requestSeq;             // 请求序号，超时后用于丢弃过期回复
    private static int dialogueRetryCount;     // 解析修正重试（最多 1 次）

    private const float DialogueTimeoutSeconds = 30f;  // 对话超时（现实秒）

    // 对话历史
    public static List<DialogueEntry> History = new();

    // 即时事件冷却与窗口计数
    public static int dialogueCooldownUntil;            // 下次允许即时事件的最小 tick
    public static int immediateEventCountThisWindow;    // 本调度窗口已触发次数

    // 公共状态访问
    public static bool IsWaitingForResponse => isWaitingForResponse;

    // TTS 钩子（Phase 5+，预留）
    // TTS mod 注册此委托以生成语音。返回 true 表示音频已就绪，false 表示需要等待
    public static System.Func<string, bool> OnGenerateTTS;
    // TTS mod 调用此回调通知 StoryMaker 文字+音频+事件可以同步展示
    public static System.Action<DialogueResponse> OnDialogueReady;

    // ── 公共接口 ──

    // 玩家发起对话
    public static void SendMessage(string playerText)
    {
        if (isWaitingForResponse)
        {
            Messages.Message("StoryMaker_Dialogue_NarratorThinking".Translate(), MessageTypeDefOf.NeutralEvent, false);
            return;
        }

        var settings = StoryMaker.Instance?.Settings;
        if (settings == null) return;
        if (!settings.IsApiConfigured())
        {
            Messages.Message("StoryMaker_Dialogue_ApiNotConfigured".Translate(), MessageTypeDefOf.RejectInput, false);
            return;
        }

        int curTick = Find.TickManager?.TicksGame ?? 0;

        // 存储玩家消息文本，供 OnResponseReceived 使用
        pendingPlayerText = playerText;

        // 构建 Prompt
        var messages = DialoguePromptBuilder.Build(playerText, History, curTick);
        dialogueRetryCount = 0;

        // 发送
        isWaitingForResponse = true;
        requestSentRealtime = UnityEngine.Time.realtimeSinceStartup;
        int expectedSeq = ++requestSeq;  // 记录当前请求序号，用于丢弃过期回复

        // Debug 日志
        int debugSeq = DebugLogger.LogRequest(messages);

        Log.Message($"[StoryMaker] Dialogue: 发送对话请求 (输入长度={playerText.Length})");
        AIChatServiceAsync.Instance.SendRequest(
            messages,
            (responseBody) =>
            {
                if (requestSeq != expectedSeq) return;  // 过期回调
                if (DebugLogger.IsEnabled)
                {
                    long elapsedMs = (long)((UnityEngine.Time.realtimeSinceStartup - requestSentRealtime) * 1000f);
                    DebugLogger.LogResponse(debugSeq, responseBody, 200, elapsedMs, 1, null);
                    DebugLogger.WriteSummary();
                }
                OnResponseReceived(responseBody);
            },
            (errorMsg, failureReason) =>
            {
                if (requestSeq != expectedSeq) return;  // 过期回调
                if (DebugLogger.IsEnabled)
                {
                    DebugLogger.LogError(debugSeq, errorMsg, failureReason);
                    DebugLogger.WriteSummary();
                }
                OnResponseError(errorMsg, failureReason);
            });

        // 暂停游戏（对话需要沉浸体验）
        if (Find.TickManager != null && !Find.TickManager.Paused)
            Find.TickManager.Pause();
    }

    // ── 事件处理 ──

    private static void OnResponseReceived(string responseBody)
    {
        try
        {
            Log.Message($"[StoryMaker] Dialogue: 收到 LLM 回复 ({responseBody.Length} 字符)");

            var parseResult = DialogueResponseParser.Parse(responseBody);

            if (!parseResult.IsSuccess)
            {
                // 修正重试（限 1 次）
                if (dialogueRetryCount < 1)
                {
                    dialogueRetryCount++;
                    string correction = BuildCorrectionMessage(parseResult);
                    var msgs = new List<ChatMessage>
                    {
                        new ChatMessage { role = "user", content = correction }
                    };
                    Log.Message($"[StoryMaker] Dialogue: 修正重试 ({parseResult.Error})");
                    AIChatServiceAsync.Instance.SendRequest(
                        msgs, OnResponseReceived, OnResponseError);
                    return;
                }
                Log.Warning($"[StoryMaker] Dialogue: 修正重试后仍失败，显示错误回复");
                var fallbackEntry = new DialogueEntry
                {
                    playerText = "[玩家上一条消息]",
                    narratorText = "(叙事者暂时无法回应...请稍后再试)",
                    gameTick = Find.TickManager?.TicksGame ?? 0
                };
                History.Add(fallbackEntry);
                isWaitingForResponse = false;
                return;
            }

            var response = parseResult.Response;

            // 如果有即时事件，构造 PlannedEvent 并执行
            bool eventTriggered = false;
            string triggeredType = null;
            if (response.HasEvent)
            {
                eventTriggered = ExecuteImmediateEvent(response);
                triggeredType = response.event_type;
            }

            // 记录对话历史
            var entry = new DialogueEntry
            {
                playerText = pendingPlayerText ?? "[玩家消息]",
                narratorText = response.dialogue_text,
                hadEvent = eventTriggered,
                triggeredEventType = triggeredType,
                gameTick = Find.TickManager?.TicksGame ?? 0
            };
            History.Add(entry);
            pendingPlayerText = null;

            // TTS 钩子：通知 TTS mod 有新的对话文本
            OnGenerateTTS?.Invoke(response.dialogue_text);

            // 通知 UI 展示
            OnDialogueReady?.Invoke(response);

            isWaitingForResponse = false;
            Log.Message($"[StoryMaker] Dialogue: 回复就绪 (触发事件={eventTriggered})");
        }
        catch (System.Exception ex)
        {
            Log.Error($"[StoryMaker] Dialogue OnResponseReceived 异常: {ex.Message}\n{ex.StackTrace}");
            isWaitingForResponse = false;
        }
    }

    private static void OnResponseError(string errorMessage, string failureReason)
    {
        Log.Error($"[StoryMaker] Dialogue: HTTP 错误 — {failureReason}: {errorMessage}");
        isWaitingForResponse = false;

        // 对话通道的错误不触发降级弹窗，仅轻量提示
        Messages.Message("StoryMaker_Dialogue_NetworkError".Translate(failureReason), MessageTypeDefOf.NeutralEvent, false);
    }

    // ── 超时检测 ──

    // 检查是否超时。由对话窗口每帧调用。返回 true 表示已超时
    public static bool CheckTimeout()
    {
        if (!isWaitingForResponse) return false;

        float elapsed = UnityEngine.Time.realtimeSinceStartup - requestSentRealtime;
        if (elapsed >= DialogueTimeoutSeconds)
        {
            // 超时：添加错误历史条目，重置状态
            Log.Warning($"[StoryMaker] Dialogue: 超时 ({elapsed:F1}s)，丢弃此请求");
            History.Add(new DialogueEntry
            {
                playerText = pendingPlayerText ?? "[玩家消息]",
                narratorText = "StoryMaker_Dialogue_Timeout".Translate(),
                gameTick = Find.TickManager?.TicksGame ?? 0
            });
            pendingPlayerText = null;
            isWaitingForResponse = false;
            requestSeq++;  // 递增序号，确保任何飞行中的回调都被丢弃
            return true;
        }

        return false;
    }

    // ── 即时事件执行 ──

    private static bool ExecuteImmediateEvent(DialogueResponse response)
    {
        var evt = new PlannedEvent
        {
            event_id = $"dialogue_{System.DateTime.Now.Ticks}",
            scheduled_tick = Find.TickManager?.TicksGame ?? 0,
            generated_at_world_tick = Find.TickManager?.TicksGame ?? 0,
            event_type = response.event_type,
            parameters = response.parameters ?? new Dictionary<string, string>(),
            narration_text = "",
            narrative_context = response.narrative_context ?? ""
        };

        evt.ResolveDef();
        if (evt.resolvedDef == null)
        {
            Log.Warning($"[StoryMaker] Dialogue: 即时事件 '{evt.event_type}' 的 IncidentDef 不存在");
            return false;
        }

        Log.Message($"[StoryMaker] Dialogue: 执行即时事件 {evt.event_type}");

        // 直接通过 ActionExecutor 执行（不排入队列）
        bool success = ActionRegistry.Execute(evt);
        if (!success)
        {
            Log.Warning($"[StoryMaker] Dialogue: 即时事件执行失败 ({evt.event_type})，尝试回退原版默认参数");
            // 回退：用纯原版默认参数再试
            var map = Find.CurrentMap ?? Find.AnyPlayerHomeMap;
            if (map != null)
            {
                var fallbackParms = StorytellerUtility.DefaultParmsNow(evt.resolvedDef.category, map);
                if (evt.resolvedDef.Worker.TryExecute(fallbackParms))
                {
                    Log.Message($"[StoryMaker] Dialogue: 原版默认参数成功 (回退, {evt.event_type})");
                    return true;
                }
            }
            return false;
        }

        return true;
    }

    // ── 辅助 ──

    private static string BuildCorrectionMessage(DialogueParseResult result)
    {
        return $"STORYMAKER_DIALOGUE_ERROR: {result.Error}\n"
             + $"{result.ErrorDetail}\n"
             + "请修正后重新输出完整的 JSON 对象。回复格式：{\"dialogue_text\":\"...\",\"immediate_event\":false} "
             + "或 {\"dialogue_text\":\"...\",\"immediate_event\":true,\"event_type\":\"...\",\"parameters\":{...},\"narrative_context\":\"...\"}";
    }

    // 存档序列化（由 StoryMakerExpose 调用）
    public static void ExposeData()
    {
        Scribe_Values.Look(ref dialogueCooldownUntil, "dialogueCooldownUntil");
        Scribe_Values.Look(ref immediateEventCountThisWindow, "immediateEventCountThisWindow");
        Scribe_Collections.Look(ref History, "dialogueHistory", LookMode.Deep);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
            History ??= new List<DialogueEntry>();
    }
}
