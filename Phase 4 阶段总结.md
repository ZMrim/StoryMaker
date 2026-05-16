# Phase 4 阶段总结：完整调度协议 + 存档兼容

## 里程碑状态：✅ 完成

Phase 3 的简化调度机制被升级为完整的 TCP 滑动窗口协议：正式请求锁（含暂停冻结计时器）、降级弹窗通知、第 4 层 EmptyPlanGuard、全部运行时状态序列化、读档恢复逻辑。同时修复了 Phase 3 遗留的 6 处 `TryExecute` 返回值未检查的 Bug，并为所有 Action Handler 增加了自定义参数失败时回退原版默认参数的两级重试机制。

---

## 1. Phase 4 新增/升级的核心机制

### 1.1 RequestLock 正式请求锁

替代 Phase 3 的 `static bool requestLocked` 简易锁。

```
RequestLock 状态机：
  Unlocked → Lock (SendSchedulingRequest)
  Locked:
    ├── OnResponseReceived (有效回复) → Unlock
    ├── CheckRealtimeTimeout (暂停冻结) → HandleRealtimeTimeout
    │     ├── 重传剩余 > 0 → OnRetransmitRequested
    │     └── 重传耗尽 → OnDegradationRequested
    └── OnTick: curTick >= requestSentAtTick + bufferTicks → HandleBufferExhaustion
          ├── 重传剩余 > 0 → OnRetransmitRequested
          └── 重传耗尽 → OnDegradationRequested
```

**暂停冻结**：`RequestLock.CheckRealtimeTimeout()` 在每帧检测 `Find.TickManager.Paused`，暂停时累加 `elapsedPaused`，恢复后从冻结时间点继续计时。暂停期间的帧时间不计入超时。

### 1.2 DegradationHandler 降级弹窗

当重传次数耗尽（实时超时或缓冲耗尽）时，游戏自动暂停并弹出 `Dialog_MessageBox`：

```
┌─────────────────────────────────────┐
│  AI 叙事者连接失败                    │
│                                      │
│  窗口 [第 X 天 ~ 第 Y 天] 未能获得     │
│  有效回复。                           │
│  错误详情: {reason}                   │
│                                      │
│  [重试] — 忽略缓冲期，立即请求下一窗口   │
│  [放弃] — 永久切换至原版叙事者          │
└─────────────────────────────────────┘
```

- 重试：清除降级标记，立即调用 `SendSchedulingRequest`（跳过缓冲期检查）
- 放弃：调用 `MarkDegraded()`，`OnTick` 停止调度
- 恢复：Mod 设置中的"恢复连接"按钮可清除永久降级状态

### 1.3 EmptyPlanGuard 第 4 层 Guard

```
LLM 返回空事件 → consecutiveEmptyPlans++
  ↓ 累积 3 次
下窗口 SendSchedulingRequest 注入警告
  ↓ LLM 仍然返回空事件
触发强制修正重试（限 1 次，追加修正消息）
  ↓ 强制修正后仍为空
接受此回复，不再无限重试
```

- `consecutiveEmptyPlans` 计数器持久化到存档
- 有事件时计数器归零
- 强制修正重试与前三层 Guard 重试共享同一通道（追加 user 消息 + 重发请求）

### 1.4 存档完全序列化

**序列化入口**：`StoryMakerWorldComponent`（继承 `WorldComponent`），在存档/读档时自动调用 `StoryMakerExpose.ExposeAll()`。

**序列化内容**：

| 数据 | 类型 | 说明 |
|------|------|------|
| `ack` | int | ACK 指针 |
| `permanentDegraded` | bool | 永久降级标记 |
| `degradationReason` | string | 降级原因（新增序列化） |
| `consecutiveEmptyPlans` | int | 空事件计数 |
| `contextVersion` | int | 上下文版本号 |
| `eventQueue` | List\<PlannedEvent\> | 事件队列（新增序列化） |
| `incidentStack` | List\<RecentEventEntry\> | 事件栈（新增序列化） |
| `deathStack` | List\<DeathEntry\> | 死亡栈（新增序列化） |
| `failedEvents` | List\<DeviationEntry\> | 失败事件（新增序列化） |

**不序列化的状态**：`RequestLock` 锁状态、`lastMessages`、各层重试计数器——读档后通过 `HandlePostLoad` 重置为空闲状态。

**技术实现**：
- `PlannedEvent` 实现 `IExposable`，`Dictionary<string, string> parameters` 转为两个平行 `List<string>` (`paramKeys` + `paramValues`) 序列化
- `RecentEventEntry`、`DeathEntry`、`DeviationEntry` 实现 `IExposable`
- `EventQueue.ExposeData()`、`IncidentEventStack.ExposeData()`、`ActionExecutor.ExposeData()` 作为模块序列化方法

### 1.5 读档后处理

`HandlePostLoad(curTick)`：
1. 调用 `requestLock.Unlock()` 清除残留锁状态
2. 调用 `DegradationHandler.CloseIfOpen()` 重置弹窗标记
3. 重置所有 Guard 重试计数器
4. 过滤 `eventQueue` 中 `scheduled_tick < curTick` 的过期事件
5. 过期事件转为 `deviation_report`（通过 `ActionExecutor.AddFailedEvent()`）
6. 保留有效事件，重建按 tick 排序的队列

### 1.6 TryExecute 返回值检查 + 两级重试回退（Phase 3 Bug 修复）

**Bug**：6 处 `incidentDef.Worker.TryExecute(parms)` 调用均忽略返回值，导致事件静默失败时仍报"执行成功"。

**修复**：所有 Action Handler 改为两级执行：
```
第一次: LLM 自定义参数（faction + intensity + strategy/trader_kind）
  ↓ TryExecute 返回 false
第二次: 原版默认参数（纯 DefaultParmsNow，无任何覆盖）
  ↓ 仍返回 false
Error 日志 + return false → ActionExecutor 收集到 deviation_report
```

覆盖范围：ActionRaidEnemy、ActionTraderCaravan、ActionDisease、ActionManhunterPack、ActionPsychicDrone、ActionRegistry.ExecuteGeneric。

---

## 2. 文件产出清单（4 新文件 + 10 修改 + 4 资源文件更新 + 1 项目文件更新）

```
Source/StoryMaker/StoryMaker/
├── StoryMaker.csproj                      ← 修改: +4 新文件编译条目
├── Core/
│   ├── StoryMakerState.cs                 ← 修改: 移除废弃的 requestLocked
│   ├── StoryMakerExpose.cs                ← 修改: 重写为 ExposeAll() 聚合入口
│   └── StoryMakerWorldComponent.cs        ← ★ 新增: WorldComponent 序列化入口
├── LLM/
│   └── AIChatServiceAsync.cs              ← 不变 (CheckRealtimeTimeout 委托至 RequestLock)
├── Response/
│   ├── ResponseModels.cs                  ← 修改: PlannedEvent + IExposable + generated_at_world_tick + Dictionary 序列化
│   └── EmptyPlanGuard.cs                  ← ★ 新增: 第 4 层 Guard
├── Schedule/
│   ├── EventScheduler.cs                  ← 修改: 集成 RequestLock + DegradationHandler + EmptyPlanGuard + HandlePostLoad
│   ├── EventQueue.cs                      ← 修改: +ExposeData()
│   ├── RequestLock.cs                     ← ★ 新增: 正式请求锁 + 暂停冻结计时器
│   └── DegradationHandler.cs              ← ★ 新增: 降级弹窗 UI
├── Action/
│   ├── ActionExecutor.cs                  ← 修改: +ExposeData() + AddFailedEvent()
│   ├── ActionRegistry.cs                  ← 修改: TryExecute 返回值检查 + 两级回退
│   └── Actions/
│       ├── ActionRaidEnemy.cs             ← 修改: TryExecute 返回值检查 + 两级回退
│       ├── ActionTraderCaravan.cs         ← 修改: TryExecute 返回值检查 + 两级回退
│       ├── ActionDisease.cs               ← 修改: TryExecute 返回值检查 + 两级回退
│       ├── ActionManhunterPack.cs         ← 修改: TryExecute 返回值检查 + 两级回退
│       └── ActionPsychicDrone.cs          ← 修改: TryExecute 返回值检查 (无回退，无自定义参数)
└── Snapshot/
    ├── GameStateSnapshot.cs               ← 修改: RecentEventEntry/DeathEntry/DeviationEntry + IExposable
    └── IncidentEventStack.cs              ← 修改: +ExposeData()

Resources/PromptTemplates/
├── SystemPrompt_CN.txt                    ← 修改: +17 种 trader_kind 参数说明
├── SystemPrompt_CN_LowToken.txt           ← 修改: +trader_kind 精简说明
├── SystemPrompt_EN.txt                    ← 修改: +17 种 trader_kind 英文说明
└── SystemPrompt_EN_LowToken.txt           ← 修改: +trader_kind 英文精简说明
```

---

## 3. 与 Phase 3 的衔接

| Phase 3 基础设施 | Phase 4 改动 |
|-----------------|-------------|
| `EventScheduler` static bool `requestLocked` | → 独立 `RequestLock` 类，支持暂停冻结 + 事件通知 |
| `EventScheduler.CheckRealtimeTimeout()` (简单 elapsed 检测) | → 委托至 `RequestLock.CheckRealtimeTimeout()`（带暂停冻结） |
| `EventScheduler.HandleTimeoutOrDegradation()` | → `RequestLock.HandleRealtimeTimeout()` + `HandleBufferExhaustion()` → 事件驱动 |
| `StoryMakerState.MarkDegraded()` (仅日志) | → `DegradationHandler.ShowDialog()` + `MarkDegraded()` |
| `StoryMakerExpose.Expose()` (5 字段) | → `ExposeAll()` (全部状态集合) |
| `StoryMakerState.requestLocked` (废弃) | → 移除，锁状态由 `RequestLock` 管理 |
| `consecutiveEmptyPlans` (存在但从未读写) | → `EmptyPlanGuard` 完整驱动 |
| `PlannedEvent` (无 IExposable) | → 实现 `IExposable` + 新增 `generated_at_world_tick` |
| `EventQueue` (无序列化) | → `ExposeData()` |
| `IncidentEventStack` (无序列化) | → `ExposeData()` |
| `ActionExecutor.FailedEvents` (无序列化) | → `ExposeData()` + `AddFailedEvent()` |
| 6 处 `TryExecute` 返回值未检查 | → 全部检查 + 两级回退 |
| 系统提示词 trader_kind 无说明 | → 4 模板 +17 种有效值 |

---

## 4. Phase 4 期间修复的 Phase 3 遗留 Bug

| # | Bug | 影响 | 修复 |
|---|-----|------|------|
| 1 | `TryExecute` 返回值未检查 (6 处) | 事件静默失败时误报"执行成功"，不入 deviation_report | 全部检查返回值，false 时 Warning 日志 + return false |
| 2 | 自定义参数导致 TryExecute 失败后无兜底 | LLM 虚构的派系名/商队类型导致事件完全不触发 | 增加两级重试：自定义参数失败 → 原版默认参数再试 |
| 3 | 系统提示词未规定 trader_kind 有效值 | LLM 输出"常规"等无效 defName | 4 模板全部添加 17 种 TraderKindDef 清单 |
| 4 | DegradationHandler 未检查 WindowStack null | 极端情况下 NRE | 增加 null 检查 |

---

## 5. 核心架构决策

| # | 决策 | 结论 |
|---|------|------|
| 1 | 请求锁实现 | 独立 `RequestLock` 类，通过 C# event 委托通知 `EventScheduler`。锁只管实时超时，缓冲耗尽由 `EventScheduler.OnTick` 单独判断 |
| 2 | 暂停冻结方案 | 在 `Update()` 每帧检测 `Find.TickManager.Paused`，暂停帧的 `Time.deltaTime` 不计入超时。不依赖 tick 推进 |
| 3 | 降级弹窗 | 使用原版 `Dialog_MessageBox`（`forcePause=true`），双按钮：buttonA=重试(右), buttonB=放弃(左) |
| 4 | 序列化入口 | `StoryMakerWorldComponent` (继承 `WorldComponent`)，由 RimWorld 反射自动发现并在存档/读档时自动调用 `ExposeData()` |
| 5 | Dictionary 序列化 | `PlannedEvent.parameters` → 两个平行 `List<string>`（`paramKeys` + `paramValues`），避免嵌套 Dictionary 序列化问题 |
| 6 | Static 类序列化 | `EventScheduler`、`IncidentEventStack`、`ActionExecutor` 的静态字段通过各自的静态 `ExposeData()` 方法序列化，由 `StoryMakerExpose.ExposeAll()` 统一调用 |
| 7 | 读档锁状态 | 不序列化 `RequestLock`——读档后默认 Unlocked，由 `HandlePostLoad` 重置 |
| 8 | EmptyPlanGuard 位置 | 在 `EventScheduler.OnResponseReceived` 中前三层 Guard 通过后、ACK 校验前调用。警告注入在 `SendSchedulingRequest` 中通过追加 user 消息实现 |
| 9 | 事件执行回退 | 自定义参数失败后自动用原版默认参数重试，确保 LLM 参数错误不阻塞事件触发。回退成功标"回退"，两次都失败才入 deviation_report |
| 10 | trader_kind 参数 | 系统提示词列出全部 17 种有效 TraderKindDef，要求 LLM 使用精确 defName |

---

## 6. 用户验证结果

| 验证项 | 结果 |
|--------|------|
| 基础调度（Phase 3 自动窗口 + 请求 + 回复 + 执行） | ✅ 正常 |
| 超时重传（`RequestLock` 计时器超时 → 重传） | ✅ 正常 |
| 存档 → 读档（ACK + EventQueue + 状态恢复） | ✅ 正常 |
| 暂停冻结计时器（暂停期间不计时） | ✅ 正常 |
| TryExecute 返回值 + 两级回退 | ✅ 正常 |
| 系统提示词 trader_kind 清单 | ✅ 正常 |

未逐项验证但已在代码路径上覆盖：
- 降级弹窗交互（重试/放弃按钮）— 可在后续测试中通过短超时触发
- EmptyPlanGuard 连续空事件 — 需积累 3 次空窗口
- HTTP 401/404 降级弹窗 — 需要配置错误的 API Key

---

## 7. 已知限制（留到 Phase 5）

| 限制 | 说明 |
|------|------|
| `generated_at_world_tick` 未用于 stale 检测 | 字段已设置和序列化，但 `HandlePostLoad` 只过滤 `scheduled_tick < curTick`，未检测生成时间久远的 stale 事件 |
| 底部菜单管理面板 | Phase 5 `StoryMakerBottomTab` 可实现状态查看、恢复连接、跳过窗口等 UI |
| 40+ 事件 Handler 参数定制化 | 当前仅 5 个核心 Handler 有自定义参数，其余走通用兜底 |
| 交互式对话（Dialogue） | Phase 5 |
| readme 资料更新 | 需要将阶段 1-4 的成果更新到项目文档 |

---

## 8. 项目规划书版本

`项目规划.md` Phase 4 任务清单全部标记完成：
- [x] 实现 `RequestLock` + 超时计时器（现实时间，暂停冻结）
- [x] 实现 `DegradationHandler`（弹窗、重试/放弃逻辑）
- [x] 实现 `EmptyPlanGuard`（连续空事件检测 + 强制要求）
- [x] 实现 `StoryMakerExpose`（全部状态序列化/反序列化）
- [x] 测试：存档→读档→调度状态正确恢复；contextVersion 保护机制验证
