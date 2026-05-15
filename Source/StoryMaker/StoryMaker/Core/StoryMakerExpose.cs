using Verse;

namespace StoryMaker.Core;

public static class StoryMakerExpose
{
    public static void Expose(StoryMakerState state)
    {
        Scribe_Values.Look(ref state.ack, "ack", 0);
        Scribe_Values.Look(ref state.permanentDegraded, "permanentDegraded", false);
        Scribe_Values.Look(ref state.consecutiveEmptyPlans, "consecutiveEmptyPlans", 0);
        Scribe_Values.Look(ref state.contextVersion, "contextVersion", 0);
        Scribe_Values.Look(ref state.requestLocked, "requestLocked", false);
    }
}
