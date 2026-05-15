using System.Collections.Generic;

namespace StoryMaker.Snapshot;

public class GameStateSnapshot
{
    public int fromTick;
    public int toTick;
    public string colonyName;
    public int population;
    public float averageMood;
    public float foodDays;
    public float totalWealth;
    public string season;
    public string biome;
    public float currentTemperature;
    public List<FactionRelationEntry> factionRelations;
    public List<RecentEventEntry> recentEvents;
    public List<DeathEntry> recentDeaths;
    public List<DeviationEntry> deviationReport;
}

public class FactionRelationEntry
{
    public string name;
    public int relation;
}

// 近期发生的 Incident — 仅输出事件名和类别
public class RecentEventEntry
{
    public string type;
    public string category;
}

// 近期死亡 — 仅输出角色名
public class DeathEntry
{
    public string pawnName;
}

public class DeviationEntry
{
    public string eventId;
    public string eventType;
    public string failReason;
}
