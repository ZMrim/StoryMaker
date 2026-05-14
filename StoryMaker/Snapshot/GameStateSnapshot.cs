using System.Collections.Generic;

namespace StoryMaker.Snapshot;

public class GameStateSnapshot
{
    // 请求范围
    public int fromTick;
    public int toTick;

    // 殖民地
    public string colonyName;
    public int population;
    public float averageMood;
    public float foodDays;
    public float totalWealth;

    // 环境
    public string season;
    public string biome;
    public float currentTemperature;

    // 派系关系
    public List<FactionRelationEntry> factionRelations;

    // 近期事件
    public List<RecentEventEntry> recentEvents;

    // 叙事反馈
    public List<DeviationEntry> deviationReport;
}

public class FactionRelationEntry
{
    public string name;
    public int relation;
}

public class RecentEventEntry
{
    public string typeLabel;
    public int day;
    public string result;
}

public class DeviationEntry
{
    public string eventId;
    public string eventType;
    public string failReason;
}
