using RimWorld;
using StoryMaker.Response;
using Verse;

namespace StoryMaker.Action;

// 心灵噪音/抚慰：无 LLM 参数，全部原版自动
public class ActionPsychicDrone : IActionHandler
{
    public string EventType => "PsychicDrone";  // 统一入口
    public bool IsAllowedInImmediateMode => true;
    public float MaxImmediatePointsMultiplier => 0.3f;

    public bool Execute(PlannedEvent evt)
    {
        var map = Find.CurrentMap ?? Find.AnyPlayerHomeMap;
        if (map == null) return false;

        IncidentParms parms = StorytellerUtility.DefaultParmsNow(evt.resolvedDef.category, map);

        try
        {
            if (!evt.resolvedDef.Worker.TryExecute(parms))
            {
                Log.Warning($"[StoryMaker] PsychicDrone: TryExecute 返回 false, {evt.event_type}");
                return false;
            }
            Log.Message($"[StoryMaker] PsychicDrone: {evt.event_type}");
            return true;
        }
        catch (System.Exception ex)
        {
            Log.Error($"[StoryMaker] PsychicDrone 执行异常 ({evt.event_type}): {ex.Message}");
            return false;
        }
    }
}
