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
            event_type = defName,
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
        int before = incidentStack.Count;
        var result = new List<RecentEventEntry>(incidentStack);
        incidentStack.Clear();
        Log.Message($"[StoryMaker] PopAllIncidents: 出栈 {result.Count} 条事件 (清空前深度={before})");
        return result;
    }

    public static List<DeathEntry> PopAllDeaths()
    {
        int before = deathStack.Count;
        var result = new List<DeathEntry>(deathStack);
        deathStack.Clear();
        Log.Message($"[StoryMaker] PopAllDeaths: 出栈 {result.Count} 条死亡 (清空前深度={before})");
        return result;
    }

    // 新游戏/读档时清空所有跨存档残留数据
    public static void ClearAll()
    {
        incidentStack.Clear();
        deathStack.Clear();
    }

    // 存档序列化（由 StoryMakerExpose 调用）
    public static void ExposeData()
    {
        Scribe_Collections.Look(ref incidentStack, "incidentStack", LookMode.Deep);
        Scribe_Collections.Look(ref deathStack, "deathStack", LookMode.Deep);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            incidentStack ??= new List<RecentEventEntry>();
            deathStack ??= new List<DeathEntry>();
        }
    }
}
