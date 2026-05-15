using Verse;

namespace StoryMaker.Core;

public class StoryMakerState
{
    public int ack;
    public bool permanentDegraded;
    public string degradationReason;
    public int consecutiveEmptyPlans;

    public StoryMakerState()
    {
        ack = 0;
        permanentDegraded = false;
        degradationReason = "";
        consecutiveEmptyPlans = 0;
    }

    public void ResetDegradation()
    {
        permanentDegraded = false;
        degradationReason = "";
        Log.Message("[StoryMaker] 降级状态已清除，恢复 AI 叙事者调度。");
    }

    public void MarkDegraded(string reason)
    {
        permanentDegraded = true;
        degradationReason = reason;
        Log.Warning($"[StoryMaker] 触发永久降级: {reason}");
    }
}
