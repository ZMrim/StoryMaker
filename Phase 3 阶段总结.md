# Phase 3 阶段总结：回复解析 + 事件调度 + 事件执行

## 里程碑状态：✅ 完成

LLM 回复被解析为结构化事件计划，经三层 Guard 校验后排入队列，按精确 tick 自动触发游戏事件。TCP 滑动窗口调度器实现自动请求-响应-执行闭环，超时重传和降级机制就绪。

---

## 1. 已验证的端到端流程

```
游戏运行中（自动触发）
  → EventScheduler.OnTick() 检测滑动窗口 (ack - curTick < B)
  → SnapshotCollector.Collect() 采集快照
  → PromptBuilder.Build() 组装 Prompt
  → AIChatServiceAsync.SendRequest() HTTP 发送
  → LLM 回复 (OpenAI 格式 / 纯 JSON / markdown 包裹)
  → JsonExtractor.ExtractCleanJson() 提取 JSON（自动识别 OpenAI 包装 + 转义还原）
  → ParseGuard → SchemaGuard → EventGuard 三层校验
  → 校验通过 → EventQueue.InsertAll() → ACK 推进
  → EventScheduler.OnTick() → ActionExecutor.Execute() → 游戏事件触发
  → 校验失败 → FormatCorrectionMessages.Build() → 修正重试（每层1次）
```

### 真实测试数据（硅基流动 DeepSeek-V3.2，低 Token 模式）

**请求：** window=[185947, 485947], ack=0, system=3431 字符 + user=1177 字符，1757 prompt tokens

**LLM 回复（OpenAI 包装格式）：**
```json
{"id":"...","object":"chat.completion","model":"Pro/deepseek-ai/DeepSeek-V3.2","choices":[{"message":{"role":"assistant","content":"```json\n{\n  \"plan_range\": { \"from_tick\": 185947, \"to_tick\": 485947 },\n  \"empty_plan\": false,\n  \"narrative_summary\": \"三个倒霉蛋在沙漠里安家...\",\n  \"events\": [\n    { \"event_id\": \"event_001\", \"scheduled_tick\": 195000, \"event_type\": \"Disease_Malaria\", \"parameters\": { \"intensity_multiplier\": 1.5 }, ... },\n    { \"event_id\": \"event_002\", \"scheduled_tick\": 227500, \"event_type\": \"HeatWave\", \"parameters\": { \"intensity_multiplier\": 1.8 }, ... },\n    { \"event_id\": \"event_003\", \"scheduled_tick\": 295000, \"event_type\": \"RaidEnemy\", \"parameters\": { \"faction\": \"利-波格协定\", \"intensity_multiplier\": 1.2 }, ... },\n    { \"event_id\": \"event_004\", \"scheduled_tick\": 355000, \"event_type\": \"PsychicDrone\", \"parameters\": { \"intensity_multiplier\": 1.3 }, ... }\n  ]\n}\n```"}}],"usage":{"prompt_tokens":1757,"completion_tokens":748,"total_tokens":2505}}}
```

**解析结果：** JsonExtractor 成功从 OpenAI 格式中提取 `content` 字段 → 剥离 markdown 代码块 → 还原 JSON 转义 → 4 个事件全部解析通过。

**执行结果（自动调度，2 个窗口周期）：**
```
窗口1 [1, 300000]: ColdSnap @60000 | CropBlight @120000 | GiveQuest_DistressCall @180000 | RaidEnemy @240000
  全部事件通过通用执行器成功触发。

窗口2 [300001, 600000]: 4 个事件入队（队列深度=6），ACK 推进至 600000
```

---

## 2. 文件产出清单（17 新文件 + 11 修改 + 4 资源文件更新）

```
Source/StoryMaker/StoryMaker/
├── StoryMaker.cs                         ← 修改: 模拟模式 FloatMenu UI
├── Core/
│   └── StoryMakerSettings.cs             ← 修改: +DebugSimulationMode 枚举 + simulationMode 字段
├── LLM/
│   └── AIChatServiceAsync.cs             ← 修改: Action→System.Action + 实时超时检测调用
├── Response/                              ★ 新增模块
│   ├── ResponseModels.cs                 ← LLMResponse / PlannedEvent / PlanRange / GuardResult / ParseResult
│   ├── JsonExtractor.cs                  ← 手动 JSON 解析工具（OpenAI 格式检测 + markdown 剥离 + 转义还原 + 递归提取）
│   ├── ResponseParser.cs                 ← 回复 → 结构化对象（含 parameters 键值对解析）
│   ├── ParseGuard.cs                     ← 第1层: 空响应 / 无效 JSON 检测
│   ├── SchemaGuard.cs                    ← 第2层: plan_range / events / narrative_summary 必需字段
│   ├── EventGuard.cs                     ← 第3层: event_type 白名单 / scheduled_tick 范围+2500 对齐 / 无效事件剔除
│   └── FormatCorrectionMessages.cs       ← 修正提示词模板（双语标签前缀，5 种违规类型）
├── Schedule/
│   ├── EventScheduler.cs                 ★ 完全重写: TCP 滑动窗口状态机 + ACK 管理 + 重传 + 降级 + 保护期注入
│   └── EventQueue.cs                     ★ 新增: 按 scheduled_tick 升序优先队列
├── Action/                               ★ 新增模块
│   ├── IActionHandler.cs                 ← 事件处理接口 + Phase 5 预留字段
│   ├── ActionRegistry.cs                 ← 统一注册表（[StaticConstructorOnStartup]，11 疾病子类型 + 通用兜底）
│   ├── ActionExecutor.cs                 ← 执行分发器 + 生命周期钩子 + deviation_report 收集
│   └── Actions/
│       ├── ActionRaidEnemy.cs             ← faction + intensity_multiplier + 7 种 raid_strategy
│       ├── ActionTraderCaravan.cs         ← faction（可选）+ trader_kind（可选）
│       ├── ActionDisease.cs               ← 覆盖 10 种人类/动物疾病
│       ├── ActionManhunterPack.cs         ← intensity_multiplier
│       └── ActionPsychicDrone.cs          ← PsychicDrone + PsychicSoothe（通用）
├── Management/
│   └── StoryMakerManager.cs              ← 修改: 测试请求输出解析后的结构化事件 + 耗时记录
├── Debug/
│   ├── DebugLogger.cs                    ← 修改: LogResponse +parsedJson 可选参数（响应文件内含解析结果）
│   └── DebugSimulator.cs                 ★ 新增: 错误模拟器（5 种模式，拦截自动调度请求，验证重传/修正流程）
└── Snapshot/
    ├── GameStateSnapshot.cs              ← 修改: type→event_type（defName），FactionRelationEntry +relationKind
    ├── IncidentEventStack.cs             ← 修改: 存储 defName 而非 label
    ├── SnapshotFields.cs                 ← 修改: faction_relations 采集 PlayerRelationKind
    └── SnapshotSerializer.cs             ← 修改: JSON 字段名同步更新

Resources/PromptTemplates/
├── SystemPrompt_CN.txt                   ← 修改: +raid_strategy 参数说明（7 种策略）+ trader_kind 参数
├── SystemPrompt_CN_LowToken.txt          ← 修改: +raid_strategy 参数说明
├── SystemPrompt_EN.txt                   ← 修改: +raid_strategy 参数说明（英文）
└── SystemPrompt_EN_LowToken.txt          ← 修改: +raid_strategy 参数说明（英文）
```

---

## 3. 与 Phase 2 的衔接

| Phase 2 基础设施 | Phase 3 使用方式 |
|-----------------|-----------------|
| `AIChatServiceAsync.SendRequest()` | EventScheduler 自动调用发送调度请求，Guard 修正重试复用 |
| `PromptBuilder.Build()` | EventScheduler 每次窗口构建 Prompt |
| `SnapshotCollector.Collect()` | EventScheduler 采集快照 + 附加 deviation_report |
| `IncidentWhitelist.IsWhitelisted()` | EventGuard 校验 event_type |
| `StoryMakerSettings` | 新增 `simulationMode` 调试开关 |
| `StoryMakerState` | ack / permanentDegraded / requestLocked 由 EventScheduler 管理 |
| `DebugLogger` | 自动调度请求现在也保存 Debug 日志 + 解析后 JSON |
| `EventScheduler.OnTick()` | 从快照输出骨架升级为完整 TCP 滑动窗口状态机 |
| `StoryMakerManager.SendTestRequest()` | 集成 ResponseParser，输出结构化事件详情 |

---

## 4. 核心架构决策

| # | 决策 | 结论 |
|---|------|------|
| 1 | JSON 解析方案 | 手动解析（JsonExtractor），不使用 Unity JsonUtility。原因：LLM 回复可能包裹 OpenAI 格式、markdown 代码块、JSON 转义序列，且参数键值对无法用 JsonUtility 的 `Dictionary` 限制处理 |
| 2 | OpenAI 格式适配 | JsonExtractor 自动检测 `choices[0].message.content` 字段并递归提取，兼容 OpenAI / DeepSeek / SiliconFlow 等多种提供商 |
| 3 | Guard 修正重试 | 参考 RimChat 的 `Append*RetryMessage` 模式。每层 Guard 最多 1 次重试，修正消息以 `STORYMAKER_{TAG}` 前缀追加到对话历史 |
| 4 | 事件类型暴露 | 暴露具体 defName（如 `RaidEnemy`、`Disease_Flu`）而非 Category，与快照 `event_type` 字段保持一致 |
| 5 | 参数化分级 | RaidEnemy 独享 `faction` + `intensity_multiplier` + `raid_strategy`（7 种可选）；商队类事件有 `faction` + `trader_kind`；其余事件仅 `intensity_multiplier`；参数无效时自动回退原版默认值 |
| 6 | 事件执行架构 | ActionRegistry + IActionHandler 注册表模式，5 个核心 Handler + 通用兜底。未注册事件通过 `IncidentDef.Worker.TryExecute()` 通用执行 |
| 7 | 实时超时检测 | 超时检测从 `OnTick`（游戏 tick 驱动）移至 `AIChatServiceAsync.Update()`（MonoBehaviour 帧驱动），不受游戏暂停影响 |
| 8 | 重传/降级双触发 | 实时超时（`elapsed >= timeoutSeconds`）和游戏 tick 缓冲耗尽（`curTick >= requestSentAtTick + bufferTicks`）两个独立条件，共享 `R_max` 重传池 |
| 9 | 前5天保护期 | 窗口起始 tick ≤300000 时自动注入保护期提示词，禁止 LLM 安排恶性事件 |
| 10 | 调试错误模拟器 | 内置 5 种模拟模式（Timeout / ConnectionError / BadJson / SchemaViolation / EventViolation），拦截自动调度请求，无需真实网络即可验证全部分支逻辑 |

---

## 5. Phase 3 期间遇到的坑

| 坑 | 现象 | 修复 |
|----|------|------|
| OpenAI 格式 JSON 解析失败 | ParseGuard 报告"提取的 JSON 不是完整对象结构" | JsonExtractor 增加 OpenAI 格式自动检测 + 递归 `content` 字段提取 + JSON 转义序列还原 |
| Markdown 代码块转义残留 | 提取的 JSON 以 `\n{` 开头而非 `{` | `ExtractCleanJson` 增加 `UnescapeJsonString()` 还原 `\n` / `\"` 等转义序列 |
| Unicode 转义偏移错误 | `\uXXXX` 处理时 `i += 4` 应为 `i += 5`（少跳 1 个字符） | 修正为 `i += 5` |
| `Action` 命名空间冲突 | `StoryMaker.Action` 命名空间遮蔽 `System.Action` 委托类型 | `AIChatServiceAsync` 中 `Action` 改为 `System.Action` |
| ACK 校验误判 | `expectedAckStart = ack+1` 但发给 LLM 的 `llmFromTick = Max(ack+1, curTick)` 不一致 | `expectedAckStart` 改为 `llmFromTick`（与发给 LLM 的值一致） |
| 降级检测过早触发 | `curTick >= ack+1` 在 ack=0 时游戏开始即触发降级 | 改为 `curTick >= requestSentAtTick + bufferTicks` |
| 超时检测依赖游戏 tick | 暂停游戏时 tick 不推进，永远检测不到超时 | 实时超时检测移至 `MonoBehaviour.Update()`，帧驱动，不受暂停影响 |
| 系统提示词未涵盖 raid_strategy | LLM 不输出袭击策略参数 | 4 个系统提示词模板全部添加 7 种策略说明 + 参数示例 |
| Debug 响应无解析后内容 | 开发者需手动解析 LLM 回复 JSON | `LogResponse` 新增 `parsedJson` 可选参数，响应文件内含结构化解析结果 |
| Debug 耗时显示 0ms | `LogResponse` 传入硬编码 `0` | `StoryMakerManager` 和 `EventScheduler` 记录发送时间，回调中计算真实耗时 |

---

## 6. 已知限制（留到后续阶段）

| 限制 | 负责阶段 | 说明 |
|------|---------|------|
| 存档序列化 | Phase 4 | ack / EventQueue / IncidentEventStack 不持久化。读档后状态全部重置 |
| `RequestLock` 正式版 | Phase 4 | 当前使用简化 bool 锁。Phase 4 实现正式锁 + 暂停冻结计时器 |
| `DegradationHandler` 弹窗 | Phase 4 | 当前降级仅日志 + 标记，无游戏内弹窗通知玩家 |
| `EmptyPlanGuard` | Phase 4 | 连续空事件检测 + 强制要求 |
| 底部菜单管理面板 | Phase 5 | 状态查看、恢复连接、跳过窗口、Token 统计等 UI |
| 40+ 事件 Handler 参数定制化 | Phase 5 | 当前仅 5 个核心 Handler 有参数定制，其余通过通用兜底 |
| 交互式对话（Dialogue） | Phase 5 | |

---

## 7. 项目规划书版本

`项目规划.md` Phase 3 任务清单可全部标记完成。需同步更新：
- `6.2 各阶段待定事项` 中 Phase 3 部分（事件类型最终清单、参数化程度、Category vs 具体事件已全部决议）
- Phase 3 任务清单标记完成
