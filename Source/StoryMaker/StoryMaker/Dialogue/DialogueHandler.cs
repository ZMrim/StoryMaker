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

    private const float DialogueTimeoutSeconds = 30f;   // LLM 对话超时（现实秒）
    private const float TTSTimeoutSeconds = 120f;       // TTS 生成超时（纯 CPU 可能很慢）

    // 对话历史
    public static List<DialogueEntry> History = new();
    private static int lastGameStartAbsTick = -1;

    // 即时事件冷却与窗口计数
    public static int dialogueCooldownUntil;            // 下次允许即时事件的最小 tick
    public static int immediateEventCountThisWindow;    // 本调度窗口已触发次数

    // 公共状态访问
    public static bool IsWaitingForResponse => isWaitingForResponse;

    // 检测并清除跨存档残留的对话历史（由对话框 Open() 在打开前调用）
    public static void EnsureHistoryCleared()
    {
        int curAbsTick = Find.TickManager?.gameStartAbsTick ?? -1;
        if (curAbsTick != lastGameStartAbsTick)
        {
            lastGameStartAbsTick = curAbsTick;
            History.Clear();
            Log.Message("[StoryMaker] Dialogue: 新游戏/读档，对话历史已清除");
        }
    }

    // TTS 钩子
    // TTS mod 注册此委托以生成语音。返回 true 表示由 TTS mod 接管，DialogHandler 将等待回调
    // 返回 false 表示不处理（开关关闭或生成失败），立刻显示
    public static System.Func<string, bool> OnGenerateTTS;
    // TTS mod 生成完成后调用，通知 DialogueHandler 文字+音频+事件同步展示
    public static System.Action OnDialogueReady;
    // 等待 TTS 期间暂存的 LLM 回复
    private static DialogueResponse pendingTTSResponse;
    private static string pendingTTSPlayerText;
    private static bool pendingTTSEventTriggered;
    private static string pendingTTSTriggeredType;
    private static float ttsSentRealtime;          // TTS 开始等待时的现实时间
    // 是否正在等待 TTS 完成
    public static bool IsWaitingForTTS => pendingTTSResponse != null;

    // ── 公共接口 ──

    // 玩家发起对话
    public static void SendMessage(string playerText)
    {
        // 检测新游戏 / 读档，清除上一存档的对话历史
        int curAbsTick = Find.TickManager?.gameStartAbsTick ?? -1;
        if (curAbsTick != lastGameStartAbsTick)
        {
            lastGameStartAbsTick = curAbsTick;
            History.Clear();
            Log.Message("[StoryMaker] Dialogue: 新游戏/读档，对话历史已清除");
        }

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

        // 立即显示玩家消息（不等 LLM 回复）
        pendingPlayerText = playerText;
        History.Add(new DialogueEntry
        {
            playerText = playerText,
            narratorText = "",
            gameTick = curTick
        });

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
                // 更新末条玩家消息的回复
                var pText = pendingPlayerText ?? "";
                for (int i = History.Count - 1; i >= 0; i--)
                {
                    if (string.IsNullOrEmpty(History[i].narratorText) && History[i].playerText == pText)
                    {
                        History[i].narratorText = "(叙事者暂时无法回应...请稍后再试)";
                        break;
                    }
                }
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

            // TTS 钩子：通知 TTS mod 有新的对话文本
            bool ttsHandled = OnGenerateTTS != null && OnGenerateTTS(response.dialogue_text);

            if (ttsHandled)
            {
                // TTS mod 接管，暂存回复等待回调
                pendingTTSResponse = response;
                pendingTTSPlayerText = pendingPlayerText;
                pendingTTSEventTriggered = eventTriggered;
                pendingTTSTriggeredType = triggeredType;
                pendingPlayerText = null;
                ttsSentRealtime = UnityEngine.Time.realtimeSinceStartup;
                Log.Message("[StoryMaker] Dialogue: TTS mod 已接管，等待音频生成...");
                // isWaitingForResponse 保持 true，玩家在此期间无法发送新消息
            }
            else
            {
                // 无 TTS 或 TTS 开关关闭，直接显示
                FinalizeDialogueDisplay(response, pendingPlayerText, eventTriggered, triggeredType);
                isWaitingForResponse = false;
            }
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

        // 更新末条玩家消息的回复为错误文本
        var playerMsg = pendingPlayerText ?? "";
        for (int i = History.Count - 1; i >= 0; i--)
        {
            if (string.IsNullOrEmpty(History[i].narratorText) && History[i].playerText == playerMsg)
            {
                History[i].narratorText = "StoryMaker_Dialogue_NetworkError".Translate(failureReason);
                break;
            }
        }
        pendingPlayerText = null;

        Messages.Message("StoryMaker_Dialogue_NetworkError".Translate(failureReason), MessageTypeDefOf.NeutralEvent, false);
    }

    // ── 超时检测 ──

    // 检查 LLM 阶段是否超时。由对话窗口每帧调用。返回 true 表示已超时
    // 如果正在等待 TTS，跳过（由 CheckTTSTimeout 单独管理）
    public static bool CheckTimeout()
    {
        if (!isWaitingForResponse) return false;
        if (IsWaitingForTTS) return false;  // TTS 阶段不受 LLM 超时影响

        float elapsed = UnityEngine.Time.realtimeSinceStartup - requestSentRealtime;
        if (elapsed >= DialogueTimeoutSeconds)
        {
            // 超时：更新末条玩家消息的回复为超时文本
            Log.Warning($"[StoryMaker] Dialogue: LLM 超时 ({elapsed:F1}s)，丢弃此请求");
            var playerMsg = pendingPlayerText ?? "";
            for (int i = History.Count - 1; i >= 0; i--)
            {
                if (string.IsNullOrEmpty(History[i].narratorText) && History[i].playerText == playerMsg)
                {
                    History[i].narratorText = "StoryMaker_Dialogue_Timeout".Translate();
                    break;
                }
            }
            pendingPlayerText = null;
            isWaitingForResponse = false;
            requestSeq++;
            return true;
        }

        return false;
    }

    // 检查 TTS 阶段是否超时。由对话窗口每帧调用。返回 true 表示已超时
    public static bool CheckTTSTimeout()
    {
        if (!IsWaitingForTTS) return false;

        float elapsed = UnityEngine.Time.realtimeSinceStartup - ttsSentRealtime;
        if (elapsed >= TTSTimeoutSeconds)
        {
            Log.Warning($"[StoryMaker] Dialogue: TTS 超时 ({elapsed:F1}s)，放弃等待语音，直接显示文字");
            // 直接触发展示（无音频），NotifyTTSReady 内部是幂等的
            NotifyTTSReady();
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

    // ── TTS 回调入口 ──

    // TTS mod 生成完成后调用此方法，触发文字+音频+事件同步展示
    public static void NotifyTTSReady()
    {
        if (pendingTTSResponse == null) return;

        var resp = pendingTTSResponse;
        var playerText = pendingTTSPlayerText;
        var eventTriggered = pendingTTSEventTriggered;
        var triggeredType = pendingTTSTriggeredType;

        pendingTTSResponse = null;
        pendingTTSPlayerText = null;
        pendingTTSEventTriggered = false;
        pendingTTSTriggeredType = null;

        FinalizeDialogueDisplay(resp, playerText, eventTriggered, triggeredType);
        isWaitingForResponse = false;
        Log.Message($"[StoryMaker] Dialogue: TTS 完成，回复展示 (触发事件={eventTriggered})");
    }

    // ── 内部显示逻辑 ──

    private static void FinalizeDialogueDisplay(DialogueResponse response,
        string playerText, bool eventTriggered, string triggeredType)
    {
        // 找到上次 SendMessage 时添加的玩家消息条目，补充叙事者回复
        for (int i = History.Count - 1; i >= 0; i--)
        {
            if (string.IsNullOrEmpty(History[i].narratorText) && History[i].playerText == playerText)
            {
                History[i].narratorText = response.dialogue_text;
                History[i].hadEvent = eventTriggered;
                History[i].triggeredEventType = triggeredType;
                break;
            }
        }

        // 通知 UI 展示
        OnDialogueReady?.Invoke();

        Log.Message($"[StoryMaker] Dialogue: 回复就绪 (触发事件={eventTriggered})");
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
