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

        string text = $"AI 叙事者连接失败，窗口 [第 {fromDay} 天 ~ 第 {toDay} 天] 未能获得有效回复。\n\n"
                    + $"错误详情: {reason}\n\n"
                    + "你可以选择重试（忽略缓冲期，立即重新请求）或放弃（永久切换至原版叙事者，"
                    + "之后可通过 Mod 设置中的\"恢复连接\"按钮重新启用 AI 叙事者）。\n\n"
                    + "（游戏已自动暂停，可从容选择）";

        var dialog = new Dialog_MessageBox(
            text: text,
            buttonAText: "重试",
            buttonAAction: () =>
            {
                IsDialogOpen = false;
                onRetry?.Invoke();
            },
            buttonBText: "放弃",
            buttonBAction: () =>
            {
                IsDialogOpen = false;
                onGiveUp?.Invoke();
            },
            title: "AI 叙事者连接失败",
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
