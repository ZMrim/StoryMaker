# Phase 5 前期规划：交互式对话 + 完善打磨

## 阶段目标

在前四个阶段搭建的完整调度协议和存档兼容基础之上，Phase 5 实现两大核心功能：(1) 玩家与 AI 叙事者的自由对话系统，支持即时事件触发；(2) 游戏底部菜单管理面板，让玩家能查看状态和手动干预。同时完成事件类型全量覆盖、stale 事件检测、多语言 UI 翻译和性能优化，使模组达到可正式发布的品质。

具体目标：
1. **交互式对话系统**：玩家通过游戏内输入框与叙事者对话，叙事者可绕过事件队列即时触发一次性事件
2. **底部菜单管理面板**：状态查看、恢复连接、跳过窗口、Token 统计等完整管理功能
3. **事件类型全量覆盖**：ActionRegistry 覆盖三位原版叙事者的全部事件类型
4. **Stale 事件检测**：利用 Phase 4 已序列化的 `generated_at_world_tick` 检测过期事件
5. **多语言 UI 翻译**：非提示词部分的 UI 文本中英文双语支持
6. **性能优化**：快照采集缓存、JSON 序列化优化
7. **完整集成测试**：网络故障、读档恢复、多 mod 兼容、Token 消耗验证

---

## 1. 现状分析

### 1.1 Phase 4 已提供的基础设施

| 组件 | Phase 4 状态 | Phase 5 可用性 |
|------|-------------|---------------|
| **RequestLock** | 正式请求锁 + 暂停冻结计时器 | 对话通道可复用其设计模式，但需独立的轻量锁（对话与调度不互斥） |
| **DegradationHandler** | 降级弹窗 + 重试/放弃 | 弹窗 UI 模式可复用到管理面板；恢复连接逻辑已就绪 |
| **EmptyPlanGuard** | 连续空事件检测 + 强制修正 | 对话通道不需要此 Guard（对话是玩家主动发起，无"空事件"概念） |
| **WorldComponent 序列化** | 全部状态持久化 | 对话状态和对话安全计数器需要新增序列化 |
| **ActionExecutor** | 事件生命周期钩子 + deviation_report | IActionHandler 接口已有 `IsAllowedInImmediateMode` / `MaxImmediatePointsMultiplier` 预留字段 |
| **ActionRegistry** | 5 个核心 Handler + 通用兜底 | 注册表模式可直接扩展，无需改动架构 |
| **PlannedEvent.generated_at_world_tick** | 已设置、已序列化 | 可直接用于 stale 检测，无需新增字段 |
| **PromptInjector** | 外接提示词注入（C# API + JSON 配置） | 对话通道的 system prompt 同样可通过此机制扩展 |

### 1.2 Phase 3/4 遗留的已知限制

| 限制 | 来源 | Phase 5 处理方式 |
|------|------|-----------------|
| 40+ 事件 Handler 参数定制化 | Phase 3 已知限制 | 任务 5.3：全量覆盖 |
| `generated_at_world_tick` 未用于 stale 检测 | Phase 4 已知限制 | 任务 5.4：stale 事件检测 |
| 底部菜单管理面板 | Phase 3/4 已知限制 | 任务 5.2：StoryMakerBottomTab |
| 交互式对话 | Phase 3 已知限制 | 任务 5.1：Dialogue 系统 |
| readme 资料更新 | Phase 4 已知限制 | 任务 5.8：文档更新 |

### 1.3 当前代码中 Phase 5 相关的"预留"

- `IActionHandler.IsAllowedInImmediateMode` — bool 属性，当前所有 Handler 均返回 `false`（Phase 5 需要根据安全限制配置返回正确值）
- `IActionHandler.MaxImmediatePointsMultiplier` — float 属性，当前均返回 `0.5f`（Phase 5 需要与无限制模式开关联动）
- `EventExecutionMode` 枚举 — 已在 ResponseModels/ActionExecutor 中定义 `Queued` / `Immediate`，但 `Immediate` 分支从未被调用
- `Dialogue/` 目录 — 在项目规划书 2.1 节模块总览中已预留，但目录本身和三个类文件均未创建

### 1.4 关键约束

- **对话与调度的关系**：对话是独立的轻量通道，不应阻塞调度请求（调度是模组的核心命脉）。两个通道需要各自的锁，互不干扰
- **即时事件安全性**：对话触发的即时事件必须有严格的安全限制（频率、类型、强度），不能允许玩家通过反复对话"刷"事件
- **TTS 同步要求**：对话中的文字显示、语音播放、即时事件触发必须同步进行（调度队列中的 narration_text 则允许跳过语音直接触发事件）
- **底部选项卡注册**：RimWorld 的 `MainTabDef` 和 `MainButtonWorker` 有特定的注册方式，需要通过 RimSage 查阅原版实现
- **翻译系统**：RimWorld 使用 `Keyed` 目录下的 XML 文件和 `"key".Translate()` 方法，不能自行发明翻译机制

---

## 2. 任务分解

### 任务 5.1：StoryMakerBottomTab（游戏底部菜单管理面板）

**优先级**：最高（为对话提供 UI 入口，同时独立可验证）

**目标**：在 RimWorld 底部菜单栏增加 StoryMaker 选项卡，显示运行时状态和操作按钮。

**实现方式详述**：

#### 5.1.1 底部选项卡注册

RimWorld 的底部菜单栏通过 `MainTabDef` 和 `MainButtonWorker` 机制工作。实现步骤：

1. 通过 RimSage 查阅原版 `MainTabDef`（如 `World`、`History`、`Factions`）的 XML 定义和 `MainButtonWorker` 实现
2. 创建 `MainButtonWorker_StoryMaker` 类，继承 `MainButtonWorker`，重写 `Activated()` 方法打开自定义窗口
3. 在 `Defs/` 目录下创建 `MainTabDef` XML（或通过 C# `DefGenerator` 动态注册）
4. 参考 RimWorld `MainTabWindow_History` 等类的窗口实现模式

#### 5.1.2 管理面板窗口

创建 `StoryMakerBottomTab.cs` 作为主窗口类（继承 `MainTabWindow` 或 `Window`）：

- 窗口尺寸和位置遵循原版底部选项卡窗口的惯例
- 使用 `Listing_Standard` 布局（与 Mod Settings 页面相同的 UI 模式）

**显示信息区**：

| 信息项 | 数据来源 | 显示格式 |
|--------|---------|---------|
| 连接状态 | `StoryMakerState.permanentDegraded`, `RequestLock.IsLocked` | 正常 / 请求中 / 降级中 / 永久降级（颜色标记） |
| ACK 位置 | `StoryMakerState.ack` | "第 X 天"（ack / 60000） |
| 事件队列深度 | `EventQueue.Count` | "X 个待执行事件" |
| 上次请求时间 | `EventScheduler` 记录 | 游戏内日历时间 |
| 上次错误 | `StoryMakerState.degradationReason` | 错误类型 + 发生时间 |
| Token 消耗 | 累计统计（需新增计数器） | 本次会话 prompt/completion/total |

**操作按钮区**：

| 按钮 | 实现逻辑 | 适用场景 |
|------|---------|---------|
| **恢复连接** | 调用 `StoryMakerState.ResetDegradation()` + 忽略缓冲期立即发请求 | API Key 问题解决后恢复 |
| **跳过当前窗口** | 若 `RequestLock.IsLocked`，强制解锁（丢弃飞行请求），保持 ack 不动 | LLM 响应异常缓慢时主动放弃 |
| **立即刷新** | 忽略缓冲期，立即从 `ack+1` 发起请求 | 读档后发现 ack 太旧 |
| **重置统计** | 清零 EmptyPlanGuard 计数器 + Token 统计 | 调试用途 |

#### 5.1.3 对话入口

在管理面板中增加一个"与叙事者对话"按钮或文本输入区域，作为交互式对话的 UI 入口：

- 点击后显示文本输入框（使用 `Widgets.TextField` 或弹出独立对话窗口）
- 发送按钮触发 `DialogueHandler.SendMessage(playerText)`
- 对话历史在窗口中滚动显示

#### 5.1.4 需要的原版 API 研究（RimSage）

- `MainTabDef` 的 XML 结构和注册方式
- `MainButtonWorker` 的 `Activated()` 和 `InterfaceTryActivate()` 方法
- `MainTabWindow` 基类的 `DoWindowContents()` 重写模式
- 底部选项卡图标的加载方式（`Texture2D`）
- `Listing_Standard` 的按钮、标签、分隔线 API

---

### 任务 5.2：交互式对话系统（Dialogue）

**优先级**：高（Phase 5 核心功能）

**目标**：实现玩家与 AI 叙事者的自由文本对话，支持叙事者即时触发一次性事件。

**实现方式详述**：

#### 5.2.1 对话状态机

`DialogueHandler` 管理以下状态：

```
Idle → WaitingForResponse (玩家发送消息后)
  → 收到回复 → Idle
  → 超时 → Idle (显示错误提示)
```

- 对话状态机与调度 `RequestLock` 完全独立——两个通道可以同时进行
- 对话请求有独立的超时时间（建议 30 秒，比调度请求的 60 秒短，因为数据量小）
- 对话请求失败时，不触发降级弹窗（对话是辅助功能），仅显示轻量提示

#### 5.2.2 对话 Prompt 构建（DialoguePromptBuilder）

与调度请求的完整快照不同，对话 Prompt 是精简版：

**System Prompt（对话专用）**：
- 叙事者身份声明 + 对话风格指导
- 强调"In-character"回复（以叙事者口吻，而非 AI 助手口吻）
- 附带轻量殖民地概况（名称、人口、心情、财富、当前季节/生物群系）
- 回复格式说明（dialogue_text + 可选 immediate_event）
- 即时事件安全规则（仅非破坏性事件、强度不超过 50% 等）
- 系统提示词外部化到 `Resources/PromptTemplates/`，复用 Phase 2 的多语言模板机制

**User Message**：
- 玩家输入的对话文本
- 精简殖民地数据（只含 name, population, average_mood, food_days, total_wealth, recent_events 概要）
- 对话历史（最近 N 轮，建议保留 3~5 轮）
- 不包含 faction_relations、environment 等调度才需要的细节
- 数据量约为调度请求的 1/3 到 1/2

#### 5.2.3 对话回复解析（DialogueResponseParser）

回复 JSON 格式（来自项目规划书 3.9.4）：

```json
{
  "dialogue_text": "叙事者的对话文本",
  "immediate_event": {
    "event_type": "TraderCaravanArrival",
    "parameters": { "faction": "帝国", "trader_kind": "Caravan_Outlander_BulkGoods" },
    "narrative_context": "叙事者因玩家表扬而主动给予的奖励事件"
  }
}
```

解析流程：
1. 复用 `JsonExtractor.ExtractCleanJson()` 处理 OpenAI 格式 / markdown 包裹
2. 手动提取 `dialogue_text`（必需）和 `immediate_event`（可选）
3. `dialogue_text` 不得为空——空回复视为解析失败
4. `immediate_event` 为 null 或缺失时，仅显示对话文本
5. 解析失败时向 LLM 发送修正请求（最多 1 次，复用 Phase 3 的修正重试模式但简化）

#### 5.2.4 对话 Guard（简化版）

对话通道不需要完整的四层 Guard。仅需：

- **DialogueParseGuard**：回复是否为空、是否包含 `dialogue_text`
- **DialogueEventGuard**（仅当有 `immediate_event` 时）：事件类型是否在白名单中、参数是否合法

违规时追加修正消息（复用 `FormatCorrectionMessages` 的模式），最多重试 1 次。

#### 5.2.5 即时事件执行

当对话回复包含 `immediate_event` 时：

1. 首先检查所有安全限制（见 5.2.6）
2. 通过后，构造 `PlannedEvent` 对象（`scheduled_tick = curTick`，`generated_at_world_tick = curTick`）
3. 调用 `ActionExecutor.Execute(event, EventExecutionMode.Immediate)`
4. `EventExecutionMode.Immediate` 模式下，ActionExecutor 跳过 EventQueue，直接调用 Handler
5. 每个 `IActionHandler` 的 `IsAllowedInImmediateMode` 控制哪些事件类型允许即时执行
6. 执行结果记录到日志，但不影响 deviation_report（即时事件是奖励性质的）

**TTS 同步流程**（来自项目规划书 3.9.2）：

```
收到 LLM 对话回复
  → 解析 dialogue_text + immediate_event
  → 调用 OnGenerateTTS(dialogue_text) — TTS mod 如已注册
  → 等待 TTS 音频就绪（TTS mod 负责）
  → TTS mod 调用 OnDialogueReady(response)
  → 同步执行：显示文本 + 播放语音 + 触发 immediate_event
```

- 如果未安装 TTS mod：直接显示文本 + 触发事件
- 如果安装了 TTS mod 但音频未就绪：**等待**（与调度队列的 narration_text 策略不同——调度事件不能等，对话事件可以等）

#### 5.2.6 对话安全限制

所有安全参数在 `StoryMakerSettings` 中可配置：

**频率限制**：
- 两次对话触发的即时事件之间至少间隔 `dialogueCooldownTicks` 游戏 ticks（默认 60000 = 1 天）
- 使用游戏时间而非现实时间（防止玩家通过调整游戏速度绕过）
- 冷却计时器需要持久化到存档

**事件类型白名单**：
- 默认仅允许非破坏性事件：商队、访客、物资空投、旅行者、派系好感变化等
- 通过 `IActionHandler.IsAllowedInImmediateMode` 控制每个 Handler 的白名单状态
- 默认禁止：袭击、虫害、猎杀人类、疾病、心灵冲击等破坏性事件

**强度上限**：
- 即时事件的 points 不超过当前殖民地财富对应的 50%（`MaxImmediatePointsMultiplier = 0.5`）
- 具体乘法在 `ActionExecutor` 中计算：`finalPoints = vanillaPoints * intensity_multiplier * MaxImmediatePointsMultiplier`

**全局冷却**：
- 每个调度窗口内最多触发 1 次即时事件
- 计数器在 ACK 推进时重置
- 计数器需要持久化到存档

**无限制模式开关**：
- Mod Settings 中提供布尔开关 `unlimitedDialogueMode`（默认关闭）
- 开启后：白名单全部放行、强度上限取消（使用正常的 0.5~2.0 intensity_multiplier）、仅频率限制和全局冷却仍然生效
- 开关文本提示风险："允许叙事者通过对话触发任意事件（包括袭击等破坏性事件）"

#### 5.2.7 对话状态序列化

需要新增序列化的对话相关状态：

| 数据 | 类型 | 说明 |
|------|------|------|
| `dialogueCooldownUntil` | int | 即时事件冷却到何时（游戏 tick） |
| `dialogueImmediateCountThisWindow` | int | 当前调度窗口内已触发的即时事件数 |
| `dialogueHistory` | List\<DialogueEntry\> | 最近 N 轮对话历史（用于 Prompt 上下文） |

序列化方式：在 `StoryMakerExpose.ExposeAll()` 中添加上述字段，复用 Phase 4 的 Scribe 模式。

`DialogueEntry` 结构：
- `playerText` (string)：玩家发言
- `narratorText` (string)：叙事者回复
- `triggeredEvent` (PlannedEvent?)：此轮对话触发的即时事件（可为 null）
- `gameTick` (int)：对话发生时的游戏 tick

#### 5.2.8 对话通道错误处理

与调度通道的错误处理策略不同（对话是辅助功能）：

| 错误类型 | 处理方式 |
|---------|---------|
| 网络超时 | 显示轻量提示"叙事者暂时无法回应..."，不弹窗 |
| HTTP 401/404 | 显示提示"API 配置错误，请检查设置"，不弹窗 |
| 解析失败 | 修正重试 1 次；仍失败则显示"叙事者的话难以理解..." |
| 对话锁冲突 | 玩家连续发送多条消息时，忽略新消息（提示"请等待叙事者回应"） |

**设计原则**：对话通道的任何错误都不应影响调度通道的正常运行。

#### 5.2.9 需要的原版 API 研究（RimSage）

- 原版 `Dialog_NodeTree` 或 `FloatMenu` 的文本输入实现（参考玩家命名殖民者/角色的输入方式）
- `Widgets.TextField` / `Widgets.TextArea` 的多行文本输入 API
- 如何在非 Mod Settings 的窗口中实现输入框

---

### 任务 5.3：事件类型注册表扩展至全量

**优先级**：高（直接影响 LLM 可规划的事件范围）

**目标**：将 ActionRegistry 的覆盖范围从当前的 ~27 个重点事件 + 通用兜底，扩展至覆盖三位原版叙事者的全部事件类型。

**实现方式详述**：

#### 5.3.1 原版事件审计

通过 RimSage 系统性地审计：

1. 加载 `DefDatabase<IncidentDef>` 中的所有 IncidentDef
2. 列出完整清单（defName、category、label、workerClass）
3. 对照三位原版叙事者（Cassandra、Phoebe、Randy）的 StorytellerDef，提取各自的事件池
4. 确认哪些事件存在于叙事者的事件池中但当前 ActionRegistry 未显式注册

#### 5.3.2 事件参数化分级

对每个事件类型判断参数化程度，分为三级：

**A 级 —— 需要自定义 Handler**：
事件有独特的参数需求，LLM 需要控制特定参数。当前已有的 5 个核心 Handler 即属于此级。Phase 5 可能扩展的候选：

- `Quest` 类事件（`GiveQuest_DistressCall` 等）：可能需要 `quest_def` 参数让 LLM 选择任务类型
- `Infestation`：`intensity_multiplier` + 可选 `insect_count` 参数
- `WandererJoin`：无特殊参数，但可能需要 `pawn_kind` 参数让 LLM 选择流浪者类型
- `ResourceDrop`：可能需要 `item_type` / `item_count` 参数
- `AnimalInsanity`：类似 ManhunterPack，需要 `animal_kind` + `intensity_multiplier`

但大多数事件（天气类、杂项类）仅需 `intensity_multiplier` 或不需任何参数——这类事件走通用兜底即可，无需专门 Handler。

**B 级 —— 仅需系统提示词告知存在**：
事件本身无需 LLM 控制特殊参数，但 LLM 需要知道这些事件可选（如 `HeatWave`、`ColdSnap`、`SolarFlare`、`CropBlight`、`VolcanicWinter`、`ToxicFallout` 等）。这些事件走通用兜底，系统提示词中列出其名称和含义即可。

**C 级 —— 不应暴露给 LLM**：
一次性事件（已在 Incident 白名单中排除的 Special 类）、纯原版机制事件（如 `StrangerInBlackJoin` 由 BaseStoryteller comp 自动触发）。

#### 5.3.3 系统提示词同步更新

事件清单更新后，需要同步更新 4 个系统提示词模板（`SystemPrompt_CN.txt`、`SystemPrompt_CN_LowToken.txt`、`SystemPrompt_EN.txt`、`SystemPrompt_EN_LowToken.txt`）：

- 补充所有 B 级事件的名称和简要说明
- A 级事件更新参数说明
- 低 Token 版本可以省略 B 级事件的详细说明，仅列出事件名
- 确保 event_type 的 defName 格式一致（如 `RaidEnemy`、`Disease_Flu`、`HeatWave`）

#### 5.3.4 实现步骤

1. 用 RimSage 审计全量 IncidentDef → 生成事件清单
2. 对照三位原版叙事者的事件池 → 标记覆盖缺口
3. 对每个未覆盖的事件，判断 A/B/C 分级
4. A 级事件：编写 Handler（实现 `IActionHandler`），在 `ActionRegistry` 中注册
5. B 级事件：确认通用兜底能正确处理（`IncidentDef.Worker.TryExecute()`），在系统提示词中添加说明
6. C 级事件：确认 Incident 白名单已正确排除
7. 更新所有 4 个系统提示词模板

---

### 任务 5.4：Stale 事件检测

**优先级**：中（Phase 4 已预留字段，逻辑较简单）

**目标**：利用 `generated_at_world_tick` 在读档时检测"在旧时间线规划但已不再适用"的事件，将其转为 deviation_report 反馈给 LLM。

**实现方式详述**：

#### 5.4.1 检测逻辑

在 `EventScheduler.HandlePostLoad()` 中（Phase 4 已有的读档后处理方法），在过期事件过滤之后增加 stale 检测：

```
HandlePostLoad(curTick):
  1. 过滤 scheduled_tick < curTick 的事件（Phase 4 已有）
  2. [新增] 对剩余事件检查 stale:
     staleThreshold = 900000  // 15 天（可在 Settings 中配置）
     foreach evt in remainingEvents:
         if curTick - evt.generated_at_world_tick > staleThreshold:
             staleEvents.Add(evt)
             remainingEvents.Remove(evt)
  3. stale 事件 → ActionExecutor.AddFailedEvent() 生成 deviation_report
  4. deviation_report 附带特殊标记 "stale_event"，让 LLM 知道这些事件是因为时间过久被移除的
  5. 重建队列（Phase 4 已有）
```

#### 5.4.2 与过期事件的区别

| 检测类型 | 条件 | 处理 |
|---------|------|------|
| **过期** | `scheduled_tick < curTick` | 丢弃 + deviation_report（Phase 4 已有） |
| **stale** | `scheduled_tick >= curTick` 但 `generated_at_world_tick` 太旧 | 从队列移除 + deviation_report，让 LLM 重新评估 |

stale 检测的意义：假设玩家玩了 Day 10 存档，队列中有 LLM 为 Day 10~15 规划的事件。然后玩家回到 Day 5 的旧存档继续玩。当游戏进行到 Day 10 时，那些事件虽然 `scheduled_tick >= curTick`，但它们是为"另一条时间线"规划的——殖民地状态可能已经完全不同。将这些事件标记为 stale 并反馈给 LLM，让 LLM 基于当前殖民地状态重新规划。

#### 5.4.3 配置项

在 `StoryMakerSettings` 中新增：
- `staleThresholdDays`（默认 15 天，可调范围 5~30 天）
- 实际阈值 = `staleThresholdDays * 60000`

---

### 任务 5.5：多语言 UI 翻译

**优先级**：中（影响非中文玩家的使用体验）

**目标**：将当前硬编码的中文 UI 文本改为通过 RimWorld 标准翻译系统支持中英双语。

**实现方式详述**：

#### 5.5.1 当前硬编码文本审计

需要提取的硬编码中文文本分布在：

- `StoryMaker.cs`：Mod Settings UI 的标签、按钮文字、提示文本
- `DegradationHandler.cs`：降级弹窗的标题、正文、按钮文字
- `StoryMakerManager.cs`：测试按钮和调试面板的文本
- 系统提示词模板（已外部化，不需要翻译系统处理——它们本身就是多语言模板）
- 即将新增的 StoryMakerBottomTab 中的 UI 文本

#### 5.5.2 RimWorld 翻译系统接入

RimWorld 使用 `Languages/` 目录下的 XML 文件进行翻译。标准结构：

```
Languages/
├── ChineseSimplified/
│   └── Keyed/
│       └── StoryMaker_Keys.xml
└── English/
    └── Keyed/
        └── StoryMaker_Keys.xml
```

实现步骤：
1. 在 `Languages/` 下创建中英文翻译 XML
2. 定义所有 UI 文本的 key（如 `StoryMaker_ConnectionStatus`、`StoryMaker_RestoreConnection` 等）
3. 替换代码中的硬编码字符串为 `"StoryMaker_Key".Translate()`
4. 对于带参数的文本，使用 `"StoryMaker_Key".Translate(arg1, arg2)` 模式

#### 5.5.3 翻译覆盖范围

| 类别 | 内容 | 优先级 |
|------|------|--------|
| Mod Settings | 所有标签、按钮、提示 | 高 |
| 降级弹窗 | 标题、正文模板、按钮 | 高 |
| 管理面板 | 状态标签、按钮文字、列标题 | 高 |
| 对话 UI | 输入框提示、发送按钮、错误提示 | 中 |
| 日志消息 | `Log.Message()` 中的中文日志 | 低（开发者面向） |

#### 5.5.4 注意事项

- 系统提示词模板（`Resources/PromptTemplates/`）不走翻译系统——它们通过 `PromptTemplates` 类按语言加载
- PromptData（`Resources/PromptData/`）也不走翻译系统——它们按语言目录加载
- 翻译 key 命名规范：`StoryMaker_<Category>_<Specific>`（如 `StoryMaker_Settings_ApiKey`）

---

### 任务 5.6：性能优化

**优先级**：低（当前性能无明显问题，此任务是预防性优化）

**目标**：减少不必要的快照采集和 JSON 序列化开销。

**实现方式详述**：

#### 5.6.1 快照采集缓存

当前每次 `SendSchedulingRequest()` 调用 `SnapshotCollector.Collect()` 时，所有 `ISnapshotField` 都会被重新采集。但部分字段变化频率极低：

| 字段 | 变化频率 | 缓存策略 |
|------|---------|---------|
| `faction_relations` | 极低（仅在好感变化时） | 缓存，好感变化事件后失效 |
| `biome` | 永不变化 | 采集一次后永久缓存 |
| `season` | 极低（每 15 天变化） | 缓存，季节变化事件后失效 |
| `average_mood` | 高（每 tick 可能变化） | 不缓存 |
| `food_days` | 中（每天变化） | 不缓存（采集本身开销极小） |
| `total_wealth` | 中（交易/建筑后变化） | 不缓存 |

实现方案：
- 在 `SnapshotCollector` 中增加缓存字典 `Dictionary<string, (object value, int cachedAtTick)>`
- 每个 `ISnapshotField` 新增 `CacheDurationTicks` 属性（返回缓存有效期，-1 表示不缓存）
- `Collect()` 方法先检查缓存，命中且未过期则跳过 `Collect()` 调用

但需要权衡：这些"极低频率变化"的字段采集开销本身极小（`biome` 就是一个 `def.LabelCap` 字符串读取），引入缓存机制反而增加了复杂度。**建议保持现状，仅在遇到实际性能问题时再优化。**

#### 5.6.2 JSON 序列化优化

当前 `SnapshotSerializer.ToJson()` 使用 StringBuilder 手动拼接 JSON——这实际上已经是最快的方式（没有反射、没有中间对象）。但可以考虑两个微优化：

1. **复用 StringBuilder**：使用 `ThreadLocal<StringBuilder>` 或每次 Clear() 而非 new StringBuilder()
2. **预计算固定部分**：`request_range` 以外的大部分快照字段在每次请求之间变化不大，但拆分为固定部分和可变部分带来的复杂度不值得

**建议**：不做 JSON 序列化优化。当前 StringBuilder 方式已是 RimWorld 环境下的最优解（Unity JsonUtility 不支持 Dictionary，Newtonsoft.Json 不适用于游戏运行时）。

#### 5.6.3 对话通道的 Token 消耗优化

对话 Prompt 的数据量约为调度请求的 1/3~1/2，但仍需注意：

1. 对话历史仅保留最近 3~5 轮（而非所有历史）
2. 殖民地数据仅含核心字段（name, population, mood, food_days, wealth），不含 faction_relations 等细节
3. 对话专用 system prompt 尽量精简（不要重复调度 system prompt 中的事件列表和参数说明）

---

### 任务 5.7：完整集成测试

**优先级**：高（品质保证，但 Agent 无法直接执行游戏内测试）

**目标**：编写详细的测试计划，指导用户逐项验证 Phase 5 的全部新功能。

**测试场景清单**：

#### 5.7.1 底部管理面板

| # | 场景 | 操作 | 预期 |
|---|------|------|------|
| 1 | 面板打开 | 点击 StoryMaker 底部选项卡 | 面板正常显示，信息区数据与实际状态一致 |
| 2 | 状态显示-正常 | 调度正常运行中 | 显示"正常"，ACK 和队列深度正确 |
| 3 | 状态显示-请求中 | 调度请求飞行中 | 显示"请求中" |
| 4 | 状态显示-降级 | 触发降级后 | 显示"降级中"或"永久降级" |
| 5 | 恢复连接 | 永久降级后点击恢复连接 | 标记清除，立即发起调度请求 |
| 6 | 跳过窗口 | 请求飞行中点击跳过窗口 | 锁释放，ack 不变，重新评估 |
| 7 | 立即刷新 | 正常状态点击立即刷新 | 忽略缓冲期，立即发请求 |
| 8 | 重置统计 | 有空事件计数后点击重置 | 计数器归零 |

#### 5.7.2 交互式对话

| # | 场景 | 操作 | 预期 |
|---|------|------|------|
| 1 | 基础对话 | 输入消息 → 发送 | 收到叙事者文本回复 |
| 2 | 对话触发即时事件 | 叙事者返回 immediate_event | 事件立即触发（如商队抵达） |
| 3 | 安全限制-破坏性事件 | 尝试让叙事者触发袭击 | 被拒绝或自动转为安全事件 |
| 4 | 安全限制-频率 | 短时间内多次对话触发事件 | 第二次被冷却限制阻止 |
| 5 | 安全限制-窗口上限 | 当前调度窗口已触发 1 次即时事件 | 再次请求被拒绝 |
| 6 | 无限制模式 | 开启无限制模式 → 请求袭击 | 袭击正常触发 |
| 7 | 对话超时 | 发送后网络断开 | 轻量提示"暂时无法回应"，不影响调度 |
| 8 | 对话与调度并行 | 对话进行中时调度请求自动发起 | 两个通道独立运行，互不干扰 |
| 9 | 对话历史 | 连续对话 5 轮 | 叙事者记住前文语境 |
| 10 | 存档读档 | 对话中途存档 → 读档 | 对话历史恢复，冷却计时器恢复 |

#### 5.7.3 事件类型全量覆盖

| # | 场景 | 操作 | 预期 |
|---|------|------|------|
| 1 | 新增事件类型执行 | LLM 规划了 Phase 5 新增的事件类型 | 事件正常触发 |
| 2 | 系统提示词覆盖 | 查看 DebugLogger 的完整请求 | 事件列表包含全部事件类型 |
| 3 | 通用兜底 | LLM 选择了非常规事件 | 走通用兜底，正常执行 |

#### 5.7.4 Stale 事件检测

| # | 场景 | 操作 | 预期 |
|---|------|------|------|
| 1 | 回到旧存档 | 在 Day 20 存档 → 读到 Day 5 的旧存档 → 玩到 Day 12 | Day 20 规划的事件被标记 stale，deviation_report 包含标记 |
| 2 | 正常读档 | 正常存档 → 立即读档 | 无 stale 事件（时间差在阈值内） |

#### 5.7.5 多语言翻译

| # | 场景 | 操作 | 预期 |
|---|------|------|------|
| 1 | 中文 UI | 游戏语言设为中文 | 所有 StoryMaker UI 文本为中文 |
| 2 | 英文 UI | 游戏语言设为英文 | 所有 StoryMaker UI 文本为英文 |
| 3 | 降级弹窗 | 触发降级 | 弹窗文本与游戏语言一致 |

#### 5.7.6 多 Mod 兼容性

| # | 场景 | 操作 | 预期 |
|---|------|------|------|
| 1 | 事件注入兼容 | 使用 PromptInjector 注入自定义事件 | 注入内容正确出现在 system prompt 中 |
| 2 | TTS 钩子兼容 | 模拟 TTS mod 注册钩子 | 钩子正确触发，对话同步等待 |
| 3 | 其他叙事者 Mod | 同时加载其他叙事者 Mod | StoryMaker 的 Harmony Patch 仅对 defName=="StoryMaker" 生效，不干扰其他叙事者 |

---

### 任务 5.8：项目文档更新

**优先级**：低（但应在 Phase 5 结束时完成）

**目标**：将 Phases 1-5 的全部成果更新到项目文档。

**更新内容**：
- `项目规划.md` 第 5 节：Phase 5 任务清单标记完成
- `项目规划.md` 第 6.2 节：Phase 5 待定事项解决状态
- 模块总览中的 `Dialogue/` 和 `UI/` 目录标注为已实现
- `CLAUDE.md` 或 README 更新当前版本状态
- 各阶段总结中已知限制的关闭

---

## 3. 文件产出预估

### 3.1 新文件

| 文件 | 模块 | 说明 |
|------|------|------|
| `Source/StoryMaker/StoryMaker/Dialogue/DialogueHandler.cs` | Dialogue | 对话状态机、TTS 钩子注册、即时事件分发 |
| `Source/StoryMaker/StoryMaker/Dialogue/DialoguePromptBuilder.cs` | Dialogue | 对话专用轻量 Prompt 构建 |
| `Source/StoryMaker/StoryMaker/Dialogue/DialogueResponseParser.cs` | Dialogue | 对话回复解析（dialogue_text + immediate_event） |
| `Source/StoryMaker/StoryMaker/Dialogue/DialogueGuard.cs` | Dialogue | 对话回复简化 Guard（Parse + Event 两层） |
| `Source/StoryMaker/StoryMaker/Dialogue/DialogueSafetyLimiter.cs` | Dialogue | 即时事件安全限制检查（频率、白名单、强度、冷却） |
| `Source/StoryMaker/StoryMaker/UI/StoryMakerBottomTab.cs` | UI | 底部菜单管理面板窗口 |
| `Source/StoryMaker/StoryMaker/UI/MainButtonWorker_StoryMaker.cs` | UI | 底部选项卡的按钮 Worker |
| `Defs/MainTabDefs/StoryMaker.xml` | Defs | 底部选项卡 Def 定义 |
| `Languages/ChineseSimplified/Keyed/StoryMaker_Keys.xml` | Languages | 中文 UI 翻译 |
| `Languages/English/Keyed/StoryMaker_Keys.xml` | Languages | 英文 UI 翻译 |

### 3.2 修改文件

| 文件 | 修改内容 |
|------|---------|
| `StoryMaker.cs` | 硬编码 UI 文本 → `.Translate()`；新增 `unlimitedDialogueMode` / `dialogueCooldownDays` / `staleThresholdDays` 设置项 |
| `Core/StoryMakerSettings.cs` | 新增对话安全限制参数（cooldown, stale threshold, unlimited mode） |
| `Core/StoryMakerState.cs` | 新增 `dialogueCooldownUntil`、`dialogueImmediateCountThisWindow`、`dialogueHistory` 字段 |
| `Core/StoryMakerExpose.cs` | 新增对话状态序列化 |
| `Response/ResponseModels.cs` | 新增 `DialogueEntry` 数据类 + IExposable |
| `Action/ActionExecutor.cs` | `EventExecutionMode.Immediate` 分支实现；安全检查调用；TTS 钩子触发 |
| `Action/ActionRegistry.cs` | 事件类型扩展注册（新增 N 个 Handler） |
| `Action/IActionHandler.cs` | `IsAllowedInImmediateMode` 实现（各 Handler 返回正确的布尔值） |
| `Schedule/EventScheduler.cs` | `HandlePostLoad` 增加 stale 事件检测 |
| `Schedule/DegradationHandler.cs` | 硬编码 UI 文本 → `.Translate()` |
| `Management/StoryMakerManager.cs` | 硬编码 UI 文本 → `.Translate()` |
| `Resources/PromptTemplates/` (4 文件) | 事件类型列表更新为全量 |
| `Source/StoryMaker/StoryMaker.csproj` | 新增 10 个源文件的编译条目 |

**预估**：10 新文件 + 14 修改 = 24 文件变更

### 3.3 可能需要新增的 Action Handler（A 级事件）

最终数量取决于 RimSage 审计结果。初步预估 3~5 个新 Handler：

| 候选 Handler | 覆盖事件 | 自定义参数 |
|-------------|---------|-----------|
| `ActionQuest` | GiveQuest_* 系列 | quest_def（可选）、faction（可选） |
| `ActionInfestation` | Infestation | intensity_multiplier |
| `ActionResourceDrop` | ResourcePodCrash 等 | item_type（可选）、count（可选） |
| `ActionWandererJoin` | WandererJoin / WildManWandersIn | pawn_kind（可选） |
| `ActionAnimalInsanity` | AnimalInsanity / AnimalInsanityMass 等 | animal_kind（可选）、intensity_multiplier |

---

## 4. 任务依赖关系

```
任务 5.1 (BottomTab) ─────────────────────────────┐
  │                                                │
  ├── 为任务 5.2 (Dialogue) 提供 UI 入口            │
  │                                                │
任务 5.3 (事件扩展) ── 独立 ───────────────────────┤
  │                                                │
  ├── 任务 5.2 (Dialogue) 的即时事件需要完整注册表   │
  │                                                │
任务 5.4 (Stale检测) ── 独立 ─────────────────────┤
任务 5.5 (多语言) ── 独立（可与其他任务并行）───────┤
任务 5.6 (性能优化) ── 独立，最后实施 ─────────────┤
  │                                                │
任务 5.7 (集成测试) ── 依赖所有上述任务 ────────────┤
任务 5.8 (文档更新) ── 依赖所有上述任务 ────────────┤
```

**推荐实施顺序**：
1. 任务 5.1：StoryMakerBottomTab（最先，为对话提供 UI 基础）
2. 任务 5.3：事件类型注册表扩展（独立，可与 5.1 并行）
3. 任务 5.4：Stale 事件检测（独立，快速完成）
4. 任务 5.2：交互式对话系统（依赖 5.1 的 UI + 5.3 的注册表 + 5.4 的 stale 逻辑可参考）
5. 任务 5.5：多语言 UI 翻译（等 UI 代码基本稳定后统一替换硬编码字符串）
6. 任务 5.6：性能优化（最后）
7. 任务 5.7：集成测试（全程伴随，最终汇总）
8. 任务 5.8：文档更新（阶段结束时）

---

## 5. 风险与注意事项

| # | 风险 | 影响 | 应对 |
|---|------|------|------|
| 1 | **底部选项卡注册机制**：`MainTabDef` 的注册方式可能与标准 Def 不同，需要特定 XML 结构或 `DefGenerator` 动态注册 | 任务 5.1/5.2 的 UI 入口受阻 | 优先通过 RimSage 查阅原版 `MainTabWindow_World` / `MainTabWindow_History` 的实现，确认注册方式后再动手写代码 |
| 2 | **对话通道与调度通道的并发**：两个通道可能同时发送 HTTP 请求 | LLM API 可能有限流限制（如每分钟请求数），同时发送可能触发 429 | AIChatServiceAsync 需要维护请求队列，短时间内的多个请求排队串行发送（而非并发），等待中的请求显示"队列中"状态 |
| 3 | **对话 Prompt Token 消耗**：即使精简版，每次对话仍有 ~500-800 prompt tokens | 频繁对话会增加 API 费用 | 对话 system prompt 尽量精简（不包含事件列表），对话历史严格限制在 5 轮以内 |
| 4 | **即时事件执行失败**：LLM 指定的参数（faction/trader_kind）可能因游戏状态变化而无效 | 事件静默失败，玩家期望落空 | 复用 Phase 4 的两级重试机制（自定义参数失败 → 原版默认参数回退），但即时事件失败不加入 deviation_report（与调度事件不同） |
| 5 | **对话历史序列化**：`DialogueEntry` 列表可能较长 | 存档体积增大 | 限制序列化的对话历史数量（如 5 轮），对话文本截断（如每条 500 字符） |
| 6 | **安全限制被绕过**：玩家可能通过巧妙的对话提示词工程绕过安全限制 | 白名单被攻破，破坏性事件通过即时通道触发 | 安全限制在代码层硬检查（`DialogueSafetyLimiter`），不依赖 LLM 自觉遵守。代码层检查在 LLM 解析后、事件执行前进行 |
| 7 | **TTS 钩子的异步等待**：Unity 不支持 `Task`，等待音频就绪不能用 async/await | 对话显示和事件触发延迟实现复杂 | 使用协程（`IEnumerator` + `yield return`）在 `DialogueHandler` 的 MonoBehaviour 中等待 TTS 就绪回调 |
| 8 | **事件类型审计遗漏**：某些模组专属事件或罕见事件未被审计覆盖 | LLM 无法规划这些事件 | 审计范围限定为三位原版叙事者的事件池，不要求覆盖所有可能的 Mod 事件。Mod 事件由 PromptInjector 机制负责告知 LLM |
| 9 | **对话锁与调度锁独立后死锁风险**：两个锁如果存在隐式依赖（如共享 HTTP 客户端），可能互相阻塞 | 调度和对话同时卡住 | AIChatServiceAsync 的请求队列机制确保两个通道的请求串行发送，不存在资源竞争 |
| 10 | **LLM 在对话中产生不当内容**：叙事者可能生成不符合游戏分级的内容 | 影响模组在 Workshop 的发布 | 系统提示词中加入叙事者行为约束（"你是一个 PG-13 级别的游戏叙事者，避免涉及过度暴力、政治和成人主题"） |

---

## 6. 与项目规划书的对齐

### 6.1 项目规划书 Phase 5 任务对照

| 规划书任务 | 本规划对应 | 状态 |
|-----------|-----------|------|
| `DialogueHandler` + `DialoguePromptBuilder` + `DialogueResponseParser`（含 TTS 钩子） | 任务 5.2 | 详细设计 |
| 即时事件触发机制（`EventExecutionMode.Immediate`） | 任务 5.2.5 | 详细设计 |
| 对话安全限制（tick 频率、白名单、强度上限、全局冷却、无限制模式） | 任务 5.2.6 | 详细设计 |
| `StoryMakerBottomTab`（管理面板） | 任务 5.1 | 详细设计 |
| 事件类型寄存器扩展至全量 | 任务 5.3 | 详细设计 |
| 读档过期事件处理 + deviation_report | 任务 5.4 | 详细设计 |
| 多语言文本补充 | 任务 5.5 | 详细设计 |
| 性能优化 | 任务 5.6 | 详细设计 |
| 完整集成测试 | 任务 5.7 | 详细设计 |

### 6.2 项目规划书 6.2 节 Phase 5 待定事项决议

| 待定事项 | 本规划决议 |
|---------|-----------|
| **对话安全限制的具体数值** | tick 频率：60000 (1天)；强度上限：50%；全局冷却：每调度窗口 1 次；无限制模式：仅取消白名单+强度限制，保留频率和冷却 |
| **管理面板 Token 统计 UI** | 显示 prompt/completion/total tokens，每次请求/对话后累加，存储在 `StoryMakerState` 中并序列化 |

---

## 7. 里程碑验收标准

Phase 5 完成的标准：

- [ ] **底部管理面板**：底部 StoryMaker 选项卡可点击，面板正确显示状态信息（连接状态、ACK、队列深度、Token 消耗），所有操作按钮功能正常
- [ ] **交互式对话**：玩家可输入文本并收到叙事者回复，对话历史正确维护（3~5 轮），叙事者风格与调度模式一致
- [ ] **即时事件触发**：叙事者可在对话中触发即时事件（商队等非破坏性事件），事件绕过队列立即执行，失败时回退原版默认参数
- [ ] **对话安全限制**：频率限制、白名单、强度上限、全局冷却全部生效；无限制模式开关可正确放宽限制
- [ ] **TTS 钩子**：`OnGenerateTTS` / `OnDialogueReady` 预留接口可用，等待流程可正确暂停和恢复
- [ ] **事件类型全量覆盖**：ActionRegistry 覆盖三位原版叙事者的全部事件类型，每个事件要么有自定义 Handler（A 级），要么在系统提示词中列出（B 级），要么被正确排除（C 级）
- [ ] **Stale 事件检测**：读档时检测生成时间过久的队列事件，标记 stale 并生成 deviation_report
- [ ] **多语言 UI**：所有面向玩家的 UI 文本在中英文环境下正确显示对应语言
- [ ] **对话与调度独立**：对话通道的任何错误（超时、解析失败、429 限流）不影响调度通道的正常运行
- [ ] **存档兼容**：对话状态（冷却计时器、即时事件计数、对话历史）正确序列化和恢复
- [ ] **集成测试通过**：任务 5.7 的全部测试场景通过
