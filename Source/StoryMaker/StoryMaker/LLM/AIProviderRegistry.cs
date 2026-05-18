using System.Collections.Generic;
using Verse;

namespace StoryMaker;

[StaticConstructorOnStartup]
public static class AIProviderRegistry
{
    public static readonly Dictionary<AIProvider, ProviderDef> Defs = new()
    {
        {
            AIProvider.DeepSeek, new ProviderDef
            {
                Label = "DeepSeek",
                EndpointUrl = "https://api.deepseek.com/v1/chat/completions",
                ListModelsUrl = "https://api.deepseek.com/models"
            }
        },
        {
            AIProvider.OpenAI, new ProviderDef
            {
                Label = "OpenAI",
                EndpointUrl = "https://api.openai.com/v1/chat/completions",
                ListModelsUrl = "https://api.openai.com/v1/models"
            }
        },
        {
            AIProvider.Google, new ProviderDef
            {
                Label = "Google",
                EndpointUrl = "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions",
                ListModelsUrl = "https://generativelanguage.googleapis.com/v1beta/models"
            }
        },
        {
            AIProvider.OpenRouter, new ProviderDef
            {
                Label = "OpenRouter",
                EndpointUrl = "https://openrouter.ai/api/v1/chat/completions",
                ListModelsUrl = "https://openrouter.ai/api/v1/models"
            }
        },
        {
            AIProvider.Custom, new ProviderDef
            {
                Label = "自定义",
                EndpointUrl = "",
                ListModelsUrl = ""
            }
        }
    };

    public static string GetLabel(this AIProvider p)
    {
        string key = $"StoryMaker_Provider_{p}";
        string translated = key.Translate();
        if (translated != key) return translated;
        // 翻译键未找到时回退到 Def Label 或枚举名
        if (Defs.TryGetValue(p, out var def) && !string.IsNullOrEmpty(def.Label))
            return def.Label;
        return p.ToString();
    }

    public static string GetEndpointUrl(this AIProvider p)
    {
        return Defs.TryGetValue(p, out var def) ? def.EndpointUrl : "";
    }

    public static string GetListModelsUrl(this AIProvider p)
    {
        return Defs.TryGetValue(p, out var def) ? def.ListModelsUrl : "";
    }

    public static Dictionary<string, string> GetExtraHeaders(this AIProvider p)
    {
        if (Defs.TryGetValue(p, out var def) && def.ExtraHeaders != null)
            return def.ExtraHeaders;
        return null;
    }

    public static bool RequiresApiKey(this AIProvider p)
    {
        return true;
    }

    static AIProviderRegistry()
    {
        Log.Message($"[StoryMaker] Provider 注册表已加载，共 {Defs.Count} 个提供商。");
    }
}
