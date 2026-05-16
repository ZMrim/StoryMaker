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
    public string relationKind;  // Hostile / Neutral / Ally
}

// 近期发生的 Incident — 输出 defName 和 category，与 LLM 回复的 event_type 保持一致
public class RecentEventEntry
{
    public string event_type;  // defName，如 RaidEnemy、PsychicDrone
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
