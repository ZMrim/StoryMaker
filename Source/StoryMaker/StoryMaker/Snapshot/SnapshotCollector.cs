using System.Collections.Generic;
using Verse;

namespace StoryMaker.Snapshot;

public static class SnapshotCollector
{
    private static List<ISnapshotField> fields = new();

    static SnapshotCollector()
    {
        fields.Add(new SnapshotField_ColonyName());
        fields.Add(new SnapshotField_Population());
        fields.Add(new SnapshotField_AverageMood());
        fields.Add(new SnapshotField_FoodDays());
        fields.Add(new SnapshotField_TotalWealth());
        fields.Add(new SnapshotField_Season());
        fields.Add(new SnapshotField_Biome());
        fields.Add(new SnapshotField_Temperature());
        fields.Add(new SnapshotField_FactionRelations());
        fields.Add(new SnapshotField_RecentEvents());
        fields.Add(new SnapshotField_RecentDeaths());
    }

    public static GameStateSnapshot Collect(int fromTick, int toTick)
    {
        var snapshot = CollectBaseFields();
        snapshot.fromTick = fromTick;
        snapshot.toTick = toTick;

        bool lowTokenMode = StoryMaker.Instance?.Settings?.lowTokenMode ?? false;

        // 消费型字段（需要 Pop 的）
        foreach (var field in fields)
        {
            if (field.Key != "recentEvents" && field.Key != "recentDeaths")
                continue;
            if (lowTokenMode && !field.IncludeInLowTokenMode)
                continue;
            try { ApplyField(snapshot, field); }
            catch (System.Exception ex) { Log.Warning($"[StoryMaker] 快照字段 '{field.Key}' 采集失败: {ex.Message}"); }
        }

        return snapshot;
    }

    // 采集不消耗事件栈的殖民地基础数据（colony / environment / faction），
    // 供对话模块等无需 tick 范围的场景复用。
    public static GameStateSnapshot CollectColonyState()
    {
        return CollectBaseFields();
    }

    private static GameStateSnapshot CollectBaseFields()
    {
        var snapshot = new GameStateSnapshot
        {
            factionRelations = new List<FactionRelationEntry>(),
            recentEvents = new List<RecentEventEntry>(),
            recentDeaths = new List<DeathEntry>(),
            deviationReport = new List<DeviationEntry>()
        };

        foreach (var field in fields)
        {
            if (field.Key == "recentEvents" || field.Key == "recentDeaths")
                continue;
            try { ApplyField(snapshot, field); }
            catch (System.Exception ex) { Log.Warning($"[StoryMaker] 快照字段 '{field.Key}' 采集失败: {ex.Message}"); }
        }

        return snapshot;
    }

    private static void ApplyField(GameStateSnapshot snapshot, ISnapshotField field)
    {
        var value = field.Collect();
        switch (field.Key)
        {
            case "colonyName": snapshot.colonyName = (string)value; break;
            case "population": snapshot.population = (int)value; break;
            case "averageMood": snapshot.averageMood = (float)value; break;
            case "foodDays": snapshot.foodDays = (float)value; break;
            case "totalWealth": snapshot.totalWealth = (float)value; break;
            case "season": snapshot.season = (string)value; break;
            case "biome": snapshot.biome = (string)value; break;
            case "currentTemperature": snapshot.currentTemperature = (float)value; break;
            case "factionRelations": snapshot.factionRelations = (List<FactionRelationEntry>)value; break;
            case "recentEvents": snapshot.recentEvents = (List<RecentEventEntry>)value; break;
            case "recentDeaths": snapshot.recentDeaths = (List<DeathEntry>)value; break;
        }
    }

    public static void RegisterField(ISnapshotField field) => fields.Add(field);
    public static void UnregisterField(ISnapshotField field) => fields.Remove(field);
}
