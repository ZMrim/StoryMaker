using RimWorld;
using StoryMaker.Core;
using UnityEngine;
using Verse;

namespace StoryMaker.UI;

// 游戏底部玩家面板：连接状态 + Token 消耗 + 恢复按钮 + 对话入口
public class StoryMakerBottomTab : MainTabWindow
{
    public override Vector2 RequestedTabSize => new(480f, 380f);

    public override void DoWindowContents(Rect inRect)
    {
        var listing = new Listing_Standard();
        listing.Begin(inRect);

        Text.Font = GameFont.Medium;
        listing.Label("StoryMaker_BottomTab_Title".Translate());
        Text.Font = GameFont.Small;
        listing.GapLine();
        listing.Gap(4f);

        DrawConnectionStatus(listing);
        listing.Gap(8f);
        DrawTokenStats(listing);
        listing.Gap(8f);
        DrawRestoreButton(listing);
        listing.GapLine();
        listing.Gap(4f);
        DrawDialogueButtons(listing);

        listing.End();
    }

    private void DrawConnectionStatus(Listing_Standard listing)
    {
        var state = StoryMakerState.Instance;

        string statusKey;
        Color statusColor;
        if (state != null && state.permanentDegraded)
        {
            statusKey = "StoryMaker_Status_Permanent";
            statusColor = Color.red;
        }
        else if (Schedule.EventScheduler.IsRequestLocked)
        {
            statusKey = "StoryMaker_Status_Requesting";
            statusColor = Color.cyan;
        }
        else
        {
            statusKey = "StoryMaker_Status_Normal";
            statusColor = Color.green;
        }

        GUI.color = statusColor;
        listing.Label("StoryMaker_BottomTab_Connection".Translate(statusKey.Translate()));
        GUI.color = Color.white;
    }

    private void DrawTokenStats(Listing_Standard listing)
    {
        var state = StoryMakerState.Instance;
        if (state == null) return;

        long total = state.totalTokens;
        string totalStr = total > 1000000 ? $"{total / 1000000f:F1}M"
            : total > 1000 ? $"{total / 1000f:F1}K"
            : $"{total}";

        listing.Label("StoryMaker_BottomTab_Tokens".Translate(totalStr, state.totalPromptTokens, state.totalCompletionTokens));
    }

    private void DrawRestoreButton(Listing_Standard listing)
    {
        var state = StoryMakerState.Instance;
        bool degraded = state != null && state.permanentDegraded;

        GUI.color = degraded ? Color.green : Color.gray;
        if (listing.ButtonText("StoryMaker_BottomTab_RestoreConnection".Translate()) && degraded)
        {
            StoryMakerManager.ResetDegradation();
            Messages.Message("StoryMaker_Msg_ConnectionRestored".Translate(), MessageTypeDefOf.PositiveEvent, false);
        }
        GUI.color = Color.white;
    }

    private void DrawDialogueButtons(Listing_Standard listing)
    {
        listing.Label("StoryMaker_BottomTab_Dialogue".Translate());
        listing.Gap(2f);

        bool barVisible = StoryMakerFloatingBar.CurrentlyOpen;
        GUI.color = barVisible ? Color.green : Color.white;
        string barLabel = barVisible ? "StoryMaker_BottomTab_FloatingBar_On".Translate() : "StoryMaker_BottomTab_FloatingBar_Off".Translate();
        if (listing.ButtonText(barLabel))
        {
            if (barVisible)
                StoryMakerFloatingBar.CloseIfOpen();
            else
                Find.WindowStack.Add(new StoryMakerFloatingBar());
        }
        GUI.color = Color.white;

        if (listing.ButtonText("StoryMaker_BottomTab_OpenChat".Translate()))
            StoryMakerDialogueWindow.Open();
    }
}
