using Verse;

namespace StoryMaker.Schedule;

// 降级弹窗处理：游戏内弹窗通知玩家网络故障，提供重试/放弃操作。
// 使用原版 Dialog_MessageBox，forcePause=true 自动暂停游戏。
public static class DegradationHandler
{
    public static bool IsDialogOpen { get; private set; }

    // 显示降级弹窗。onRetry 和 onGiveUp 在玩家点击按钮时回调。
    public static void ShowDialog(string reason, int missedFromTick, int missedToTick,
        System.Action onRetry, System.Action onGiveUp)
    {
        if (IsDialogOpen) return;  // 防止重复弹窗

        IsDialogOpen = true;

        int fromDay = missedFromTick / 60000;
        int toDay = missedToTick / 60000;

        string text = "StoryMaker_Degradation_Message".Translate(fromDay, toDay, reason);

        var dialog = new Dialog_MessageBox(
            text: text,
            buttonAText: "StoryMaker_Degradation_Retry".Translate(),
            buttonAAction: () =>
            {
                IsDialogOpen = false;
                onRetry?.Invoke();
            },
            buttonBText: "StoryMaker_Degradation_GiveUp".Translate(),
            buttonBAction: () =>
            {
                IsDialogOpen = false;
                onGiveUp?.Invoke();
            },
            title: null,
            buttonADestructive: false
        );

        if (Find.WindowStack != null)
            Find.WindowStack.Add(dialog);
        else
            Log.Error($"[StoryMaker] 无法显示降级弹窗: WindowStack 不可用。原因: {reason}");
        Log.Warning($"[StoryMaker] 降级弹窗已显示: {reason}");
    }

    // 关闭弹窗（外部调用，如读档时清理残留弹窗）
    public static void CloseIfOpen()
    {
        IsDialogOpen = false;
        // Dialog_MessageBox 会自动关闭，此处仅重置标记
    }
}
