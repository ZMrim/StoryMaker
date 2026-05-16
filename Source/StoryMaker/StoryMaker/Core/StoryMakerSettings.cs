using System.Collections.Generic;
using Verse;

namespace StoryMaker;

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
    public int bufferDays = 2;
    public int planWindowDays = 5;
    public float timeoutSeconds = 60f;
    public int maxRetransmissions = 2;

    // LLM API 配置
    public AIProvider provider = AIProvider.DeepSeek;
    public string apiKey = "";
    public string modelName = "";
    public string customBaseUrl = "";

    // 玩家个性化
    public string playerPersonality = "";

    // 死亡记录种族白名单（defName 列表，如 Human, Ratkin 等）
    public List<string> deathRaceWhitelist = new() { "Human" };

    // 思考模式（仅 DeepSeek 等支持 reasoning 的模型有效）
    // 开启后 LLM 会输出 reasoning_content，消耗额外 token
    public bool enableThinking = true;

    // 调试选项
    public bool lowTokenMode = false;
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
        Scribe_Values.Look(ref bufferDays, "bufferDays", 2);
        Scribe_Values.Look(ref planWindowDays, "planWindowDays", 5);
        Scribe_Values.Look(ref timeoutSeconds, "timeoutSeconds", 60f);
        Scribe_Values.Look(ref maxRetransmissions, "maxRetransmissions", 2);
        Scribe_Values.Look(ref playerPersonality, "playerPersonality", "");
        Scribe_Collections.Look(ref deathRaceWhitelist, "deathRaceWhitelist", LookMode.Value);
        Scribe_Values.Look(ref enableThinking, "enableThinking", true);
        Scribe_Values.Look(ref lowTokenMode, "lowTokenMode", false);
        Scribe_Values.Look(ref debugMode, "debugMode", false);
        Scribe_Values.Look(ref simulationMode, "simulationMode", DebugSimulationMode.None);

        // Phase 2 API 配置
        Scribe_Values.Look(ref provider, "provider", AIProvider.DeepSeek);
        Scribe_Values.Look(ref apiKey, "apiKey", "");
        Scribe_Values.Look(ref modelName, "modelName", "");
        Scribe_Values.Look(ref customBaseUrl, "customBaseUrl", "");
    }
}
