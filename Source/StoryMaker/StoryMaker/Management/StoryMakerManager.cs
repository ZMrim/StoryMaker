using System;
using StoryMaker.Core;
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

        // 发送请求
        AIChatServiceAsync.Instance.SendRequest(
            messages,
            onSuccess: (responseBody) =>
            {
                Log.Message($"[StoryMaker] 收到回复:\n{responseBody}");
                if (DebugLogger.IsEnabled)
                {
                    DebugLogger.LogResponse(seq, responseBody, 200, 0, 1);
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
