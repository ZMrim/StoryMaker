using StoryMaker.Core;
using Verse;

namespace StoryMaker.Response;

// 第 4 层 Guard：连续空事件检测 + 强制要求。
// 与 Phase 3 前三层 Guard 不同，此 Guard 在 EventScheduler 中直接调用，
// 不在 ResponseParser.Parse() 中，因为它需要修改 system prompt（而非追加修正消息）。
public static class EmptyPlanGuard
{
    private static bool warningInjectedThisWindow;  // 本窗口是否已注入警告
    private static bool forceRetryUsed;             // 强制修正重试是否已使用

    // 新窗口重置（每次 SendSchedulingRequest 前调用）
    public static void ResetWindow()
    {
        warningInjectedThisWindow = false;
        forceRetryUsed = false;
    }

    // 是否需要在发送请求时注入空事件警告（在 PromptBuilder 组装时注入到 system prompt）
    public static bool ShouldInjectWarning()
    {
        var state = StoryMakerState.Instance;
        if (state == null) return false;
        return state.consecutiveEmptyPlans >= 3 && !warningInjectedThisWindow;
    }

    // 构建注入到 system prompt 的警告消息
    public static string BuildWarningMessage()
    {
        warningInjectedThisWindow = true;
        return "【系统提示】你已经连续 3 个规划窗口没有安排任何事件。"
             + "本窗口你**必须**至少安排 1 个合理事件。"
             + "如果确实无事可发生，请在 narrative_summary 中用至少 50 字给出充分的叙事理由。";
    }

    // 评估 LLM 回复是否为空事件，更新计数器。
    // 返回 null 表示通过（可直接推进 ACK）。
    // 返回非空字符串表示需要强制修正重试（该字符串为追加到对话的修正消息）。
    public static string Evaluate(LLMResponse response)
    {
        var state = StoryMakerState.Instance;
        if (state == null) return null;

        bool isEmpty = response.empty_plan
                    || response.events == null
                    || response.events.Count == 0;

        if (!isEmpty)
        {
            state.consecutiveEmptyPlans = 0;
            return null;
        }

        // 空事件 → 计数器递增
        state.consecutiveEmptyPlans++;
        Log.Message($"[StoryMaker] EmptyPlanGuard: 连续空事件计数 → {state.consecutiveEmptyPlans}");

        // 警告未注入 → 不需要强制重试（下周期自动注入警告）
        if (!warningInjectedThisWindow)
            return null;

        // 警告已注入但仍然空 → 强制修正重试（限 1 次）
        if (!forceRetryUsed)
        {
            forceRetryUsed = true;
            Log.Warning("[StoryMaker] EmptyPlanGuard: 警告注入后仍为空，触发强制修正重试");
            return BuildForceRetryMessage();
        }

        // 强制重试后仍空 → 接受此回复，不再无限循环
        Log.Warning("[StoryMaker] EmptyPlanGuard: 强制修正后仍为空，接受此回复");
        return null;
    }

    private static string BuildForceRetryMessage()
    {
        return "STORYMAKER_EMPTY_PLAN_FORCE\n"
             + "你上一窗口在收到强制要求后仍然没有安排任何事件。"
             + "请重新规划，本窗口**必须**包含至少 1 个合理事件，"
             + "否则请在 narrative_summary 中用至少 50 字给出充分的叙事理由。";
    }
}
