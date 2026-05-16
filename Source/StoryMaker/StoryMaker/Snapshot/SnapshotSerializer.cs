using System;
using System.Collections.Generic;
using System.Text;
using Verse;

namespace StoryMaker.Snapshot;

public static class SnapshotSerializer
{
    public static string ToJson(GameStateSnapshot snapshot)
    {
        var sb = new StringBuilder();
        sb.AppendLine("{");

        // request_range
        sb.AppendLine("  \"request_range\": {");
        sb.AppendLine($"    \"from_tick\": {snapshot.fromTick},");
        sb.AppendLine($"    \"to_tick\": {snapshot.toTick}");
        sb.AppendLine("  },");

        // colony
        sb.AppendLine("  \"colony\": {");
        sb.AppendLine($"    \"name\": \"{Escape(snapshot.colonyName)}\",");
        sb.AppendLine($"    \"population\": {snapshot.population},");
        sb.AppendLine($"    \"average_mood\": {snapshot.averageMood:F2},");
        sb.AppendLine($"    \"food_days\": {snapshot.foodDays:F1},");
        sb.AppendLine($"    \"total_wealth\": {snapshot.totalWealth:F0}");
        sb.AppendLine("  },");

        // environment
        sb.AppendLine("  \"environment\": {");
        sb.AppendLine($"    \"season\": \"{Escape(snapshot.season)}\",");
        sb.AppendLine($"    \"biome\": \"{Escape(snapshot.biome)}\",");
        sb.AppendLine($"    \"current_temperature\": {snapshot.currentTemperature:F1}");
        sb.AppendLine("  },");

        // faction_relations
        WriteFactionRelations(sb, snapshot.factionRelations);

        // recent_events (from Incident stack)
        WriteRecentEvents(sb, snapshot.recentEvents);

        // recent_deaths (from Death letter stack)
        WriteRecentDeaths(sb, snapshot.recentDeaths);

        // deviation_report
        WriteDeviationReport(sb, snapshot.deviationReport);

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void WriteFactionRelations(StringBuilder sb, List<FactionRelationEntry> list)
    {
        sb.AppendLine("  \"faction_relations\": [");
        if (list != null && list.Count > 0)
        {
            for (int i = 0; i < list.Count; i++)
            {
                var fr = list[i];
                string comma = i < list.Count - 1 ? "," : "";
                sb.AppendLine($"    {{ \"name\": \"{Escape(fr.name)}\", \"relation\": {fr.relation}, \"relation_kind\": \"{Escape(fr.relationKind)}\" }}{comma}");
            }
        }
        sb.AppendLine("  ],");
    }

    private static void WriteRecentEvents(StringBuilder sb, List<RecentEventEntry> list)
    {
        sb.AppendLine("  \"recent_events\": [");
        if (list != null && list.Count > 0)
        {
            for (int i = 0; i < list.Count; i++)
            {
                var re = list[i];
                string comma = i < list.Count - 1 ? "," : "";
                sb.AppendLine($"    {{ \"event_type\": \"{Escape(re.event_type)}\", \"category\": \"{Escape(re.category)}\" }}{comma}");
            }
        }
        sb.AppendLine("  ],");
    }

    private static void WriteRecentDeaths(StringBuilder sb, List<DeathEntry> list)
    {
        sb.AppendLine("  \"recent_deaths\": [");
        if (list != null && list.Count > 0)
        {
            for (int i = 0; i < list.Count; i++)
            {
                var de = list[i];
                string comma = i < list.Count - 1 ? "," : "";
                sb.AppendLine($"    {{ \"pawn_name\": \"{Escape(de.pawnName)}\" }}{comma}");
            }
        }
        sb.AppendLine("  ],");
    }

    private static void WriteDeviationReport(StringBuilder sb, List<DeviationEntry> list)
    {
        sb.AppendLine("  \"deviation_report\": [");
        if (list != null && list.Count > 0)
        {
            for (int i = 0; i < list.Count; i++)
            {
                var de = list[i];
                string comma = i < list.Count - 1 ? "," : "";
                sb.AppendLine($"    {{ \"event_id\": \"{Escape(de.eventId)}\", \"event_type\": \"{Escape(de.eventType)}\", \"fail_reason\": \"{Escape(de.failReason)}\" }}{comma}");
            }
        }
        sb.AppendLine("  ]");
    }

    private static string Escape(string str)
    {
        if (string.IsNullOrEmpty(str)) return "";
        return str.Replace("\\", "\\\\")
                  .Replace("\"", "\\\"")
                  .Replace("\n", "\\n")
                  .Replace("\r", "\\r")
                  .Replace("\t", "\\t");
    }
}
