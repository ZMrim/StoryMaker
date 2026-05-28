using System;
using System.Collections.Generic;
using System.Linq;
using StoryMaker.Core;
using StoryMaker.Schedule;
using StoryMaker.Response;
using StoryMaker.UI;
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
    private int selectedTab;

    private static readonly string[] TabLabels =
    {
        "StoryMaker_Tab_Basic",
        "StoryMaker_Tab_Advanced",
        "StoryMaker_Tab_Developer"
    };

    public StoryMaker(ModContentPack content) : base(content)
    {
        Instance = this;
        Settings = GetSettings<StoryMakerSettings>();
        PromptTemplates.ModRootDir = content.RootDir;
        deathRaceWhitelistBuffer = string.Join(",", Settings.deathRaceWhitelist ?? new());
        cachedApiKey = Settings.apiKey ?? "";
        Log.Message("[StoryMaker] Mod 已加载，等待叙事者激活。");
    }

    public override string SettingsCategory() => "StoryMaker - AI 叙事者";

    public override void DoSettingsWindowContents(Rect inRect)
    {
        float contentHeight = 1400f;
        Rect contentRect = new Rect(0f, 0f, inRect.width - 16f, contentHeight);
        Widgets.BeginScrollView(inRect, ref settingsScrollPos, contentRect);

        var listing = new Listing_Standard();
        listing.Begin(contentRect);

        DrawTabs(listing);
        listing.GapLine();
        listing.Gap(6f);

        switch (selectedTab)
        {
            case 0: DrawBasicSettings(listing); break;
            case 1: DrawAdvancedSettings(listing); break;
            case 2: DrawDeveloperSettings(listing); break;
        }

        listing.End();
        Widgets.EndScrollView();
        Settings.Write();
    }

    private void DrawTabs(Listing_Standard listing)
    {
        var rowRect = listing.GetRect(30f);
        float spacing = 10f;
        float btnWidth = (rowRect.width - spacing * 2f) / 3f;
        for (int i = 0; i < TabLabels.Length; i++)
        {
            GUI.color = selectedTab == i ? Color.cyan : Color.gray;
            var btnRect = new Rect(rowRect.x + i * (btnWidth + spacing), rowRect.y, btnWidth, 28f);
            if (Widgets.ButtonText(btnRect, TabLabels[i].Translate()))
                selectedTab = i;
        }
        GUI.color = Color.white;
    }

    // ── 基础设置 ──

    private void DrawBasicSettings(Listing_Standard listing)
    {
        listing.Label("StoryMaker_Settings_ApiSection".Translate());

        Rect providerRect = listing.GetRect(Text.LineHeight);
        Widgets.Label(providerRect.LeftPart(0.35f), "StoryMaker_Settings_Provider".Translate());
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

        Rect keyRect = listing.GetRect(Text.LineHeight);
        Widgets.Label(keyRect.LeftPart(0.35f), "StoryMaker_Settings_ApiKey".Translate());
        cachedApiKey = Widgets.TextField(keyRect.RightPart(0.65f), cachedApiKey);
        Settings.apiKey = cachedApiKey.Trim();

        Rect modelRect = listing.GetRect(Text.LineHeight);
        Widgets.Label(modelRect.LeftPart(0.35f), "StoryMaker_Settings_ModelName".Translate());
        Settings.modelName = Widgets.TextField(modelRect.RightPart(0.65f), Settings.modelName ?? "");

        if (Settings.provider == AIProvider.Custom)
        {
            Rect urlRect = listing.GetRect(Text.LineHeight);
            Widgets.Label(urlRect.LeftPart(0.35f), "StoryMaker_Settings_CustomUrl".Translate());
            Settings.customBaseUrl = Widgets.TextField(urlRect.RightPart(0.65f), Settings.customBaseUrl ?? "");
        }

        listing.Gap();
        listing.GapLine();
        listing.Gap(4f);

        // ── 叙事者基本信息 ──
        listing.Label("StoryMaker_Settings_NarratorInfo".Translate());

        // 叙事者名称
        listing.Label("StoryMaker_Settings_NarratorName".Translate());
        Settings.narratorName = Widgets.TextField(listing.GetRect(Text.LineHeight), Settings.narratorName ?? "");

        // 叙事者头像路径
        listing.Label("StoryMaker_Settings_AvatarPath".Translate());
        Settings.avatarPath = Widgets.TextField(listing.GetRect(Text.LineHeight), Settings.avatarPath ?? "");

        listing.Gap();
        listing.GapLine();
        listing.Gap(4f);

        // ── 玩家自定义叙事风格 ──
        listing.Label("StoryMaker_Settings_PersonalitySection".Translate());

        // 预设 / 自定义切换
        listing.CheckboxLabeled("StoryMaker_Settings_UseCustomStyle".Translate(),
            ref Settings.useCustomStyle, "StoryMaker_Settings_UseCustomStyle_Desc".Translate());

        if (!Settings.useCustomStyle)
        {
            // ── 预设模式 ──
            listing.Label("StoryMaker_Settings_Difficulty".Translate());
            Rect diffRect = listing.GetRect(Text.LineHeight);
            string diffLabel = ("StoryMaker_Difficulty_" + Settings.difficultyLevel.ToString()).Translate();
            if (Widgets.ButtonText(diffRect, diffLabel))
            {
                var options = new List<FloatMenuOption>();
                foreach (DifficultyLevel d in Enum.GetValues(typeof(DifficultyLevel)))
                {
                    var captured = d;
                    options.Add(new FloatMenuOption(
                        ("StoryMaker_Difficulty_" + captured.ToString()).Translate(),
                        () => Settings.difficultyLevel = captured));
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }

            listing.Label("StoryMaker_Settings_Density".Translate());
            Rect densRect = listing.GetRect(Text.LineHeight);
            string densLabel = ("StoryMaker_Density_" + Settings.densityLevel.ToString()).Translate();
            if (Widgets.ButtonText(densRect, densLabel))
            {
                var options = new List<FloatMenuOption>();
                foreach (DensityLevel d in Enum.GetValues(typeof(DensityLevel)))
                {
                    var captured = d;
                    options.Add(new FloatMenuOption(
                        ("StoryMaker_Density_" + captured.ToString()).Translate(),
                        () => Settings.densityLevel = captured));
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }

            listing.Label("StoryMaker_Settings_Persona".Translate());
            Rect personaRect = listing.GetRect(Text.LineHeight * 4);
            Settings.storytellerPersona = Widgets.TextArea(personaRect, Settings.storytellerPersona ?? "");
        }
        else
        {
            // ── 自定义模式 ──

            listing.Label("StoryMaker_Settings_CustomStyle".Translate());
            Rect customStyleRect = listing.GetRect(Text.LineHeight * 4);
            Settings.customNarratorStyle = Widgets.TextArea(customStyleRect, Settings.customNarratorStyle ?? "");

            listing.Label("StoryMaker_Settings_CustomPersona".Translate());
            Rect customPersonaRect = listing.GetRect(Text.LineHeight * 4);
            Settings.customNarratorPersona = Widgets.TextArea(customPersonaRect, Settings.customNarratorPersona ?? "");
        }
    }

    // ── 高级设置 ──

    private void DrawAdvancedSettings(Listing_Standard listing)
    {
        listing.Label("StoryMaker_Settings_ScheduleSection".Translate());

        listing.Label("StoryMaker_Settings_BufferDays".Translate(Settings.bufferDays));
        Settings.bufferDays = (int)listing.Slider(Settings.bufferDays, 1f, 10f);

        listing.Label("StoryMaker_Settings_PlanWindowDays".Translate(Settings.planWindowDays));
        Settings.planWindowDays = (int)listing.Slider(Settings.planWindowDays, 2f, 15f);

        listing.Label("StoryMaker_Settings_TimeoutSeconds".Translate(Settings.timeoutSeconds));
        Settings.timeoutSeconds = (int)listing.Slider(Settings.timeoutSeconds, 15f, 180f);

        listing.Label("StoryMaker_Settings_MaxRetransmissions".Translate(Settings.maxRetransmissions));
        Settings.maxRetransmissions = (int)listing.Slider(Settings.maxRetransmissions, 0f, 5f);

        listing.Label("StoryMaker_Settings_StaleThreshold".Translate(Settings.staleThresholdDays));
        Settings.staleThresholdDays = (int)listing.Slider(Settings.staleThresholdDays, 5f, 30f);

        listing.Gap();
        listing.GapLine();
        listing.Gap(4f);

        listing.CheckboxLabeled("StoryMaker_Settings_EnableThinking".Translate(), ref Settings.enableThinking);
        listing.CheckboxLabeled("StoryMaker_Settings_LowTokenMode".Translate(), ref Settings.lowTokenMode);

        listing.Gap();
        listing.GapLine();
        listing.Gap(4f);

        listing.Label("StoryMaker_Settings_DeathWhitelist".Translate());
        Rect raceRect = listing.GetRect(Text.LineHeight * 2);
        deathRaceWhitelistBuffer = Widgets.TextArea(raceRect, deathRaceWhitelistBuffer);
        Settings.deathRaceWhitelist = deathRaceWhitelistBuffer
            .Split(',')
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();
    }

    // ── 开发者设置 ──

    private void DrawDeveloperSettings(Listing_Standard listing)
    {
        if (listing.ButtonText("StoryMaker_Settings_SendTestRequest".Translate()))
            StoryMakerManager.SendTestRequest();

        if (listing.ButtonText("StoryMaker_Settings_RestoreConnectionBtn".Translate()))
            StoryMakerManager.ResetDegradation();

        if (EventScheduler.IsRequestLocked && listing.ButtonText("StoryMaker_Settings_SkipWindow".Translate()))
            EventScheduler.SkipCurrentWindow();

        if (listing.ButtonText("StoryMaker_Settings_RefreshNow".Translate()))
            EventScheduler.ForceRefresh();

        if (listing.ButtonText("StoryMaker_Settings_ResetStats".Translate()))
            EmptyPlanGuard.ResetCounters();

        listing.Gap();
        listing.GapLine();
        listing.Gap(4f);

        listing.CheckboxLabeled("StoryMaker_Settings_DebugMode".Translate(), ref Settings.debugMode);

        // 错误模拟器
        var simModes = new List<DebugSimulationMode>
        {
            DebugSimulationMode.None,
            DebugSimulationMode.Timeout,
            DebugSimulationMode.ConnectionError,
            DebugSimulationMode.BadJson,
            DebugSimulationMode.SchemaViolation,
            DebugSimulationMode.EventViolation,
        };
        var simKeyLabels = new List<string>
        {
            "StoryMaker_Sim_None",
            "StoryMaker_Sim_Timeout",
            "StoryMaker_Sim_ConnectionError",
            "StoryMaker_Sim_BadJson",
            "StoryMaker_Sim_SchemaViolation",
            "StoryMaker_Sim_EventViolation",
        };
        int simIdx = simModes.IndexOf(Settings.simulationMode);
        if (simIdx < 0) simIdx = 0;
        string simLabel = "StoryMaker_Settings_SimHeader".Translate(simKeyLabels[simIdx].Translate());
        if (listing.ButtonText(simLabel))
        {
            var menu = new List<FloatMenuOption>();
            for (int i = 0; i < simModes.Count; i++)
            {
                var mode = simModes[i];
                var label = simKeyLabels[i].Translate();
                menu.Add(new FloatMenuOption(label, () => Settings.simulationMode = mode));
            }
            Find.WindowStack.Add(new FloatMenu(menu));
        }

        listing.Gap();
        listing.GapLine();
        listing.Gap(4f);

        // 运行时状态
        listing.Label("StoryMaker_Settings_RuntimeStatus".Translate());
        listing.Gap(2f);

        var state = StoryMakerState.Instance;
        int ackVal = state?.ack ?? 0;
        listing.Label("StoryMaker_Settings_AckPosition".Translate(ackVal / 60000, ackVal));

        int queueDepth = EventScheduler.QueueCount;
        listing.Label("StoryMaker_Settings_QueueDepth".Translate(queueDepth));

        if (EventScheduler.LastRequestTick > 0)
        {
            int curTick = Find.TickManager?.TicksGame ?? 0;
            float daysAgo = (curTick - EventScheduler.LastRequestTick) / 60000f;
            listing.Label("StoryMaker_Settings_LastRequest".Translate(daysAgo));
        }
        else
        {
            listing.Label("StoryMaker_Settings_LastRequest_None".Translate());
        }

        string lastErr = state?.degradationReason;
        if (!string.IsNullOrEmpty(lastErr))
            listing.Label("StoryMaker_Settings_LastError".Translate(lastErr));

        int emptyCnt = state?.consecutiveEmptyPlans ?? 0;
        listing.Label("StoryMaker_Settings_EmptyCount".Translate(emptyCnt));
    }
}
