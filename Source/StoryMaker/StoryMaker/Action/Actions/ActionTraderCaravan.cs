using System.Linq;
using RimWorld;
using StoryMaker.Response;
using Verse;

namespace StoryMaker.Action;

// 商队抵达：faction（可选）+ trader_kind（可选）
public class ActionTraderCaravan : IActionHandler
{
    public string EventType => "TraderCaravanArrival";
    public bool IsAllowedInImmediateMode => true;
    public float MaxImmediatePointsMultiplier => 0.5f;

    public bool Execute(PlannedEvent evt)
    {
        var map = Find.CurrentMap ?? Find.AnyPlayerHomeMap;
        if (map == null) return false;

        IncidentDef incidentDef = IncidentDefOf.TraderCaravanArrival;
        IncidentParms parms = StorytellerUtility.DefaultParmsNow(incidentDef.category, map);

        // 1. 派系（可选）
        string factionName = evt.GetStringParam("faction");
        if (!string.IsNullOrEmpty(factionName))
        {
            var faction = Find.FactionManager.AllFactionsListForReading
                .FirstOrDefault(f => f.Name == factionName && !f.HostileTo(Faction.OfPlayer) && !f.IsPlayer);
            if (faction != null)
                parms.faction = faction;
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
            incidentDef.Worker.TryExecute(parms);
            Log.Message($"[StoryMaker] TraderCaravan: faction={parms.faction?.Name ?? "自动"}, trader={parms.traderKind?.defName ?? "自动"}");
            return true;
        }
        catch (System.Exception ex)
        {
            Log.Error($"[StoryMaker] TraderCaravan 执行异常: {ex.Message}");
            return false;
        }
    }
}
