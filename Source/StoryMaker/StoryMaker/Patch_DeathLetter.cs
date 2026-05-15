using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace StoryMaker.Snapshot;

[HarmonyPatch(typeof(LetterStack), "ReceiveLetter", new Type[] { typeof(Letter), typeof(string), typeof(int), typeof(bool) })]
static class Patch_DeathLetter
{
    static void Postfix(Letter let)
    {
        if (let?.def != LetterDefOf.Death) return;

        // 从 DeathLetter 的 lookTargets 中提取死亡的 Pawn
        var target = let.lookTargets.PrimaryTarget;
        if (!target.IsValid) return;

        Pawn deadPawn = target.Pawn;
        if (deadPawn == null) return;

        // 必须属于玩家派系
        if (deadPawn.Faction != Faction.OfPlayer) return;

        // 种族白名单检查
        var raceWhitelist = StoryMaker.Instance?.Settings?.deathRaceWhitelist;
        if (raceWhitelist == null || raceWhitelist.Count == 0)
        {
            if (deadPawn.def.defName != "Human") return;
        }
        else
        {
            if (!raceWhitelist.Contains(deadPawn.def.defName)) return;
        }

        string pawnName = deadPawn.LabelShort;
        IncidentEventStack.PushDeath(pawnName);
    }
}
