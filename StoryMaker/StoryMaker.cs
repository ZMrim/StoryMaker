using UnityEngine;
using Verse;

namespace StoryMaker;

public class StoryMaker : Mod
{
    public static StoryMaker Instance { get; private set; }
    public StoryMakerSettings Settings { get; private set; }

    public StoryMaker(ModContentPack content) : base(content)
    {
        Instance = this;
        Settings = GetSettings<StoryMakerSettings>();
        Log.Message("[StoryMaker] Mod 已加载，等待叙事者激活。");
    }

    public override string SettingsCategory() => "StoryMaker - AI 叙事者";

    public override void DoSettingsWindowContents(Rect inRect)
    {
        var listing = new Listing_Standard();
        listing.Begin(inRect);

        // bufferDays
        listing.Label($"缓冲期（游戏天数）: {Settings.bufferDays}");
        Settings.bufferDays = (int)listing.Slider(Settings.bufferDays, 1f, 10f);

        // planWindowDays
        listing.Label($"规划窗口（游戏天数）: {Settings.planWindowDays}");
        Settings.planWindowDays = (int)listing.Slider(Settings.planWindowDays, 2f, 15f);

        // timeoutSeconds
        listing.Label($"请求超时（秒）: {Settings.timeoutSeconds}");
        Settings.timeoutSeconds = (int)listing.Slider(Settings.timeoutSeconds, 15f, 180f);

        // maxRetransmissions
        listing.Label($"最大重传次数: {Settings.maxRetransmissions}");
        Settings.maxRetransmissions = (int)listing.Slider(Settings.maxRetransmissions, 0f, 5f);

        listing.Gap();

        // 玩家个性化描述
        listing.Label("玩家个性化描述（影响 LLM 叙事风格）:");
        Rect textRect = listing.GetRect(Text.LineHeight * 6);
        Settings.playerPersonality = Widgets.TextArea(textRect, Settings.playerPersonality ?? "");

        listing.Gap();

        // 开关
        listing.CheckboxLabeled("低 Token 模式", ref Settings.lowTokenMode);
        listing.CheckboxLabeled("开发者调试模式", ref Settings.debugMode);

        listing.End();
        Settings.Write();
    }
}
