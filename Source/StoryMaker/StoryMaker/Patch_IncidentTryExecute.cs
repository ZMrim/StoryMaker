using HarmonyLib;
using RimWorld;

namespace StoryMaker.Snapshot;

// Postfix on IncidentWorker.TryExecute() — 捕获所有实际执行的事件（含开发模式和任意叙事者）
[HarmonyPatch(typeof(IncidentWorker), "TryExecute")]
static class Patch_IncidentTryExecute
{
    static void Postfix(IncidentWorker __instance, bool __result)
    {
        if (!__result) return;
        if (__instance?.def == null) return;

        IncidentEventStack.PushIncident(__instance.def.defName);
    }
}
