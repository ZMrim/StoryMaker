using System.Collections.Generic;
using System.IO;
using System.Text;
using Verse;
using StoryMaker.Snapshot;

namespace StoryMaker;

public static class PromptBuilder
{
    private static string lastLang;
    private static bool lastLowToken;
    private static Dictionary<string, string> cachedCategories;
    private static Dictionary<string, string> cachedKeyEvents;

    public static List<ChatMessage> Build(GameStateSnapshot snapshot, int fromTick, int toTick)
    {
        bool lowToken = StoryMaker.Instance?.Settings?.lowTokenMode ?? false;
        int eventCount = IncidentWhitelist.Count;

        string systemPrompt = BuildSystemPrompt(lowToken);
        string userMessage = BuildUserMessage(snapshot, fromTick, toTick, lowToken);

        Log.Message($"[StoryMaker] Prompt 构建完成: system={systemPrompt.Length} 字符, user={userMessage.Length} 字符, mode={(lowToken ? "lowToken" : "normal")}, 事件类型={eventCount}");

        return new List<ChatMessage>
        {
            new ChatMessage { role = "system", content = systemPrompt },
            new ChatMessage { role = "user", content = userMessage }
        };
    }

    private static string BuildSystemPrompt(bool lowToken)
    {
        string template = PromptTemplates.GetSystemPrompt(lowToken);
        EnsureDataLoaded(lowToken);

        string categories = BuildCategorySection();
        string eventList = BuildEventListSection(lowToken);
        string eventDetails = lowToken ? "" : BuildKeyEventSection();
        string personality = BuildPersonalitySection();
        string injections = PromptInjector.BuildInjectedSection();

        return template
            .Replace("{categories}", categories)
            .Replace("{event_list}", eventList)
            .Replace("{event_details}", eventDetails)
            .Replace("{personality}", personality)
            .Replace("{injections}", injections);
    }

    // ---- 数据加载 ----

    private static void EnsureDataLoaded(bool lowToken)
    {
        const string lang = "CN";  // 始终使用中文数据

        if (lang == lastLang && lowToken == lastLowToken && cachedCategories != null)
            return;

        lastLang = lang;
        lastLowToken = lowToken;
        string root = PromptTemplates.ModRootDir;
        string dataDir = Path.Combine(root ?? "", "Resources", "PromptData", lang);

        string catFile = lowToken ? "Categories_LowToken.txt" : "Categories.txt";
        cachedCategories = LoadKeyValueFile(Path.Combine(dataDir, catFile))
            ?? GetFallbackCategories(lowToken);

        // 低 Token 模式不加载关键事件说明
        if (!lowToken)
            cachedKeyEvents = LoadKeyValueFile(Path.Combine(dataDir, "KeyEvents.txt"))
                ?? GetFallbackKeyEvents();
        else
            cachedKeyEvents = null;
    }

    private static Dictionary<string, string> LoadKeyValueFile(string path)
    {
        if (!File.Exists(path))
        {
            Log.Warning($"[StoryMaker] 提示词数据文件不存在: {path}");
            return null;
        }

        var dict = new Dictionary<string, string>();
        foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
        {
            string trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;
            int colonIdx = trimmed.IndexOf(':');
            if (colonIdx < 0) continue;
            string key = trimmed.Substring(0, colonIdx).Trim();
            string value = trimmed.Substring(colonIdx + 1).Trim();
            if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
                dict[key] = value;
        }
        return dict.Count > 0 ? dict : null;
    }

    private static Dictionary<string, string> GetFallbackCategories(bool lowToken)
    {
        if (lowToken)
            return new() { {"ThreatBig", "大型威胁"}, {"ThreatSmall", "小型威胁"}, {"Misc", "杂项"} };
        return new() { {"ThreatBig", "大型威胁事件（袭击、虫害、机械族等）"}, {"ThreatSmall", "小型威胁事件"}, {"Misc", "杂项事件"} };
    }

    private static Dictionary<string, string> GetFallbackKeyEvents()
    {
        return new() { {"ColdSnap", "寒流"}, {"HeatWave", "热浪"}, {"PsychicDrone", "心灵噪音"}, {"PsychicSoothe", "心灵抚慰波"} };
    }

    // ---- Prompt 节构建 ----

    private static string BuildCategorySection()
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("## 事件类别说明");
        if (cachedCategories != null)
            foreach (var kv in cachedCategories)
                sb.AppendLine($"- {kv.Key}: {kv.Value}");
        return sb.ToString().TrimEnd();
    }

    private static string BuildEventListSection(bool lowToken)
    {
        // 低 Token 模式仍发送事件列表，但不发送 key events 说明
        string list = IncidentWhitelist.FormatEventListForPrompt();
        return "\n" + (string.IsNullOrEmpty(list) ? "" : list);
    }

    private static string BuildKeyEventSection()
    {
        if (cachedKeyEvents == null || cachedKeyEvents.Count == 0) return "";
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("## 重点事件说明");
        foreach (var kv in cachedKeyEvents)
            sb.AppendLine($"- {kv.Key}: {kv.Value}");
        return sb.ToString().TrimEnd();
    }

    internal static string BuildPersonalitySection()
    {
        var settings = StoryMaker.Instance?.Settings;
        if (settings == null) return "";

        string diffContent = PromptTemplates.GetDifficultyPrompt(settings.difficultyLevel.ToString());
        string densContent = PromptTemplates.GetDensityPrompt(settings.densityLevel.ToString());
        string persona = (settings.storytellerPersona ?? "").Trim();

        if (string.IsNullOrWhiteSpace(diffContent) && string.IsNullOrWhiteSpace(densContent) && string.IsNullOrWhiteSpace(persona))
            return "";

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("## 玩家自定义叙事风格");

        if (!string.IsNullOrWhiteSpace(diffContent))
        {
            sb.AppendLine();
            sb.AppendLine("### 叙事难度");
            sb.Append(diffContent.TrimEnd());
        }

        if (!string.IsNullOrWhiteSpace(densContent))
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("### 叙事密度");
            sb.Append(densContent.TrimEnd());
        }

        if (!string.IsNullOrWhiteSpace(persona))
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("### 叙事者人设");
            sb.Append(persona);
        }

        return sb.ToString().TrimEnd();
    }

    private static string BuildUserMessage(GameStateSnapshot snapshot, int fromTick, int toTick, bool lowToken)
    {
        string json = SnapshotSerializer.ToJson(snapshot);

        string tickInstruction = lowToken
            ? $"\n规划tick {fromTick}-{toTick}。scheduled_tick为2500整数倍。勿在文本中提数值。"
            : $"\n请规划 tick {fromTick} 至 tick {toTick} 范围内的事件。每个事件的 scheduled_tick 精确到小时（2500 整倍数）。勿在叙事文本中提及事件点数。";

        string langInstruction = $"\n请使用{PromptTemplates.GetPlayerLanguageName()}回复。";

        return "以下是殖民地状态数据，请据此规划游戏事件：\n\n```json\n" + json + "\n```" + tickInstruction + langInstruction;
    }
}
