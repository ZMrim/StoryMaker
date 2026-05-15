using System.Collections.Generic;
using Verse;

namespace StoryMaker.Snapshot;

public static class IncidentEventStack
{
    private static List<RecentEventEntry> incidentStack = new();
    private static List<DeathEntry> deathStack = new();

    public static void PushIncident(string defName)
    {
        if (!IncidentWhitelist.IsWhitelisted(defName))
            return;

        incidentStack.Add(new RecentEventEntry
        {
            type = IncidentWhitelist.GetLabel(defName),
            category = IncidentWhitelist.GetCategory(defName)
        });

        Log.Message($"[StoryMaker] 捕获 Incident: {defName} (堆栈深度: {incidentStack.Count})");
    }

    public static void PushDeath(string pawnName)
    {
        if (string.IsNullOrEmpty(pawnName)) return;
        deathStack.Add(new DeathEntry { pawnName = pawnName });
    }

    public static List<RecentEventEntry> PopAllIncidents()
    {
        var result = new List<RecentEventEntry>(incidentStack);
        incidentStack.Clear();
        return result;
    }

    public static List<DeathEntry> PopAllDeaths()
    {
        var result = new List<DeathEntry>(deathStack);
        deathStack.Clear();
        return result;
    }

    // 新游戏/读档时清空所有跨存档残留数据
    public static void ClearAll()
    {
        incidentStack.Clear();
        deathStack.Clear();
    }
}
