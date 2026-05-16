using System;
using System.Collections.Generic;
using System.Linq;
using StoryMaker.Action;
using StoryMaker.Core;
using StoryMaker.Response;
using StoryMaker.Snapshot;
using Verse;

namespace StoryMaker.Schedule;

// TCP 滑动窗口调度器：窗口检测 → 请求 → 解析 → 入队 → 执行 → ACK 推进
public static class EventScheduler
{
    private static int lastGameStartAbsTick = -1;

    // 请求锁状态
    private static bool requestLocked;
    private static int expectedAckStart;       // 期望回复的 from_tick == ack+1
    private static int requestSentAtTick;      // 请求发出时的 curTick，用于计算缓冲耗尽
    private static int retransmitRemaining;    // 剩余重传次数
    private static List<ChatMessage> lastMessages;  // 上次发送的消息（用于修正重试时追加）
    private static int lastRequestSeq;            // Debug 日志序号
    private static float requestSentRealtime;     // 请求发出时的现实时间（用于计算耗时）

    // 修正重试计数器（每层 Guard 最多 1 次）
    private static int parseRetryCount;
    private static int schemaRetryCount;
    private static int eventRetryCount;

    // 事件队列
    private static EventQueue eventQueue = new();

    // ACK 指针
    private static int ack => StoryMakerState.Instance.ack;
    private static void SetAck(int value) { StoryMakerState.Instance.ack = value; }

    public static void OnTick(int curTick)
    {
        var settings = StoryMaker.Instance?.Settings;
        if (settings == null) return;

        // 检测新游戏/读档
        if (lastGameStartAbsTick != Find.TickManager.gameStartAbsTick)
        {
            lastGameStartAbsTick = Find.TickManager.gameStartAbsTick;
            StoryMakerState.Instance.contextVersion++;
            Log.Message($"[StoryMaker] 新游戏/读档, contextVersion={StoryMakerState.Instance.contextVersion}, gameStartAbsTick={lastGameStartAbsTick}");
        }

        // 永久降级 → 原版接管
        if (StoryMakerState.Instance.permanentDegraded)
            return;

        // 1. 执行队列中到期的事件
        ExecuteDueEvents(curTick);

        // 2. 检查是否需要发起新请求
        int bufferTicks = settings.BufferTicks;
        int windowTicks = settings.PlanWindowTicks;

        if (!requestLocked && (ack - curTick < bufferTicks))
        {
            SendSchedulingRequest(curTick, bufferTicks, windowTicks);
        }

        // 3. 游戏 tick 缓冲耗尽检测（仅当 tick 在推进时检查）
        if (requestLocked && curTick >= requestSentAtTick + bufferTicks)
        {
            HandleTimeoutOrDegradation($"缓冲耗尽 {curTick - requestSentAtTick} ticks");
        }
    }

    // 实时超时检测——由 AIChatServiceAsync.Update() 每帧调用，不受游戏暂停影响
    internal static void CheckRealtimeTimeout()
    {
        if (!requestLocked) return;

        var settings = StoryMaker.Instance?.Settings;
        if (settings == null) return;

        float elapsedRealtime = UnityEngine.Time.realtimeSinceStartup - requestSentRealtime;
        if (elapsedRealtime >= settings.timeoutSeconds)
        {
            HandleTimeoutOrDegradation($"超时 {elapsedRealtime:F1}s");
        }
    }

    private static void HandleTimeoutOrDegradation(string reason)
    {
        if (retransmitRemaining > 0)
        {
            retransmitRemaining--;
            Log.Warning($"[StoryMaker] {reason}，重传请求 (剩余={retransmitRemaining})");

            // 调试模拟器记录重传
            DebugSimulator.RecordRetransmission();

            bool simulated = DebugSimulator.SimulateIfActive(OnResponseReceived, OnResponseError);
            if (!simulated)
            {
                AIChatServiceAsync.Instance.SendRequest(
                    lastMessages, OnResponseReceived, OnResponseError);
            }
            requestSentRealtime = UnityEngine.Time.realtimeSinceStartup;  // 重置现实计时
            requestSentAtTick = Find.TickManager?.TicksGame ?? 0;         // 重置 tick 计时
        }
        else
        {
            requestLocked = false;
            StoryMakerState.Instance.MarkDegraded(reason + "，重传次数用尽");
            Log.Error($"[StoryMaker] 永久降级: {reason} (ack={ack}, 重传已耗尽)");
        }
    }

    private static void ExecuteDueEvents(int curTick)
    {
        while (eventQueue.Count > 0)
        {
            var next = eventQueue.Peek();
            if (next.scheduled_tick > curTick) break;

            eventQueue.Pop();
            bool success = ActionExecutor.Execute(next);
            if (!success)
            {
                Log.Warning($"[StoryMaker] 事件执行失败: {next.event_id} ({next.event_type})");
            }
        }
    }

    private static void SendSchedulingRequest(int curTick, int bufferTicks, int windowTicks)
    {
        var settings = StoryMaker.Instance?.Settings;
        if (settings == null) return;

        // ACK 窗口: [ack+1, ack+1+windowTicks)，但不早于 curTick
        int llmFromTick = Math.Max(ack + 1, curTick);
        int llmToTick = ack + 1 + windowTicks - 1;
        expectedAckStart = llmFromTick;  // ACK 校验使用实际发给 LLM 的值

        // 采集快照
        GameStateSnapshot snapshot = SnapshotCollector.Collect(llmFromTick, llmToTick);

        // 附加 deviation_report（上周期失败事件）
        var failedEvents = ActionExecutor.PopFailedEvents();
        snapshot.deviationReport = failedEvents;

        // 构建 Prompt
        lastMessages = PromptBuilder.Build(snapshot, llmFromTick, llmToTick);

        // 前5天保护期：窗口起始在300000 tick 以内时不安排恶性事件
        if (llmFromTick <= 300000)
        {
            lastMessages.Add(new ChatMessage
            {
                role = "user",
                content = "【系统提示】当前游戏处于初期（前5天保护期）。请不要安排任何恶性事件（袭击、猎杀人类、虫害、机械族等）。可以安排中性或正面事件（商队、访客、天气变化、资源舱等）帮助殖民地建立基础。"
            });
        }

        // 重置 Guard 重试计数
        parseRetryCount = 0;
        schemaRetryCount = 0;
        eventRetryCount = 0;

        requestLocked = true;
        requestSentAtTick = curTick;
        requestSentRealtime = UnityEngine.Time.realtimeSinceStartup;
        retransmitRemaining = settings.maxRetransmissions;

        lastRequestSeq = DebugLogger.LogRequest(lastMessages);

        Log.Message($"[StoryMaker] 发送调度请求: window=[{llmFromTick}, {llmToTick}], ack={ack}, 栈={failedEvents.Count}");

        // 调试模拟器拦截（新窗口重置）
        DebugSimulator.ResetWindow();
        DebugSimulator.SetExpectedRange(llmFromTick, llmToTick);
        bool simulated = DebugSimulator.SimulateIfActive(OnResponseReceived, OnResponseError);
        if (!simulated)
        {
            AIChatServiceAsync.Instance.SendRequest(
                lastMessages, OnResponseReceived, OnResponseError);
        }
    }

    private static void OnResponseReceived(string responseBody)
    {
        try
        {
            Log.Message($"[StoryMaker] 收到 LLM 回复 ({responseBody.Length} 字符)");

            if (DebugLogger.IsEnabled)
            {
                long elapsedMs = (long)((UnityEngine.Time.realtimeSinceStartup - requestSentRealtime) * 1000f);
                DebugLogger.LogResponse(lastRequestSeq, responseBody, 200, elapsedMs, 1, ParseAndFormatResponse(responseBody));
            }

            // 解析 + Guard
            ParseResult parseResult = ResponseParser.Parse(responseBody);

            // Guard 修正重试
            if (parseResult.NeedsCorrection)
            {
                var violation = parseResult.FirstViolation;
                if (violation == null) return;

                if (ShouldRetry(violation.ViolationTag))
                {
                    string correction = FormatCorrectionMessages.Build(violation);
                    if (!string.IsNullOrEmpty(correction))
                    {
                        lastMessages.Add(new ChatMessage { role = "user", content = correction });
                        Log.Message($"[StoryMaker] 修正重试: {violation.ViolationTag} (parse={parseRetryCount}, schema={schemaRetryCount}, event={eventRetryCount})");
                        AIChatServiceAsync.Instance.SendRequest(
                            lastMessages, OnResponseReceived, OnResponseError);
                        return;  // 锁保持
                    }
                }

                // 重试次数用尽 → 丢弃此回复
                Log.Warning($"[StoryMaker] Guard 修正重试次数用尽 ({violation.ViolationTag})，丢弃回复");
                return;
            }

            // ACK 校验
            var response = parseResult.Response;
            if (response.plan_range.from_tick != expectedAckStart)
            {
                Log.Warning($"[StoryMaker] 过期回复: 期望 from_tick={expectedAckStart}, 实际={response.plan_range.from_tick}，丢弃");
                return;
            }

            // 有效回复：解锁 + 入队
            requestLocked = false;
            int curTick = Find.TickManager?.TicksGame ?? 0;

            // 过滤已过期事件
            var validEvents = response.events
                ?.Where(e => e.scheduled_tick >= curTick)
                .ToList();

            if (validEvents != null && validEvents.Count > 0)
                eventQueue.InsertAll(validEvents);

            if (response.events != null)
            {
                int expired = response.events.Count - (validEvents?.Count ?? 0);
                if (expired > 0)
                    Log.Message($"[StoryMaker] 过滤了 {expired} 个过期事件 (scheduled_tick < {curTick})");
            }

            // ACK 推进
            SetAck(response.plan_range.to_tick);
            Log.Message($"[StoryMaker] ACK 推进至 {ack}, 队列深度={eventQueue.Count}, {validEvents?.Count ?? 0} 个新事件入队");
        }
        catch (Exception ex)
        {
            Log.Error($"[StoryMaker] OnResponseReceived 异常: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private static void OnResponseError(string errorMessage, string failureReason)
    {
        Log.Error($"[StoryMaker] HTTP 错误: failure_reason={failureReason}, 详情={errorMessage}");

        if (DebugLogger.IsEnabled)
            DebugLogger.LogError(lastRequestSeq, errorMessage, failureReason);

        // HTTP 401/404 —— 不重试，直接永久降级
        if (failureReason == "http_401" || failureReason == "http_404")
        {
            requestLocked = false;
            StoryMakerState.Instance.MarkDegraded($"网络错误: {failureReason}");
            return;
        }

        // 其他错误由 TCP 层 OnTick 中的缓冲耗尽检测处理重传
        // （不在此处解锁，等待 OnTick 中重传或超时降级）
    }

    private static bool ShouldRetry(string violationTag)
    {
        return violationTag switch
        {
            "PARSE_EMPTY_RESPONSE" => ++parseRetryCount <= 1,
            "PARSE_NOT_JSON" => ++parseRetryCount <= 1,
            "PARSE_DESERIALIZE_FAILED" => ++parseRetryCount <= 1,
            "SCHEMA_VIOLATION" => ++schemaRetryCount <= 1,
            "EVENT_VIOLATION_ALL_INVALID" => ++eventRetryCount <= 1,
            _ => false
        };
    }

    // 解析响应并格式化为可读的 JSON（用于 Debug 日志）
    internal static string ParseAndFormatResponse(string responseBody)
    {
        try
        {
            var result = ResponseParser.Parse(responseBody);
            if (result == null || result.Response == null)
                return "解析失败: Response 为 null";

            var evt = result.Response;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"parse_success\": {result.IsSuccess.ToString().ToLower()},");
            sb.AppendLine($"  \"plan_range\": {{ \"from_tick\": {evt.plan_range?.from_tick ?? 0}, \"to_tick\": {evt.plan_range?.to_tick ?? 0} }},");
            sb.AppendLine($"  \"empty_plan\": {evt.empty_plan.ToString().ToLower()},");
            sb.AppendLine($"  \"narrative_summary\": \"{EscapeForJson(evt.narrative_summary ?? "")}\",");
            sb.AppendLine($"  \"event_count\": {evt.events?.Count ?? 0},");

            if (result.ParseGuardResult != null)
                sb.AppendLine($"  \"parse_guard\": \"{(result.ParseGuardResult.IsValid ? "pass" : "FAIL: " + result.ParseGuardResult.ViolationTag)}\",");
            if (result.SchemaGuardResult != null)
                sb.AppendLine($"  \"schema_guard\": \"{(result.SchemaGuardResult.IsValid ? "pass" : "FAIL: " + result.SchemaGuardResult.ViolationTag)}\",");
            if (result.EventGuardResult != null)
                sb.AppendLine($"  \"event_guard\": \"{(result.EventGuardResult.IsValid ? "pass" : "FAIL: " + result.EventGuardResult.ViolationTag)}\",");

            sb.AppendLine("  \"events\": [");
            if (evt.events != null)
            {
                for (int i = 0; i < evt.events.Count; i++)
                {
                    var e = evt.events[i];
                    string comma = i < evt.events.Count - 1 ? "," : "";
                    sb.AppendLine($"    {{ \"event_id\": \"{e.event_id}\", \"scheduled_tick\": {e.scheduled_tick}, \"event_type\": \"{e.event_type}\", \"params\": {FormatParams(e.parameters)} }}{comma}");
                }
            }
            sb.AppendLine("  ]");
            sb.AppendLine("}");
            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"解析异常: {ex.Message}";
        }
    }

    private static string FormatParams(Dictionary<string, string> p)
    {
        if (p == null || p.Count == 0) return "{}";
        var sb = new System.Text.StringBuilder();
        sb.Append("{ ");
        bool first = true;
        foreach (var kv in p)
        {
            if (!first) sb.Append(", ");
            sb.Append($"\"{kv.Key}\": \"{kv.Value}\"");
            first = false;
        }
        sb.Append(" }");
        return sb.ToString();
    }

    private static string EscapeForJson(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
    }
}
