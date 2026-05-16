using StoryMaker.Action;
using StoryMaker.Snapshot;
using Verse;

namespace StoryMaker.Core;

// 存档序列化聚合类。由 StoryMakerWorldComponent.ExposeData() 调用。
// 负责所有运行时状态的 Scribe 序列化/反序列化。
public static class StoryMakerExpose
{
    public static void ExposeAll()
    {
        var state = StoryMakerState.Instance;
        if (state == null) return;

        // ── 基础状态 ──
        Scribe_Values.Look(ref state.ack, "ack", 0);
        Scribe_Values.Look(ref state.permanentDegraded, "permanentDegraded", false);
        Scribe_Values.Look(ref state.degradationReason, "degradationReason", "");
        Scribe_Values.Look(ref state.consecutiveEmptyPlans, "consecutiveEmptyPlans", 0);
        Scribe_Values.Look(ref state.contextVersion, "contextVersion", 0);

        // ── 事件队列（由 EventScheduler 持有） ──
        Schedule.EventScheduler.ExposeQueue();

        // ── 事件和死亡栈 ──
        IncidentEventStack.ExposeData();

        // ── 执行失败事件 ──
        ActionExecutor.ExposeData();
    }
}
