using RimWorld;
using StoryMaker.Response;
using Verse;

namespace StoryMaker.Action;

// 疾病事件（人类 + 动物）：intensity_multiplier 影响疾病严重度
public class ActionDisease : IActionHandler
{
    public string EventType => "Disease";  // 统一入口，根据 event_type 分发具体疾病
    public bool IsAllowedInImmediateMode => false;
    public float MaxImmediatePointsMultiplier => 0f;

    // 所有疾病子类型
    public static readonly string[] DiseaseTypes =
    {
        "Disease_Flu", "Disease_Plague", "Disease_Malaria",
        "Disease_SleepingSickness", "Disease_FibrousMechanites",
        "Disease_SensoryMechanites", "Disease_GutWorms", "Disease_MuscleParasites",
        "Disease_AnimalFlu", "Disease_AnimalPlague", "Disease_Abasia"
    };

    public bool Execute(PlannedEvent evt)
    {
        var map = Find.CurrentMap ?? Find.AnyPlayerHomeMap;
        if (map == null) return false;

        IncidentParms parms = StorytellerUtility.DefaultParmsNow(evt.resolvedDef.category, map);

        // intensity_multiplier 影响疾病严重度
        float intensity = evt.GetFloatParam("intensity_multiplier", 1.0f);
        intensity = UnityEngine.Mathf.Clamp(intensity, 0.5f, 2.0f);
        if (parms.points > 0)
            parms.points *= intensity;

        try
        {
            if (!evt.resolvedDef.Worker.TryExecute(parms))
            {
                Log.Warning($"[StoryMaker] Disease: 自定义参数执行失败 ({evt.event_type}, points={parms.points:F0})，尝试原版默认参数...");
                IncidentParms fallbackParms = StorytellerUtility.DefaultParmsNow(evt.resolvedDef.category, map);
                if (!evt.resolvedDef.Worker.TryExecute(fallbackParms))
                {
                    Log.Error($"[StoryMaker] Disease: 原版默认参数也执行失败 ({evt.event_type})");
                    return false;
                }
                Log.Message($"[StoryMaker] Disease: 原版默认参数执行成功 (回退, {evt.event_type})");
                return true;
            }
            Log.Message($"[StoryMaker] Disease: {evt.event_type}, points={parms.points:F0} (intensity={intensity:F2})");
            return true;
        }
        catch (System.Exception ex)
        {
            Log.Error($"[StoryMaker] Disease 执行异常 ({evt.event_type}): {ex.Message}");
            return false;
        }
    }
}
