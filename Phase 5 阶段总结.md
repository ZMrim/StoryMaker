# Phase 5 阶段总结：交互式对话 + 完善打磨

## 里程碑状态：✅ 基本完成

底部管理面板上线，交互式对话系统可用，事件类型覆盖扩展至全量，Stale 事件检测实现，多语言翻译基础设施就绪。

---

## 1. 核心功能

### 1.1 底部管理面板 (StoryMakerBottomTab)

在 RimWorld 底部菜单栏新增 StoryMaker 选项卡（order=85，位于 History 和 Factions 之间）。

**状态显示区**：连接状态（正常/请求中/降级/永久降级，彩色标注）、ACK 位置、事件队列深度、上次请求时间、最近错误、空事件计数。

**操作按钮**：恢复连接、跳过当前窗口、立即刷新、重置统计。

### 1.2 交互式对话系统

**架构**：独立的对话通道，与调度通道互不干扰。`DialogueHandler` 管理对话状态机，`DialoguePromptBuilder` 构建轻量 Prompt，`DialogueResponseParser` 解析 LLM 回复。

**Prompt 结构**：
```
system: 身份声明 → 轻量殖民地概况 → 全量事件列表 → 玩家风格 → 回复格式 → 规则
user:   对话历史(最近5轮) → 当前殖民地状态 → 玩家输入
```

**回复格式**：
```json
// 无事件: {"dialogue_text":"...", "immediate_event":false}
// 有事件: {"dialogue_text":"...", "immediate_event":true, "event_type":"...", "parameters":{...}, "narrative_context":"..."}
```

**超时处理**：30 秒现实超时，超时后在聊天窗口显示系统消息并恢复可发送状态。使用 `requestSeq` 递增机制丢弃超时后到达的过期回复。

**DebugLogger 集成**：开启调试模式时，对话请求/回复自动保存到 `StoryMaker_DebugLogs/`。

**TTS 预留**：`OnGenerateTTS` / `OnDialogueReady` 钩子已就绪。

### 1.3 浮动条 (StoryMakerFloatingBar)

半透明浮动条，可拖动、可调大小，显示最近一条叙事者回复。展开按钮打开对话窗口。通过底部选项卡按钮切换显示/隐藏。

### 1.4 对话窗口 (StoryMakerDialogueWindow)

聊天气泡风格界面：玩家消息（右对齐，绿色气泡）、叙事者消息（左对齐，深色气泡）、事件触发提示（青色）。对话时自动暂停游戏。全局单例——不允许同时打开多个对话窗口。

### 1.5 PlannedEvent 提前解析

`event_type` 字符串在 `EventScheduler.OnResponseReceived()` 入队前通过 `PlannedEvent.ResolveDef()` 解析为 `IncidentDef` 引用。解析失败的事件直接进入 `deviation_report`，不入队。读档后通过 `HandlePostLoad` 恢复引用。

所有 Handler 和 `ExecuteGeneric` 不再各自调用 `DefDatabase<IncidentDef>.GetNamed()`，直接使用 `evt.resolvedDef`。

### 1.6 事件类型扩展

| Handler | 新增覆盖 |
|---------|---------|
| ActionRaidEnemy | +RaidFriendly（盟友派系匹配） |
| ActionTraderCaravan | +OrbitalTraderArrival |
| ActionManhunterPack | +AnimalInsanitySingle, AnimalInsanityMass |
| ActionInfestation (新) | Infestation, Infestation_Jelly |
| ActionDisease | +Disease_Abasia |

所有 Handler 改为动态 `IncidentDef` 查找（根据 `evt.event_type`），不再硬编码。

### 1.7 Stale 事件检测

读档时检测 `curTick - generated_at_world_tick > staleThreshold` 的事件（默认阈值 15 天，可在 Settings 中调整 5~30 天）。Stale 事件从队列移除并转为 `deviation_report`（标记 `stale_event`）。

---

## 2. 文件产出清单

### 新文件（9 源文件 + 2 资源文件 + 2 翻译文件）

```
Source/StoryMaker/StoryMaker/
├── UI/
│   ├── StoryMakerBottomTab.cs              ← 底部管理面板
│   ├── StoryMakerFloatingBar.cs            ← 半透明浮动条
│   └── StoryMakerDialogueWindow.cs          ← 聊天气泡窗口
├── Dialogue/
│   ├── DialogueModels.cs                   ← DialogueResponse + DialogueEntry
│   ├── DialoguePromptBuilder.cs            ← 对话 Prompt 构建
│   ├── DialogueResponseParser.cs           ← 对话回复解析
│   └── DialogueHandler.cs                  ← 对话状态机 + TTS钩子 + 即时事件
└── Action/Actions/
    └── ActionInfestation.cs                ← 虫害事件 Handler

Defs/MainButtonDefs/
└── StoryMaker.xml                          ← 底部选项卡 Def

Resources/PromptTemplates/
├── DialoguePrompt_CN.txt                   ← 对话系统提示词（中文）
└── DialoguePrompt_EN.txt                   ← 对话系统提示词（英文）

Languages/
├── ChineseSimplified/Keyed/StoryMaker_Keys.xml   ← 中文 UI 翻译
└── English/Keyed/StoryMaker_Keys.xml             ← 英文 UI 翻译
```

### 修改文件（10）

| 文件 | 改动 |
|------|------|
| Response/ResponseModels.cs | PlannedEvent: +`[Unsaved] resolvedDef` + `ResolveDef()` |
| Schedule/EventScheduler.cs | 入队前解析 Def + HandlePostLoad stale 检测 + 静态访问器 |
| Action/ActionRegistry.cs | 新增 Infestation/RaidFriendly/OrbitalTrader/AnimalInsanity 注册 |
| Action/Actions/ActionRaidEnemy.cs | 动态 Def 查找 + RaidFriendly 支持 |
| Action/Actions/ActionTraderCaravan.cs | 动态 Def 查找 + OrbitalTraderArrival 支持 |
| Action/Actions/ActionManhunterPack.cs | 动态 Def 查找 + AnimalInsanity 支持 |
| Action/Actions/ActionDisease.cs | +Disease_Abasia |
| Prompt/PromptTemplates.cs | +`GetDialogueSystemPrompt()` |
| Core/StoryMakerExpose.cs | +对话状态序列化 |
| Core/StoryMakerSettings.cs | +`staleThresholdDays` |

---

## 3. 与 Phase 4 的衔接

| Phase 4 基础设施 | Phase 5 使用方式 |
|-----------------|-----------------|
| RequestLock | 对话通道复用设计模式但不共享实例 |
| DegradationHandler | 弹窗 UI 模式参考；对话通道不使用降级弹窗 |
| EmptyPlanGuard | 对话通道不需要 |
| WorldComponent 序列化 | 新增对话状态序列化 |
| ActionExecutor 钩子 | 即时事件绕过队列直接调用 |
| PlannedEvent.resolvedDef | Handler 不再各自查找 Def |

---

## 4. 核心架构决策

| # | 决策 | 结论 |
|---|------|------|
| 1 | 对话与调度双通道 | 各自独立的锁和状态机，互不干扰。AIChatServiceAsync 请求队列串行发送 |
| 2 | 对话 Prompt | 系统提示词外部化 + 全量事件列表 + 轻量殖民地快照 + 最近5轮对话历史 |
| 3 | 即时事件执行 | 构造 PlannedEvent → ActionRegistry.Execute() 直接执行（不排队列）。失败时两级回退 |
| 4 | 对话超时 | 30 秒现实超时，不设重传。requestSeq 防过期回调 |
| 5 | PlannedEvent 提前解析 | event_type → IncidentDef 在入队前完成。Handler 不再各自 GetNamed() |
| 6 | Stale 事件检测 | curTick - generated_at_world_tick > 阈值 → deviation_report |
| 7 | 对话窗口单例 | 同一时间只允许一个对话窗口 |
| 8 | 翻译策略 | XML 翻译文件就绪，代码侧 .Translate() 调用留待后续逐步迁移 |

---

## 5. 已知限制

| 限制 | 说明 |
|------|------|
| 翻译键未接入代码 | `Languages/` XML 文件已创建，但代码中仍使用硬编码中文。需逐步替换为 `.Translate()` 调用 |
| 对话安全限制 | 按用户要求，当前未启用事件白名单/强度上限/频率限制。后续版本按需添加 |
| 无限制模式 | 按用户要求暂不实现 |
| 性能优化 | 按前期规划分析，当前无需优化 |
| 对话 Prompt 事件列表 | 使用全量 IncidentWhitelist 列表，约 80+ 事件。后续可精简为即时事件专用列表 |

---

## 6. Phase 5 任务完成状态

- [x] Task 5.1: StoryMakerBottomTab（底部管理面板）
- [x] Task 5.2: 交互式对话系统（DialogueHandler + 浮动条 + 对话窗口）
- [x] Task 5.3: 事件类型注册表扩展（Infestation/RaidFriendly/OrbitalTrader/AnimalInsanity/Abasia）
- [x] Task 5.4: Stale 事件检测
- [x] Task 5.5: 多语言 UI 翻译（基础设施就绪，代码侧逐步迁移）
- [x] Task 5.6: 性能优化（无需操作）
- [ ] Task 5.7: 集成测试（后期由用户执行）
- [x] Task 5.8: 项目文档更新（本总结）

---

## 7. 问题修复记录

| 问题 | 修复 |
|------|------|
| playerText 显示 "[最近一次玩家消息]" | DialogueHandler 改为存储 `pendingPlayerText`，响应到达后写入正确文本 |
| 对话无超时处理导致永久卡死 | 30 秒超时 + requestSeq 防过期回调 |
| 关闭按钮遮挡输入区域 | `doCloseButton=false`，仅保留右上角 X |
| 可以打开多个对话窗口 | 全局单例 + `Open()` 静态方法防重复 |
