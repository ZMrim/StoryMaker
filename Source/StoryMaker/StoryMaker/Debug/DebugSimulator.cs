using System;
using StoryMaker.Core;
using Verse;

namespace StoryMaker;

// 调试用错误模拟器：拦截 LLM 请求，模拟各种失败场景。
// Mod 设置中选择模拟模式后，自动调度请求会被此拦截器接管。
public static class DebugSimulator
{
    private static int attemptCount;
    private static int totalRetransmissions;  // 跟踪当前窗口的重传总次数

    public static bool IsActive
    {
        get
        {
            var settings = StoryMaker.Instance?.Settings;
            return settings != null && settings.simulationMode != DebugSimulationMode.None;
        }
    }

    public static DebugSimulationMode Mode
    {
        get
        {
            var settings = StoryMaker.Instance?.Settings;
            return settings?.simulationMode ?? DebugSimulationMode.None;
        }
    }

    // 新一轮窗口开始，重置计数器
    public static void ResetWindow()
    {
        attemptCount = 0;
        totalRetransmissions = 0;
        Log.Message($"[StoryMaker] [模拟器] 新窗口开始，模式={Mode}");
    }

    // 记录一次重传
    public static void RecordRetransmission()
    {
        totalRetransmissions++;
        attemptCount++;
        Log.Message($"[StoryMaker] [模拟器] 第 {totalRetransmissions} 次重传 (总尝试={attemptCount})");
    }

    // 生成模拟的错误回调
    // 返回 true 表示模拟器接管了（不需要真实 HTTP 请求）
    // 返回 false 表示模拟器不接管（走正常 HTTP 流程）
    public static bool SimulateIfActive(
        Action<string> onSuccess,
        Action<string, string> onError)
    {
        if (!IsActive) return false;

        attemptCount++;
        var mode = Mode;
        Log.Message($"[StoryMaker] [模拟器] 拦截请求 #{attemptCount}，模式={mode}");

        switch (mode)
        {
            case DebugSimulationMode.Timeout:
                // 不调用任何回调——由实时超时检测触发重传/降级
                Log.Message($"[StoryMaker] [模拟器] 模拟超时：不触发回调，等待 OnTick 超时检测 (timeout={StoryMaker.Instance?.Settings?.timeoutSeconds ?? 60}s)");
                break;

            case DebugSimulationMode.ConnectionError:
                onError?.Invoke("Simulated connection error: Cannot resolve destination host", "connection_error");
                Log.Message($"[StoryMaker] [模拟器] 模拟连接错误已触发");
                break;

            case DebugSimulationMode.BadJson:
                onSuccess?.Invoke("This is not JSON at all. Just some random text from the LLM.");
                Log.Message($"[StoryMaker] [模拟器] 模拟坏 JSON 已触发");
                break;

            case DebugSimulationMode.SchemaViolation:
                onSuccess?.Invoke(@"{""empty_plan"": false, ""narrative_summary"": ""missing plan_range"", ""events"": []}");
                Log.Message($"[StoryMaker] [模拟器] 模拟 Schema 违规已触发");
                break;

            case DebugSimulationMode.EventViolation:
                onSuccess?.Invoke(
                    "{" +
                    "\"plan_range\": { \"from_tick\": " + GetExpectedFromTick() + ", \"to_tick\": " + GetExpectedToTick() + " }," +
                    "\"empty_plan\": false," +
                    "\"narrative_summary\": \"Simulated response with invalid events\"," +
                    "\"events\": [" +
                    "  { \"event_id\": \"bad1\", \"scheduled_tick\": 99999999, \"event_type\": \"NotARealEvent\", \"parameters\": {} }," +
                    "  { \"event_id\": \"bad2\", \"scheduled_tick\": 123, \"event_type\": \"RaidEnemy\", \"parameters\": { \"intensity_multiplier\": 99 } }" +
                    "]" +
                    "}");
                Log.Message($"[StoryMaker] [模拟器] 模拟 Event 违规已触发");
                break;
        }

        return true;
    }

    // 模拟器需要知道期望的 ACK 范围来生成模拟响应
    // 这些值在 EventScheduler.SendSchedulingRequest 中设置
    private static int expectedFromTick;
    private static int expectedToTick;

    public static void SetExpectedRange(int fromTick, int toTick)
    {
        expectedFromTick = fromTick;
        expectedToTick = toTick;
        Log.Message($"[StoryMaker] [模拟器] 期望范围: [{expectedFromTick}, {expectedToTick}]");
    }

    private static int GetExpectedFromTick() => expectedFromTick;
    private static int GetExpectedToTick() => expectedToTick;
}
