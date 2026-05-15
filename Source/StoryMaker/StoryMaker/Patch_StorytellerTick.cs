using HarmonyLib;
using RimWorld;
using Verse;
using StoryMaker.Schedule;

namespace StoryMaker;

[HarmonyPatch(typeof(Storyteller), "StorytellerTick")]
static class Patch_StorytellerTick
{
    static void Prefix()
    {
        if (Find.Storyteller?.def?.defName == "StoryMaker")
            EventScheduler.OnTick(Find.TickManager.TicksGame);
    }
}
