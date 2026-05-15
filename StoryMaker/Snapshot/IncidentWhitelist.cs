using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace StoryMaker.Snapshot;

// 从 DefDatabase 构建白名单：排除远行队事件和 Special 一次性事件，其余全收
// 启动时强制加载以确保白名单日志在开局即输出
[StaticConstructorOnStartup]
public static class IncidentWhitelist
{
    // defName → category.defName
    private static Dictionary<string, string> whitelist = new();
    // defName → label (用于快照输出)
    private static Dictionary<string, string> labels = new();

    // 按名称精确排除，避免误伤 targetTags 含 Caravan 的非远行队事件（如疾病）
    private static HashSet<string> excludedByDefName = new()
    {
        "Ambush", "ManhunterAmbush", "CaravanMeeting", "CaravanDemand",
        "DeepDrillInfestation" // 由深钻井 comp 触发，非叙事事件
    };

    static IncidentWhitelist()
    {
        var allDefs = DefDatabase<IncidentDef>.AllDefsListForReading;

        foreach (var def in allDefs)
        {
            // 排除 Special 类别（一次性/系统独有事件）
            if (def.category?.defName == "Special")
                continue;

            // 排除纯远行队事件等（按名称精确排除，不用 targetTags 过滤——疾病等也有 Caravan 标签）
            if (excludedByDefName.Contains(def.defName))
                continue;

            whitelist[def.defName] = def.category?.defName ?? "Unknown";
            labels[def.defName] = def.label ?? def.defName;
        }

        // 按 category 分组输出全部白名单事件
        Log.Message($"[StoryMaker] ── Incident 白名单已构建：{whitelist.Count} 个事件类型 ──");
        var byCategory = whitelist
            .GroupBy(kv => kv.Value)
            .OrderBy(g => g.Key);
        foreach (var group in byCategory)
        {
            var names = group.Select(kv => kv.Key).OrderBy(n => n);
            Log.Message($"[StoryMaker]   [{group.Key}] {string.Join(", ", names)}");
        }
        Log.Message("[StoryMaker] ── 白名单输出完毕 ──");
    }

    public static bool IsWhitelisted(string defName) => whitelist.ContainsKey(defName);

    public static string GetCategory(string defName)
    {
        whitelist.TryGetValue(defName, out var cat);
        return cat ?? "Unknown";
    }

    public static string GetLabel(string defName)
    {
        labels.TryGetValue(defName, out var label);
        return label ?? defName;
    }
}
