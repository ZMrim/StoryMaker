# Phase 1 阶段总结：Mod 框架 + 游戏状态快照

## 里程碑状态：✅ 完成

StoryMaker 模组已可加载，`Storyteller_StoryMaker` 可选，Harmony 补丁正常注入，每游戏天（60000 tick）自动采集殖民地状态快照并序列化为 JSON 输出到控制台。

---

## 1. 已验证的快照 JSON 输出

以下为实际游戏控制台输出（Day 1, tick 60000），数据已肉眼对照游戏内状态验证正确：

```json
{
  "request_range": {
    "from_tick": 180000,
    "to_tick": 480000
  },
  "colony": {
    "name": "殖民地",
    "population": 1,
    "average_mood": 0.45,
    "food_days": 47.7,
    "total_wealth": 16352
  },
  "environment": {
    "season": "Spring",
    "biome": "温带森林",
    "current_temperature": 44.1
  },
  "faction_relations": [
    { "name": "绍哈敏拉有机协约", "relation": 0 },
    { "name": "阿比勒氏族联盟", "relation": 0 },
    { "name": "神圣帝国", "relation": 0 },
    { "name": "古尔库尔", "relation": -80 },
    { "name": "德尤尔", "relation": -100 },
    { "name": "哈克哈克撒阿", "relation": -100 },
    { "name": "博普摩伊", "relation": -80 },
    { "name": "窒息污骸", "relation": -100 },
    { "name": "虚空交易所", "relation": 0 }
  ],
  "recent_events": [
    { "type": "袭击", "category": "ThreatBig" },
    { "type": "心灵低语", "category": "Misc" },
    { "type": "热浪", "category": "Misc" },
    { "type": "心灵抚慰波", "category": "Misc" }
  ],
  "recent_deaths": [
    { "pawn_name": "Lester" }
  ],
  "deviation_report": [
  ]
}
```

---

## 2. 快照 JSON 字段说明

| JSON 键 | 类型 | 说明 | Phase 2 用法 |
|---------|------|------|-------------|
| `request_range` | `{from_tick, to_tick}` | 请求 LLM 规划的 tick 区间 | LLM 回复的 `plan_range` 必须与此匹配 |
| `colony` | 对象 | name/population/average_mood(0~1)/food_days/total_wealth | 核心叙事输入 |
| `environment` | 对象 | season/biome/current_temperature | 天气和温度是 LLM 规划事件的上下文 |
| `faction_relations[]` | 数组 | name + relation(-100~100) | LLM 选择袭击/商队等的派系来源 |
| `recent_events[]` | 数组 | type(事件label) + category(分类标签) | 维持叙事连贯性 |
| `recent_deaths[]` | 数组 | pawn_name | 独立死亡记录栈 |
| `deviation_report[]` | 数组 | 上周期执行失败的事件 | Phase 1 占位，Phase 3 填充 |

**Incident category 值集合（已确认出现过）：** `ThreatBig`, `ThreatSmall`, `Misc`, `FactionArrival`, `OrbitalVisitor`, `AllyAssistance`, `GiveQuest`, `DiseaseHuman`, `DiseaseAnimal`, `ShipChunkDrop`

---

## 3. 文件产出清单（14 源文件 + 1 XML Def + About）

```
StoryMaker/
├── About/About.xml                              # 元数据，依赖 brrainz.harmony
├── Defs/Storyteller_StoryMaker.xml               # ParentName="BaseStoryteller"，comps 最小化
├── Assemblies/StoryMaker.dll                     # 编译产物 (Debug)
├── Source/StoryMaker/StoryMaker/
│   ├── StoryMaker.cs                             # Mod 入口 + Mod Settings UI
│   ├── HarmonyPatches.cs                         # [StaticConstructorOnStartup] Harmony.PatchAll()
│   ├── Patch_StorytellerTick.cs                  # Prefix on Storyteller.StorytellerTick()
│   ├── Patch_IncidentTryExecute.cs               # Postfix on IncidentWorker.TryExecute()
│   ├── Patch_DeathLetter.cs                      # Postfix on LetterStack.ReceiveLetter(Letter,…)
│   ├── Core/
│   │   ├── StoryMakerSettings.cs                 # B/N/T/R_max + deathRaceWhitelist + lowToken/debug
│   │   ├── StoryMakerState.cs                    # ack / permanentDegraded / consecutiveEmptyPlans
│   │   └── StoryMakerExpose.cs                   # Scribe 序列化骨架
│   ├── Snapshot/
│   │   ├── ISnapshotField.cs                     # { Key, Collect(), IncludeInLowTokenMode } 接口
│   │   ├── GameStateSnapshot.cs                  # 数据模型 + FactionRelationEntry / RecentEventEntry / DeathEntry / DeviationEntry
│   │   ├── SnapshotCollector.cs                  # 字段注册表 + Collect(fromTick, toTick)
│   │   ├── SnapshotSerializer.cs                 # StringBuilder 手写 JSON
│   │   ├── SnapshotFields.cs                     # 11 个 ISnapshotField 实现
│   │   ├── IncidentWhitelist.cs                  # [StaticConstructorOnStartup] 程序化构建白名单 + 日志输出
│   │   └── IncidentEventStack.cs                 # 双栈：IncidentStack + DeathStack，PopAll 清空
│   └── Schedule/
│       └── EventScheduler.cs                     # OnTick(curTick)：新游戏检测清栈 + 60000tick快照输出
```

---

## 4. 关键架构决策（Phase 1 期间确定）

| # | 决策 | 结论 |
|---|------|------|
| 1 | StorytellerDef 继承方案 | `ParentName="BaseStoryteller"`，移除所有自动事件 comp，仅保留 Caravan/Temp 的 3 条 CategoryIndividualMTBByBiome + 黑衣人 |
| 2 | Tick 注入点 | Harmony Prefix on `Storyteller.StorytellerTick()` |
| 3 | Incident 捕获 hook | **最终方案：** Postfix on `IncidentWorker.TryExecute()`（非 `Storyteller.TryFire`）。前者捕获所有事件（含开发模式、任意叙事者），后者只捕获 storyteller comp 生成的事件 |
| 4 | 快照近期事件方案 | Letter 方案废弃 → **Incident 白名单栈方案**。白名单程序化构建：排除 category="Special" + 按 defName 精确排除 4 个远行队事件（Ambush/ManhunterAmbush/CaravanMeeting/CaravanDemand）+ DeepDrillInfestation |
| 5 | 死亡记录 | 仅捕获 `LetterDefOf.Death`，从 `lookTargets.PrimaryTarget.Pawn` 提取 Pawn，过滤种族白名单 + 玩家派系 |
| 6 | 跨存档隔离 | `EventScheduler.OnTick` 每次检测 `gameStartAbsTick` 变化，自动清空两栈 |
| 7 | 白名单构建方式 | 程序化从 `DefDatabase<IncidentDef>` 构建（非硬编码），排除时按 defName 而非 targetTags（避免误伤疾病等也有 Caravan 标签的事件） |
| 8 | Incident 数据输出 | 只输出 `type`（事件 label）+ `category`（分类），不输出详情。系统提示词解释 category 含义即可 |

---

## 5. Harmony Patch 清单（共 3 个 patch，4 个目标方法）

| Patch 文件 | 目标方法 | 类型 | 用途 |
|-----------|---------|------|------|
| `Patch_StorytellerTick.cs` | `Storyteller.StorytellerTick()` | Prefix | 注入 `EventScheduler.OnTick()` |
| `Patch_IncidentTryExecute.cs` | `IncidentWorker.TryExecute(IncidentParms)` | Postfix | 捕获所有执行的事件 → 入 Incident 栈 |
| `Patch_DeathLetter.cs` | `LetterStack.ReceiveLetter(Letter, string, int, bool)` | Postfix | 捕获 Death 信件 → 提取 Pawn → 入 Death 栈 |

> `Patch_DeathLetter` 必须指定完整参数类型 `new Type[] { typeof(Letter), typeof(string), typeof(int), typeof(bool) }`，因为 `LetterStack.ReceiveLetter` 有 3 个重载，不消歧义会抛 `AmbiguousMatchException`。

---

## 6. Settings 当前参数

| 参数 | 默认值 | 说明 | UI 控件 |
|------|--------|------|---------|
| `bufferDays` | 2 | B：缓冲期（游戏天数） | 滑条 1~10 |
| `planWindowDays` | 5 | N：规划窗口（游戏天数） | 滑条 2~15 |
| `timeoutSeconds` | 60 | T：超时（现实秒） | 滑条 15~180 |
| `maxRetransmissions` | 2 | R_max：最大重传次数 | 滑条 0~5 |
| `playerPersonality` | "" | 玩家个性化描述 | 多行文本区 |
| `deathRaceWhitelist` | ["Human"] | 死亡记录种族白名单 | 逗号分隔文本区 |
| `lowTokenMode` | false | 低 Token 模式 | 复选框 |
| `debugMode` | false | 开发者调试模式 | 复选框 |

---

## 7. Phase 1 期间遇到的坑

| 坑 | 现象 | 修复 |
|----|------|------|
| 文件作用域命名空间 | `CS8370: Feature 'file-scoped namespace' not available in C# 7.3` | `.csproj` 加 `<LangVersion>10.0</LangVersion>` |
| Harmony 不自动发现 patch | Patch 不生效 | 创建 `HarmonyPatches.cs` + `[StaticConstructorOnStartup]` + `harmony.PatchAll()` |
| `TaggedString` 值类型不能用 `?.` | `CS0023: Operator '?' cannot be applied to operand of type 'TaggedString'` | 直接调用 `.ToString()`，然后判空 |
| `LetterStack.ReceiveLetter` 重载歧义 | `AmbiguousMatchException` → 所有 patch 失效 | 指定 `new Type[] { typeof(Letter), typeof(string), typeof(int), typeof(bool) }` |
| `IncidentWhitelist` 输出不出现 | 静态构造函数延迟触发 | 加 `[StaticConstructorOnStartup]` 强制启动时执行 |
| 疾病事件全部缺失 | `targetTags.Contains(Caravan)` 误杀 | 改为 defName 精确排除 |
| 死亡栈跨存档污染 | static 字段 AppDomain 级别不释放 | `EventScheduler` 检测 `gameStartAbsTick` 变化时清栈 |
| 开发模式事件不入栈 | `TryFire` 不是通用入口 | 换到 `IncidentWorker.TryExecute()` |

---

## 8. Phase 2 入口信息

### 8.1 Phase 2 目标（来自项目规划书）

实现 LLM 通信 + Prompt 构建，完成一次完整的请求-响应循环。具体任务：
- `AIProviderRegistry` + `ProviderDef`（含 DeepSeek、OpenAI、Gemini 预设）
- `AIChatServiceAsync`（MonoBehaviour + UnityWebRequest + 协程 + 回调队列 + HTTP 层错误处理）
- 双层重试（HTTP 层 5xx/429 冷却重试 + TCP 协议层 R_max 重传）
- `contextVersion` 上下文变化保护
- `PromptBuilder` + `PromptTemplates`（三段式 + 多语言系统提示词模板 + 语言自动匹配）
- `PromptInjector`（外接系统提示词注入）
- `DebugLogger`（开发者开关，全量请求/回复保存到 AppData）
- Mod 配置 UI 扩展（API Key、Provider 选择、模型名 + 现有参数）

### 8.2 Phase 2 可直接使用的 Phase 1 基础设施

- **`StoryMakerSettings`** — 已有所有 TCP 参数（B/N/T/R_max）、lowTokenMode、debugMode。Phase 2 需新增 API Key、Provider、ModelName 字段
- **`StoryMakerState`** — 已有 ack、permanentDegraded。Phase 2 需添加 contextVersion
- **`SnapshotCollector.Collect(fromTick, toTick)` → `GameStateSnapshot`** — 直接获取快照对象
- **`SnapshotSerializer.ToJson(snapshot)` → `string`** — JSON 序列化（会用 StringBuilder 手写，不依赖 Unity JsonUtility）
- **`EventScheduler.OnTick(curTick)`** — Phase 2 在此处接入 TCP 滑动窗口状态机
- **`IncidentWhitelist.IsWhitelisted(defName)` / `GetCategory(defName)` / `GetLabel(defName)`** — 白名单查询
- **Mod Settings UI** — `StoryMaker.DoSettingsWindowContents()` 已有 Listing_Standard 布局模式，可直接扩展

### 8.3 关键参考模组

- `参考模组RimChat/RimChat/AI/AIChatServiceAsync.cs`（2406 行）— MonoBehaviour + UnityWebRequest 异步模式，含 HTTP 错误处理、contextVersion、多 provider 支持
- `参考模组RimChat/RimChat/Config/ApiConfig.cs` — API 配置数据结构参考

### 8.4 约束提醒

- **禁止使用 C# async/await**（Unity Mono 运行时支持不完整）
- HTTP 回调必须通过 `MonoBehaviour.Update()` + 主线程回调队列投递
- 日志统一前缀 `[StoryMaker]`
- 所有凭证从 Mod Settings 读取，不硬编码
- RimWorld API 查询使用 RimSage MCP 工具

---

## 9. 项目规划书版本

`项目规划.md` 已同步更新以下章节以反映 Phase 1 实际实现：
- 3.2.1 快照字段清单（新增 `recent_deaths[]`，更新 `recent_events[]` 为 Incident 方案）
- 3.2.1 示例 JSON
- 第 5 节 Phase 1 任务清单（全部标记完成）
- 6.1 ADR #4（快照字段方案更新）
- 6.2 Phase 1 待定项（Letter 过滤 → Incident 白名单调整）
