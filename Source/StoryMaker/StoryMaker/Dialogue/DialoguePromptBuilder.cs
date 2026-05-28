using System.Collections.Generic;
using System.Text;
using RimWorld;
using StoryMaker.Core;
using StoryMaker.Snapshot;
using Verse;

namespace StoryMaker.Dialogue;

// 对话专用 Prompt 构建：通过 SnapshotCollector 获取殖民地数据 + 对话历史 + 玩家输入
public static class DialoguePromptBuilder
{
    private const int MaxDialogueHistory = 5;

    public static List<ChatMessage> Build(string playerInput, List<DialogueEntry> history,
        int curTick)
    {
        var messages = new List<ChatMessage>();

        // 1. System Prompt
        string systemPrompt = BuildSystemPrompt();
        messages.Add(new ChatMessage { role = "system", content = systemPrompt });

        // 2. User Message（对话历史 + 当前状态 + 玩家输入）
        string userContent = BuildUserContent(playerInput, history, curTick);
        messages.Add(new ChatMessage { role = "user", content = userContent });

        return messages;
    }

    private static string BuildSystemPrompt()
    {
        string template = PromptTemplates.GetDialogueSystemPrompt();

        string eventList = IncidentWhitelist.FormatEventListForPrompt();

        string personality = PromptBuilder.BuildPersonalitySection();
        if (string.IsNullOrEmpty(personality))
            personality = "## 玩家自定义叙事风格\n\n你是一个经典的 RimWorld 叙事者。";

        string injections = PromptInjector.BuildInjectedSection();

        return template
            .Replace("{event_list}", eventList)
            .Replace("{personality}", personality)
            .Replace("{injections}", injections);
    }

    private static string BuildUserContent(string playerInput, List<DialogueEntry> history,
        int curTick)
    {
        var sb = new StringBuilder();

        // 对话历史
        if (history != null && history.Count > 0)
        {
            sb.AppendLine("## 最近的对话");
            sb.AppendLine();

            int start = history.Count > MaxDialogueHistory ? history.Count - MaxDialogueHistory : 0;
            for (int i = start; i < history.Count; i++)
            {
                var entry = history[i];
                // 跳过未收到回复的进行中消息（玩家刚发但 LLM 尚未回复）
                if (string.IsNullOrEmpty(entry.narratorText)) continue;
                sb.AppendLine($"殖民者: {entry.playerText}");
                sb.AppendLine($"叙事者: {entry.narratorText}");
                if (entry.hadEvent)
                    sb.AppendLine($"(此轮对话触发了事件: {entry.triggeredEventType})");
                sb.AppendLine();
            }
        }

        // 当前殖民地状态（通过统一快照工具采集）
        sb.AppendLine("## 当前殖民地状态");
        sb.AppendLine(BuildColonySnapshot(curTick));
        sb.AppendLine();

        var settings = StoryMaker.Instance?.Settings;
        string narratorName = (settings?.narratorName ?? "").Trim();
        string nameLabel = string.IsNullOrWhiteSpace(narratorName) ? "叙事者" : narratorName;

        sb.AppendLine($"殖民者对你说: \"{playerInput}\"");
        sb.AppendLine();
        sb.AppendLine($"请以{nameLabel}的身份回复。请使用{PromptTemplates.GetPlayerLanguageName()}。");

        return sb.ToString();
    }

    // 使用 SnapshotCollector 统一采集殖民地数据，自行格式化为对话专用轻量文本
    private static string BuildColonySnapshot(int curTick)
    {
        var snap = SnapshotCollector.CollectColonyState();
        var sb = new StringBuilder();

        // colony
        sb.AppendLine($"colony: name=\"{snap.colonyName}\", population={snap.population}, average_mood={snap.averageMood:F2}, food_days={snap.foodDays:F1}, total_wealth={snap.totalWealth:F0}");

        // environment
        sb.AppendLine($"environment: season={snap.season}, biome={snap.biome}, current_temperature={snap.currentTemperature:F1}°C");

        // faction_relations（摘要格式：defName + relation_kind）
        if (snap.factionRelations != null && snap.factionRelations.Count > 0)
        {
            sb.Append("faction_relations: [");
            bool first = true;
            foreach (var fr in snap.factionRelations)
            {
                if (!first) sb.Append(", ");
                sb.Append($"{{def_name=\"{fr.defName}\", label=\"{fr.label}\", relation={fr.relationKind}}}");
                first = false;
            }
            sb.AppendLine("]");
        }

        int day = curTick / 60000;
        sb.AppendLine($"current_day: 第 {day} 天");

        return sb.ToString();
    }
}
