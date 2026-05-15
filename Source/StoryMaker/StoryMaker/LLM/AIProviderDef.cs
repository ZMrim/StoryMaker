using System.Collections.Generic;

namespace StoryMaker;

public enum AIProvider
{
    DeepSeek,
    OpenAI,
    Google,
    OpenRouter,
    Custom
}

public struct ProviderDef
{
    public string Label;
    public string EndpointUrl;
    public string ListModelsUrl;
    public Dictionary<string, string> ExtraHeaders;
}
