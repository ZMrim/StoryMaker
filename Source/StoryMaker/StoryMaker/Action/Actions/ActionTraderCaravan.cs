using System.Linq;
using RimWorld;
using StoryMaker.Response;
using Verse;

namespace StoryMaker.Action;

// 商队抵达 + 轨道商：faction（可选）+ trader_kind（可选）
// 覆盖 TraderCaravanArrival / OrbitalTraderArrival
public class ActionTraderCaravan : IActionHandler
{
    public string EventType => "TraderCaravanArrival";  // 主入口，实际根据 event_type 分发
    public bool IsAllowedInImmediateMode => true;
    public float MaxImmediatePointsMultiplier => 0.5f;

    public bool Execute(PlannedEvent evt)
    {
        var map = Find.CurrentMap ?? Find.AnyPlayerHomeMap;
        if (map == null) return false;

        IncidentParms parms = StorytellerUtility.DefaultParmsNow(evt.resolvedDef.category, map);

        // 1. 派系（LLM 返回 defName，通过 FactionDef 精确匹配）
        string factionDefName = evt.GetStringParam("faction");
        if (!string.IsNullOrEmpty(factionDefName))
        {
            var factionDef = DefDatabase<FactionDef>.GetNamed(factionDefName, false);
            if (factionDef != null)
            {
                var faction = Find.FactionManager.AllFactionsListForReading
                    .FirstOrDefault(f => f.def == factionDef && !f.HostileTo(Faction.OfPlayer) && !f.IsPlayer);
                if (faction != null)
                    parms.faction = faction;
            }
        }

        // 2. 商队类型（可选）
        string traderKind = evt.GetStringParam("trader_kind");
        if (!string.IsNullOrEmpty(traderKind))
        {
            var tk = DefDatabase<TraderKindDef>.GetNamed(traderKind, false);
            if (tk != null)
                parms.traderKind = tk;
        }

        try
        {
            if (!evt.resolvedDef.Worker.TryExecute(parms))
            {
                Log.Warning($"[StoryMaker] Trader: 自定义参数执行失败 ({evt.event_type}, faction={parms.faction?.Name ?? "自动"}, trader={parms.traderKind?.defName ?? "自动"})，尝试原版默认参数...");
                IncidentParms fallbackParms = StorytellerUtility.DefaultParmsNow(evt.resolvedDef.category, map);
                if (!evt.resolvedDef.Worker.TryExecute(fallbackParms))
                {
                    Log.Error($"[StoryMaker] Trader: 原版默认参数也执行失败 ({evt.event_type})");
                    return false;
                }
                Log.Message($"[StoryMaker] Trader: 原版默认参数执行成功 (回退, {evt.event_type})");
                return true;
            }
            Log.Message($"[StoryMaker] Trader: {evt.event_type}, faction={parms.faction?.Name ?? "自动"}, trader={parms.traderKind?.defName ?? "自动"}");
            return true;
        }
        catch (System.Exception ex)
        {
            Log.Error($"[StoryMaker] Trader 执行异常 ({evt.event_type}): {ex.Message}");
            return false;
        }
    }
}
