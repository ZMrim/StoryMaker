# Phase 3 前期规划：回复解析 + 事件调度 + 事件执行

## 目标

LLM 回复被解析为结构化事件计划，经验证后排入队列，按精确 tick 触发实际游戏事件。

---

## 1. 待定事项决议

### 1.1 事件类型：暴露具体事件 vs 暴露类别

**决议：暴露具体事件类型（如 `RaidEnemy`、`Disease_Flu`），不暴露 Category。**

理由：
- Phase 2 测试已验证 LLM 能正确输出具体事件类型（`RaidEnemy`、`NoxiousHaze`、`Disease_Flu`、`TraderCaravanArrival`）
- 系统提示词中已按具体事件列出清单，LLM 理解良好
- Category 对 LLM 来说过于抽象（`ThreatBig` 无法传达是袭击还是虫害还是机械族），LLM 无法做出有意义的叙事选择
- 事件白名单 `IncidentWhitelist` 已维护全部具体 defName，直接复用

**关联改动（已实施）：** `recent_events[].type` 改为 `recent_events[].event_type`，值从 label（如"心灵抚慰波"）改为 defName（如 `PsychicSoothe`），与 LLM 回复的 `event_type` 字段保持一致。

### 1.2 事件参数化：按事件类型分级控制

**决议：不同事件类型开放不同参数。LLM 指定的参数若无法正常生成，回退到原版自动选择。**

| 事件 | LLM 可控参数 | 说明 |
|------|-------------|------|
| **RaidEnemy** | `faction`、`intensity_multiplier`、`raid_strategy` | 7 种敌袭策略可选（见下方），条件不符时原版自动降级 |
| **TraderCaravanArrival / VisitorGroup** | `faction`（可选）、`trader_kind`（可选） | 派系从 `faction_relations[]` 选；商队类型未指定时原版随机 |
| **OrbitalTraderArrival** | `trader_kind`（可选） | 轨道商类型，未指定时原版随机 |
| **其余威胁事件** | `intensity_multiplier` | ManhunterPack、Infestation、AnimalInsanityMass、Disease 等 |
| **其余非威胁事件** | 无 | ResourcePodCrash、PsychicDrone、CropBlight、SolarFlare 等——LLM 只需决定触发时机，参数全部由原版自动生成 |

**`raid_strategy` 详细清单（仅限 RaidEnemy）：**

| defName | 叙事含义 | 最低点数 | 玩家感知 |
|---------|---------|---------|---------|
| `ImmediateAttack` | 直接进攻 | 0 | 标准袭击，从边缘冲进来 |
| `ImmediateAttackSmart` | 战术进攻 | 1000 | 避开炮塔火力区，识破部分陷阱 |
| `StageThenAttack` | 集结待攻 | 0 | 先在边缘集结，给玩家时间布防或主动出击 |
| `ImmediateAttackSappers` | 工兵挖地道 | 700 | 绕过防线挖穿山体/墙壁，需≥2 人 |
| `Siege` | 迫击炮围城 | 500 | 建迫击炮阵地远程轰炸，逼你出城，需≥4 人 |
| `ImmediateAttackBreaching` | 破墙突击 | 700 | 直接摧毁沿途墙壁杀进来 |
| `ImmediateAttackBreachingSmart` | 智能破墙 | 2000 | 破墙 + 避开炮塔 |

LLM 指定的策略如果点数不满足最低要求，自动回退到 `ImmediateAttack`（原版默认）。

**原则：LLM 做叙事决策（谁、多强、怎么打），原版做机制决策（多少人、什么装备、从哪来）。**

---

## 2. 垂直切片总览

```
S1: Response Parsing + Guard (AFK)
  └─ 原始 LLM 回复 → 解析 → 校验 → 结构化事件计划
     验证：发送测试请求，控制台输出解析后的事件 JSON

S2: Event Queue + Execution (AFK)
  └─ 结构化事件 → 排入队列 → 按 tick 触发 → 游戏事件实际发生
     验证：手动构造事件队列，在游戏中观察事件触发

S3: Autonomous Scheduling Loop (AFK, 依赖 S1+S2)
  └─ 全自动：窗口检测 → 请求 → 解析 → 入队 → 执行 → ACK 推进
     验证：新游戏运行，观察事件自动规划和触发
```

---

## 3. S1：Response Parsing + 三层 Guard

### 3.1 模块职责

```
Response/
├── ResponseModels.cs          # LLM 回复数据结构
├── ResponseParser.cs          # JSON 提取 + 反序列化
├── ParseGuard.cs              # 第1层：JSON 合法性
├── SchemaGuard.cs             # 第2层：必需字段校验
├── EventGuard.cs              # 第3层：事件字段合法性
└── FormatCorrectionMessages.cs # 违规修正提示词生成
```

### 3.2 数据结构（ResponseModels）

```
LLMResponse
├── plan_range: { from_tick: int, to_tick: int }
├── empty_plan: bool
├── narrative_summary: string
└── events: PlannedEvent[]

PlannedEvent
├── event_id: string           # LLM 生成的唯一 ID
├── scheduled_tick: int        # 必须为 2500 整数倍
├── event_type: string         # 必须在 IncidentWhitelist 中
├── parameters: Dictionary<string, object>
│   ├── faction: string?       # 派系名称（RaidEnemy / TraderCaravan / VisitorGroup）
│   ├── intensity_multiplier: float?  # 0.5~2.0（威胁类事件）
│   ├── raid_strategy: string? # 袭击策略（仅 RaidEnemy），7 种可选
│   └── trader_kind: string?   # 商队类型（TraderCaravan / OrbitalTrader）
├── narration_text: string?    # 叙事文本（可选）
└── narrative_context: string  # 叙事上下文
```

### 3.3 ResponseParser

**输入：** LLM 原始回复字符串（可能包裹 markdown 代码块）

**处理流程：**
1. 检测并剥离 markdown 代码块标记（` ```json ... ``` `、` ``` ... ``` `）
2. 尝试 `JsonUtility.FromJson<LLMResponse>` 反序列化
3. 如果失败，尝试提取首个 `{` 到末个 `}` 之间的内容后重试
4. 成功返回 `LLMResponse`，失败返回 null + 失败原因

**参考 RimChat 的 `AIJsonContentExtractor` 和 `DialogueResponseEnvelopeParser`——优先结构化解析，失败回退到文本提取。**

### 3.4 三层 Guard

每层 Guard 接口统一：

```
GuardResult {
    bool IsValid;
    string ViolationTag;       // 违规标签，如 "PARSE_ERROR", "MISSING_PLAN_RANGE"
    string ViolationDetail;    // 人类可读的违规描述
    List<string> ViolatedFields; // 具体违规字段列表
}
```

**第1层 ParseGuard：JSON 合法性**

| 检查项 | 违规标签 |
|--------|---------|
| 回复是否为空字符串 | `PARSE_EMPTY_RESPONSE` |
| 是否能提取到有效 JSON（首 `{` 末 `}`） | `PARSE_NOT_JSON` |
| 反序列化是否成功 | `PARSE_DESERIALIZE_FAILED` |

**第2层 SchemaGuard：必需字段校验**

| 检查项 | 违规标签 |
|--------|---------|
| `plan_range` 对象存在 | `MISSING_PLAN_RANGE` |
| `plan_range.from_tick` 为正整数 | `INVALID_FROM_TICK` |
| `plan_range.to_tick` 为正整数，且 > from_tick | `INVALID_TO_TICK` |
| `events` 为数组（即使为空） | `MISSING_EVENTS_ARRAY` |
| `empty_plan` 为 bool | `MISSING_EMPTY_PLAN` |
| `narrative_summary` 为非空字符串 | `MISSING_NARRATIVE_SUMMARY` |

**第3层 EventGuard：事件字段合法性**

对 `events[]` 中每一项检查：

| 检查项 | 违规标签 |
|--------|---------|
| `event_id` 为非空字符串 | `INVALID_EVENT_ID` |
| `scheduled_tick` 在 `plan_range` 范围内 | `TICK_OUT_OF_RANGE` |
| `scheduled_tick` 为 2500 整数倍 | `TICK_NOT_ALIGNED` |
| `event_type` 在 `IncidentWhitelist` 中 | `UNSUPPORTED_EVENT_TYPE` |
| `parameters` 为有效字典（或 null） | `INVALID_PARAMETERS` |
| `intensity_multiplier` 在 0.5~2.0 范围内（如有） | `INVALID_INTENSITY` |
| `narration_text` 若存在须为非空字符串 | `INVALID_NARRATION` |

**违规事件处理：** 从 events 数组中移除违规事件（记录日志），继续校验其余事件。如果移除后 events 为空且 `empty_plan == false`，触发修正请求。

### 3.5 修正重试机制

**参考 RimChat 的 `while(true)` + `Append*RetryMessage` 模式。**

每个 Guard 层最多重试 **1 次**。重试次数用尽后：
- ParseGuard 失败 → 放弃本次回复（触发 TCP 层重传）
- SchemaGuard 失败 → 放弃本次回复（触发 TCP 层重传）
- EventGuard 失败 → 移除违规事件，保留合法事件继续

**修正消息格式（参考 RimChat 的双语标签前缀模式）：**

```
STORYMAKER_{VIOLATION_TAG}
你的上一条回复存在问题：{违规描述}
请修正后重新输出完整的 JSON 对象。首字符 { 末字符 }，不要附加 markdown 或自然语言说明。

具体问题：
- {字段1}: {问题描述}
- {字段2}: {问题描述}

修正要求：
{具体要求}
```

`FormatCorrectionMessages` 为每种违规标签提供对应的修正提示词模板。

### 3.6 S1 验证方式

1. 打开游戏，进入殖民地
2. 点击"发送测试请求"
3. 控制台观察：
   - `[StoryMaker] ResponseParser: 成功解析 LLM 回复`
   - `[StoryMaker] ParseGuard: 通过`
   - `[StoryMaker] SchemaGuard: 通过`
   - `[StoryMaker] EventGuard: 通过，N 个事件全部合法`
   - 或对应的违规日志
4. DebugLogger 额外保存解析后的结构化 JSON

---

## 4. S2：Event Queue + Execution Engine

### 4.1 模块职责

```
Schedule/
├── EventQueue.cs              # 按 scheduled_tick 排序的优先队列

Action/
├── ActionRegistry.cs          # event_type → IActionHandler 注册表
├── ActionExecutor.cs          # 执行分发器 + 生命周期钩子
├── IActionHandler.cs          # 事件处理接口
└── Actions/
    ├── ActionRaidEnemy.cs      # 袭击
    ├── ActionTraderCaravan.cs  # 商队抵达
    ├── ActionDisease.cs        # 疾病（人类 + 动物）
    ├── ActionManhunterPack.cs  # 猎杀人类兽群
    └── ActionPsychicDrone.cs   # 心灵噪音/抚慰
```

### 4.2 EventQueue

```csharp
class EventQueue {
    // 始终保持按 scheduled_tick 升序排列
    List<ScheduledEvent> events;

    void InsertAll(List<ScheduledEvent> newEvents);  // 插入并排序
    ScheduledEvent Peek();         // 查看队首（最早将触发的事件）
    ScheduledEvent Pop();          // 弹出队首
    int Count { get; }             // 队列深度
    List<ScheduledEvent> GetAll(); // 获取全部（用于日志/调试）
}
```

### 4.3 IActionHandler 接口

```csharp
public interface IActionHandler {
    string EventType { get; }                        // 对应 event_type 字符串
    bool CanHandle(ScheduledEvent evt);              // 是否能处理此事件
    bool Execute(ScheduledEvent evt);                // 执行事件，返回成功/失败
    bool IsAllowedInImmediateMode { get; }           // Phase 5 预留：即时模式白名单
    float MaxImmediatePointsMultiplier { get; }      // Phase 5 预留：即时模式强度上限
}
```

### 4.4 ActionRegistry

```csharp
public static class ActionRegistry {
    static Dictionary<string, IActionHandler> handlers;

    static ActionRegistry();                    // [StaticConstructorOnStartup] 注册全部 handler
    static void Register(IActionHandler h);     // 外部 Mod 也可调用
    static bool IsSupported(string eventType);  // EventGuard 使用
    static IActionHandler GetHandler(string eventType);
    static List<string> SupportedTypes { get; } // 系统提示词使用（未来可替代硬编码清单）
}
```

### 4.5 ActionExecutor

```csharp
public static class ActionExecutor {
    // 生命周期钩子（Phase 5 TTS mod 使用）
    static Func<ScheduledEvent, bool> OnEventWillExecute;
    static Action<ScheduledEvent> OnEventExecuted;
    static Action<ScheduledEvent> OnEventExecutionFailed;

    static bool Execute(ScheduledEvent evt);
}
```

执行流程：
1. 从 ActionRegistry 获取对应 handler
2. 应用 `intensity_multiplier`（参见 6.1 节）
3. 调用 `OnEventWillExecute` 钩子 → 返回 false 则中止
4. 调用 `handler.Execute(evt)`
5. 如果 `narration_text` 非空，发送 Letter 显示叙事文本
6. 调用 `OnEventExecuted` 或 `OnEventExecutionFailed` 钩子

### 4.6 5 个核心 Handler 实现

#### ActionRaidEnemy

- 调用 `IncidentDefOf.RaidEnemy.Worker.TryExecute(parms)`
- 传入参数：
  - `faction`（如有）：LLM 指定的派系名，从 `Find.FactionManager` 查找；未找到或非敌对则由原版自动选择
  - `points` × `intensity_multiplier`：LLM 强度乘数
  - `raidStrategy`（如有）：LLM 指定的 7 种策略之一，校验点数是否满足最低要求，不满足则回退原版自动选择
- 其余参数（arrivalMode、pawnGroupKind、pawnCount）由原版 `IncidentWorker_RaidEnemy` 自动解析

#### ActionTraderCaravan

- 调用 `IncidentDefOf.TraderCaravanArrival.Worker.TryExecute(parms)`
- 传入参数：`faction`（如有）、`trader_kind`（如有）
- faction/trader_kind 未指定时由原版随机

#### ActionDisease

- 统一处理人类和动物疾病
- 根据 `event_type` 查找对应的 `IncidentDef`（如 `Disease_Flu` → `IncidentDefOf.Disease_Flu`）
- 调用 `incidentDef.Worker.TryExecute(parms)`
- `intensity_multiplier` 影响疾病严重度（通过 `points` 参数）

#### ActionManhunterPack

- 调用 `IncidentDefOf.ManhunterPack.Worker.TryExecute(parms)`
- 传入参数：`animal_kind`（如有）、`points` × `intensity_multiplier`
- animal_kind 由 LLM 可选指定，未指定时原版根据生物群系自动选择

#### ActionPsychicDrone

- 处理 `PsychicDrone`（噪音）和 `PsychicSoothe`（抚慰）
- 根据 event_type 查找对应 IncidentDef
- LLM 可选指定 `gender`（Male/Female/All），默认 All

### 4.7 事件类型覆盖策略

Phase 3 采用**渐进覆盖**策略：

- **核心 5 个 Handler**（Phase 3 实现）：`RaidEnemy`、`TraderCaravanArrival`、`Disease_Flu/Plague/Malaria` 等疾病类、`ManhunterPack`、`PsychicDrone/PsychicSoothe`
- **其余事件**：通过通用 Handler 调用 `IncidentDef.Worker.TryExecute()`，不做参数定制化
- **完全不支持的事件**：不在 ActionRegistry 中的事件被 EventGuard 过滤

Phase 5 将逐批实现其余 Handler 的参数定制化。

### 4.8 S2 验证方式

1. 在 `StoryMakerManager` 中添加调试方法（或修改 `SendTestRequest` 回调），手动将解析后的事件插入 EventQueue
2. 恢复游戏时间，观察事件是否在预定 tick 触发
3. 验证：
   - 事件实际触发（控制台日志 + 游戏内效果可见）
   - `narration_text` 以 Letter 显示
   - `intensity_multiplier` 影响事件强度
   - 同一 tick 多个事件按队列顺序依次执行

---

## 5. S3：Autonomous Scheduling Loop

### 5.1 模块职责

S3 重写 `EventScheduler`，将 Phase 1 的快照输出骨架升级为完整的 TCP 滑动窗口调度器。

### 5.2 EventScheduler 状态机

```
OnTick(curTick):
    if permanentDegraded: return  // 永久降级，原版接管

    // 1. 检查事件队列：触发到达预定 tick 的事件
    while EventQueue.Count > 0 && EventQueue.Peek().scheduled_tick <= curTick:
        evt = EventQueue.Pop()
        success = ActionExecutor.Execute(evt)
        if !success:
            failedEvents.Add(evt)  // 下周期 feedback

    // 2. 检查是否需要发起新请求
    if !requestLocked && (ack - curTick < bufferTicks):
        expectedFrom = ack + 1
        expectedTo = ack + planWindowTicks
        snapshot = SnapshotCollector.Collect(expectedFrom, expectedTo)
        // 附加 deviation_report（上周期失败的事件）
        snapshot.deviationReport = BuildDeviationReport()
        messages = PromptBuilder.Build(snapshot, expectedFrom, expectedTo)
        AIChatServiceAsync.SendRequest(messages, OnResponseReceived)
        requestLocked = true
        expectedAckStart = ack + 1  // 用于 ACK 校验
        retransmitRemaining = R_max

OnResponseReceived(response):
    // S1: 解析 + Guard
    parsed = ResponseParser.Parse(response)
    guardResult = RunGuardPipeline(parsed)
    
    if guardResult.NeedsCorrection:
        correctionMsg = FormatCorrectionMessages.Build(guardResult)
        AIChatServiceAsync.SendRequest(correctionMsg, OnResponseReceived)
        return  // 锁保持，重试不计入 R_max
    
    // ACK 校验
    if parsed.plan_range.from_tick != expectedAckStart:
        Log.Warning("过期回复，丢弃")
        return
    
    // 有效回复
    requestLocked = false
    // 过滤已过期的事件（scheduled_tick < curTick）
    validEvents = parsed.events.Where(e => e.scheduled_tick >= curTick)
    EventQueue.InsertAll(validEvents)
    ack = parsed.plan_range.to_tick
    consecutiveEmptyPlans = parsed.empty_plan ? consecutiveEmptyPlans + 1 : 0
    failedEvents.Clear()  // 已反馈给 LLM
```

### 5.3 TCP 协议层重传

Phase 3 使用简化的重传机制（完整 RequestLock + 暂停冻结计时器留到 Phase 4）：

- 在 `EventScheduler.OnTick` 中检测：如果 `requestLocked == true` 且 `curTick` 已超过 `expectedAckStart`（缓冲耗尽），触发重传
- 重传次数由 `R_max` 控制
- 重传时使用相同的 messages（快照数据不变）
- 重传耗尽后触发降级（Phase 3 简化版：日志警告 + `permanentDegraded = true`，完整降级弹窗留到 Phase 4）

**与 HTTP 层重试的关系：**
- HTTP 层（AIChatServiceAsync 内）：5xx/429 快速重试 1 次，不计入 R_max
- TCP 层（EventScheduler 内）：整请求超时/失败，消耗 R_max
- 两层独立计数，互不干扰

### 5.4 ACK 管理细节

- `ack` 初始值 = 0
- 新游戏开始时，`curTick` 通常在 60000~120000 左右
- 首次检查：`ack(0) - curTick(~60000) = -60000 < B` → 立即触发首次请求
- 首次请求范围：`[ack+1, ack+1+N]` = `[1, N]`
- ACK 只在校验 `from_tick == ack+1` 通过后更新

### 5.5 deviation_report 反馈

- `failedEvents` 列表收集上周期执行失败的事件
- 下次请求时附加到快照的 `deviation_report` 字段
- LLM 在 system prompt 中被要求：如果 deviation_report 非空，应重新评估这些事件
- 失败事件信息：`{ event_id, event_type, fail_reason }`

### 5.6 当前 EventScheduler 的改造

Phase 1 的 `EventScheduler.OnTick` 目前只做快照输出。S3 将其完全替换为上述状态机。

保留的 Phase 1 行为：
- `gameStartAbsTick` 变化检测 → `contextVersion++`
- 日志输出（增强：输出 ACK 位置、队列深度、锁状态）

### 5.7 S3 验证方式

1. 配置 API Key 和模型
2. 开始新游戏
3. 观察控制台：
   - Day 0: `ack=0 < curTick+B` → 自动发送首次请求
   - LLM 回复 → 解析/Guard 通过 → 事件入队
   - 游戏进行中事件自动触发
   - 窗口即将耗尽时自动发送下一次请求
4. 验证：
   - 事件在预定 tick 触发
   - ACK 正确推进
   - 窗口衔接无间隙
   - 超时重传正常工作（可通过断开网络测试）
   - deviation_report 正确反馈

---

## 6. 关键机制

### 6.1 intensity_multiplier 机制

**数据流：**

```
LLM 回复: { "intensity_multiplier": 1.3 }
  → PlannedEvent.intensity_multiplier = 1.3
  → ActionExecutor.Execute():
      原版点数 = StorytellerUtility.DefaultThreatPointsNow(target)
      LLM 调整后 = 原版点数 × intensity_multiplier  // 钳制 0.5~2.0
      策略调整后 = LLM 调整后 × raidStrategy.pointsFactorCurve.Evaluate(LLM调整后)
      parms.points = 策略调整后
      incidentWorker.TryExecute(parms)
```

两层乘数：
1. **`intensity_multiplier`**（LLM 控制）：叙事层面的强度意图。"小骚扰"=0.5、"正常袭击"=1.0、"全面入侵"=2.0
2. **`raidStrategy.pointsFactorCurve`**（系统自动）：每种策略有内置修正。围攻 0.8x（人数少但带迫击炮），工兵 0.85x，破墙 0.7x。策略越"聪明"，人数越少

- 仅威胁类事件（ThreatBig/ThreatSmall）使用 `intensity_multiplier`
- 非威胁事件（商队、访客、天气等）忽略此字段
- 钳制范围：0.5~2.0（与 system prompt 中的要求一致）
- 日志输出：`[StoryMaker] RaidEnemy: 原版=1200 × LLM=1.3 × 策略(Siege)=0.8 → 最终=1248`

### 6.2 请求锁简化方案

Phase 3 使用 `StoryMakerState.requestLocked`（bool 字段，已存在）作为简单锁：

- `requestLocked == true` → 不允许发起新请求
- `requestLocked == false` → 允许发起新请求
- 锁在 `SendRequest` 时置 true，在 `OnResponseReceived` 或重传耗尽时置 false

**Phase 4 将替换为完整的 `RequestLock` 类**，增加：
- 现实时间计时器（`Time.realtimeSinceStartup`）
- 暂停冻结检测（`Find.TickManager.Paused`）
- 超时回调
- 飞行中请求的引用

### 6.3 contextVersion 校验

已有机制（Phase 2），S3 在 `OnResponseReceived` 回调中利用此机制丢弃陈旧回调。

### 6.4 过期事件过滤

- `OnResponseReceived` 中：丢弃 `scheduled_tick < curTick` 的事件（已过期）
- 读档时（Phase 4 完整实现）：过滤队列中 `generated_at_world_tick` 与当前 `Find.TickManager.TicksGame` 差距过大的事件

---

## 7. 依赖关系

```
S1 (Response Parsing + Guard)
  ├── 依赖: Phase 2 的 AIChatServiceAsync, PromptBuilder, IncidentWhitelist
  └── 产出: ResponseModels, ResponseParser, ParseGuard, SchemaGuard, EventGuard, FormatCorrectionMessages

S2 (Event Queue + Execution)
  ├── 依赖: S1 (使用 ResponseModels 数据结构)
  ├── 依赖: Phase 1 的 IncidentWhitelist, SnapshotFields
  └── 产出: EventQueue, ActionRegistry, ActionExecutor, IActionHandler, 5 个 Action Handler

S3 (Autonomous Scheduling Loop)
  ├── 依赖: S1 (ResponseParser + Guards), S2 (EventQueue + ActionExecutor)
  ├── 依赖: Phase 2 的 AIChatServiceAsync, PromptBuilder, SnapshotCollector, StoryMakerState
  └── 产出: EventScheduler 完整实现, ACK 管理, 重传, deviation_report
```

---

## 8. 文件产出预估

```
Source/StoryMaker/StoryMaker/
├── Response/
│   ├── ResponseModels.cs          # S1: LLMResponse, PlannedEvent, PlanRange, GuardResult
│   ├── ResponseParser.cs          # S1: JSON 提取 + 反序列化
│   ├── ParseGuard.cs              # S1: 第1层 Guard
│   ├── SchemaGuard.cs             # S1: 第2层 Guard
│   ├── EventGuard.cs              # S1: 第3层 Guard
│   └── FormatCorrectionMessages.cs # S1: 修正提示词模板
├── Schedule/
│   ├── EventScheduler.cs          # S3: 重写（替换 Phase 1 骨架）
│   └── EventQueue.cs              # S2: 优先队列
├── Action/
│   ├── IActionHandler.cs          # S2: 接口
│   ├── ActionRegistry.cs          # S2: 注册表
│   ├── ActionExecutor.cs          # S2: 执行分发器 + 钩子
│   └── Actions/
│       ├── ActionRaidEnemy.cs      # S2
│       ├── ActionTraderCaravan.cs  # S2
│       ├── ActionDisease.cs        # S2
│       ├── ActionManhunterPack.cs  # S2
│       └── ActionPsychicDrone.cs   # S2
└── Core/
    └── StoryMakerState.cs          # S3: 新增 expectedAckStart, retransmitRemaining, failedEvents
```

---

## 9. Phase 3 不包含的内容（留到 Phase 4+）

| 内容 | 负责阶段 | 说明 |
|------|---------|------|
| `RequestLock` 类（现实时间计时器 + 暂停冻结） | Phase 4 | Phase 3 用简单 bool 锁 |
| `DegradationHandler`（弹窗 UI + 重试/放弃） | Phase 4 | Phase 3 降级仅日志 + 标记 |
| `EmptyPlanGuard`（连续空事件检测 + 强制要求） | Phase 4 | Phase 3 仅维护计数器 |
| `StoryMakerExpose`（存档序列化） | Phase 4 | EventQueue 等暂不序列化 |
| 底部菜单管理面板 | Phase 5 | |
| 剩余 40+ 事件 Handler 参数定制化 | Phase 5 | Phase 3 用通用 Handler 兜底 |
| 交互式对话（Dialogue） | Phase 5 | |
