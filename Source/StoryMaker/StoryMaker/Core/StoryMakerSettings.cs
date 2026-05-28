using System.Collections.Generic;
using Verse;

namespace StoryMaker;

// 叙事难度
public enum DifficultyLevel
{
    Merciful,    // 仁慈
    Balanced,    // 均衡
    Challenging, // 挑战
    Cruel        // 残忍
}

// 叙事密度
public enum DensityLevel
{
    Silent,    // 沉默
    Balanced,  // 均衡
    Chatty,    // 碎嘴
    Talkative  // 话痨
}

public enum DebugSimulationMode
{
    None,
    Timeout,           // 所有请求超时（无回调，由实时超时检测触发重传）
    ConnectionError,   // 所有请求返回 connection_error
    BadJson,           // 返回不可解析的文本（触发 ParseGuard 修正）
    SchemaViolation,   // 返回缺少 plan_range 的 JSON（触发 SchemaGuard 修正）
    EventViolation     // 返回包含无效事件的 JSON（触发 EventGuard 过滤）
}

public class StoryMakerSettings : ModSettings
{
    // TCP 协议参数
    public int bufferDays = 3;
    public int planWindowDays = 5;
    public float timeoutSeconds = 60f;
    public int maxRetransmissions = 2;

    // Stale 事件阈值（游戏天数，读档时清理生成时间过久的事件）
    public int staleThresholdDays = 15;
    public int StaleThresholdTicks => staleThresholdDays * 60000;

    // LLM API 配置
    public AIProvider provider = AIProvider.DeepSeek;
    public string apiKey = "";
    public string modelName = "";
    public string customBaseUrl = "";

    // 叙事者基本信息
    public string narratorName = "古明地恋";

    // 玩家自定义叙事风格
    public bool useCustomStyle = false;
    public DifficultyLevel difficultyLevel = DifficultyLevel.Balanced;
    public DensityLevel densityLevel = DensityLevel.Balanced;
    public string storytellerPersona = "地灵殿的古明地恋，黑暗与纯真并存。";
    // 自定义风格（useCustomStyle = true 时生效）
    public string customNarratorStyle = "地灵殿的古明地恋是无意识的妖怪，因此恋恋的叙事风格完全无法预测，叙事完全随机，毫无规律可言。";
    public string customNarratorPersona = "地灵殿的古明地恋，黑暗与纯真并存。";

    // 叙事者头像（绝对路径指向 PNG，空不使用自定义头像）
    public string avatarPath = "";

    // 死亡记录种族白名单（defName 列表，如 Human, Ratkin 等）
    public List<string> deathRaceWhitelist = new() { "Human","Ratkin","Rabbie","Axolotl","Kiiro_Race","Milira_Race","Wolfein_Race","Anty","Dragonian_Race","Yuran_Race","Alien_Miho","Mincho_ThingDef","Alien_Moyo","Paniel_Race"};

    // 思考模式（仅 DeepSeek 等支持 reasoning 的模型有效）
    // 开启后 LLM 会输出 reasoning_content，消耗额外 token
    public bool enableThinking = false;

    // 调试选项
    public bool lowTokenMode = true;
    public bool debugMode = false;
    public DebugSimulationMode simulationMode = DebugSimulationMode.None;

    public int BufferTicks => bufferDays * 60000;
    public int PlanWindowTicks => planWindowDays * 60000;

    // 获取实际使用的 endpoint URL（Custom provider 时使用 customBaseUrl）
    public string GetEffectiveEndpoint()
    {
        if (provider == AIProvider.Custom)
            return string.IsNullOrWhiteSpace(customBaseUrl) ? "" : customBaseUrl.Trim();
        return provider.GetEndpointUrl();
    }

    // 配置是否完整（API Key + Model 已填写）
    public bool IsApiConfigured()
    {
        return !string.IsNullOrWhiteSpace(apiKey) && !string.IsNullOrWhiteSpace(modelName);
    }

    public override void ExposeData()
    {
        // Phase 1 参数
        Scribe_Values.Look(ref bufferDays, "bufferDays", 3);
        Scribe_Values.Look(ref planWindowDays, "planWindowDays", 5);
        Scribe_Values.Look(ref timeoutSeconds, "timeoutSeconds", 60f);
        Scribe_Values.Look(ref maxRetransmissions, "maxRetransmissions", 2);
        Scribe_Values.Look(ref staleThresholdDays, "staleThresholdDays", 15);
        Scribe_Values.Look(ref narratorName, "narratorName", "AI 叙事者");
        Scribe_Values.Look(ref useCustomStyle, "useCustomStyle", false);
        Scribe_Values.Look(ref difficultyLevel, "difficultyLevel", DifficultyLevel.Balanced);
        Scribe_Values.Look(ref densityLevel, "densityLevel", DensityLevel.Balanced);
        Scribe_Values.Look(ref storytellerPersona, "storytellerPersona", "");
        Scribe_Values.Look(ref customNarratorStyle, "customNarratorStyle", "");
        Scribe_Values.Look(ref customNarratorPersona, "customNarratorPersona", "");
        Scribe_Values.Look(ref avatarPath, "avatarPath", "");
        Scribe_Collections.Look(ref deathRaceWhitelist, "deathRaceWhitelist", LookMode.Value);
        Scribe_Values.Look(ref enableThinking, "enableThinking", false);
        Scribe_Values.Look(ref lowTokenMode, "lowTokenMode", true);
        Scribe_Values.Look(ref debugMode, "debugMode", false);
        Scribe_Values.Look(ref simulationMode, "simulationMode", DebugSimulationMode.None);

        // Phase 2 API 配置
        Scribe_Values.Look(ref provider, "provider", AIProvider.DeepSeek);
        Scribe_Values.Look(ref apiKey, "apiKey", "");
        Scribe_Values.Look(ref modelName, "modelName", "");
        Scribe_Values.Look(ref customBaseUrl, "customBaseUrl", "");
    }
}
