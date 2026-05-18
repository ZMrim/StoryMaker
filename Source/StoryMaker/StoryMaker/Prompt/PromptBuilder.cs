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
        bool isChinese = (Prefs.LangFolderName ?? "").StartsWith("Chinese");
        string lang = isChinese ? "CN" : "EN";

        if (lang == lastLang && lowToken == lastLowToken && cachedCategories != null)
            return;

        lastLang = lang;
        lastLowToken = lowToken;
        string root = PromptTemplates.ModRootDir;
        string dataDir = Path.Combine(root ?? "", "Resources", "PromptData", lang);

        string catFile = lowToken ? "Categories_LowToken.txt" : "Categories.txt";
        cachedCategories = LoadKeyValueFile(Path.Combine(dataDir, catFile))
            ?? GetFallbackCategories(lang, lowToken);

        // 低 Token 模式不加载关键事件说明
        if (!lowToken)
            cachedKeyEvents = LoadKeyValueFile(Path.Combine(dataDir, "KeyEvents.txt"))
                ?? GetFallbackKeyEvents(lang);
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

    private static Dictionary<string, string> GetFallbackCategories(string lang, bool lowToken)
    {
        if (lowToken)
        {
            return lang == "CN"
                ? new() { {"ThreatBig", "大型威胁"}, {"ThreatSmall", "小型威胁"}, {"Misc", "杂项"} }
                : new() { {"ThreatBig", "Major threat"}, {"ThreatSmall", "Minor threat"}, {"Misc", "Misc"} };
        }
        return lang == "CN"
            ? new() { {"ThreatBig", "大型威胁事件（袭击、虫害、机械族等）"}, {"ThreatSmall", "小型威胁事件"}, {"Misc", "杂项事件"} }
            : new() { {"ThreatBig", "Major threat events"}, {"ThreatSmall", "Minor threat events"}, {"Misc", "Misc events"} };
    }

    private static Dictionary<string, string> GetFallbackKeyEvents(string lang)
    {
        return lang == "CN"
            ? new() { {"ColdSnap", "寒流"}, {"HeatWave", "热浪"}, {"PsychicDrone", "心灵噪音"}, {"PsychicSoothe", "心灵抚慰波"} }
            : new() { {"ColdSnap", "Cold snap"}, {"HeatWave", "Heat wave"}, {"PsychicDrone", "Psychic drone"}, {"PsychicSoothe", "Psychic soothe"} };
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

    private static string BuildPersonalitySection()
    {
        string personality = StoryMaker.Instance?.Settings?.playerPersonality ?? "";
        if (string.IsNullOrWhiteSpace(personality)) return "";

        // 检测当前语言以确定标题
        bool isChinese = (Prefs.LangFolderName ?? "").StartsWith("Chinese");
        string title = isChinese ? "## 玩家自定义叙事风格" : "## Player-Defined Storyteller Style";

        return $"\n{title}\n{personality.Trim()}";
    }

    private static string BuildUserMessage(GameStateSnapshot snapshot, int fromTick, int toTick, bool lowToken)
    {
        string json = SnapshotSerializer.ToJson(snapshot);

        string tickInstruction = lowToken
            ? $"\n规划tick {fromTick}-{toTick}。scheduled_tick为2500整数倍。勿在文本中提数值。"
            : $"\n请规划 tick {fromTick} 至 tick {toTick} 范围内的事件。每个事件的 scheduled_tick 精确到小时（2500 整倍数）。勿在叙事文本中提及事件点数。";

        return "以下是殖民地状态数据，请据此规划游戏事件：\n\n```json\n" + json + "\n```" + tickInstruction;
    }
}
