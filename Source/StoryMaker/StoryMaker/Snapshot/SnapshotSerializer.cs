using System.Collections.Generic;
using System.Text;
using StoryMaker.Response;

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
        sb.AppendLine($"    \"name\": \"{JsonExtractor.EscapeJsonString(snapshot.colonyName)}\",");
        sb.AppendLine($"    \"population\": {snapshot.population},");
        sb.AppendLine($"    \"average_mood\": {snapshot.averageMood:F2},");
        sb.AppendLine($"    \"food_days\": {snapshot.foodDays:F1},");
        sb.AppendLine($"    \"total_wealth\": {snapshot.totalWealth:F0}");
        sb.AppendLine("  },");

        // environment
        sb.AppendLine("  \"environment\": {");
        sb.AppendLine($"    \"season\": \"{JsonExtractor.EscapeJsonString(snapshot.season)}\",");
        sb.AppendLine($"    \"biome\": \"{JsonExtractor.EscapeJsonString(snapshot.biome)}\",");
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
                sb.AppendLine($"    {{ \"def_name\": \"{JsonExtractor.EscapeJsonString(fr.defName)}\", \"label\": \"{JsonExtractor.EscapeJsonString(fr.label)}\", \"relation\": {fr.relation}, \"relation_kind\": \"{fr.relationKind}\" }}{comma}");
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
                sb.AppendLine($"    {{ \"event_type\": \"{JsonExtractor.EscapeJsonString(re.event_type)}\", \"category\": \"{JsonExtractor.EscapeJsonString(re.category)}\" }}{comma}");
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
                sb.AppendLine($"    {{ \"pawn_name\": \"{JsonExtractor.EscapeJsonString(de.pawnName)}\" }}{comma}");
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
                sb.AppendLine($"    {{ \"event_id\": \"{JsonExtractor.EscapeJsonString(de.eventId)}\", \"event_type\": \"{JsonExtractor.EscapeJsonString(de.eventType)}\", \"fail_reason\": \"{JsonExtractor.EscapeJsonString(de.failReason)}\" }}{comma}");
            }
        }
        sb.AppendLine("  ]");
    }

}
