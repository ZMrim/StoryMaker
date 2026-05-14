using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace StoryMaker.Snapshot;

// ── 殖民地字段 ──

class SnapshotField_ColonyName : ISnapshotField
{
    public string Key => "colonyName";
    public bool IncludeInLowTokenMode => true;
    public object Collect()
    {
        var map = Find.CurrentMap;
        if (map == null) return "Unknown";
        return map.info?.parent?.Label ?? "Unknown";
    }
}

class SnapshotField_Population : ISnapshotField
{
    public string Key => "population";
    public bool IncludeInLowTokenMode => true;
    public object Collect()
    {
        return PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists.Count;
    }
}

class SnapshotField_AverageMood : ISnapshotField
{
    public string Key => "averageMood";
    public bool IncludeInLowTokenMode => true;
    public object Collect()
    {
        var colonists = PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists;
        if (colonists.Count == 0) return 0.5f;
        float sum = 0f;
        int count = 0;
        foreach (var pawn in colonists)
        {
            if (pawn.needs?.mood != null)
            {
                sum += pawn.needs.mood.CurLevel;
                count++;
            }
        }
        return count > 0 ? sum / count : 0.5f;
    }
}

class SnapshotField_FoodDays : ISnapshotField
{
    public string Key => "foodDays";
    public bool IncludeInLowTokenMode => true;
    public object Collect()
    {
        var maps = Find.Maps;
        float totalNutrition = 0f;
        int colonistCount = 0;

        foreach (var map in maps)
        {
            if (!map.IsPlayerHome) continue;

            foreach (var thing in map.listerThings.AllThings)
            {
                if (thing.def.IsIngestible && thing.def.ingestible.HumanEdible && !(thing is Plant))
                    totalNutrition += thing.GetStatValue(StatDefOf.Nutrition) * thing.stackCount;
            }

            colonistCount += map.mapPawns.FreeColonistsCount;
        }

        // 成年殖民者每天消耗约 1.6 营养（2 顿正餐 × 0.9）
        float dailyConsumption = colonistCount * 1.6f;
        if (dailyConsumption <= 0f) return 999f;
        return totalNutrition / dailyConsumption;
    }
}

class SnapshotField_TotalWealth : ISnapshotField
{
    public string Key => "totalWealth";
    public bool IncludeInLowTokenMode => true;
    public object Collect()
    {
        var map = Find.CurrentMap;
        if (map == null) return 0f;
        return map.wealthWatcher?.WealthTotal ?? 0f;
    }
}

// ── 环境字段 ──

class SnapshotField_Season : ISnapshotField
{
    public string Key => "season";
    public bool IncludeInLowTokenMode => true;
    public object Collect()
    {
        var map = Find.CurrentMap;
        if (map == null) return "Unknown";
        return GenLocalDate.Season(map).ToString();
    }
}

class SnapshotField_Biome : ISnapshotField
{
    public string Key => "biome";
    public bool IncludeInLowTokenMode => true;
    public object Collect()
    {
        var map = Find.CurrentMap;
        if (map == null) return "Unknown";
        return map.Biome?.label ?? "Unknown";
    }
}

class SnapshotField_Temperature : ISnapshotField
{
    public string Key => "currentTemperature";
    public bool IncludeInLowTokenMode => true;
    public object Collect()
    {
        var map = Find.CurrentMap;
        if (map == null) return 0f;
        return map.mapTemperature?.OutdoorTemp ?? 0f;
    }
}

// ── 派系关系字段 ──

class SnapshotField_FactionRelations : ISnapshotField
{
    public string Key => "factionRelations";
    public bool IncludeInLowTokenMode => true;
    public object Collect()
    {
        var result = new List<FactionRelationEntry>();
        var playerFaction = Find.FactionManager?.OfPlayer;
        if (playerFaction == null) return result;

        foreach (var faction in Find.FactionManager.AllFactionsListForReading)
        {
            if (faction == playerFaction || faction.IsPlayer || faction.Hidden)
                continue;
            // 排除机械族和虫族——它们始终是固定敌对，对叙事无参考价值
            if (faction.def == FactionDefOf.Mechanoid || faction.def == FactionDefOf.Insect)
                continue;

            result.Add(new FactionRelationEntry
            {
                name = faction.Name,
                relation = faction.GoodwillWith(playerFaction)
            });
        }

        return result;
    }
}

// ── 近期事件字段 ──

class SnapshotField_RecentEvents : ISnapshotField
{
    public string Key => "recentEvents";
    public bool IncludeInLowTokenMode => false;

    private static HashSet<int> extractedLetterIds = new();
    private const int MaxRecentEvents = 10;
    private const int LookbackTicks = 300000; // 5 天

    public object Collect()
    {
        var result = new List<RecentEventEntry>();
        int curTick = Find.TickManager.TicksGame;
        var archive = Find.Archive;
        if (archive == null) return result;

        var letters = archive.ArchivablesListForReading
            .OfType<Letter>()
            .Where(l => l.arrivalTick >= curTick - LookbackTicks
                     && l.arrivalTick <= curTick)
            .OrderByDescending(l => l.arrivalTick)
            .ToList();

        int count = 0;
        foreach (var letter in letters)
        {
            if (count >= MaxRecentEvents) break;
            if (extractedLetterIds.Contains(letter.ID)) continue;
            if (!IsNarrativelySignificant(letter)) continue;

            extractedLetterIds.Add(letter.ID);
            string labelText = letter.Label.ToString();
            if (string.IsNullOrEmpty(labelText))
                labelText = letter.def?.label ?? "Unknown";
            result.Add(new RecentEventEntry
            {
                typeLabel = labelText,
                // arrivalTick 是游戏内tick（从殖民地建立开始计数），除以60000得到天数
                day = letter.arrivalTick / 60000,
                result = "触发"
            });
            count++;
        }

        CleanupExtractedIds(letters);
        return result;
    }

    private static bool IsNarrativelySignificant(Letter letter)
    {
        // Phase 1: 粗糙过滤，Phase 3 结合 LetterDef 精确判断
        if (letter.def == LetterDefOf.NeutralEvent) return false;
        return true;
    }

    private static void CleanupExtractedIds(List<Letter> currentLetters)
    {
        var currentIds = new HashSet<int>(currentLetters.Select(l => l.ID));
        extractedLetterIds.RemoveWhere(id => !currentIds.Contains(id));
    }
}

// ── 叙事反馈字段（Phase 1 占位）──

class SnapshotField_DeviationReport : ISnapshotField
{
    public string Key => "deviationReport";
    public bool IncludeInLowTokenMode => false;
    public object Collect()
    {
        return new List<DeviationEntry>();
    }
}
