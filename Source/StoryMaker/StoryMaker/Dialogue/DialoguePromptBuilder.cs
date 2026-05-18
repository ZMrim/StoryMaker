using System.Collections.Generic;
using System.Text;
using RimWorld;
using StoryMaker.Core;
using StoryMaker.Snapshot;
using Verse;

namespace StoryMaker.Dialogue;

// 对话专用 Prompt 构建：轻量殖民地数据 + 对话历史 + 玩家输入
public static class DialoguePromptBuilder
{
    // 对话历史最大保留轮数
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
        var state = StoryMakerState.Instance;
        var settings = StoryMaker.Instance?.Settings;

        // 事件列表（复用调度用的全量事件清单）
        string eventList = BuildEventListForPrompt();

        // 玩家个性化
        string personality = "";
        if (!string.IsNullOrEmpty(settings?.playerPersonality))
        {
            personality = "## 玩家自定义叙事风格\n\n" + settings.playerPersonality;
        }
        else
        {
            personality = "## 玩家自定义叙事风格\n\n你是一个经典的 RimWorld 叙事者。";
        }

        // 外接注入
        string injections = PromptInjector.BuildInjectedSection();

        return template
            .Replace("{event_list}", eventList)
            .Replace("{personality}", personality)
            .Replace("{injections}", injections);
    }

    private static string BuildEventListForPrompt()
    {
        return IncidentWhitelist.FormatEventListForPrompt();
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
                sb.AppendLine($"殖民者: {entry.playerText}");
                sb.AppendLine($"叙事者: {entry.narratorText}");
                if (entry.hadEvent)
                    sb.AppendLine($"(此轮对话触发了事件: {entry.triggeredEventType})");
                sb.AppendLine();
            }
        }

        // 当前殖民地状态（轻量版）
        sb.AppendLine("## 当前殖民地状态");
        sb.AppendLine(BuildColonySnapshot(curTick));
        sb.AppendLine();

        // 玩家输入
        sb.AppendLine($"殖民者对你说: \"{playerInput}\"");
        sb.AppendLine();
        sb.AppendLine("请以叙事者身份回复。");

        return sb.ToString();
    }

    private static string BuildColonySnapshot(int curTick)
    {
        var map = Find.CurrentMap ?? Find.AnyPlayerHomeMap;
        if (map == null) return "无法获取殖民地数据。";

        var sb = new StringBuilder();

        // colony
        var colonists = map.mapPawns.FreeColonists;
        int pop = colonists?.Count ?? 0;
        float mood = 0.5f;
        if (pop > 0)
        {
            float totalMood = 0f;
            foreach (var c in colonists)
            {
                if (c.needs?.mood != null)
                    totalMood += c.needs.mood.CurLevel;
            }
            mood = totalMood / pop;
        }
        // 计算食物天数
        float totalNutrition = 0f;
        foreach (var thing in map.listerThings.AllThings)
        {
            if (thing.def.IsIngestible && thing.def.ingestible.HumanEdible && !(thing is Plant))
                totalNutrition += thing.GetStatValue(StatDefOf.Nutrition) * thing.stackCount;
        }
        float foodDays = pop > 0 ? totalNutrition / (pop * 1.6f) : 999f;
        float wealth = map.wealthWatcher?.WealthTotal ?? 0;

        sb.AppendLine($"colony: name=\"{map.Parent?.Label ?? "殖民地"}\", population={pop}, average_mood={mood:F2}, food_days={foodDays:F1}, total_wealth={wealth:F0}");

        // environment
        string season = GenLocalDate.Season(map).ToString();
        string biome = map.Biome?.LabelCap ?? "未知";
        float temp = map.mapTemperature?.OutdoorTemp ?? 0;
        sb.AppendLine($"environment: season={season}, biome={biome}, current_temperature={temp:F1}°C");

        // faction_relations
        var factions = Find.FactionManager?.AllFactionsListForReading;
        if (factions != null && factions.Count > 0)
        {
            sb.Append("faction_relations: [");
            bool first = true;
            foreach (var f in factions)
            {
                if (f.IsPlayer || f.defeated || f.def?.permanentEnemy == true) continue;
                if (!first) sb.Append(", ");
                sb.Append($"{{name=\"{f.Name}\", relation={f.PlayerRelationKind}}}");
                first = false;
            }
            sb.AppendLine("]");
        }

        // current_day
        int day = curTick / 60000;
        sb.AppendLine($"current_day: 第 {day} 天");

        return sb.ToString();
    }
}
