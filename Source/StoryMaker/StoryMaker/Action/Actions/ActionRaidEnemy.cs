using System.Linq;
using RimWorld;
using StoryMaker.Response;
using Verse;

namespace StoryMaker.Action;

// 袭击事件处理：faction + intensity_multiplier + raid_strategy
// 覆盖 RaidEnemy / RaidFriendly
public class ActionRaidEnemy : IActionHandler
{
    public string EventType => "RaidEnemy";  // 主入口，实际根据 event_type 分发
    public bool IsAllowedInImmediateMode => false;
    public float MaxImmediatePointsMultiplier => 0f;

    public bool Execute(PlannedEvent evt)
    {
        var map = Find.CurrentMap ?? Find.AnyPlayerHomeMap;
        if (map == null) return false;

        IncidentParms parms = StorytellerUtility.DefaultParmsNow(evt.resolvedDef.category, map);

        bool isFriendly = evt.event_type == "RaidFriendly";

        // 派系（LLM 返回 defName，通过 FactionDef 精确匹配）
        string factionDefName = evt.GetStringParam("faction");
        if (!string.IsNullOrEmpty(factionDefName))
        {
            var factionDef = DefDatabase<FactionDef>.GetNamed(factionDefName, false);
            if (factionDef != null)
            {
                var faction = Find.FactionManager.AllFactionsListForReading
                    .FirstOrDefault(f =>
                    {
                        if (f.def != factionDef) return false;
                        return isFriendly ? !f.HostileTo(Faction.OfPlayer) : f.HostileTo(Faction.OfPlayer);
                    });
                if (faction != null && !faction.IsPlayer)
                    parms.faction = faction;
                else
                    Log.Message($"[StoryMaker]   LLM 指定派系 '{factionDefName}' 当前不匹配 ({evt.event_type})，原版自动选择");
            }
            else
                Log.Message($"[StoryMaker]   LLM 指定派系 defName='{factionDefName}' 不在 FactionDef 数据库中，原版自动选择");
        }

        // 强度乘数
        float intensity = evt.GetFloatParam("intensity_multiplier", 1.0f);
        intensity = UnityEngine.Mathf.Clamp(intensity, 0.5f, 2.0f);
        float originalPoints = parms.points;
        parms.points *= intensity;

        // 袭击策略（仅 RaidEnemy 支持）
        if (!isFriendly)
        {
            string strategyName = evt.GetStringParam("raid_strategy");
            if (!string.IsNullOrEmpty(strategyName))
            {
                var strategy = DefDatabase<RaidStrategyDef>.GetNamed(strategyName, false);
                if (strategy != null && parms.points >= GetMinPointsForStrategy(strategy))
                {
                    parms.raidStrategy = strategy;
                }
                else
                {
                    Log.Message($"[StoryMaker]   LLM 指定策略 '{strategyName}' 不可用（点数={parms.points:F0}），原版自动选择");
                }
            }
        }

        try
        {
            if (!evt.resolvedDef.Worker.TryExecute(parms))
            {
                Log.Warning($"[StoryMaker] Raid: 自定义参数执行失败 ({evt.event_type}, faction={parms.faction?.Name ?? "自动"}, points={parms.points:F0}, strategy={parms.raidStrategy?.defName ?? "自动"})，尝试原版默认参数...");
                IncidentParms fallbackParms = StorytellerUtility.DefaultParmsNow(evt.resolvedDef.category, map);
                if (!evt.resolvedDef.Worker.TryExecute(fallbackParms))
                {
                    Log.Error($"[StoryMaker] Raid: 原版默认参数也执行失败 ({evt.event_type})");
                    return false;
                }
                Log.Message($"[StoryMaker] Raid: 原版默认参数执行成功 (回退, {evt.event_type})");
                return true;
            }
            Log.Message($"[StoryMaker] Raid: {evt.event_type}, faction={parms.faction?.Name ?? "自动"}, points={originalPoints:F0}×{intensity:F2}={parms.points:F0}, strategy={parms.raidStrategy?.defName ?? "自动"}");
            return true;
        }
        catch (System.Exception ex)
        {
            Log.Error($"[StoryMaker] Raid 执行异常 ({evt.event_type}): {ex.Message}");
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
