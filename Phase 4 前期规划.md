# Phase 4 前期规划：完整调度协议 + 存档兼容

## 阶段目标

将当前 Phase 3 中简化的"bool 锁 + 仅日志降级 + 无 EmptyPlanGuard + 基础序列化"升级为项目规划书第 3.1、3.3、3.6 节定义的完整形态：

1. **RequestLock 正式化**：带暂停冻结计时器的正式请求锁，替代当前简化 bool
2. **DegradationHandler**：游戏内弹窗通知玩家 + 重试/放弃双操作
3. **EmptyPlanGuard**：第 4 层 Guard 完整逻辑（连续空事件检测 + 强制要求）
4. **存档完全序列化**：ack、EventQueue、PlannedEvent、IncidentEventStack、FailedEvents 等全部持久化
5. **读档恢复逻辑**：过期事件过滤、deviation_report 自动生成、contextVersion 保护

---

## 1. 现状分析

### 1.1 Phase 3 已有但需升级的部分

| 组件 | Phase 3 状态 | Phase 4 目标 |
|------|-------------|-------------|
| **请求锁** | `EventScheduler` 中 `static bool requestLocked` | 独立 `RequestLock` 类，含暂停冻结计时器、超时回调 |
| **超时检测** | 分散两处：`AIChatServiceAsync.Update()` 实时检测 + `OnTick()` 缓冲耗尽检测 | 统一到 `RequestLock`，暂停时冻结，恢复后继续 |
| **降级处理** | `StoryMakerState.MarkDegraded()` 仅设置标记 + 日志 | `DegradationHandler.ShowDialog()` 弹窗 + 自动暂停 + 重试/放弃 |
| **EmptyPlanGuard** | `consecutiveEmptyPlans` 字段存在但**从未被读写** | 完整第 4 层 Guard 逻辑：计数 → 警告 → 强制 |
| **序列化** | `StoryMakerExpose.Expose()` 只序列化 5 个基础字段 | 完整序列化：EventQueue、PlannedEvent、IncidentEventStack、FailedEvents 等 |
| **`generated_at_world_tick`** | `PlannedEvent` 无此字段 | 新增此字段，用于读档过期检测 |

### 1.2 当前代码中 Phase 4 相关的"预留"

- `StoryMakerState.requestLocked` — bool 字段，已在 `ExposeData` 中序列化（但实际上 EventScheduler 用的是自己的 static bool）
- `StoryMakerState.consecutiveEmptyPlans` — int 字段，已在 `ExposeData` 中序列化，但从未被赋值
- `EventQueue.GetAll()` — 返回队列副本，可用于序列化，但 `PlannedEvent` 的字段当前不可序列化（`Dictionary<string, string>`）

### 1.3 关键约束

- RimWorld 存档使用 `Scribe_Values` / `Scribe_Collections` / `Scribe_Deep`，序列化方法必须定义为 `ExposeData()`
- `PlannedEvent` 的 `Dictionary<string, string> parameters` 不能直接用 `Scribe_Collections.Look` 序列化（不支持嵌套字典），需要转换
- 存档/读档时机：通过 `ExposeData()` 自动调用，不能手动控制时机
- 静态类（`EventScheduler`、`ActionExecutor`、`IncidentEventStack`）的静态字段需要在外层持有者中序列化
- `contextVersion` 在存档/读档时递增，飞行中的 HTTP 请求被丢弃

---

## 2. 任务分解

### 任务 4.1：RequestLock 正式化

**目标**：将当前分散在 `EventScheduler` 中的锁逻辑提取为独立的 `RequestLock` 类，实现暂停冻结计时器。

**新文件**：`Source/StoryMaker/StoryMaker/Schedule/RequestLock.cs`

**核心逻辑**：

```
RequestLock 状态机：
  Unlocked → Locked (发送请求时)
  Locked:
    ├── 收到有效回复 → Unlocked
    ├── 实时超时 (game not paused, elapsed >= T) → OnTimeout
    └── 缓冲耗尽 (curTick >= requestSentAtTick + B) → OnBufferExhausted

OnTimeout:
  if retransmitRemaining > 0:
    retransmitRemaining--
    重传请求
    重置计时器
  else:
    OnBufferExhausted()

OnBufferExhausted:
  触发降级弹窗 (→ DegradationHandler)
```

**暂停冻结规则**：
- 游戏暂停（`Find.TickManager.Paused`）时，计时器冻结（记录已流逝时间，恢复时累加）
- 非暂停时正常计时
- 实际实现：不依赖 `Time.realtimeSinceStartup` 直接做差，而是在 `Update()` 中每帧累加 `Time.unscaledDeltaTime`（但需跳过暂停帧）

**注意**：当前 `CheckRealtimeTimeout()` 位于 `AIChatServiceAsync.Update()` 中，Phase 4 后应改为调用 `RequestLock.Update()`。

**接口设计（供 EventScheduler 使用）**：

```csharp
public class RequestLock
{
    public bool IsLocked { get; }
    public int RetransmitRemaining { get; }
    
    public void Lock(int retransmitMax);                    // 上锁，开始计时
    public bool UnlockIfMatch(int expectedAckStart);        // 条件解锁（校验通过）
    public void ForceUnlock();                              // 强制解锁（降级放弃时）
    public void Update();                                   // 每帧调用（MonoBehaviour.Update 驱动）
    
    public event Action OnRetransmit;                       // 触发重传
    public event Action<string> OnDegradationTriggered;     // 触发降级（reason）
}
```

---

### 任务 4.2：DegradationHandler（降级弹窗）

**目标**：当 `RequestLock` 触发降级时，通过游戏内弹窗通知玩家，提供重试/放弃选择。

**新文件**：`Source/StoryMaker/StoryMaker/Schedule/DegradationHandler.cs`

**弹窗设计**：

```
┌─────────────────────────────────────────────┐
│  ⚠ AI 叙事者连接失败                          │
│                                              │
│  请求超时，窗口 [Day X ~ Day Y] 未能获得       │
│  有效回复。原版叙事者已临时接管。               │
│                                              │
│  错误详情: {reason}                           │
│                                              │
│  你可以选择:                                  │
│                                              │
│  [重试] 忽略缓冲期，立即重新请求下一窗口         │
│  [放弃] 永久切换至原版叙事者                    │
│                                              │
│  (游戏已自动暂停，可从容选择)                   │
└─────────────────────────────────────────────┘
```

**实现方式选择**：
- 方案 A：`Dialog_MessageBox` + 两个按钮 — 简单但样式有限
- 方案 B：自定义 `Window`（继承 `Verse.Window`） — 灵活但代码量大
- **推荐方案 A**：使用 `Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(...))` 或手动构建 `FloatMenuOption`，因为功能简单，不需要复杂 UI。

**实际实现**：
- 参考 RimWorld 的 `DiaNode` / `Dialog_NodeTree` 模式
- 或者更简单地，使用 `Find.WindowStack.Add(new Dialog_MessageBox(text, "重试", () => OnRetry(), "放弃", () => OnGiveUp()))` 
- 但 `Dialog_MessageBox` 的构造函数参数需要对照原版 API 确认

**重试逻辑**（`OnRetry`）：
1. 清除 `permanentDegraded` 标记（如有）
2. 清除请求锁
3. 忽略缓冲期 B，立即发起从 `ack+1` 开始的请求
4. 关闭弹窗

**放弃逻辑**（`OnGiveUp`）：
1. 调用 `StoryMakerState.MarkDegraded(reason)`
2. 清除请求锁
3. 关闭弹窗
4. 原版叙事者接管

**自动暂停**：触发降级弹窗时自动调用 `Find.TickManager.Pause()`（如果未暂停）。

---

### 任务 4.3：EmptyPlanGuard（第 4 层 Guard）

**目标**：实现连续空事件检测，达到阈值后强制要求 LLM 安排事件。

**新文件**：`Source/StoryMaker/StoryMaker/Response/EmptyPlanGuard.cs`

**逻辑流程**：

```
OnResponseReceived 中，前 3 层 Guard 通过后：

1. 检查 LLMResponse:
   if empty_plan == true && events is empty:
       consecutiveEmptyPlans += 1
   else if events has items:
       consecutiveEmptyPlans = 0  // 重置
   else if empty_plan == false && events is empty:
       // LLM 故意空（有 narrative_summary 但无事件）
       consecutiveEmptyPlans = 0  // 不算连续空

2. 检查是否需要强制警告:
   if consecutiveEmptyPlans >= 3:
       下次 SendSchedulingRequest 时，在 system prompt 末尾追加:
       "警告：你已经连续 3 个规划窗口没有安排任何事件。
        本窗口你必须至少安排 1 个合理事件。如果确实无事可发生，
        请在 narrative_summary 中给出充分的叙事理由。"

3. 强制警告后的修正重试（仅限 1 次）:
   if 已追加警告 && 本次回复 still empty_plan == true && events is empty:
       // 再追加一条更严厉的修正消息
       修正消息 = "STORYMAKER_EMPTY_PLAN\n你上一窗口在收到强制要求后仍然没有安排事件。
                   请重新规划，本窗口必须包含至少 1 个合理事件。"
       调用 AIChatServiceAsync.SendRequest(修正消息, OnResponseReceived)
       保持 Locked 状态
       return  // 不推进 ACK

   if 严厉修正后仍然空:
       接受此回复（不无限重试），推进 ACK
```

**文件设计**：

```csharp
public static class EmptyPlanGuard
{
    // 检查并更新计数器，返回是否需要注入警告
    public static bool ShouldInjectWarning(LLMResponse response, StoryMakerState state);
    
    // 构建警告消息（注入到 system prompt）
    public static string BuildWarningMessage();
    
    // 判断是否需要强制修正重试
    public static bool NeedsForceRetry(LLMResponse response, bool warningWasInjected, out string correctionMessage);
}
```

**注意**：`consecutiveEmptyPlans` 计数器已在 `StoryMakerState` 中存在且已被序列化，只需在 `EmptyPlanGuard` 中读写即可。

---

### 任务 4.4：存档完全序列化

**目标**：所有运行时状态在读档后完整恢复，`ack` 和 `eventQueue` 保持原子一致性。

#### 4.4.1 PlannedEvent 数据模型增强

**修改文件**：`Source/StoryMaker/StoryMaker/Response/ResponseModels.cs`

为 `PlannedEvent` 新增 `generated_at_world_tick` 字段：

```csharp
public class PlannedEvent
{
    // ... 现有字段 ...
    public int generated_at_world_tick;  // 新增：LLM 生成此事件时的世界 tick
}
```

赋值时机：`EventScheduler.OnResponseReceived()` 中，有效回复入队前，设置 `evt.generated_at_world_tick = curTick`。

#### 4.4.2 需要序列化的完整状态清单

| 数据 | 当前位置 | 序列化方式 | 说明 |
|------|---------|-----------|------|
| `ack` | StoryMakerState | `Scribe_Values` | 已有 |
| `permanentDegraded` | StoryMakerState | `Scribe_Values` | 已有 |
| `degradationReason` | StoryMakerState | `Scribe_Values` | **新增**：读档后显示给玩家 |
| `consecutiveEmptyPlans` | StoryMakerState | `Scribe_Values` | 已有 |
| `contextVersion` | StoryMakerState | `Scribe_Values` | 已有 |
| `eventQueue` | EventScheduler (static) | `Scribe_Collections` | **新增**：队列中所有未触发的 PlannedEvent |
| `incidentStack` | IncidentEventStack (static) | `Scribe_Collections` | **新增**：未被 PopAll 消费的事件 |
| `deathStack` | IncidentEventStack (static) | `Scribe_Collections` | **新增**：未被 PopAll 消费的死亡记录 |
| `failedEvents` | ActionExecutor (static) | `Scribe_Collections` | **新增**：未反馈给 LLM 的失败事件 |

**不需要序列化的状态**（来自 3.6.2）：
- `requestLocked` / `retransmitRemaining` / `lastMessages` / `lastRequestSeq` — 读档后默认 Unlocked，由 `OnGameTick` 重新判断
- `parseRetryCount` / `schemaRetryCount` / `eventRetryCount` — 请求级别临时状态
- `expectedAckStart` / `requestSentAtTick` / `requestSentRealtime` — 请求级别临时状态
- 飞行中的 HTTP 请求 — contextVersion 机制丢弃

#### 4.4.3 PlannedEvent 序列化实现

`PlannedEvent` 不是 `IExposable`，需要通过 `Scribe_Deep` 或手动 Scribe 实现：

```csharp
// 方案：为 PlannedEvent 实现 IExposable 接口
public class PlannedEvent : IExposable
{
    public void ExposeData()
    {
        Scribe_Values.Look(ref event_id, "event_id");
        Scribe_Values.Look(ref scheduled_tick, "scheduled_tick");
        Scribe_Values.Look(ref generated_at_world_tick, "generated_at_world_tick");
        Scribe_Values.Look(ref event_type, "event_type");
        Scribe_Values.Look(ref narration_text, "narration_text");
        Scribe_Values.Look(ref narrative_context, "narrative_context");
        
        // Dictionary<string, string> 需转换为 List<KeyValuePair> 再序列化
        // 使用 Scribe_Collections 序列化键值对列表
    }
}
```

**Dictionary 序列化方案**：
- 将 `Dictionary<string, string>` 转换为 `List<string>`（格式：`"key=value"`）
- 序列化时：`parameters.Select(kv => $"{kv.Key}={kv.Value}").ToList()`
- 反序列化时：解析回 Dictionary
- 或使用两个平行 List：`List<string> paramKeys` + `List<string> paramValues`

**推荐**：使用两个平行 List（`paramKeys` + `paramValues`），避免转义问题。

#### 4.4.4 序列化入口重构

**修改文件**：`Source/StoryMaker/StoryMaker/Core/StoryMakerExpose.cs`

```csharp
public static class StoryMakerExpose
{
    public static void ExposeAll()
    {
        var state = StoryMakerState.Instance;
        if (state == null) return;
        
        // 基础状态
        Scribe_Values.Look(ref state.ack, "ack", 0);
        Scribe_Values.Look(ref state.permanentDegraded, "permanentDegraded", false);
        Scribe_Values.Look(ref state.degradationReason, "degradationReason", "");
        Scribe_Values.Look(ref state.consecutiveEmptyPlans, "consecutiveEmptyPlans", 0);
        Scribe_Values.Look(ref state.contextVersion, "contextVersion", 0);
        
        // 事件队列
        EventQueue.SerializeExpose();
        
        // 事件和死亡栈
        IncidentEventStack.SerializeExpose();
        
        // 失败事件
        ActionExecutor.SerializeExpose();
    }
}
```

**调用时机**：需要找到 RimWorld 的存档序列化入口点。由于 `EventScheduler` 是 static 类，无法直接实现 `IExposable`。考虑以下方案：

- **方案 A**：创建一个 `StoryMakerWorldComponent`（继承 `WorldComponent`），在 `ExposeData()` 中调用 `StoryMakerExpose.ExposeAll()`
- **方案 B**：利用现有的 `StoryMakerState` 作为聚合根，在其中持有所有需要序列化的数据

**推荐方案 A**：`WorldComponent` 是 RimWorld 标准的跨存档持久化机制，`ExposeData()` 会自动在存档/读档时被调用。参照 RimChat 的实现。

**新文件**：`Source/StoryMaker/StoryMaker/Core/StoryMakerWorldComponent.cs`

```csharp
[StaticConstructorOnStartup]
public class StoryMakerWorldComponent : WorldComponent
{
    public StoryMakerWorldComponent(World world) : base(world) { }
    
    public override void ExposeData()
    {
        base.ExposeData();
        StoryMakerExpose.ExposeAll();
    }
}
```

#### 4.4.5 各模块序列化实现要点

**EventQueue 序列化**：
- `Scribe_Collections.Look(ref events, "eventQueue", LookMode.Deep)`
- 由于 `LookMode.Deep` 要求元素实现 `IExposable`，需要 `PlannedEvent` 实现 `IExposable`

**IncidentEventStack 序列化**：
- `RecentEventEntry` 和 `DeathEntry` 需要实现 `IExposable` 或改为可序列化结构
- 使用 `Scribe_Collections.Look` 序列化两个栈

**ActionExecutor.FailedEvents 序列化**：
- `DeviationEntry` 需要实现 `IExposable`

---

### 任务 4.5：读档后处理

**目标**：读档后正确恢复调度状态，过滤过期事件，生成 deviation_report。

**实现位置**：`EventScheduler.OnTick()` 中，`gameStartAbsTick` 变化检测后

**处理流程**：

```
OnGameLoad (在 EventScheduler.OnTick 检测到 contextVersion 递增后):
  1. contextVersion++ (Phase 2 已有)
  
  2. 过滤 eventQueue 中 scheduled_tick < curTick 的事件:
     expiredEvents = []
     while eventQueue.Peek()?.scheduled_tick < curTick:
         expiredEvents.Add(eventQueue.Pop())
     
  3. 检测"过期"事件 (generated_at_world_tick 差距过大):
     // 例如，读了一个很久以前的存档，队列中事件可能不再适用
     staleThreshold = 900000  // 15 天
     foreach evt in eventQueue:
         if curTick - evt.generated_at_world_tick > staleThreshold:
             标记为 stale，可选的移到 expiredEvents 作为 deviation_report
     
  4. 生成 deviation_report:
     expiredEvents 加入 ActionExecutor.FailedEvents（作为 deviation_report）
     下周期通过 snapshot.deviationReport 反馈给 LLM
  
  5. 重置请求锁状态:
     确保 requestLocked = false（即使存档时是 Locked）
     
  6. 如果 permanentDegraded == true:
     停止，不发起任何请求
     
  7. OnGameTick 将自动评估 ack - curTick < B 并发起请求（如需要）
```

**关键判断**：读档后哪些队列事件仍然有效？

| 情况 | 处理 |
|------|------|
| `scheduled_tick < curTick` | 已过期，丢弃，生成 deviation_report |
| `scheduled_tick >= curTick` 且 `generated_at_world_tick` 正常 | 保留在队列中 |
| `scheduled_tick >= curTick` 但 `generated_at_world_tick` 差距 > 15 天 | 可选丢弃（让 LLM 重新评估），生成 deviation_report |

---

### 任务 4.6：EventScheduler 适配重构

**目标**：将 Phase 3 中 `EventScheduler` 的分散锁逻辑迁移到 `RequestLock`，接入 `DegradationHandler`，整合 `EmptyPlanGuard`。

**无需新文件，修改 `EventScheduler.cs`**。

**修改要点**：

1. **移除字段**：`requestLocked`, `retransmitRemaining`, `requestSentAtTick`, `requestSentRealtime` → 移到 `RequestLock`
2. **移除方法**：`CheckRealtimeTimeout()` → 移到 `RequestLock.Update()`
3. **移除方法**：`HandleTimeoutOrDegradation()` → 拆分为 `RequestLock.OnTimeout` 事件处理 + `DegradationHandler.ShowDialog()`
4. **新增调用**：`RequestLock.Update()` 在 `AIChatServiceAsync.Update()` 中调用
5. **新增 Guard**：在 `OnResponseReceived` 中调用 `EmptyPlanGuard`
6. **新增 load 逻辑**：读档后过滤 + deviation_report 生成

**重构后的 `OnTick` 流程**：

```
OnTick(curTick):
  1. 检测新游戏/读档:
     if gameStartAbsTick changed:
       contextVersion++
       HandlePostLoad(curTick)   // 过滤过期事件 + 生成 deviation_report
     
  2. 永久降级 → return
  
  3. ExecuteDueEvents(curTick)
  
  4. 检查是否需要发起请求:
     if !requestLock.IsLocked && (ack - curTick < bufferTicks):
       SendSchedulingRequest(curTick, bufferTicks, windowTicks)
  
  5. // 缓冲耗尽检测现在在 RequestLock.Update() 中处理
```

**重构后的 `OnResponseReceived` 流程**：

```
OnResponseReceived(responseBody):
  1. 解析 + 前 3 层 Guard (Phase 3 已有)
  2. 修正重试逻辑 (Phase 3 已有)
  3. ACK 校验 (Phase 3 已有)
  
  4. [新增] EmptyPlanGuard:
     EmptyPlanGuard.Evaluate(response, StoryMakerState.Instance)
     if EmptyPlanGuard.NeedsForceRetry:
         发送修正消息，保持 Locked
         return
     if EmptyPlanGuard.ShouldInjectWarning:
         标记警告（下次 SendSchedulingRequest 注入）
  
  5. 有效回复处理 (Phase 3 已有):
     requestLock.UnlockIfMatch(expectedAckStart)
     过滤过期事件
     入队 + ACK 推进
```

---

### 任务 4.7：测试验证计划

由于 Agent 无法直接打开游戏，此任务以**测试指引**形式交付给用户。

#### 4.7.1 测试场景清单

| # | 测试场景 | 操作 | 预期结果 |
|---|---------|------|---------|
| 1 | **基础存档读档** | 游戏运行一段时间（有事件入队）→ 存档 → 读档 | ack + 队列恢复正确；过期的被丢弃；新请求自动发起 |
| 2 | **空队列存档** | 队列为空时存档 → 读档 | 正常恢复，ack 不变，OnTick 自动判断是否发请求 |
| 3 | **降级状态存档** | 触发降级 → 弹窗出现 → 存档 → 读档 | 读档后弹窗重新显示？还是直接标记 degraded？→ 应只保留 permanentDegraded 标记，不恢复弹窗（因为弹窗在 Locked 时触发，读档后 Locked 已清除） |
| 4 | **请求飞行中存档** | 发送请求但未回复 → 立即存档 → 读档 | 飞行请求被丢弃（contextVersion 递增）；ack 不变；OnTick 自动发起新请求 |
| 5 | **连续空事件计数持久化** | 触发 2 次空事件 → 存档 → 读档 → 再触发 1 次空事件 | consecutiveEmptyPlans=3，注入警告 |
| 6 | **过期事件过滤** | 队列有大量事件 → 等待部分过期（不存档）→ 验证 EventScheduler 自动过滤 | 过期事件在 OnTick 中自动执行还是丢弃？→ Phase 3 已有 `ExecuteDueEvents` 检查 `scheduled_tick > curTick`，应该不会执行。验证队列中没有过期事件残留 |
| 7 | **降级弹窗交互** | 触发降级 → 弹窗出现 → 点击 [重试] / [放弃] | 重试：立即发请求；放弃：标记永久降级 |
| 8 | **暂停冻结计时器** | 发送请求 → 暂停游戏 → 等待超过 T 秒 → 恢复 | 计时器在暂停期间不计数，恢复后才可能超时 |

#### 4.7.2 验证工具

- 游戏控制台日志（`[StoryMaker]` 前缀）
- DebugLogger 保存的请求/回复文件
- Mod Settings 中的"发送测试请求"按钮（手动触发验证请求通路）
- Mod Settings 中的"恢复连接"按钮（手动清除降级状态）

---

## 3. 文件产出预估

| 文件 | 类型 | 说明 |
|------|------|------|
| `Schedule/RequestLock.cs` | **新增** | 正式请求锁 + 暂停冻结计时器 |
| `Schedule/DegradationHandler.cs` | **新增** | 降级弹窗 UI + 重试/放弃逻辑 |
| `Response/EmptyPlanGuard.cs` | **新增** | 第 4 层 Guard：连续空事件检测 + 强制要求 |
| `Core/StoryMakerWorldComponent.cs` | **新增** | WorldComponent，作为序列化入口 |
| `Response/ResponseModels.cs` | **修改** | PlannedEvent +generated_at_world_tick + IExposable |
| `Response/ResponseModels.cs` | **修改** | DeviationEntry / RecentEventEntry / DeathEntry + IExposable |
| `Core/StoryMakerExpose.cs` | **修改** | 扩展至全部序列化状态 |
| `Schedule/EventScheduler.cs` | **修改** | 适配 RequestLock + DegradationHandler + EmptyPlanGuard + 读档处理 |
| `LLM/AIChatServiceAsync.cs` | **修改** | CheckRealtimeTimeout → RequestLock.Update() |
| `Core/StoryMakerState.cs` | **修改** | 移除 requestLocked bool（移到 RequestLock） |
| `Snapshot/GameStateSnapshot.cs` | **修改** | RecentEventEntry / DeathEntry / DeviationEntry + IExposable |

**预估**：5 新文件 + 7 修改 = 12 文件变更

---

## 4. 风险与注意事项

| # | 风险 | 应对 |
|---|------|------|
| 1 | **WorldComponent 注册**：`[StaticConstructorOnStartup]` 标记的 WorldComponent 是否自动注册？ | 查阅 RimWorld 原版 `WorldComponent` 用法（RimSage），确认是否需要额外注册步骤 |
| 2 | **Dictionary 序列化**：`Scribe_Collections` 不支持嵌套复杂类型 | 使用两个平行 List 转换，或序列化为 JSON 字符串 |
| 3 | **静态类序列化**：`EventScheduler`、`IncidentEventStack`、`ActionExecutor` 都是 static 类 | 通过 `WorldComponent.ExposeData()` 作为聚合入口手动调用各模块的序列化方法 |
| 4 | **读档后弹窗恢复**：存档时正在显示降级弹窗，读档后怎么办？ | 不恢复弹窗。存档时 `requestLocked` 不序列化，读档后默认 Unlocked。如果 `permanentDegraded == true`，OnTick 直接返回 |
| 5 | **RequestLock 与 EventScheduler 耦合**：当前锁逻辑与调度逻辑紧密交织 | RequestLock 通过事件（OnRetransmit / OnDegradationTriggered）解耦，EventScheduler 订阅事件 |
| 6 | **暂停检测**：`Find.TickManager.Paused` 的可靠性和帧率 | `AIChatServiceAsync.Update()` 在暂停时仍被调用（因为使用了 `DontDestroyOnLoad`），需要在此处判断暂停状态 |
| 7 | **EmptyPlanGuard 提示词注入时机**：警告应在 system prompt 还是 user prompt 中？ | 作为 system prompt 追加（因为它是持久性的指令修改，而非一次性的用户输入）→ 通过 `PromptBuilder` 在构建请求时注入 |

---

## 5. 与后续阶段的接口预留

Phase 4 完成后，Phase 5 需要以下入口：

| Phase 5 需求 | Phase 4 准备 |
|-------------|-------------|
| `StoryMakerBottomTab` 管理面板 | `DegradationHandler` 的 UI 模式可复用；"恢复连接"按钮逻辑已就绪 |
| 交互式对话 | `RequestLock` 的锁机制可供调度和对话两个通道共用（或建立独立的轻量锁） |
| 40+ 事件 Handler | `ActionRegistry` 注册表模式已支持扩展，无需 Phase 4 改动 |

---

## 6. 里程碑验收标准

Phase 4 完成的标准（对照项目规划书 5.5 节）：

- [ ] **RequestLock 正式化**：暂停时计时器冻结，恢复后继续；超时/缓冲耗尽分别触发重传/降级
- [ ] **降级弹窗**：游戏内弹窗显示失败原因 + 窗口信息 + 重试/放弃按钮；选择重试立即发请求，选择放弃标记永久降级
- [ ] **EmptyPlanGuard**：连续 3 次空事件后注入警告；强制要求后仍空则修正重试 1 次；计数器持久化
- [ ] **存档序列化**：ack、EventQueue、PlannedEvent（含 generated_at_world_tick）、IncidentEventStack、FailedEvents 全部正确序列化和恢复
- [ ] **读档恢复**：过期事件过滤 + deviation_report 自动生成；contextVersion 防陈旧回调
- [ ] **完整滑动窗口调度**：Phase 3 的自动调度流程在存档/读档/暂停/降级各场景下均正确运行
