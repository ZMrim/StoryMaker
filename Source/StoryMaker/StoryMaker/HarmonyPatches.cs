using HarmonyLib;
using Verse;

namespace StoryMaker;

// 在模组加载时自动应用所有 Harmony 补丁
[StaticConstructorOnStartup]
public static class HarmonyPatches
{
    static HarmonyPatches()
    {
        var harmony = new Harmony("ZM.StoryMaker");
        harmony.PatchAll();
        Log.Message("[StoryMaker] Harmony 补丁已全部加载。");
    }
}
