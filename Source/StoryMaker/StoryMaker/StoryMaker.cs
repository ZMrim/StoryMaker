using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace StoryMaker;

public class StoryMaker : Mod
{
    public static StoryMaker Instance { get; private set; }
    public StoryMakerSettings Settings { get; private set; }

    // UI 缓存
    private string deathRaceWhitelistBuffer = "";
    private string cachedApiKey = "";
    private Vector2 settingsScrollPos;

    public StoryMaker(ModContentPack content) : base(content)
    {
        Instance = this;
        Settings = GetSettings<StoryMakerSettings>();
        PromptTemplates.ModRootDir = content.RootDir;
        deathRaceWhitelistBuffer = string.Join(", ", Settings.deathRaceWhitelist ?? new());
        cachedApiKey = Settings.apiKey ?? "";
        Log.Message("[StoryMaker] Mod 已加载，等待叙事者激活。");
    }

    public override string SettingsCategory() => "StoryMaker - AI 叙事者";

    public override void DoSettingsWindowContents(Rect inRect)
    {
        // 内容总高度估算：按钮(~100) + API(~200) + 调度(~250) + 个性化(~200) + 白名单(~80) + 开关(~100) + gap = ~1000
        float contentHeight = 1100f;
        Rect contentRect = new Rect(0f, 0f, inRect.width - 16f, contentHeight);
        Widgets.BeginScrollView(inRect, ref settingsScrollPos, contentRect);

        var listing = new Listing_Standard();
        listing.Begin(contentRect);

        // ===== 操作按钮（最顶部，最容易找到）=====
        if (listing.ButtonText("发送测试请求"))
            StoryMakerManager.SendTestRequest();

        if (Settings.debugMode)
        {
            if (listing.ButtonText("恢复连接（清除降级状态）"))
                StoryMakerManager.ResetDegradation();
        }

        listing.Gap();

        // ===== LLM API 配置 =====
        listing.Label("=== LLM API 配置 ===");

        // Provider 选择
        Rect providerRect = listing.GetRect(Text.LineHeight);
        Widgets.Label(providerRect.LeftPart(0.35f), "LLM 提供商");
        if (Widgets.ButtonText(providerRect.RightPart(0.65f), Settings.provider.GetLabel()))
        {
            var options = new List<FloatMenuOption>();
            foreach (AIProvider p in Enum.GetValues(typeof(AIProvider)))
            {
                var captured = p;
                options.Add(new FloatMenuOption(p.GetLabel(), () => Settings.provider = captured));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        // API Key
        Rect keyRect = listing.GetRect(Text.LineHeight);
        Widgets.Label(keyRect.LeftPart(0.35f), "API Key");
        cachedApiKey = Widgets.TextField(keyRect.RightPart(0.65f), cachedApiKey);
        Settings.apiKey = cachedApiKey.Trim();

        // 模型名称
        Rect modelRect = listing.GetRect(Text.LineHeight);
        Widgets.Label(modelRect.LeftPart(0.35f), "模型名称");
        Settings.modelName = Widgets.TextField(modelRect.RightPart(0.65f), Settings.modelName ?? "");

        // 自定义 Base URL（仅 Custom provider 可见）
        if (Settings.provider == AIProvider.Custom)
        {
            Rect urlRect = listing.GetRect(Text.LineHeight);
            Widgets.Label(urlRect.LeftPart(0.35f), "自定义 URL");
            Settings.customBaseUrl = Widgets.TextField(urlRect.RightPart(0.65f), Settings.customBaseUrl ?? "");
        }

        listing.Gap();

        // ===== TCP 协议参数 =====
        listing.Label("=== 调度参数 ===");

        listing.Label($"缓冲期（游戏天数）: {Settings.bufferDays}");
        Settings.bufferDays = (int)listing.Slider(Settings.bufferDays, 1f, 10f);

        listing.Label($"规划窗口（游戏天数）: {Settings.planWindowDays}");
        Settings.planWindowDays = (int)listing.Slider(Settings.planWindowDays, 2f, 15f);

        listing.Label($"请求超时（秒）: {Settings.timeoutSeconds}");
        Settings.timeoutSeconds = (int)listing.Slider(Settings.timeoutSeconds, 15f, 180f);

        listing.Label($"最大重传次数: {Settings.maxRetransmissions}");
        Settings.maxRetransmissions = (int)listing.Slider(Settings.maxRetransmissions, 0f, 5f);

        listing.Gap();

        // ===== 玩家个性化 =====
        listing.Label("玩家个性化描述（影响 LLM 叙事风格，建议 200 字以内）:");
        Rect textRect = listing.GetRect(Text.LineHeight * 6);
        Settings.playerPersonality = Widgets.TextArea(textRect, Settings.playerPersonality ?? "");

        listing.Gap();

        // ===== 死亡记录白名单 =====
        listing.Label("死亡记录种族白名单（defName，逗号分隔，默认 Human）:");
        Rect raceRect = listing.GetRect(Text.LineHeight * 2);
        deathRaceWhitelistBuffer = Widgets.TextArea(raceRect, deathRaceWhitelistBuffer);
        Settings.deathRaceWhitelist = deathRaceWhitelistBuffer
            .Split(',')
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();

        listing.Gap();

        // ===== 开关 =====
        listing.CheckboxLabeled("启用思考/推理模式（自动适配各提供商格式，关闭可大幅降低 Token 消耗）", ref Settings.enableThinking);
        listing.CheckboxLabeled("低 Token 模式（精简 Prompt，降低 API 费用）", ref Settings.lowTokenMode);
        listing.CheckboxLabeled("开发者调试模式（保存完整请求/回复到本地）", ref Settings.debugMode);

        // 错误模拟器开关
        listing.GapLine();
        var simModes = new List<DebugSimulationMode>
        {
            DebugSimulationMode.None,
            DebugSimulationMode.Timeout,
            DebugSimulationMode.ConnectionError,
            DebugSimulationMode.BadJson,
            DebugSimulationMode.SchemaViolation,
            DebugSimulationMode.EventViolation,
        };
        var simLabels = new List<string>
        {
            "关闭",
            "模拟超时 (Timeout)",
            "模拟连接错误 (ConnectionError)",
            "模拟坏JSON (BadJson)",
            "模拟Schema违规 (SchemaViolation)",
            "模拟Event违规 (EventViolation)",
        };
        int simIdx = simModes.IndexOf(Settings.simulationMode);
        if (simIdx < 0) simIdx = 0;
        string simLabel = "错误模拟模式: " + simLabels[simIdx];
        if (listing.ButtonText(simLabel))
        {
            var menu = new List<FloatMenuOption>();
            for (int i = 0; i < simModes.Count; i++)
            {
                var mode = simModes[i];
                var label = simLabels[i];
                menu.Add(new FloatMenuOption(label, () => Settings.simulationMode = mode));
            }
            Find.WindowStack.Add(new FloatMenu(menu));
        }

        listing.End();
        Widgets.EndScrollView();
        Settings.Write();
    }
}
