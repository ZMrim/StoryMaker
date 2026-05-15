# Phase 2 前期规划：LLM 通信 + Prompt 构建

## 里程碑

点击 Mod Settings 中的"测试请求"按钮 → 组装完整三段式 Prompt → 通过 HTTP 发送至 LLM → 收到原始回复并记录到游戏控制台和 Debug 日志。

---

## 1. Phase 2 目标回顾（来自项目规划书）

实现 LLM 通信层的全部基础设施，完成一次完整的请求-响应循环。具体任务：

- `AIProviderRegistry` + `ProviderDef`（含 DeepSeek、OpenAI、Gemini 预设）
- `AIChatServiceAsync`（MonoBehaviour + UnityWebRequest + 协程 + 回调队列；HTTP 错误处理）
- 双层重试：HTTP 层 5xx/429 冷却重试（Phase 2 完成）+ TCP 协议层 R_max 重传（Phase 2 预留参数，Phase 4 完整实现）
- `contextVersion` 上下文变化保护
- `PromptBuilder` + `PromptTemplates`（三段式 + 多语言系统提示词模板 + 语言自动匹配）
- `PromptInjector`（外接系统提示词注入，支持 JSON 配置文件和 C# API 两种来源）
- `DebugLogger`（开发者开关，全量请求/回复保存到 AppData，含 HTTP 元数据）
- Mod Settings UI 扩展（API Key、Provider 选择、模型名 + 沿用 Phase 1 的 B/N/T/R_max 等参数）
- `StoryMakerManager` 基础框架（开发期提供"测试请求"和"恢复连接"按钮）

---

## 2. 垂直切片总览

Phase 2 拆分为 **4 个垂直切片**，每个切片是完整可验证的端到端功能增量。

| # | 切片 | 类型 | 依赖 | 说明 |
|---|------|------|------|------|
| S1 | API 配置 + Provider 注册表 | AFK | 无 | Settings 扩展 + Provider 预设 |
| S2 | 异步 HTTP 通信层 | AFK | S1 | MonoBehaviour + UnityWebRequest + 协程 |
| S3 | Prompt 构建系统 | AFK | S1 | 三段式模板 + 语言匹配 + 注入接口 |
| S4 | 调试日志 + 集成测试 | HITL | S2, S3 | DebugLogger + 测试按钮 + 端到端验证 |

### 依赖关系图

```
S1 (API 配置 + Provider)
 ├── S2 (HTTP 通信层)
 └── S3 (Prompt 构建)
       └── S4 (集成 + 测试按钮) ← 依赖 S2 + S3
```

S2 和 S3 在 S1 完成后可以并行开发。

---

## 3. 切片详情

### S1：API 配置 + Provider 注册表 [AFK]

**端到端行为：** 玩家在 Mod Settings 中可以看到 Provider 下拉菜单（DeepSeek/OpenAI/Google/OpenRouter/自定义），填写 API Key 和模型名。切换到自定义 Provider 时可输入 Base URL。所有字段自动保存到存档。

**涉及模块：** `Core/StoryMakerSettings.cs`（扩展）、`LLM/AIProviderRegistry.cs`（新建）、`LLM/AIProviderDef.cs`（新建）、`StoryMaker.cs`（UI 扩展）

**与 Phase 1 衔接：**
- `StoryMakerSettings` 已有 B/N/T/R_max、lowTokenMode、debugMode、playerPersonality、deathRaceWhitelist。S1 在此类中新增：
  - `Provider`（枚举，默认 DeepSeek）
  - `ApiKey`（字符串，通过 `Scribe_Values.Look` 持久化）
  - `ModelName`（字符串）
  - `CustomBaseUrl`（字符串，仅 Custom provider 使用）
- `DoSettingsWindowContents()` 已有 Listing_Standard 布局。新增 UI 控件：Provider 下拉选择、ApiKey 输入框、ModelName 输入框、Custom URL 输入框（Provider=Custom 时可见）

**参考 RimChat：**
- `AIProviderRegistry` 结构完全参照 `RimChat.AI.AIProviderRegistry`（static Dictionary + ProviderDef），但精简至 Phase 2 需要的 5 个 Provider（DeepSeek/OpenAI/Google/OpenRouter/Custom）
- `ProviderDef` 结构参照 RimChat（Label + EndpointUrl + ListModelsUrl + ExtraHeaders）
- API 配置序列化参照 `RimChat.Config.ApiConfig.ExposeData()`（Scribe_Values.Look）

**新增文件：**
- `LLM/AIProviderDef.cs` — `ProviderDef` struct + `AIProvider` enum
- `LLM/AIProviderRegistry.cs` — static registry with presets

**修改文件：**
- `Core/StoryMakerSettings.cs` — 新增 ApiKey、Provider、ModelName、CustomBaseUrl 字段 + ExposeData
- `StoryMaker.cs` — `DoSettingsWindowContents()` 扩展 UI 控件

**验收标准：**
- [ ] Mod Settings 中可切换 Provider，API Key 输入框正常输入且不显示明文
- [ ] 切换至 Custom 时出现 Base URL 输入框，切换回预设时隐藏
- [ ] 关闭游戏重开后所有配置保留
- [ ] 控制台输出 `[StoryMaker] Provider 注册表已加载，共 X 个提供商`

---

### S2：异步 HTTP 通信层 [AFK]

**端到端行为：** 当 `AIChatServiceAsync.SendRequest(messages, callback)` 被调用时，创建一个 `GameObject` 挂载 `MonoBehaviour`，通过 `UnityWebRequest` + 协程发送 POST 请求到配置的 LLM endpoint。请求完成后在主线程执行回调。包含 contextVersion 保护、HTTP 错误分类、5xx/429 冷却重试。

**涉及模块：** `LLM/AIChatServiceAsync.cs`（新建）、`LLM/AIChatClient.cs`（新建）、`Core/StoryMakerState.cs`（扩展）

**与 Phase 1 衔接：**
- `StoryMakerState` 已有 ack、permanentDegraded。S2 在此类中新增 `contextVersion`（int，每次存档/读档/新游戏时递增）
- `EventScheduler.OnTick()` 在 Phase 1 检测 `gameStartAbsTick` 变化。S2 在同一位置触发 `contextVersion++`（读档时版本递增，飞行请求回调失效）
- 使用 Phase 1 的 `Settings.timeoutSeconds` 作为 UnityWebRequest 超时
- 使用 Phase 1 的 `Settings.maxRetransmissions`（R_max）为 TCP 层预留，但 Phase 2 暂不实现 TCP 层重传逻辑

**参考 RimChat（核心模式，必须严格参照）：**

| 模式 | RimChat 来源 | StoryMaker 用法 |
|------|-------------|----------------|
| 单例 + GameObject | `AIChatServiceAsync.Instance` | 直接复用：`new GameObject("StoryMakerAsyncService")` → `DontDestroyOnLoad` |
| 协程发送 | `StartCoroutine(ProcessRequestCoroutine(...))` | 直接复用 |
| 主线程回调 | `ExecuteRequestActionOnMainThread(id, version, action)` | 直接复用——回调在 `Update()` 中统一执行，保证主线程安全 |
| contextVersion | `contextVersion` 字段 + `IsContextVersionCurrent()` | 直接复用：读档时递增，协程内每帧检查版本 |
| HTTP 错误分类 | `ResolveFailureReason()` + `FormatProtocolError()` | 直接复用其逻辑：ConnectionError→timeout/connection_error, ProtocolError→http_{code}, DataProcessingError→data_processing_error |
| 冷却重试 | HTTP 5xx/429 冷却后重试 1 次 | 直接复用（不计入 TCP 层 R_max） |
| 401/404 处理 | 不重试，直接失败回调 | 直接复用 |

**实现要点：**
1. `AIChatServiceAsync` 是 `MonoBehaviour` 单例，懒初始化（首次调用时创建 GameObject）
2. `SendRequest(messages, onSuccess, onError)` 方法启动协程
3. 协程中 `UnityWebRequest.SendWebRequest()` + `yield return` 轮询 `isDone`
4. 每帧检查 `contextVersion` 是否过期（读档导致），过期则 Abort 并静默丢弃回调
5. HTTP 完成后的错误分类：
   - `ConnectionError` → 根据错误信息判断是 `timeout` 还是 `connection_error` → 触发 TCP 层重传（Phase 4 完整实现；Phase 2 仅在回调中上报错误类型）
   - `ProtocolError` → `http_{responseCode}`。401/404 直接失败并提示用户检查配置；429/5xx HTTP 层冷却重试 1 次
   - `DataProcessingError` → 同 Parse Guard（Phase 3 实现）
6. 回调在主线程 `Update()` 中执行，避免多线程竞态
7. 请求锁：Phase 2 要求在调用 `SendRequest` 前外部检查锁状态（锁的管理在 Phase 4 完善，Phase 2 在 `StoryMakerState` 中预留 `requestLocked` 字段）

**T = timeoutSeconds 参数的使用：**
- 作为 `UnityWebRequest.timeout` 的值
- Phase 2 的 HTTP 层超时直接由 UnityWebRequest 原生超时处理
- Phase 4 的 TCP 层超时计时器（现实时间 + 暂停冻结）将在此基础上叠加

**callback 签名设计（参照 RimChat 保持简单）：**
```csharp
public void SendRequest(
    List<ChatMessage> messages,
    Action<string> onSuccess,   // 参数：LLM 原始响应体字符串
    Action<string, string> onError  // 参数：(errorMessage, failureReason)
)
```

**新增文件：**
- `LLM/AIChatServiceAsync.cs` — MonoBehaviour 单例 + 协程 + 回调队列
- `LLM/ChatMessage.cs` — 简单的 `{role, content}` 数据类

**修改文件：**
- `Core/StoryMakerState.cs` — 新增 `contextVersion`、`requestLocked` 字段
- `Schedule/EventScheduler.cs` — 在 gameStartAbsTick 检测处置 contextVersion++

**验收标准：**
- [ ] `AIChatServiceAsync.Instance` 正确初始化且 `DontDestroyOnLoad`
- [ ] 调用 `SendRequest()` 后协程启动，控制台输出 `[StoryMaker] HTTP 请求已发送到 <endpoint>`
- [ ] 收到 LLM 回复后回调在主线程执行，原始回复输出到控制台
- [ ] HTTP 错误（如 401）正确分类并输出 `failure_reason` 标签
- [ ] 读档后 contextVersion 递增，飞行中请求被 Abort 并静默丢弃
- [ ] 5xx/429 错误触发冷却重试 1 次（控制台日志确认）

---

### S3：Prompt 构建系统 [AFK]

**端到端行为：** `PromptBuilder.Build(snapshot, fromTick, toTick)` 返回一个 `List<ChatMessage>`，包含 system 角色消息（系统提示词模板 + 玩家个性化描述 + PromptInjector 注入内容）和 user 角色消息（快照 JSON + 请求指令）。系统提示词语言自动匹配玩家游戏语言设置。模组外可通过 `PromptInjector.Register()` 注入额外提示词。

**涉及模块：** `Prompt/PromptBuilder.cs`（新建）、`Prompt/PromptTemplates.cs`（新建）、`Prompt/PromptInjector.cs`（新建）

**与 Phase 1 衔接：**
- 使用 `SnapshotSerializer.ToJson(snapshot)` 获取快照 JSON（Phase 1 已验证）
- `snapshot.request_range.from_tick / to_tick` 由 `EventScheduler` 在调用时填入
- 使用 `StoryMakerSettings.playerPersonality` 作为第 2 段（玩家个性化）
- 使用 `StoryMakerSettings.lowTokenMode` 决定是否跳过非核心字段
- 使用 `IncidentWhitelist` 的 category 解释信息写入 system prompt（让 LLM 理解 `ThreatBig` 等分类含义）
- 使用 `IncidentWhitelist` 的事件列表写入 system prompt（告诉 LLM 支持哪些事件类型）

**System Prompt 模板包含（中文/英文两套）：**
1. **身份声明：** "你是 RimWorld 的 AI 叙事者。你的职责是根据殖民地状态规划未来 N 天的游戏事件。"
2. **字段说明：** 快照 JSON 每个字段的含义和合法值范围（基于 Phase 1 `Phase 1 阶段总结.md` 第 2 节的字段说明表）
3. **回复格式要求：** 完整 JSON 结构说明（对应 `项目规划.md` 3.2.2 节）
4. **事件类型清单：** 从 `IncidentWhitelist` 程序化生成，按 category 分组列出所有事件名称
5. **Category 说明：** 10 个 category（AllyAssistance / DiseaseAnimal / DiseaseHuman / FactionArrival / GiveQuest / Misc / OrbitalVisitor / ShipChunkDrop / ThreatBig / ThreatSmall）的叙事含义说明
6. **重点事件单独说明（仅普通模式）：** 对名称含义不够明确的事件提供一句简述，分为两类：
   - 日常关键事件（4 个）：`ColdSnap`（寒流降温）、`HeatWave`（热浪升温）、`PsychicDrone`（特定性别心情大幅下降）、`PsychicSoothe`（特定性别心情大幅提升）
   - Anomaly DLC ThreatBig 事件（~23 个）：BloodRain, ChimeraAssault, DeathPall, DevourerAssault, DevourerWaterAssault, FleshbeastAttack, FleshmassHeart, GorehulkAssault, HateChanters, Nociosphere, PitGate, PsychicRitualSiege, Revenant, ShamblerAssault, ShamblerSwarm, ShamblerSwarmAnimals, SightstealerArrival, SightstealerSwarm, UnnaturalDarkness, WarpedObelisk_Abductor, WarpedObelisk_Duplicator, WarpedObelisk_Mutator（其余 Anomaly Misc 事件仅列名称，不做单独说明）
7. **Tick 格式规范：** 1 天 = 60000 tick，1 小时 = 2500 tick，`scheduled_tick` 必须为 2500 整倍数
8. **叙事规范：** intensity_multiplier 范围 0.5~2.0，不要在叙事文本中提及具体点数或乘数值
9. **PromptInjector 注入内容：** 拼接在模板末尾

**两种 Prompt 模式（由 `Settings.lowTokenMode` 控制）：**

| 模式 | 条件 | Category 说明 | 事件名称列表 | 重点事件单独说明 |
|------|------|:---:|:---:|:---:|
| 普通模式 | `lowTokenMode == false` | ✅ | ✅ | ✅（~27 个事件一句话说明） |
| 低 Token 模式 | `lowTokenMode == true` | ✅ | ❌ 跳过 | ❌ 跳过 |

> 低 Token 模式下，LLM 完全依赖 category 标签（每期 `recent_events[]` 中附带）+ category 说明来理解事件类型。事件名称列表不发送，节省约 2000-3000 字符。

**PromptInjector 设计（参照项目规划书 3.2.3）：**
- `PromptInjectionEntry` 数据类：ModName、Category、Content
- `PromptInjector.Register(entry)` 静态方法（供其他 Mod 在 `StaticConstructorOnStartup` 调用）
- `PromptInjector.LoadFromConfigFiles()` 从 `Resources/PromptInjections/*.json` 加载
- 注入内容拼接在 system prompt 末尾，带来源标记

**语言自动匹配（参照项目规划书 3.2.4）：**
- 读取 `Prefs.LangFolderName`
- 若文件夹名以 "Chinese" 开头 → 中文模板；否则 → 英文模板
- System prompt 中追加指令：`请使用与系统提示词相同的语言回复玩家`

**多语言模板文件位置：**
- 不放在 `Languages/`（那是翻译文件目录），而是放在 `Resources/PromptTemplates/` 下
- `Resources/PromptTemplates/SystemPrompt_CN.txt`
- `Resources/PromptTemplates/SystemPrompt_EN.txt`
- 使用 `ContentFinder` 或直接文件读取加载

**新增文件：**
- `Prompt/PromptBuilder.cs` — 组装 system + user 消息列表
- `Prompt/PromptTemplates.cs` — 加载 + 缓存多语言模板
- `Prompt/PromptInjector.cs` — 外接提示词注入注册表
- `Resources/PromptTemplates/SystemPrompt_CN.txt` — 中文系统提示词模板
- `Resources/PromptTemplates/SystemPrompt_EN.txt` — 英文系统提示词模板

**验收标准：**
- [ ] `PromptBuilder.Build()` 返回包含 1 条 system + 1 条 user 的 `List<ChatMessage>`
- [ ] 切换游戏语言后 system prompt 自动切换语言
- [ ] System prompt 中包含 IncidentWhitelist 所有事件按 category 分组列出
- [ ] Category 说明覆盖全部 10 个分类
- [ ] 普通模式下包含 ~27 个重点事件（ColdSnap+HeatWave+PsychicDrone+PsychicSoothe + ~23 Anomaly ThreatBig）的一句简述
- [ ] 低 Token 模式下不出现事件名称列表和重点事件说明，仅保留 category 说明
- [ ] 玩家个性化描述正确拼接到 user 消息开头
- [ ] `PromptInjector.Register()` 注入的内容出现在 system prompt 末尾
- [ ] 控制台输出 `[StoryMaker] Prompt 构建完成: system=xxx 字符, user=xxx 字符, mode=normal/lowToken`

---

### S4：调试日志 + 集成测试 [HITL]

**端到端行为：** 玩家在 Mod Settings 中点击"测试请求"按钮 → 触发一次完整的请求-响应循环：采集快照 → 构建 Prompt → 发送 HTTP → 接收回复 → 原始回复输出到控制台。同时，当 debugMode 开启时，每次请求和回复自动保存到 AppData 目录。控制台输出简洁的状态摘要。

**涉及模块：** `Debug/DebugLogger.cs`（新建）、`Management/StoryMakerManager.cs`（新建）、`StoryMaker.cs`（添加测试按钮）

**与 Phase 1 衔接：**
- `Settings.debugMode` 控制 DebugLogger 开关（Phase 1 已有复选框）
- `SnapshotCollector.Collect(fromTick, toTick)` → `SnapshotSerializer.ToJson(snapshot)` 提供测试快照
- `EventScheduler.OnTick()` 当前每 60000 tick 输出快照。S4 不修改此逻辑，仅新增手动触发路径

**DebugLogger 规格（参照项目规划书 3.8 节）：**
- 输出路径：`GenFilePaths.SaveDataFolder` + `../StoryMaker_DebugLogs/session_{timestamp}/`
- 每次请求：`{seq}_request.md`（完整 system prompt + user message）
- 每次回复：`{seq}_response.md`（LLM 原始响应 + HTTP 状态码 + 响应时间 + attempt 次数）
- Guard 修正重试：`{seq}_retry_request.md` / `{seq}_retry_response.md`
- `summary.json`：会话摘要（起始时间、Provider、Model、请求次数、错误统计）
- DebugLogger 通过 `Settings.debugMode` 开关控制，关闭时跳过所有文件 I/O

**StoryMakerManager（开发期简化版，参照项目规划书 3.1.6）：**
- 开发期仅提供两个按钮（在 Mod Settings 中）："测试请求"和"恢复连接"
- "测试请求"按钮：手动触发一次请求。构造请求范围：`fromTick = curTick + bufferTicks`，`toTick = curTick + bufferTicks + windowTicks`。采集快照 → 构建 Prompt → 发送 → 控制台输出结果
- "恢复连接"按钮：清除 `permanentDegraded` 标记（Phase 1 已有此字段）

**"测试请求"流程：**
```
用户点击 [测试请求]
  │
  ├─ 1. 检查配置完整性（ApiKey + ModelName 非空）
  │    └─ 缺失 → 弹窗引导到 Mod 设置页
  ├─ 2. 采集快照 SnapshotCollector.Collect(fromTick, toTick)
  ├─ 3. 构建 Prompt PromptBuilder.Build(snapshot, fromTick, toTick)
  ├─ 4. 发送请求 AIChatServiceAsync.SendRequest(messages, onSuccess, onError)
  ├─ 5. onSuccess → 控制台输出完整回复 + DebugLogger 保存
  └─ 6. onError → 控制台输出错误 + failure_reason 标签
```

**控制台输出规范（每个步骤必须输出一条 `[StoryMaker]` 日志）：**
- `[StoryMaker] ===== 测试请求开始 =====`
- `[StoryMaker] 快照采集完成，from_tick=X, to_tick=Y`
- `[StoryMaker] Prompt 构建完成，共 Z 条消息`
- `[StoryMaker] HTTP 请求已发送至 <endpoint>`
- `[StoryMaker] 收到回复: HTTP 200, 耗时 X.Xs, 首字节到达=Y.Ys`
- `[StoryMaker] ===== 测试请求结束 =====`

**新增文件：**
- `Debug/DebugLogger.cs` — 全量请求/回复文件保存
- `Management/StoryMakerManager.cs` — 开发期管理面板（测试请求 + 恢复连接）

**修改文件：**
- `StoryMaker.cs` — 在 `DoSettingsWindowContents()` 中添加"测试请求"和"恢复连接"按钮（仅在 debugMode 时显示恢复连接按钮）

**验收标准（本切片需要用户在游戏内操作验证）：**
- [ ] 用户填写正确的 API Key + Provider + Model → 点击"测试请求" → 控制台看到 LLM 原始回复
- [ ] 控制台每一步都有 `[StoryMaker]` 日志输出
- [ ] 用户填写错误的 API Key → 点击"测试请求" → 控制台输出 `failure_reason=http_401`
- [ ] debugMode 开启时，请求/回复 .md 文件正确保存到 AppData
- [ ] `summary.json` 正确记录请求次数和错误统计
- [ ] 点击"恢复连接"按钮清除降级状态

---

## 4. 与 Phase 1 基础设施的衔接汇总

| Phase 1 产出 | 文件 | Phase 2 如何使用 |
|-------------|------|-----------------|
| Mod Settings 框架 | `StoryMakerSettings.cs` + `StoryMaker.cs:DoSettingsWindowContents()` | S1 扩展字段、S4 添加测试按钮 |
| 运行时状态 | `StoryMakerState.cs`（ack, permanentDegraded） | S2 新增 contextVersion + requestLocked |
| 快照采集 | `SnapshotCollector.Collect()` + `SnapshotSerializer.ToJson()` | S3 Prompt 构建 + S4 测试请求 |
| Tick 注入 | `EventScheduler.OnTick()` | S2 在此处置 contextVersion++（读档检测） |
| 白名单 | `IncidentWhitelist` | S3 将支持的事件类型写入 system prompt |
| Debug 开关 | `Settings.debugMode` | S4 控制 DebugLogger 开关 |
| Incident 栈 | `IncidentEventStack` | S4 测试请求前清栈（可选，保证测试数据干净） |
| TCP 参数 | `Settings.bufferDays/planWindowDays/timeoutSeconds/maxRetransmissions` | S2 使用 timeoutSeconds 作为请求超时；S4 用 bufferDays/planWindowDays 构造请求范围 |

---

## 5. Phase 2 开发顺序建议

```
第1步: S1 (API 配置 + Provider 注册表)     ← 1-2 天
第2步: S2 (异步 HTTP 通信层) + S3 (Prompt 构建) ← 可并行，2-3 天
第3步: S4 (调试日志 + 集成测试)              ← 1 天
```

---

## 6. 关键参考（RimChat 对应文件）

| 参考点 | RimChat 文件 | 行数/说明 |
|--------|------------|----------|
| Provider 注册表 | `AI/AIProvider.cs` | 完整 enum + ProviderDef + Registry（180 行），直接参照 |
| API 配置 | `Config/ApiConfig.cs` | IExposable + Scribe，URL 标准化逻辑 |
| 异步通信框架 | `AI/AIChatServiceAsync.cs` | MonoBehaviour 单例 + 协程 + 回调队列 + contextVersion + HTTP 错误处理（2400+ 行）。StoryMaker 取其核心协程模式，忽略 Queue/Scheduling/LocalControl 等不需要的分部类 |
| HTTP 错误分类 | `AI/AIChatClient.cs` | `ResolveFailureReason()` + `LooksLikeTimeout()` + `BuildRequestErrorText()` |
| 错误消息格式化 | `AI/AIChatServiceAsync.cs` | `FormatProtocolError()` — switch 表达式按状态码映射 |

---

## 7. 风险与注意事项

1. **UnityWebRequest 在游戏暂停时的行为：** 协程在游戏暂停时不会执行（`Time.timeScale=0` 导致 `WaitForSeconds` 无限等待）。这与设计意图一致（计时器应冻结），但需在回调中确认 `contextVersion` 仍然有效。

2. **API Key 安全性：** API Key 通过 `Scribe_Values.Look` 以明文存入存档和 Mod Settings XML。不加密是 RimWorld Mod 的通用做法（RimChat 同样明文存储），但不在代码中硬编码、不在日志中打印完整的 API Key。

3. **System Prompt 长度控制：** ✅ 已确定方案——两套模式（普通/低Token）切换，普通模式列出全部事件+category说明+~27个重点事件简述；低Token模式仅保留category说明。详见 S3 "两种 Prompt 模式"。

4. **自定义 Provider URL 格式兼容性：** 不同 OpenAI-compatible 服务商的 endpoint 路径不一致（有的需要 `/v1/chat/completions`，有的不需要）。S1 的 URL 标准化逻辑参照 RimChat 的 `NormalizeUrl` + `EnsureChatCompletionsEndpoint`。

5. **Settings 字段增多后的 UI 布局：** Phase 1 已有 8 个控件。Phase 2 再增 4 个 API 配置控件 + 1 个测试按钮。需确保在默认模组设置窗口尺寸（约 500x400）内可滚动显示。必要时使用 `Listing_Standard` 的自动滚动或分段布局。

6. **Phase 4 TCP 协议所需的字段预留：** Phase 2 不要实现完整的 TCP 滑动窗口（那是 Phase 4 的范围），但需要确保 `StoryMakerState` 中预留的 `requestLocked`、`contextVersion`、`retryCount` 等字段在 Phase 4 可以直接使用，不需要重构。
