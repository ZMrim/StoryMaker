using RimWorld;
using StoryMaker.Response;
using Verse;

namespace StoryMaker.Action;

// 虫害事件：intensity_multiplier
public class ActionInfestation : IActionHandler
{
    public string EventType => "Infestation";
    public bool IsAllowedInImmediateMode => false;
    public float MaxImmediatePointsMultiplier => 0f;

    public bool Execute(PlannedEvent evt)
    {
        var map = Find.CurrentMap ?? Find.AnyPlayerHomeMap;
        if (map == null) return false;

        IncidentParms parms = StorytellerUtility.DefaultParmsNow(evt.resolvedDef.category, map);

        float intensity = evt.GetFloatParam("intensity_multiplier", 1.0f);
        intensity = UnityEngine.Mathf.Clamp(intensity, 0.5f, 2.0f);
        float originalPoints = parms.points;
        parms.points *= intensity;

        try
        {
            if (!evt.resolvedDef.Worker.TryExecute(parms))
            {
                Log.Warning($"[StoryMaker] Infestation: 自定义参数执行失败 ({evt.event_type}, points={parms.points:F0})，尝试原版默认参数...");
                IncidentParms fallbackParms = StorytellerUtility.DefaultParmsNow(evt.resolvedDef.category, map);
                if (!evt.resolvedDef.Worker.TryExecute(fallbackParms))
                {
                    Log.Error($"[StoryMaker] Infestation: 原版默认参数也执行失败 ({evt.event_type})");
                    return false;
                }
                Log.Message($"[StoryMaker] Infestation: 原版默认参数执行成功 (回退, {evt.event_type})");
                return true;
            }
            Log.Message($"[StoryMaker] Infestation: {evt.event_type}, points={originalPoints:F0}×{intensity:F2}={parms.points:F0}");
            return true;
        }
        catch (System.Exception ex)
        {
            Log.Error($"[StoryMaker] Infestation 执行异常 ({evt.event_type}): {ex.Message}");
            return false;
        }
    }
}
