using System.Linq;
using RimWorld;
using StoryMaker.Response;
using Verse;

namespace StoryMaker.Action;

// 袭击事件处理：faction + intensity_multiplier + raid_strategy
public class ActionRaidEnemy : IActionHandler
{
    public string EventType => "RaidEnemy";
    public bool IsAllowedInImmediateMode => false;
    public float MaxImmediatePointsMultiplier => 0f;

    public bool Execute(PlannedEvent evt)
    {
        var map = Find.CurrentMap ?? Find.AnyPlayerHomeMap;
        if (map == null) return false;

        IncidentDef incidentDef = IncidentDefOf.RaidEnemy;
        IncidentParms parms = StorytellerUtility.DefaultParmsNow(incidentDef.category, map);

        // 1. 派系
        string factionName = evt.GetStringParam("faction");
        if (!string.IsNullOrEmpty(factionName))
        {
            var faction = Find.FactionManager.AllFactionsListForReading
                .FirstOrDefault(f => f.Name == factionName && f.HostileTo(Faction.OfPlayer));
            if (faction != null)
                parms.faction = faction;
            else
                Log.Message($"[StoryMaker]   LLM 指定派系 '{factionName}' 未找到或非敌对，原版自动选择");
        }

        // 2. 强度乘数
        float intensity = evt.GetFloatParam("intensity_multiplier", 1.0f);
        intensity = UnityEngine.Mathf.Clamp(intensity, 0.5f, 2.0f);
        float originalPoints = parms.points;
        parms.points *= intensity;

        // 3. 袭击策略
        string strategyName = evt.GetStringParam("raid_strategy");
        if (!string.IsNullOrEmpty(strategyName))
        {
            var strategy = DefDatabase<RaidStrategyDef>.GetNamed(strategyName, false);
            // 校验策略存在且点数满足最低要求
            if (strategy != null && parms.points >= GetMinPointsForStrategy(strategy))
            {
                parms.raidStrategy = strategy;
            }
            else
            {
                Log.Message($"[StoryMaker]   LLM 指定策略 '{strategyName}' 不可用（点数={parms.points:F0}），原版自动选择");
            }
        }

        try
        {
            incidentDef.Worker.TryExecute(parms);
            Log.Message($"[StoryMaker] RaidEnemy: faction={parms.faction?.Name ?? "自动"}, points={originalPoints:F0}×{intensity:F2}={parms.points:F0}, strategy={parms.raidStrategy?.defName ?? "自动"}");
            return true;
        }
        catch (System.Exception ex)
        {
            Log.Error($"[StoryMaker] RaidEnemy 执行异常: {ex.Message}");
            return false;
        }
    }

    // 每种策略的最低点数要求（从 XML 中提取的 selectionWeightPerPointsCurve 起点）
    private static float GetMinPointsForStrategy(RaidStrategyDef strategy)
    {
        return strategy.defName switch
        {
            "ImmediateAttack" => 0f,
            "StageThenAttack" => 0f,
            "ImmediateAttackSmart" => 1000f,
            "ImmediateAttackSappers" => 700f,
            "Siege" => 500f,
            "ImmediateAttackBreaching" => 700f,
            "ImmediateAttackBreachingSmart" => 2000f,
            _ => 0f
        };
    }
}
