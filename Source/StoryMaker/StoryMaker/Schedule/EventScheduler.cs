using Verse;
using StoryMaker.Snapshot;
using StoryMaker.Core;

namespace StoryMaker.Schedule;

public static class EventScheduler
{
    // 用于检测新游戏/读档，触发事件栈清空和 contextVersion 递增
    private static int lastGameStartAbsTick = -1;

    public static void OnTick(int curTick)
    {
        // 检测新游戏/读档（gameStartAbsTick 每次新游戏/读档后都会变化）
        // 递增 contextVersion 以丢弃陈旧 HTTP 回调，但不清空事件栈。
        // 事件栈由每次快照时的 PopAll 自然消耗，Phase 4 序列化将解决跨存档持久化。
        if (lastGameStartAbsTick != Find.TickManager.gameStartAbsTick)
        {
            lastGameStartAbsTick = Find.TickManager.gameStartAbsTick;
            StoryMakerState.Instance.contextVersion++;
            Log.Message($"[StoryMaker] 检测到新游戏/读档, contextVersion={StoryMakerState.Instance.contextVersion} (gameStartAbsTick={lastGameStartAbsTick})");
        }

        // Phase 1: 每游戏天采集一次快照并输出到日志
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
