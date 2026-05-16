using RimWorld;
using StoryMaker.Response;
using Verse;

namespace StoryMaker.Action;

// 猎杀人类兽群：intensity_multiplier
public class ActionManhunterPack : IActionHandler
{
    public string EventType => "ManhunterPack";
    public bool IsAllowedInImmediateMode => false;
    public float MaxImmediatePointsMultiplier => 0f;

    public bool Execute(PlannedEvent evt)
    {
        var map = Find.CurrentMap ?? Find.AnyPlayerHomeMap;
        if (map == null) return false;

        IncidentDef incidentDef = IncidentDefOf.ManhunterPack;
        IncidentParms parms = StorytellerUtility.DefaultParmsNow(incidentDef.category, map);

        float intensity = evt.GetFloatParam("intensity_multiplier", 1.0f);
        intensity = UnityEngine.Mathf.Clamp(intensity, 0.5f, 2.0f);
        float originalPoints = parms.points;
        parms.points *= intensity;

        try
        {
            if (!incidentDef.Worker.TryExecute(parms))
            {
                Log.Warning($"[StoryMaker] ManhunterPack: 自定义参数执行失败 (points={parms.points:F0})，尝试原版默认参数...");
                IncidentParms fallbackParms = StorytellerUtility.DefaultParmsNow(incidentDef.category, map);
                if (!incidentDef.Worker.TryExecute(fallbackParms))
                {
                    Log.Error($"[StoryMaker] ManhunterPack: 原版默认参数也执行失败");
                    return false;
                }
                Log.Message($"[StoryMaker] ManhunterPack: 原版默认参数执行成功 (回退)");
                return true;
            }
            Log.Message($"[StoryMaker] ManhunterPack: points={originalPoints:F0}×{intensity:F2}={parms.points:F0}");
            return true;
        }
        catch (System.Exception ex)
        {
            Log.Error($"[StoryMaker] ManhunterPack 执行异常: {ex.Message}");
            return false;
        }
    }
}
