# Phase 1 规划书：Mod 框架 + 游戏状态快照

## 1. 阶段目标

**可量化的里程碑：** 玩家在开局选择 "AI 叙事者" 后，模组能采集殖民地游戏状态，序列化为 JSON 并输出到游戏控制台日志，验证数据正确性。

本阶段 **不涉及** LLM 通信、Prompt 构建、事件调度或事件执行。

---

## 2. 任务分解

### 任务 1.1：Mod 基础结构搭建

**产出文件：**
- `About/About.xml`
- `Source/StoryMaker.cs`

**About.xml 结构：**

```xml
<?xml version="1.0" encoding="utf-8"?>
<ModMetaData>
    <name>StoryMaker - AI Storyteller</name>
    <author>[作者]</author>
    <packageId>[author].storymaker</packageId>
    <description>用云端大语言模型 (LLM) 替代原版叙事者。LLM 阅读你的殖民地状态，提前规划未来多天的叙事事件序列，实现有上下文感知的动态叙事。</description>
    <supportedVersions><li>1.6</li></supportedVersions>
    <modDependencies>
        <li><packageId>brrainz.harmony</packageId><displayName>Harmony</displayName></li>
    </modDependencies>
    <loadAfter>
        <li>brrainz.harmony</li>
        <li>Ludeon.RimWorld</li>
        <li>Ludeon.RimWorld.Royalty</li>
        <li>Ludeon.RimWorld.Ideology</li>
        <li>Ludeon.RimWorld.Biotech</li>
        <li>Ludeon.RimWorld.Anomaly</li>
    </loadAfter>
</ModMetaData>
```

**StoryMaker.cs（Mod 入口类）：**

```csharp
namespace StoryMaker;

public class StoryMaker : Mod
{
    public static StoryMaker Instance { get; private set; }
    public StoryMakerSettings Settings { get; private set; }

    public StoryMaker(ModContentPack content) : base(content)
    {
        Instance = this;
        Settings = GetSettings<StoryMakerSettings>();
        // Harmony 补丁由 Harmony 自动发现并应用（通过 [HarmonyPatch] attribute）
    }
}
```

**实现要点：**
- 使用 `[StaticConstructorOnStartup]` 或 `Harmony` 自动发现机制注册 patch
- `StoryMakerSettings` 继承 `ModSettings`，在 `Mod` 构造函数中通过 `GetSettings<>()` 加载
- 日志统一使用 `Log.Message("[StoryMaker] ...")`

---

### 任务 1.2：StorytellerDef XML 定义

**产出文件：**
- `Defs/Storyteller_StoryMaker.xml`

**设计决策：**

继承 `BaseStoryteller`（`ParentName="BaseStoryteller"`），自动获得以下基础 comps：
- 通关任务（飞船逃离、超凡枢纽、皇家飞升）
- 深钻井虫害（DeepDrillInfestation）
- 工作站点任务（WorkSite）
- 皇家 DLC 任务线（Intro_Wimp、Intro_Deserter）
- 远征机械信号（Odyssey grav engine）
- 贡品收集者（TributeCollector）
- 灵树生成（AnimaTreeSpawn）
- 母树果荚（GauranlenPodSpawn）
- 巨石迁移（MonolithMigration）
- 乞丐任务（CharityBeggers）
- 史诗任务（RandomEpicQuest）
- 圣物朝圣者（ReliquaryPilgrims）
- 机控师古代复合体（MechanitorComplexQuest）
- 废包装虫害（DissolutionTriggered）
- 毒雾（NoxiousHaze）
- 波鲁树生成（PoluxTreeSpawn）
- 人口意图曲线、适应度曲线等数值曲线

子类需要 **显式添加** 以下 comps（这三项不在 BaseStoryteller 中，需要保留以保证远行队和临时地图正常运行）：

**必须添加的 comps（3 条）：**

1. **Caravan/TempMap Misc 事件：** `StorytellerCompProperties_CategoryIndividualMTBByBiome` (category=Misc, targets=Caravan/Map_TempIncident)
2. **Caravan/TempMap ThreatSmall 事件：** `StorytellerCompProperties_CategoryIndividualMTBByBiome` (category=ThreatSmall, applyCaravanVisibility=true, targets=Caravan/Map_TempIncident)
3. **Caravan/TempMap ThreatBig 事件：** `StorytellerCompProperties_CategoryIndividualMTBByBiome` (category=ThreatBig, applyCaravanVisibility=true, targets=Caravan/Map_TempIncident)
4. **StrangerInBlackJoin（黑衣人救援）：** `StorytellerCompProperties_Triggered` (incident=StrangerInBlackJoin, delayTicks=180) — 殖民者全倒后的紧急救援机制

**绝对不添加的 comps（由 LLM 替代）：**
- `StorytellerCompProperties_OnOffCycle`（ThreatBig/ThreatSmall — 袭击和威胁由 LLM 调度）
- `StorytellerCompProperties_CategoryMTB`（Misc — 杂项事件由 LLM 调度）
- `StorytellerCompProperties_RandomMain`（Randy 的随机事件生成器）
- `StorytellerCompProperties_FactionInteraction`（TraderCaravanArrival、VisitorGroup、TravelerGroup — 商队和访客由 LLM 调度）
- `StorytellerCompProperties_ShipChunkDrop`（飞船碎片由 LLM 调度）
- `StorytellerCompProperties_Disease`（疾病由 LLM 调度）
- `StorytellerCompProperties_RandomQuest`（随机任务由 LLM 调度）
- `StorytellerCompProperties_ClassicIntro`（经典开场由 LLM 调度）
- `StorytellerCompProperties_RaidBeacon` / `ThreatsGenerator`（信标袭击由 LLM 调度）
- `StorytellerCompProperties_OnOffCycle`（OrbitalTraderArrival — 轨道商由 LLM 调度）

**完整 XML 定义：**

```xml
<StorytellerDef ParentName="BaseStoryteller">
    <defName>StoryMaker</defName>
    <label>AI 叙事者</label>
    <description>云端大语言模型驱动的智能叙事者。它会阅读殖民地的实时状态，提前规划未来多天的叙事事件序列，实现有上下文感知的连贯叙事。需要配置 AI 服务。</description>
    <portraitLarge>UI/Storyteller/StoryMaker_Large</portraitLarge>
    <portraitTiny>UI/Storyteller/StoryMaker_Tiny</portraitTiny>
    <listOrder>10</listOrder>

    <comps>
        <!-- 以下 4 条继承自 BaseStoryteller 之外必须保留的原版 comp -->
        <!-- Caravan/Temp Map: Misc -->
        <li Class="StorytellerCompProperties_CategoryIndividualMTBByBiome">
            <category>Misc</category>
            <allowedTargetTags>
                <li>Caravan</li>
                <li>Map_TempIncident</li>
            </allowedTargetTags>
        </li>
        <!-- Caravan/Temp Map: ThreatSmall -->
        <li Class="StorytellerCompProperties_CategoryIndividualMTBByBiome">
            <category>ThreatSmall</category>
            <applyCaravanVisibility>true</applyCaravanVisibility>
            <allowedTargetTags>
                <li>Caravan</li>
                <li>Map_TempIncident</li>
            </allowedTargetTags>
        </li>
        <!-- Caravan/Temp Map: ThreatBig -->
        <li Class="StorytellerCompProperties_CategoryIndividualMTBByBiome">
            <category>ThreatBig</category>
            <applyCaravanVisibility>true</applyCaravanVisibility>
            <allowedTargetTags>
                <li>Caravan</li>
                <li>Map_TempIncident</li>
            </allowedTargetTags>
        </li>
        <!-- 黑衣人救援 -->
        <li Class="StorytellerCompProperties_Triggered">
            <incident>StrangerInBlackJoin</incident>
            <delayTicks>180</delayTicks>
        </li>
    </comps>
</StorytellerDef>
```

> **注意：** `portraitLarge` 和 `portraitTiny` 图片在 Phase 1 使用临时占位图（纯色方块 580×620 和 122×130）。

---

### 任务 1.3：Harmony Prefix Patch

**产出文件：**
- `Source/Patch_StorytellerTick.cs`
- `Source/Schedule/EventScheduler.cs`（仅骨架方法）

**Patch_StorytellerTick.cs：**

```csharp
using HarmonyLib;
using RimWorld;
using Verse;

namespace StoryMaker.Schedule;

[HarmonyPatch(typeof(Storyteller), "StorytellerTick")]
static class Patch_StorytellerTick
{
    static void Prefix()
    {
        if (Find.Storyteller?.def?.defName == "StoryMaker")
            EventScheduler.OnTick(Find.TickManager.TicksGame);
    }
}
```

**关键验证点：**
- `Find.Storyteller?.def?.defName` 必须 null-safe —— 游戏启动早期 Storyteller 可能未初始化
- `StorytellerTick()` 每 tick 都会调用，不需要手动判断 1000-tick 间隔（原版方法体内自己判断 `% 1000` 来决定是否生成事件，我们只注入调度逻辑）

**EventScheduler.cs（Phase 1 骨架）：**

```csharp
namespace StoryMaker.Schedule;

public static class EventScheduler
{
    public static void OnTick(int curTick)
    {
        // Phase 1: 仅输出日志验证 tick 注入是否正常
        // Phase 2+ 实现完整的滑动窗口逻辑
    }
}
```

---

### 任务 1.4：Core 基础设施

#### 1.4.1 StoryMakerSettings（Mod 配置）

**产出文件：** `Source/Core/StoryMakerSettings.cs`

```csharp
namespace StoryMaker.Core;

public class StoryMakerSettings : ModSettings
{
    // TCP 协议参数
    public int bufferDays = 2;      // B: 缓冲期（游戏天数）
    public int planWindowDays = 5;   // N: 规划窗口（游戏天数）
    public float timeoutSeconds = 60f; // T: 超时时间（现实秒）
    public int maxRetransmissions = 2; // R_max: 最大重传次数

    // 玩家个性化文本
    public string playerPersonality = "";

    // 开发者/调试
    public bool lowTokenMode = false;
    public bool debugMode = false;

    public override void ExposeData()
    {
        Scribe_Values.Look(ref bufferDays, "bufferDays", 2);
        Scribe_Values.Look(ref planWindowDays, "planWindowDays", 5);
        Scribe_Values.Look(ref timeoutSeconds, "timeoutSeconds", 60f);
        Scribe_Values.Look(ref maxRetransmissions, "maxRetransmissions", 2);
        Scribe_Values.Look(ref playerPersonality, "playerPersonality", "");
        Scribe_Values.Look(ref lowTokenMode, "lowTokenMode", false);
        Scribe_Values.Look(ref debugMode, "debugMode", false);
    }

    // 辅助：将游戏天数转为 ticks
    public int BufferTicks => bufferDays * 60000;
    public int PlanWindowTicks => planWindowDays * 60000;
}
```

> **Phase 1 不做 Mod Settings UI。** `StoryMakerConfigTab` 在 Phase 2 实现。Phase 1 期间所有参数通过默认值使用。

#### 1.4.2 StoryMakerState（运行时状态）

**产出文件：** `Source/Core/StoryMakerState.cs`

```csharp
namespace StoryMaker.Core;

public class StoryMakerState
{
    public int ack;                       // ACK 指针（已覆盖的最晚 tick）
    public bool permanentDegraded;        // 永久降级标记
    public string degradationReason;      // 降级原因（用于 UI 显示）
    public int consecutiveEmptyPlans;     // 连续空事件计数

    // 事件队列（Phase 1 仅定义类型，Phase 3 填充）
    // public List<ScheduledEvent> eventQueue;

    public StoryMakerState()
    {
        ack = 0;
        permanentDegraded = false;
        degradationReason = "";
        consecutiveEmptyPlans = 0;
    }

    public void ResetDegradation()
    {
        permanentDegraded = false;
        degradationReason = "";
        Log.Message("[StoryMaker] 降级状态已清除，恢复 AI 叙事者调度。");
    }

    public void MarkDegraded(string reason)
    {
        permanentDegraded = true;
        degradationReason = reason;
        Log.Warning($"[StoryMaker] 触发永久降级: {reason}");
    }
}
```

#### 1.4.3 StoryMakerExpose（存档序列化）

**产出文件：** `Source/Core/StoryMakerExpose.cs`

Phase 1 仅搭骨架。`StoryMakerState` 的字段通过 `Scribe_Values` 序列化。

```csharp
namespace StoryMaker.Core;

public static class StoryMakerExpose
{
    public static void Expose(StoryMakerState state)
    {
        Scribe_Values.Look(ref state.ack, "ack", 0);
        Scribe_Values.Look(ref state.permanentDegraded, "permanentDegraded", false);
        Scribe_Values.Look(ref state.consecutiveEmptyPlans, "consecutiveEmptyPlans", 0);
    }
}
```

> **序列化入口：** `StoryMakerExpose.Expose()` 由 `WorldComponent` 或 `GameComponent` 的 `ExposeData()` 调用。Phase 1 暂不创建 WorldComponent（Phase 2+ 视需要引入）。

---

### 任务 1.5：快照系统核心（ISnapshotField 接口 + 数据模型 + 序列化器）

**产出文件：**
- `Source/Snapshot/ISnapshotField.cs`
- `Source/Snapshot/GameStateSnapshot.cs`
- `Source/Snapshot/SnapshotCollector.cs`
- `Source/Snapshot/SnapshotSerializer.cs`

#### 1.5.1 ISnapshotField 接口

```csharp
namespace StoryMaker.Snapshot;

public interface ISnapshotField
{
    string Key { get; }
    object Collect();
    bool IncludeInLowTokenMode { get; }
}
```

#### 1.5.2 GameStateSnapshot 数据模型

```csharp
namespace StoryMaker.Snapshot;

public class GameStateSnapshot
{
    // 请求范围
    public int fromTick;
    public int toTick;

    // 殖民地基础
    public string colonyName;
    public int population;
    public float averageMood;
    public float foodDays;
    public float totalWealth;

    // 环境
    public string season;
    public string biome;
    public float currentTemperature;

    // 派系关系
    public List<FactionRelationEntry> factionRelations;

    // 近期事件
    public List<RecentEventEntry> recentEvents;

    // 叙事反馈
    public List<DeviationEntry> deviationReport;
}

public class FactionRelationEntry
{
    public string name;
    public int relation;  // -100~100
}

public class RecentEventEntry
{
    public string typeLabel;
    public int day;
    public string result;
}

public class DeviationEntry
{
    public string eventId;
    public string eventType;
    public string failReason;
}
```

#### 1.5.3 SnapshotCollector

```csharp
namespace StoryMaker.Snapshot;

public static class SnapshotCollector
{
    private static List<ISnapshotField> fields = new();

    static SnapshotCollector()
    {
        // 注册所有快照字段
        fields.Add(new SnapshotField_ColonyName());
        fields.Add(new SnapshotField_Population());
        fields.Add(new SnapshotField_AverageMood());
        fields.Add(new SnapshotField_FoodDays());
        fields.Add(new SnapshotField_TotalWealth());
        fields.Add(new SnapshotField_Season());
        fields.Add(new SnapshotField_Biome());
        fields.Add(new SnapshotField_Temperature());
        fields.Add(new SnapshotField_FactionRelations());
        fields.Add(new SnapshotField_RecentEvents());
        fields.Add(new SnapshotField_DeviationReport());
    }

    public static GameStateSnapshot Collect(int fromTick, int toTick)
    {
        var snapshot = new GameStateSnapshot
        {
            fromTick = fromTick,
            toTick = toTick,
            factionRelations = new List<FactionRelationEntry>(),
            recentEvents = new List<RecentEventEntry>(),
            deviationReport = new List<DeviationEntry>()
        };

        bool lowTokenMode = StoryMaker.Instance?.Settings?.lowTokenMode ?? false;

        foreach (var field in fields)
        {
            if (lowTokenMode && !field.IncludeInLowTokenMode)
                continue;
            ApplyField(snapshot, field);
        }

        return snapshot;
    }

    private static void ApplyField(GameStateSnapshot snapshot, ISnapshotField field)
    {
        var value = field.Collect();
        switch (field.Key)
        {
            case "colonyName": snapshot.colonyName = (string)value; break;
            case "population": snapshot.population = (int)value; break;
            case "averageMood": snapshot.averageMood = (float)value; break;
            case "foodDays": snapshot.foodDays = (float)value; break;
            case "totalWealth": snapshot.totalWealth = (float)value; break;
            case "season": snapshot.season = (string)value; break;
            case "biome": snapshot.biome = (string)value; break;
            case "currentTemperature": snapshot.currentTemperature = (float)value; break;
            case "factionRelations": snapshot.factionRelations = (List<FactionRelationEntry>)value; break;
            case "recentEvents": snapshot.recentEvents = (List<RecentEventEntry>)value; break;
            case "deviationReport": snapshot.deviationReport = (List<DeviationEntry>)value; break;
        }
    }

    // 注册外部字段（供其他 mod 通过 API 添加快照字段）
    public static void RegisterField(ISnapshotField field) => fields.Add(field);
    public static void UnregisterField(ISnapshotField field) => fields.Remove(field);
}
```

#### 1.5.4 SnapshotSerializer

```csharp
namespace StoryMaker.Snapshot;

public static class SnapshotSerializer
{
    public static string ToJson(GameStateSnapshot snapshot)
    {
        var sb = new StringBuilder();
        sb.AppendLine("{");

        // request_range
        sb.AppendLine("  \"request_range\": {");
        sb.AppendLine($"    \"from_tick\": {snapshot.fromTick},");
        sb.AppendLine($"    \"to_tick\": {snapshot.toTick}");
        sb.AppendLine("  },");

        // colony
        sb.AppendLine("  \"colony\": {");
        sb.AppendLine($"    \"name\": \"{EscapeJson(snapshot.colonyName)}\",");
        sb.AppendLine($"    \"population\": {snapshot.population},");
        sb.AppendLine($"    \"average_mood\": {snapshot.averageMood:F2},");
        sb.AppendLine($"    \"food_days\": {snapshot.foodDays:F1},");
        sb.AppendLine($"    \"total_wealth\": {snapshot.totalWealth:F0}");
        sb.AppendLine("  },");

        // environment
        sb.AppendLine("  \"environment\": {");
        sb.AppendLine($"    \"season\": \"{EscapeJson(snapshot.season)}\",");
        sb.AppendLine($"    \"biome\": \"{EscapeJson(snapshot.biome)}\",");
        sb.AppendLine($"    \"current_temperature\": {snapshot.currentTemperature:F1}");
        sb.AppendLine("  },");

        // faction_relations
        sb.AppendLine("  \"faction_relations\": [");
        if (snapshot.factionRelations != null)
        {
            for (int i = 0; i < snapshot.factionRelations.Count; i++)
            {
                var fr = snapshot.factionRelations[i];
                sb.Append(i < snapshot.factionRelations.Count - 1 ? "    { " : "    { ");
                sb.Append($"\"name\": \"{EscapeJson(fr.name)}\", \"relation\": {fr.relation} }}");
                if (i < snapshot.factionRelations.Count - 1) sb.AppendLine(",");
                else sb.AppendLine();
            }
        }
        sb.AppendLine("  ],");

        // recent_events
        sb.AppendLine("  \"recent_events\": [");
        if (snapshot.recentEvents != null)
        {
            for (int i = 0; i < snapshot.recentEvents.Count; i++)
            {
                var re = snapshot.recentEvents[i];
                sb.Append(i < snapshot.recentEvents.Count - 1 ? "    { " : "    { ");
                sb.Append($"\"type_label\": \"{EscapeJson(re.typeLabel)}\", \"day\": {re.day}, \"result\": \"{EscapeJson(re.result)}\" }}");
                if (i < snapshot.recentEvents.Count - 1) sb.AppendLine(",");
                else sb.AppendLine();
            }
        }
        sb.AppendLine("  ],");

        // deviation_report
        sb.AppendLine("  \"deviation_report\": [");
        if (snapshot.deviationReport != null)
        {
            for (int i = 0; i < snapshot.deviationReport.Count; i++)
            {
                var de = snapshot.deviationReport[i];
                sb.Append(i < snapshot.deviationReport.Count - 1 ? "    { " : "    { ");
                sb.Append($"\"event_id\": \"{EscapeJson(de.eventId)}\", \"event_type\": \"{EscapeJson(de.eventType)}\", \"fail_reason\": \"{EscapeJson(de.failReason)}\" }}");
                if (i < snapshot.deviationReport.Count - 1) sb.AppendLine(",");
                else sb.AppendLine();
            }
        }
        sb.AppendLine("  ]");

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string EscapeJson(string str)
    {
        if (string.IsNullOrEmpty(str)) return "";
        return str.Replace("\\", "\\\\").Replace("\"", "\\\"")
                  .Replace("\n", "\\n").Replace("\r", "\\r")
                  .Replace("\t", "\\t");
    }
}
```

---

### 任务 1.6：快照字段具体实现

**产出文件：** `Source/Snapshot/SnapshotFields.cs`（所有字段实现在一个文件中）

#### 1.6.1 各字段采集逻辑

**ColonyName：**
```csharp
class SnapshotField_ColonyName : ISnapshotField
{
    public string Key => "colonyName";
    public bool IncludeInLowTokenMode => true;
    public object Collect()
    {
        return Find.CurrentMap?.Parent?.Label ?? Find.World?.factionManager?.OfPlayer?.Name ?? "Unknown";
    }
}
```

**Population：**
```csharp
class SnapshotField_Population : ISnapshotField
{
    public string Key => "population";
    public bool IncludeInLowTokenMode => true;
    public object Collect()
    {
        return PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists.Count;
    }
}
```

**AverageMood：**
```csharp
class SnapshotField_AverageMood : ISnapshotField
{
    public string Key => "averageMood";
    public bool IncludeInLowTokenMode => true;
    public object Collect()
    {
        var colonists = PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists;
        if (colonists.Count == 0) return 0.5f;
        float sum = 0f;
        int count = 0;
        foreach (var pawn in colonists)
        {
            if (pawn.needs?.mood != null)
            {
                sum += pawn.needs.mood.CurLevel;  // 0~1
                count++;
            }
        }
        return count > 0 ? sum / count : 0.5f;
    }
}
```

**FoodDays：**
```csharp
class SnapshotField_FoodDays : ISnapshotField
{
    public string Key => "foodDays";
    public bool IncludeInLowTokenMode => true;
    public object Collect()
    {
        var maps = Find.Maps;
        float totalHumanEdibleNutrition = 0f;
        float dailyConsumption = 0f;
        foreach (var map in maps)
        {
            if (!map.IsPlayerHome) continue;
            // 统计可食用营养
            var foodItems = map.listerThings.ThingsInGroup(ThingRequestGroup.FoodSourceNotPlantOrTree);
            foreach (var item in foodItems)
            {
                if (item.def.IsNutritionGivingIngestible && item.def.ingestible.HumanEdible)
                {
                    totalHumanEdibleNutrition += item.GetStatValue(StatDefOf.Nutrition) * item.stackCount;
                }
            }
            // 统计殖民者日均消耗
            foreach (var pawn in map.mapPawns.FreeColonists)
            {
                dailyConsumption += pawn.needs?.food?.FoodFallPerTickAssumingCategory(HungerCategory.Fed, multi: false) ?? 0f;
            }
        }
        dailyConsumption *= 60000f; // per tick → per day
        if (dailyConsumption <= 0f) return 999f; // 无消耗则返回大方值
        return totalHumanEdibleNutrition / dailyConsumption;
    }
}
```

**TotalWealth：**
```csharp
class SnapshotField_TotalWealth : ISnapshotField
{
    public string Key => "totalWealth";
    public bool IncludeInLowTokenMode => true;
    public object Collect()
    {
        return Find.CurrentMap?.wealthWatcher?.WealthTotal ?? 0f;
    }
}
```

**Season：**
```csharp
class SnapshotField_Season : ISnapshotField
{
    public string Key => "season";
    public bool IncludeInLowTokenMode => true;
    public object Collect()
    {
        return GenDate.Season(Find.TickManager.TicksAbs, Find.WorldGrid.LongLatOf(Find.CurrentMap.Tile));
    }
}
```

> **注意：** `Season` 枚举值需要翻译为 LLM 可读的中文/英文——见 Season 枚举值映射。

**Biome：**
```csharp
class SnapshotField_Biome : ISnapshotField
{
    public string Key => "biome";
    public bool IncludeInLowTokenMode => true;
    public object Collect()
    {
        return Find.CurrentMap?.Biome?.label ?? "Unknown";
    }
}
```

**Temperature：**
```csharp
class SnapshotField_Temperature : ISnapshotField
{
    public string Key => "currentTemperature";
    public bool IncludeInLowTokenMode => true;
    public object Collect()
    {
        return Find.CurrentMap?.mapTemperature?.OutdoorTemp ?? 0f;
    }
}
```

**FactionRelations：**
```csharp
class SnapshotField_FactionRelations : ISnapshotField
{
    public string Key => "factionRelations";
    public bool IncludeInLowTokenMode => true;
    public object Collect()
    {
        var result = new List<FactionRelationEntry>();
        var playerFaction = Find.FactionManager.OfPlayer;
        if (playerFaction == null) return result;

        foreach (var faction in Find.FactionManager.AllFactionsListForReading)
        {
            if (faction == playerFaction) continue;
            if (faction.IsPlayer) continue;
            if (faction.Hidden) continue;
            // 排除机械族和虫族（LLM 不需要关系数据，它们是固定敌对）
            if (faction.def == FactionDefOf.Mechanoid) continue;
            if (faction.def == FactionDefOf.Insect) continue;

            result.Add(new FactionRelationEntry
            {
                name = faction.Name,
                relation = faction.GoodwillWith(playerFaction)
            });
        }

        return result;
    }
}
```

**RecentEvents（核心难点——Letter 过滤策略）：**

```csharp
class SnapshotField_RecentEvents : ISnapshotField
{
    public string Key => "recentEvents";
    public bool IncludeInLowTokenMode => false; // 低 token 模式下可跳过

    // 事件栈：维护已提取事件的 ID 集合，避免重复提取
    private static HashSet<int> extractedLetterIds = new();
    private const int MaxRecentEvents = 10;     // 最多发送 10 条近期事件
    private const int LookbackTicks = 300000;    // 回溯 5 天

    public object Collect()
    {
        var result = new List<RecentEventEntry>();
        int curTick = Find.TickManager.TicksGame;
        var archive = Find.Archive;

        // 从 Archive 中获取所有 Letter，按时间倒序
        var letters = archive.ArchivablesListForReading
            .OfType<Letter>()
            .Where(l => l.arrivalTick >= curTick - LookbackTicks
                     && l.arrivalTick <= curTick)
            .OrderByDescending(l => l.arrivalTick)
            .ToList();

        int count = 0;
        foreach (var letter in letters)
        {
            if (count >= MaxRecentEvents) break;

            // 跳过已提取的 Letter（事件栈去重）
            if (extractedLetterIds.Contains(letter.ID)) continue;

            // 跳过无叙事价值的 Letter（过滤规则见下文）
            if (!IsNarrativelySignificant(letter)) continue;

            extractedLetterIds.Add(letter.ID);
            result.Add(new RecentEventEntry
            {
                typeLabel = letter.label?.ToString() ?? letter.def?.label ?? "Unknown",
                day = (letter.arrivalTick - Find.TickManager.gameStartAbsTick) / 60000,
                result = "触发" // Phase 1 默认值；Phase 3+ 根据事件执行结果填充
            });
            count++;
        }

        // 清理事件栈：移除已不在回溯窗口内的旧 ID
        CleanupExtractedIds(letters, curTick);

        return result;
    }

    private static bool IsNarrativelySignificant(Letter letter)
    {
        // 过滤规则（Phase 1 初步规则，Phase 3 可细化）：
        // 1. 过滤"制作完成"类 Letter（大师级装备等一次性爆发 100+ 条）
        if (letter.def == LetterDefOf.NeutralEvent) return false;  // 中性事件多为制作通知

        // 2. 过滤"死亡"类 Letter（过多死亡信息会污染数据）
        //    → 但保留殖民者死亡（重要叙事事件）
        //    → Phase 1 先简单过滤，Phase 3 根据 letter.def 精确判断

        // 3. 只保留事件类 Letter（Incident Letter）和威胁类 Letter
        //    → 威胁类: ThreatBig, ThreatSmall
        //    → Phase 1 先用 LetterDef 的 defName 做粗筛选

        return true; // Phase 1 先不过度过滤，以验证数据为主
    }

    private static void CleanupExtractedIds(List<Letter> currentLetters, int curTick)
    {
        var currentIds = new HashSet<int>(currentLetters.Select(l => l.ID));
        extractedLetterIds.RemoveWhere(id => !currentIds.Contains(id));
    }
}
```

> **Letter 过滤策略说明：**
> Phase 1 先宽泛采集，输出到日志后人工审查数据合理性。Phase 3 结合 `LetterDef` 类型、`letter.lookTargets` 等字段精确过滤，并可能改用 `Find.History` 的 events 或 `StoryWatcher` 的数据作为补充来源。具体过滤规则在 Phase 1 数据评审阶段最终确定。

**DeviationReport（Phase 1 占位）：**
```csharp
class SnapshotField_DeviationReport : ISnapshotField
{
    public string Key => "deviationReport";
    public bool IncludeInLowTokenMode => false;
    public object Collect()
    {
        // Phase 1: 始终返回空列表
        // Phase 3: 从 EventScheduler 的 failedEvents 栈获取
        return new List<DeviationEntry>();
    }
}
```

---

### 任务 1.7：数据粒度评审

**执行方式：** 启动游戏，选择 StoryMaker 叙事者，运行 1-2 小时后查看日志输出的快照 JSON。逐字段回答以下问题：

| 审查项 | 问题 |
|--------|------|
| 字段必要性 | 此字段对 LLM 的叙事决策是否有实际帮助？ |
| 信息密度 | 是否存在冗余字段（如 biome 从 colonyName 可推断）？ |
| Token 消耗 | 此字段的 JSON 字符串长度是否与其叙事价值成正比？ |
| 数据准确性 | 字段值是否准确反映了游戏内实际情况？ |

**Letter 过滤策略细化：**

评审后确定具体的 `IsNarrativelySignificant()` 过滤规则，包括：
- 排除的 `LetterDef` 列表（如 NeutralEvent、制作通知等）
- 每类 Letter 的保留数量上限
- "事件栈" 的最大容量和剔除策略

---

### 任务 1.8：集成与里程碑验证

**验证步骤：**

1. 编译 → 模组在游戏中可见 → 新建游戏时 "AI 叙事者" 出现在叙事者选择列表
2. 选择 StoryMaker → 进入游戏 → 控制台日志无异常
3. 通过临时调试代码（或在 `EventScheduler.OnTick` 中调用 `SnapshotCollector.Collect()`），手动打印快照 JSON 到控制台
4. 人工审查 JSON 内容：字段值是否合理、是否有 null/空值、数值是否符合预期
5. 存档 → 读档 → 验证状态恢复（ack=0, permanentDegraded=false）

**Phase 1 不产出可发布的游戏体验**，但必须产出数据验证通过的快照 JSON 示例。

---

## 3. 文件产出清单

```
StoryMaker/
├── About/
│   └── About.xml                          # 任务 1.1
├── Source/
│   ├── StoryMaker.cs                      # 任务 1.1：Mod 入口
│   ├── Patch_StorytellerTick.cs           # 任务 1.3：Harmony 补丁
│   ├── Core/
│   │   ├── StoryMakerSettings.cs          # 任务 1.4.1：Mod 配置
│   │   ├── StoryMakerState.cs             # 任务 1.4.2：运行时状态
│   │   └── StoryMakerExpose.cs            # 任务 1.4.3：存档序列化
│   └── Snapshot/
│       ├── ISnapshotField.cs              # 任务 1.5.1：可扩展接口
│       ├── GameStateSnapshot.cs           # 任务 1.5.2：数据模型
│       ├── SnapshotCollector.cs           # 任务 1.5.3：采集器
│       ├── SnapshotSerializer.cs          # 任务 1.5.4：JSON 序列化
│       └── SnapshotFields.cs              # 任务 1.6：各字段实现
├── Defs/
│   └── Storyteller_StoryMaker.xml         # 任务 1.2：叙事者定义
├── Languages/
│   └── (Phase 2)
└── Resources/
    └── UI/Storyteller/
        ├── StoryMaker_Large.png           # 占位图 580×620
        └── StoryMaker_Tiny.png            # 占位图 122×130
```

共 **11 个源文件 + 1 个 XML Def + 2 个占位图**。

---

## 4. 执行顺序与依赖

```
1.1 (About.xml + StoryMaker.cs)
 │
 ├──► 1.2 (StorytellerDef XML) ── 独立，可与1.1并行
 │
 ├──► 1.3 (Harmony Patch + EventScheduler骨架)
 │      依赖: 1.1 (需要 Mod 入口加载)
 │
 ├──► 1.4 (Settings + State + Expose)
 │      依赖: 1.1
 │
 └──► 1.5+1.6 (快照系统)
        依赖: 1.1, 1.4 (需要读取 Settings.lowTokenMode)
        1.5 (接口/模型/采集器/序列化器) 与 1.6 (字段实现) 紧密耦合，应作为一个整体编写
 │
 └──► 1.7 (数据粒度评审)
        依赖: 1.5+1.6 完成后才能采集实际数据评审
 │
 └──► 1.8 (集成验证)
        依赖: 所有上述任务完成
```

**推荐执行顺序：** `1.1 → 1.4 → 1.2 → 1.3 → 1.5+1.6 → 1.7 → 1.8`

其中 `1.2`（StorytellerDef）和 `1.3`（Harmony Patch）可以并行编写。

---

## 5. 关键 RimWorld API 速查

| 需求 | API | 说明 |
|------|-----|------|
| 当前 tick | `Find.TickManager.TicksGame` | 游戏内时间戳 |
| 殖民者列表 | `PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists` | 含远行队和运输中的殖民者 |
| 当前地图 | `Find.CurrentMap` | 可能为 null（世界界面时） |
| 财富 | `Find.CurrentMap.wealthWatcher.WealthTotal` | 含物品+建筑+生物 |
| 心情 | `pawn.needs.mood.CurLevel` | 0~1 浮点数 |
| 食物 | 见 1.6.1 FoodDays 实现 | 手动计算总营养 ÷ 日均消耗 |
| 季节 | `GenDate.Season()` | 返回 `Season` 枚举 |
| 生物群系 | `Find.CurrentMap.Biome.label` | 翻译后的显示名 |
| 温度 | `Find.CurrentMap.mapTemperature.OutdoorTemp` | 户外温度（摄氏度） |
| 派系列表 | `Find.FactionManager.AllFactionsListForReading` | 所有派系 |
| 派系好感度 | `faction.GoodwillWith(playerFaction)` | -100~100 |
| Letter 归档 | `Find.Archive.ArchivablesListForReading` | 所有已归档条目（含 Letter） |
| 叙事者 defName | `Find.Storyteller.def.defName` | 判断是否 StoryMaker |
| 语言设置 | `Prefs.LangFolderName` | `"ChineseSimplified"` / `"English"` |
| 开发者模式 | `Prefs.DevMode` | bool |
| 游戏暂停状态 | `Find.TickManager.Paused` | bool |
| StorytellerTick 间隔 | `Find.TickManager.TicksGame % 1000` | 内部判断，我们不必关心 |
| Harmony 依赖 | `brrainz.harmony` | About.xml 中声明依赖 |

---

## 6. 里程碑验收清单

- [ ] **Mod 可加载：** 模组出现在 Mod 列表中，无启动错误
- [ ] **叙事者可选：** "AI 叙事者" 出现在新游戏叙事者选择界面
- [ ] **Tick 注入正常：** 控制台日志确认 `EventScheduler.OnTick()` 被按预期调用
- [ ] **快照采集完整：** 日志输出完整 JSON，所有字段非 null/非默认值（deviation_report 除外）
- [ ] **数据正确性：** 人工对照游戏内实际数据，验证 population、wealth、mood、food、faction 等字段值合理
- [ ] **Letter 过滤合理：** 近期事件列表不包含大量垃圾 Letter（如连串制作通知）
- [ ] **存档读档：** 存档加载后状态正确恢复，无异常日志
- [ ] **低 token 模式：** Settings 中开启 lowTokenMode 后，非核心字段不在 JSON 中出现

---

## 7. 已知风险与缓解

| 风险 | 影响 | 缓解 |
|------|------|------|
| `Find.CurrentMap` 为 null（世界界面） | 快照采集崩溃 | 每个字段实现 `Find.CurrentMap?.xxx ?? fallback` 模式 |
| `pawn.needs.mood` 为 null（特殊殖民者） | AverageMood 计算异常 | foreach 内判空，空值不计入平均值 |
| FoodDays 计算偏差 | Token 数偏离实际 | Phase 1 记录日志与游戏内 UI 对照验证 |
| Letter 爆炸（100+ 条制作通知） | recentEvents 被垃圾数据填满 | 初版过滤规则 + 最多 10 条硬限制 |
| Harmony patch 冲突（StorytellerTick 被多 mod patch） | patch 顺序问题 | `StorytellerTick()` 不是热门 patch 点，冲突概率低。Phase 1 先用 Prefix，不影响原版方法体 |
| `Faction.GoodwillWith()` 对特殊派系返回异常值 | factionRelations 数据不准确 | 排除 Mechanoid/Insect 等固定敌对派系 |
