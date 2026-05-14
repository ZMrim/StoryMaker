using Verse;
using StoryMaker.Snapshot;

namespace StoryMaker.Schedule;

public static class EventScheduler
{
    public static void OnTick(int curTick)
    {
        // Phase 1: 每游戏天采集一次快照并输出到日志，用于验证数据正确性
        if (curTick % 60000 != 0)
            return;

        var settings = StoryMaker.Instance?.Settings;
        int bufferTicks = settings?.BufferTicks ?? 120000;
        int windowTicks = settings?.PlanWindowTicks ?? 300000;

        int fromTick = curTick + bufferTicks;
        int toTick = curTick + bufferTicks + windowTicks;

        GameStateSnapshot snapshot = SnapshotCollector.Collect(fromTick, toTick);
        string json = SnapshotSerializer.ToJson(snapshot);

        Log.Message($"[StoryMaker] === 快照 (Day {curTick / 60000}, tick {curTick}) ===\n{json}\n=== 快照结束 ===");
    }
}
