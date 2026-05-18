using System.Collections.Generic;
using RimWorld;
using StoryMaker.Response;
using Verse;

namespace StoryMaker.Action;

// 统一事件注册表，映射 event_type → IActionHandler。
// [StaticConstructorOnStartup] 确保启动时注册全部 handler。
[StaticConstructorOnStartup]
public static class ActionRegistry
{
    private static Dictionary<string, IActionHandler> handlers = new();

    static ActionRegistry()
    {
        // 袭击 — 覆盖 RaidEnemy + RaidFriendly
        var raidHandler = new ActionRaidEnemy();
        RegisterFor("RaidEnemy", raidHandler);
        RegisterFor("RaidFriendly", raidHandler);

        // 商队 + 轨道商 — 覆盖 TraderCaravanArrival + OrbitalTraderArrival
        var traderHandler = new ActionTraderCaravan();
        RegisterFor("TraderCaravanArrival", traderHandler);
        RegisterFor("OrbitalTraderArrival", traderHandler);

        // 猎杀人类 + 动物发狂
        var manhunterHandler = new ActionManhunterPack();
        RegisterFor("ManhunterPack", manhunterHandler);
        RegisterFor("AnimalInsanitySingle", manhunterHandler);
        RegisterFor("AnimalInsanityMass", manhunterHandler);

        // 虫害
        var infestationHandler = new ActionInfestation();
        RegisterFor("Infestation", infestationHandler);
        RegisterFor("Infestation_Jelly", infestationHandler);

        // 疾病 — 一个 handler 覆盖全部疾病子类型
        var diseaseHandler = new ActionDisease();
        foreach (var dt in ActionDisease.DiseaseTypes)
            RegisterFor(dt, diseaseHandler);

        // 心灵 — 覆盖 PsychicDrone + PsychicSoothe
        var psychicHandler = new ActionPsychicDrone();
        RegisterFor("PsychicDrone", psychicHandler);
        RegisterFor("PsychicSoothe", psychicHandler);

        Log.Message($"[StoryMaker] ActionRegistry: 已注册 {handlers.Count} 个事件类型映射");
    }

    public static void Register(IActionHandler handler)
    {
        if (handler == null || string.IsNullOrEmpty(handler.EventType)) return;
        handlers[handler.EventType] = handler;
    }

    // 为特定 event_type 注册 handler（同一 handler 可注册多个类型）
    public static void RegisterFor(string eventType, IActionHandler handler)
    {
        if (string.IsNullOrEmpty(eventType) || handler == null) return;
        handlers[eventType] = handler;
    }

    public static bool IsSupported(string eventType)
    {
        return handlers.ContainsKey(eventType);
    }

    public static IActionHandler GetHandler(string eventType)
    {
        handlers.TryGetValue(eventType, out var handler);
        return handler;
    }

    public static bool Execute(PlannedEvent evt)
    {
        if (evt == null || string.IsNullOrEmpty(evt.event_type))
        {
            Log.Error("[StoryMaker] ActionRegistry.Execute: 事件为空或 event_type 为空");
            return false;
        }

        var handler = GetHandler(evt.event_type);
        if (handler == null)
        {
            Log.Warning($"[StoryMaker] ActionRegistry: 未注册的事件类型 '{evt.event_type}'，尝试通用执行");
            return ExecuteGeneric(evt);
        }

        return handler.Execute(evt);
    }

    // 通用兜底：对未注册但已有 resolvedDef 的事件，直接用 IncidentDef Worker 执行
    private static bool ExecuteGeneric(PlannedEvent evt)
    {
        var parms = StorytellerUtility.DefaultParmsNow(evt.resolvedDef.category, Find.CurrentMap ?? Find.AnyPlayerHomeMap);
        // 应用 intensity_multiplier（如有）
        float multiplier = evt.GetFloatParam("intensity_multiplier", 1.0f);
        if (multiplier != 1.0f && parms.points > 0)
            parms.points *= multiplier;

        try
        {
            if (!evt.resolvedDef.Worker.TryExecute(parms))
            {
                Log.Warning($"[StoryMaker] 通用执行: 自定义参数失败 ({evt.event_type}, intensity={multiplier:F2})，尝试原版默认参数...");
                var fallbackParms = StorytellerUtility.DefaultParmsNow(evt.resolvedDef.category, Find.CurrentMap ?? Find.AnyPlayerHomeMap);
                if (!evt.resolvedDef.Worker.TryExecute(fallbackParms))
                {
                    Log.Error($"[StoryMaker] 通用执行: 原版默认参数也执行失败 ({evt.event_type})");
                    return false;
                }
                Log.Message($"[StoryMaker] 通用执行: 原版默认参数成功 (回退, {evt.event_type})");
                return true;
            }
            Log.Message($"[StoryMaker] 通用执行: {evt.event_type} (intensity={multiplier:F2})");
            return true;
        }
        catch (System.Exception ex)
        {
            Log.Error($"[StoryMaker] 通用执行失败: {evt.event_type} - {ex.Message}");
            return false;
        }
    }

    public static List<string> SupportedTypes => new(handlers.Keys);
}
