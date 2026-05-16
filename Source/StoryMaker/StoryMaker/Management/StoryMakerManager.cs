using System;
using StoryMaker.Core;
using StoryMaker.Response;
using StoryMaker.Schedule;
using StoryMaker.Snapshot;
using Verse;

namespace StoryMaker;

public static class StoryMakerManager
{
    public static void SendTestRequest()
    {
        var settings = StoryMaker.Instance?.Settings;
        if (settings == null)
        {
            Log.Error("[StoryMaker] Settings 不可用，无法发送测试请求。");
            return;
        }

        // 检查配置完整性
        if (!settings.IsApiConfigured())
        {
            Log.Error("[StoryMaker] 测试请求取消: API Key 或模型名称未配置。请先在 Mod 设置中填写。");
            Find.WindowStack?.Add(new Dialog_MessageBox(
                "API Key 或模型名称未配置。\n请先在 Mod 设置中填写 API Key 和模型名称。", "确定", null));
            return;
        }

        int curTick = Find.TickManager?.TicksGame ?? 0;
        int bufferTicks = settings.BufferTicks;
        int windowTicks = settings.PlanWindowTicks;

        int fromTick = curTick + bufferTicks;
        int toTick = curTick + bufferTicks + windowTicks;

        Log.Message($"[StoryMaker] ===== 测试请求开始 ===== (from={fromTick}, to={toTick})");

        // 采集快照
        GameStateSnapshot snapshot = SnapshotCollector.Collect(fromTick, toTick);
        Log.Message($"[StoryMaker] 快照采集完成，from_tick={fromTick}, to_tick={toTick}");

        // 构建 Prompt
        var messages = PromptBuilder.Build(snapshot, fromTick, toTick);
        Log.Message($"[StoryMaker] Prompt 构建完成，共 {messages.Count} 条消息");

        // Debug 日志
        int seq = DebugLogger.LogRequest(messages);
        float sendTime = UnityEngine.Time.realtimeSinceStartup;

        // 发送请求
        AIChatServiceAsync.Instance.SendRequest(
            messages,
            onSuccess: (responseBody) =>
            {
                long elapsedMs = (long)((UnityEngine.Time.realtimeSinceStartup - sendTime) * 1000f);
                Log.Message($"[StoryMaker] 原始回复:\n{responseBody}");

                // Phase 3: 解析 + Guard 校验
                ParseResult parseResult = ResponseParser.Parse(responseBody);
                if (parseResult.IsSuccess)
                {
                    Log.Message($"[StoryMaker] === 解析成功 ===");
                    Log.Message($"[StoryMaker] plan_range: [{parseResult.Response.plan_range.from_tick}, {parseResult.Response.plan_range.to_tick}]");
                    Log.Message($"[StoryMaker] empty_plan: {parseResult.Response.empty_plan}");
                    Log.Message($"[StoryMaker] narrative_summary: {parseResult.Response.narrative_summary}");
                    Log.Message($"[StoryMaker] events 数量: {parseResult.Response.events?.Count ?? 0}");
                    if (parseResult.Response.HasEvents)
                    {
                        for (int i = 0; i < parseResult.Response.events.Count; i++)
                        {
                            var evt = parseResult.Response.events[i];
                            Log.Message($"[StoryMaker]   事件[{i}]: {evt.event_type} @ tick {evt.scheduled_tick} (id={evt.event_id})");
                            if (evt.parameters != null && evt.parameters.Count > 0)
                            {
                                foreach (var kv in evt.parameters)
                                    Log.Message($"[StoryMaker]     参数 {kv.Key}={kv.Value}");
                            }
                            if (!string.IsNullOrEmpty(evt.narration_text))
                                Log.Message($"[StoryMaker]     叙事: {evt.narration_text}");
                        }
                    }
                }
                else
                {
                    Log.Warning($"[StoryMaker] === 解析失败 ===");
                    var violation = parseResult.FirstViolation;
                    if (violation != null)
                    {
                        Log.Warning($"[StoryMaker] 违规: {violation.ViolationTag} - {violation.ViolationDetail}");
                        string correction = FormatCorrectionMessages.Build(violation);
                        Log.Message($"[StoryMaker] 修正提示词:\n{correction}");
                    }
                }

                if (DebugLogger.IsEnabled)
                {
                    string parsedJson = EventScheduler.ParseAndFormatResponse(responseBody);
                    DebugLogger.LogResponse(seq, responseBody, 200, elapsedMs, 1, parsedJson);
                    DebugLogger.WriteSummary();
                }
                Log.Message("[StoryMaker] ===== 测试请求结束 =====");
            },
            onError: (errorMessage, failureReason) =>
            {
                Log.Error($"[StoryMaker] 请求失败: failure_reason={failureReason}, 详情={errorMessage}");
                if (DebugLogger.IsEnabled)
                {
                    DebugLogger.LogError(seq, errorMessage, failureReason);
                    DebugLogger.WriteSummary();
                }
                Log.Message("[StoryMaker] ===== 测试请求结束 =====");
            }
        );

        Log.Message($"[StoryMaker] HTTP 请求已发送至 {settings.GetEffectiveEndpoint()}");
    }

    public static void ResetDegradation()
    {
        StoryMakerState.Instance.ResetDegradation();
        Find.WindowStack?.Add(new Dialog_MessageBox(
            "降级状态已清除。\nAI 叙事者将在下一个 tick 恢复正常调度。", "确定", null));
    }
}
