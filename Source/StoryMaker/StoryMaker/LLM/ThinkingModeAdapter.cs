using Verse;

namespace StoryMaker;

/// <summary>
/// 统一适配各 LLM 提供商的思考/推理模式参数。
/// 不同提供商的 API 参数名和值各不相同，此适配器根据 Provider 和自定义 URL 返回正确的 JSON 片段。
///
/// 文档来源（2026年5月）：
///   DeepSeek:   https://api-docs.deepseek.com  — thinking.type = "enabled"|"disabled"
///   OpenAI:     https://api.openai.com           — reasoning_effort = "low"|"medium"|"high"
///   OpenRouter: https://openrouter.ai/docs       — reasoning.effort / reasoning.enabled
///   Google:     OpenAI兼容端点无标准思考参数       — 跳过
///   阿里百炼:    https://dashscope.aliyuncs.com   — enable_thinking = true|false
///   百度千帆:    https://qianfan.baidubce.com     — enable_reasoning = true|false
///   火山方舟:    https://ark.cn-beijing.volces.com — thinking.type = "enabled"|"disabled"|"auto"
///   华为ModelArts: https://support.huaweicloud.com — thinking.type = "enabled"|"disabled"
///   腾讯混元:    https://hunyuan.tencentcloudapi.com — EnableThinking = true|false
///   京东言犀:    无 OpenAI 兼容 chat/completions 端点 — 跳过
/// </summary>
public static class ThinkingModeAdapter
{
    /// <summary>
    /// 返回要注入到请求体 JSON 中的片段（以逗号开头），如：,"thinking":{"type":"enabled"}
    /// 返回 "" 表示不添加任何参数。
    /// </summary>
    public static string GetThinkingJson(AIProvider provider, bool enable, string customUrl)
    {
        switch (provider)
        {
            // ---- 已知 Provider：按官方文档格式 ----
            case AIProvider.DeepSeek:
                // thinking.type = "enabled" | "disabled"
                return enable
                    ? ",\"thinking\":{\"type\":\"enabled\"}"
                    : ",\"thinking\":{\"type\":\"disabled\"}";

            case AIProvider.OpenAI:
                // reasoning_effort = "low"|"medium"|"high"，关闭时不发送
                return enable ? ",\"reasoning_effort\":\"medium\"" : "";

            case AIProvider.Google:
                // Google 的 OpenAI 兼容端点没有标准化的 thinking 请求体参数
                return "";

            case AIProvider.OpenRouter:
                // reasoning.effort / reasoning.enabled
                return enable
                    ? ",\"reasoning\":{\"effort\":\"medium\"}"
                    : ",\"reasoning\":{\"enabled\":false}";

            // ---- 自定义 Provider：按 URL 关键词匹配 ----
            case AIProvider.Custom:
                return ResolveCustom(enable, customUrl);

            default:
                return "";
        }
    }

    private static string ResolveCustom(bool enable, string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return enable ? ",\"thinking\":{\"type\":\"enabled\"}" : "";

        string lower = url.ToLowerInvariant();

        // DeepSeek 官方 & 硅基流动等中转
        if (lower.Contains("deepseek") || lower.Contains("siliconflow"))
            return enable
                ? ",\"thinking\":{\"type\":\"enabled\"}"
                : ",\"thinking\":{\"type\":\"disabled\"}";

        // OpenAI 官方
        if (lower.Contains("api.openai.com"))
            return enable ? ",\"reasoning_effort\":\"medium\"" : "";

        // OpenRouter
        if (lower.Contains("openrouter"))
            return enable
                ? ",\"reasoning\":{\"effort\":\"medium\"}"
                : ",\"reasoning\":{\"enabled\":false}";

        // 阿里云百炼 DashScope：enable_thinking 布尔值
        if (lower.Contains("dashscope") || lower.Contains("aliyun") || lower.Contains("alibabacloud"))
            return enable ? ",\"enable_thinking\":true" : "";

        // 百度智能云千帆：enable_reasoning 布尔值
        if (lower.Contains("qianfan") || lower.Contains("baidubce"))
            return enable ? ",\"enable_reasoning\":true" : "";

        // 火山方舟：thinking.type = "enabled"|"disabled"
        if (lower.Contains("volcengine") || lower.Contains("volces") || lower.Contains("ark.cn"))
            return enable
                ? ",\"thinking\":{\"type\":\"enabled\"}"
                : ",\"thinking\":{\"type\":\"disabled\"}";

        // 华为云 ModelArts：thinking.type = "enabled"|"disabled"
        if (lower.Contains("huawei") || lower.Contains("huaweicloud"))
            return enable
                ? ",\"thinking\":{\"type\":\"enabled\"}"
                : ",\"thinking\":{\"type\":\"disabled\"}";

        // 腾讯云混元：EnableThinking 布尔值（PascalCase）
        if (lower.Contains("hunyuan") || lower.Contains("tencent"))
            return enable ? ",\"EnableThinking\":true" : ",\"EnableThinking\":false";

        // Google / Gemini
        if (lower.Contains("google") || lower.Contains("generativelanguage") || lower.Contains("gemini"))
            return "";

        // 京东云言犀：无 OpenAI 兼容端点，跳过
        if (lower.Contains("jdcloud") || lower.Contains("jd.com"))
            return "";

        // 未知第三方：默认使用 DeepSeek 格式（国内厂商普遍兼容此格式）
        return enable ? ",\"thinking\":{\"type\":\"enabled\"}" : "";
    }
}
