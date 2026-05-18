using Verse;

namespace StoryMaker.Core;

public class StoryMakerState
{
    public static StoryMakerState Instance { get; } = new StoryMakerState();

    public int ack;
    public bool permanentDegraded;
    public string degradationReason;
    public int consecutiveEmptyPlans;

    // Phase 2: 上下文版本号，每次读档/新游戏递增，用于丢弃陈旧 HTTP 回调
    public int contextVersion;

    // Phase 5: Token 消耗累计统计
    public long totalPromptTokens;
    public long totalCompletionTokens;
    public long totalTokens => totalPromptTokens + totalCompletionTokens;

    public void RecordTokenUsage(long promptTokens, long completionTokens)
    {
        totalPromptTokens += promptTokens;
        totalCompletionTokens += completionTokens;
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
