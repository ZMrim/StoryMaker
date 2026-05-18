using System;
using System.Collections.Generic;
using System.Linq;
using StoryMaker.Action;
using StoryMaker.Core;
using StoryMaker.Response;
using StoryMaker.Snapshot;
using Verse;

namespace StoryMaker.Schedule;

// TCP 滑动窗口调度器：窗口检测 → 请求 → 解析 → Guard → 入队 → 执行 → ACK 推进
// Phase 4: 集成 RequestLock（暂停冻结计时器）+ DegradationHandler + EmptyPlanGuard + 存档序列化
public static class EventScheduler
{
    private static int lastGameStartAbsTick = -1;

    // Phase 4: 正式请求锁（替代 Phase 3 的 bool requestLocked）
    private static RequestLock requestLock;
    private static EventQueue eventQueue;

    // 请求状态（调度器特有）
    private static int expectedAckStart;       // 期望回复的 from_tick
    private static int requestSentAtTick;      // 请求发出时的 curTick
    private static List<ChatMessage> lastMessages;  // 上次发送的消息（用于修正重试追加）
    private static int lastRequestSeq;         // Debug 日志序号
    private static float requestSentRealtime;  // 请求发出时的现实时间（计算耗时）

    // 修正重试计数器（每层 Guard 最多 1 次）
    private static int parseRetryCount;
    private static int schemaRetryCount;
    private static int eventRetryCount;
    private static int emptyRetryCount;  // Phase 4: EmptyPlanGuard 重试计数

    // ACK 指针
    private static int ack => StoryMakerState.Instance.ack;
    private static void SetAck(int value) { StoryMakerState.Instance.ack = value; }

    // 确保已初始化
    private static void EnsureInit()
    {
        if (requestLock == null)
        {
            requestLock = new RequestLock();
            requestLock.OnRetransmitRequested += HandleRetransmit;
            requestLock.OnDegradationRequested += HandleDegradation;
        }
        eventQueue ??= new EventQueue();
    }

    // ── 存档序列化入口（由 StoryMakerExpose 调用）──
    public static void ExposeQueue()
    {
        eventQueue ??= new EventQueue();
        eventQueue.ExposeData();
    }

    // ── 底部管理面板访问器 ──
    public static bool IsRequestLocked
    {
        get { EnsureInit(); return requestLock?.IsLocked ?? false; }
    }

    public static int QueueCount
    {
        get { EnsureInit(); return eventQueue?.Count ?? 0; }
    }

    public static int LastRequestTick => requestSentAtTick;

    // 强制刷新：忽略缓冲期，立即发送调度请求
    public static void ForceRefresh()
    {
        EnsureInit();
        int curTick = Find.TickManager?.TicksGame ?? 0;
        var settings = StoryMaker.Instance?.Settings;
        if (settings != null)
        {
            if (requestLock != null && requestLock.IsLocked)
                requestLock.Unlock();
            SendSchedulingRequest(curTick, settings.BufferTicks, settings.PlanWindowTicks);
        }
    }

    // 强制跳过当前窗口（解锁但不发请求，下个 tick 自动评估）
    public static void SkipCurrentWindow()
    {
        EnsureInit();
        if (requestLock != null && requestLock.IsLocked)
            requestLock.Unlock();
    }

    // ── 主 Tick ──
    public static void OnTick(int curTick)
    {
        EnsureInit();

        var settings = StoryMaker.Instance?.Settings;
        if (settings == null) return;

        // 检测新游戏/读档
        if (lastGameStartAbsTick != Find.TickManager.gameStartAbsTick)
        {
            lastGameStartAbsTick = Find.TickManager.gameStartAbsTick;
            StoryMakerState.Instance.contextVersion++;
            HandlePostLoad(curTick);
            Log.Message($"[StoryMaker] 新游戏/读档, contextVersion={StoryMakerState.Instance.contextVersion}");
        }

        // 永久降级 → 原版接管
        if (StoryMakerState.Instance.permanentDegraded)
            return;

        // 1. 执行队列中到期的事件
        ExecuteDueEvents(curTick);

        // 2. 检查是否需要发起新请求
        int bufferTicks = settings.BufferTicks;
        int windowTicks = settings.PlanWindowTicks;

        if (!requestLock.IsLocked && (ack - curTick < bufferTicks))
        {
            SendSchedulingRequest(curTick, bufferTicks, windowTicks);
        }

        // 3. 缓冲耗尽检测（仅当 tick 推进时，且锁处于锁定状态）
        if (requestLock.IsLocked && curTick >= requestSentAtTick + bufferTicks)
        {
            requestLock.HandleBufferExhaustion(curTick, requestSentAtTick, bufferTicks);
        }
    }

    // ── 实时超时检测（由 AIChatServiceAsync.Update() 每帧调用，不受暂停影响）──
    internal static void CheckRealtimeTimeout()
    {
        EnsureInit();
        if (requestLock.CheckRealtimeTimeout())
        {
            requestLock.HandleRealtimeTimeout();
        }
    }

    // ── 事件处理：重传 ──
    private static void HandleRetransmit()
    {
        Log.Warning($"[StoryMaker] 触发重传 (剩余={requestLock.RetransmitRemaining})");

        DebugLogger.RecordRetry();
        DebugSimulator.RecordRetransmission();

        bool simulated = DebugSimulator.SimulateIfActive(OnResponseReceived, OnResponseError);
        if (!simulated)
        {
            AIChatServiceAsync.Instance.SendRequest(
                lastMessages, OnResponseReceived, OnResponseError);
        }
        requestSentRealtime = UnityEngine.Time.realtimeSinceStartup;
        requestSentAtTick = Find.TickManager?.TicksGame ?? 0;
    }

    // ── 事件处理：降级 ──
    private static void HandleDegradation(string reason)
    {
        Log.Error($"[StoryMaker] 降级: {reason} (ack={ack})");

        // 显示弹窗前先自动暂停游戏
        if (Find.TickManager != null && !Find.TickManager.Paused)
            Find.TickManager.Pause();

        int missedFrom = expectedAckStart;
        int missedTo = expectedAckStart + (StoryMaker.Instance?.Settings?.PlanWindowTicks ?? 300000) - 1;

        DegradationHandler.ShowDialog(reason, missedFrom, missedTo,
            onRetry: () =>
            {
                // 清除降级状态，忽略缓冲期立即请求
                StoryMakerState.Instance.permanentDegraded = false;
                StoryMakerState.Instance.degradationReason = "";
                int curTick = Find.TickManager?.TicksGame ?? 0;
                var settings = StoryMaker.Instance?.Settings;
                if (settings != null)
                    SendSchedulingRequest(curTick, settings.BufferTicks, settings.PlanWindowTicks);
            },
            onGiveUp: () =>
            {
                StoryMakerState.Instance.MarkDegraded(reason);
            });
    }

    // ── 弹出旧状态恢复（Phase 4 新增）──
    private static void HandlePostLoad(int curTick)
    {
        // 重置请求级别临时状态（锁状态不序列化，读档默认 Unlocked）
        requestLock.Unlock();
        DegradationHandler.CloseIfOpen();
        parseRetryCount = 0;
        schemaRetryCount = 0;
        eventRetryCount = 0;
        emptyRetryCount = 0;

        // 过滤 eventQueue 中已过期的事件 (scheduled_tick < curTick) + 恢复 Def 引用
        var expiredEvents = new List<PlannedEvent>();
        var remainingEvents = new List<PlannedEvent>();

        foreach (var evt in eventQueue.GetAll())
        {
            if (evt.scheduled_tick < curTick)
            {
                expiredEvents.Add(evt);
                continue;
            }

            // 读档恢复：重新解析 IncidentDef 引用
            if (!evt.ResolveDef())
            {
                Log.Warning($"[StoryMaker] 读档: 事件 '{evt.event_type}' 的 IncidentDef 不存在，转为 deviation_report");
                ActionExecutor.AddFailedEvent(evt.event_id, evt.event_type,
                    $"读档恢复: IncidentDef '{evt.event_type}' 不存在或 Worker 为空");
                continue;  // 不入队
            }

            remainingEvents.Add(evt);
        }

        if (expiredEvents.Count > 0)
        {
            Log.Message($"[StoryMaker] 读档: 过滤 {expiredEvents.Count} 个过期事件 (scheduled_tick < {curTick})");
            foreach (var evt in expiredEvents)
            {
                ActionExecutor.AddFailedEvent(evt.event_id, evt.event_type,
                    $"读档过期: scheduled_tick={evt.scheduled_tick} < curTick={curTick}");
            }
        }

        // Phase 5: Stale 事件检测（generated_at_world_tick 差距过大）
        int staleThreshold = StoryMaker.Instance?.Settings?.StaleThresholdTicks ?? 900000;
        var staleEvents = new List<PlannedEvent>();
        var trulyValid = new List<PlannedEvent>();

        foreach (var evt in remainingEvents)
        {
            int age = curTick - evt.generated_at_world_tick;
            if (evt.generated_at_world_tick > 0 && age > staleThreshold)
            {
                staleEvents.Add(evt);
                Log.Message($"[StoryMaker] 读档: 事件 {evt.event_type} 标记为 stale (生成于 {evt.generated_at_world_tick}, 当前 {curTick}, 已过 {age / 60000} 天)");
            }
            else
            {
                trulyValid.Add(evt);
            }
        }

        if (staleEvents.Count > 0)
        {
            Log.Message($"[StoryMaker] 读档: {staleEvents.Count} 个 stale 事件转为 deviation_report");
            foreach (var evt in staleEvents)
            {
                int age = curTick - evt.generated_at_world_tick;
                ActionExecutor.AddFailedEvent(evt.event_id, evt.event_type,
                    $"stale_event: generated_at={evt.generated_at_world_tick}, curTick={curTick}, age_days={age / 60000}");
            }
        }

        // 重建队列（按 tick 排序）
        eventQueue.Clear();
        if (trulyValid.Count > 0)
            eventQueue.InsertAll(trulyValid);

        Log.Message($"[StoryMaker] 读档恢复: ack={ack}, 队列深度={eventQueue.Count}, 过期={expiredEvents.Count}, stale={staleEvents.Count}");
    }

    // ── 执行到期 ──
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

    // ── 发送调度请求 ──
    private static void SendSchedulingRequest(int curTick, int bufferTicks, int windowTicks)
    {
        var settings = StoryMaker.Instance?.Settings;
        if (settings == null) return;

        // ACK 窗口: [ack+1, ack+1+windowTicks)，但不早于 curTick。
        // 当中途切换叙事者或 curTick 远超 ack 时，窗尾也需同步前移，避免反向窗口。
        int llmFromTick = Math.Max(ack + 1, curTick);
        int llmToTick = Math.Max(ack + 1 + windowTicks - 1, llmFromTick + windowTicks - 1);
        expectedAckStart = llmFromTick;

        // 采集快照
        GameStateSnapshot snapshot = SnapshotCollector.Collect(llmFromTick, llmToTick);

        // 附加 deviation_report
        var failedEvents = ActionExecutor.PopFailedEvents();
        snapshot.deviationReport = failedEvents;

        // 构建 Prompt
        lastMessages = PromptBuilder.Build(snapshot, llmFromTick, llmToTick);

        // 前5天保护期
        if (llmFromTick <= 300000)
        {
            lastMessages.Add(new ChatMessage
            {
                role = "user",
                content = "【系统提示】当前游戏处于初期（前5天保护期）。请不要安排任何恶性事件（袭击、猎杀人类、虫害、机械族等）。可以安排中性或正面事件（商队、访客、天气变化、资源舱等）帮助殖民地建立基础。"
            });
        }

        // Phase 4: EmptyPlanGuard 警告注入
        if (EmptyPlanGuard.ShouldInjectWarning())
        {
            lastMessages.Add(new ChatMessage
            {
                role = "user",
                content = EmptyPlanGuard.BuildWarningMessage()
            });
            Log.Message("[StoryMaker] EmptyPlanGuard: 警告已注入");
        }

        // 重置 Guard 重试计数
        parseRetryCount = 0;
        schemaRetryCount = 0;
        eventRetryCount = 0;
        emptyRetryCount = 0;
        EmptyPlanGuard.ResetWindow();

        // Phase 4: RequestLock 上锁
        requestLock.Lock(settings.maxRetransmissions, settings.timeoutSeconds);
        requestSentAtTick = curTick;
        requestSentRealtime = UnityEngine.Time.realtimeSinceStartup;

        lastRequestSeq = DebugLogger.LogRequest(lastMessages);

        Log.Message($"[StoryMaker] 发送调度请求: window=[{llmFromTick}, {llmToTick}], ack={ack}, 栈={failedEvents.Count}");

        DebugSimulator.ResetWindow();
        DebugSimulator.SetExpectedRange(llmFromTick, llmToTick);
        bool simulated = DebugSimulator.SimulateIfActive(OnResponseReceived, OnResponseError);
        if (!simulated)
        {
            AIChatServiceAsync.Instance.SendRequest(
                lastMessages, OnResponseReceived, OnResponseError);
        }
    }

    // ── 回复成功 ──
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

            // 解析 + 前三层 Guard
            ParseResult parseResult = ResponseParser.Parse(responseBody);

            // Guard 修正重试
            if (parseResult.NeedsCorrection)
            {
                var violation = parseResult.FirstViolation;
                if (violation == null) return;

                if (ShouldRetry(violation.ViolationTag))
                {
                    DebugLogger.RecordGuardCorrection();
                    string correction = FormatCorrectionMessages.Build(violation);
                    if (!string.IsNullOrEmpty(correction))
                    {
                        lastMessages.Add(new ChatMessage { role = "user", content = correction });
                        Log.Message($"[StoryMaker] 修正重试: {violation.ViolationTag} (parse={parseRetryCount}, schema={schemaRetryCount}, event={eventRetryCount})");
                        requestLock.ResetTimer();
                        AIChatServiceAsync.Instance.SendRequest(
                            lastMessages, OnResponseReceived, OnResponseError);
                        return;
                    }
                }

                Log.Warning($"[StoryMaker] Guard 修正重试次数用尽 ({violation.ViolationTag})，丢弃回复");
                return;
            }

            // Phase 4: EmptyPlanGuard（第 4 层）
            var response = parseResult.Response;
            string emptyCorrection = EmptyPlanGuard.Evaluate(response);
            if (!string.IsNullOrEmpty(emptyCorrection))
            {
                if (emptyRetryCount < 1)
                {
                    emptyRetryCount++;
                    DebugLogger.RecordGuardCorrection();
                    lastMessages.Add(new ChatMessage { role = "user", content = emptyCorrection });
                    Log.Message("[StoryMaker] EmptyPlanGuard: 强制修正重试");
                    requestLock.ResetTimer();
                    AIChatServiceAsync.Instance.SendRequest(
                        lastMessages, OnResponseReceived, OnResponseError);
                    return;
                }
                Log.Warning("[StoryMaker] EmptyPlanGuard: 强制修正后仍为空，接受此回复");
            }

            // ACK 校验
            if (response.plan_range.from_tick != expectedAckStart)
            {
                Log.Warning($"[StoryMaker] 过期回复: 期望 from_tick={expectedAckStart}, 实际={response.plan_range.from_tick}，丢弃");
                return;
            }

            // 有效回复：解锁 + 入队
            requestLock.Unlock();
            int curTick = Find.TickManager?.TicksGame ?? 0;

            // 解析 IncidentDef + 设置 generated_at_world_tick
            if (response.events != null)
            {
                foreach (var evt in response.events)
                {
                    evt.generated_at_world_tick = curTick;
                    if (!evt.ResolveDef())
                    {
                        ActionExecutor.AddFailedEvent(evt.event_id, evt.event_type,
                            $"IncidentDef 不存在或 Worker 为空: {evt.event_type}");
                    }
                }
            }

            // 过滤已过期事件 + Def 解析失败事件
            var validEvents = response.events
                ?.Where(e => e.scheduled_tick >= curTick && e.resolvedDef != null)
                .ToList();

            if (validEvents != null && validEvents.Count > 0)
            {
                eventQueue.InsertAll(validEvents);
            }

            if (response.events != null)
            {
                int expired = response.events.Count - (validEvents?.Count ?? 0);
                if (expired > 0)
                    Log.Message($"[StoryMaker] 过滤了 {expired} 个过期事件 (scheduled_tick < {curTick})");

                // 过期事件 → deviation_report
                var expiredEvts = response.events.Where(e => e.scheduled_tick < curTick);
                foreach (var evt in expiredEvts)
                {
                    ActionExecutor.AddFailedEvent(evt.event_id, evt.event_type,
                        $"入队时已过期: scheduled_tick={evt.scheduled_tick} < curTick={curTick}");
                }
            }

            // ACK 推进
            SetAck(response.plan_range.to_tick);
            Log.Message($"[StoryMaker] ACK 推进至 {ack}, 队列深度={eventQueue.Count}, {validEvents?.Count ?? 0} 个新事件入队");
            if (DebugLogger.IsEnabled) DebugLogger.WriteSummary();
        }
        catch (Exception ex)
        {
            Log.Error($"[StoryMaker] OnResponseReceived 异常: {ex.Message}\n{ex.StackTrace}");
        }
    }

    // ── 回复错误 ──
    private static void OnResponseError(string errorMessage, string failureReason)
    {
        Log.Error($"[StoryMaker] HTTP 错误: failure_reason={failureReason}, 详情={errorMessage}");

        if (DebugLogger.IsEnabled)
        {
            DebugLogger.LogError(lastRequestSeq, errorMessage, failureReason);
            DebugLogger.WriteSummary();
        }

        // HTTP 401/404 —— 不重试，直接降级弹窗
        if (failureReason == "http_401" || failureReason == "http_404")
        {
            requestLock.Unlock();
            HandleDegradation($"网络错误: {failureReason} — {errorMessage}");
            return;
        }

        // 其他错误由 TCP 层处理（缓冲耗尽检测或实时超时检测会触发重传/降级）
    }

    // ── Guard 重试判断 ──
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

    // ── Debug 日志辅助 ──
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
            sb.AppendLine($"  \"narrative_summary\": \"{JsonExtractor.EscapeJsonString(evt.narrative_summary ?? "")}\",");
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

}
