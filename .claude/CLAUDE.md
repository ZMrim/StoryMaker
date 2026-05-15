# CLAUDE.md — StoryMaker 项目开发指南

## 项目概述

StoryMaker 是一个 RimWorld 模组，用云端 LLM（DeepSeek、OpenAI、Gemini 等）替代原版叙事者。模组定期抓取游戏状态快照发送给 LLM，LLM 提前为未来 N 天规划事件序列，模组按精确 tick 执行事件队列。

**项目规划书：** [`项目规划.md`](项目规划.md)（位于项目根目录）。务必阅读完整版本后再开始任何开发工作。

---

## 核心开发原则

### 1. 严格遵守项目规划书

- 所有开发工作必须遵循 `项目规划.md` 中定义的架构、模块职责和开发阶段。不得偏离规划书的模块划分和接口设计。
- 规划书中的 **架构决策记录（第 6.1 节）** 是已确定的方案，不得随意更改。
- 各阶段的 **待定事项（第 6.2 节）** 留到对应阶段解决，不得提前实施。
- 各个阶段开发中同时需要遵循当前阶段的阶段规划书。

### 2. 发现问题必须报告

- 如果开发过程中发现项目规划存在错误、矛盾、遗漏，或技术上无法实现的部分，**必须停止当前工作并告知用户**。
- 由用户来决定是修改规划书还是采用替代方案。不得自行绕过或修改规划书的决策。
- 如果阶段规划书与项目规划书有冲突，也需要告知用户。

### 3. 分阶段开发

- 严格按照 Phase 1 → Phase 2 → Phase 3 → Phase 4 → Phase 5 的顺序推进。
- 每个阶段完成后，用户会确认里程碑再进入下一阶段。
- 每个阶段开始时，需要与用户商讨并共同编写阶段规划书。

### 4. 必要时进行验证

- 由于Agent无法自主打开游戏并操作，因此如果有需要验证输出的地方都要告知用户，指导用户打开游戏查看控制台输出。
- Agent可以根据需要在开发过程中要求用户进行必要的验证操作，例如查看控制台输出、检查文件内容等，以确认实现的功能是否符合预期。


---

## 工具使用要求

### RimWorld 源码查看

- **必须使用 RimSage MCP 工具或 RimSage_Skill** 来查看 RimWorld 原版源码和 Def 定义。
- 可用的 MCP 工具：`search_defs`、`get_def_details`、`search_source`、`read_csharp_symbol`、`read_file`、`list_directory`。
- 不要尝试在项目目录外直接搜索 RimWorld 的源码文件。RimSage 已索引全部原版源码和 Def。

### 参考模组

- 遇到 LLM 通信、HTTP 请求封装、多层 Guard 校验、提示词注入、响应解析等问题时，**应优先查阅 `参考模组RimChat/` 文件夹**。
- RimChat 是一个成熟的 RimWorld LLM 对话模组，其 `MonoBehaviour + UnityWebRequest + 协程` 的异步通信模式、`ResolveFailureReason` HTTP 错误分类体系、`ContractGuard` 多层校验模式均已被验证可行，相似的设计直接"拿来主义"。

---

## 代码规范

### 语言与注释

- **注释使用中文**。代码标识符（类名、方法名、变量名）使用英文。
- 注释应说明"为什么这样做"而非"做了什么"（好的命名已说明做了什么）。
- 不要在代码中写冗长的文档注释块。一行简洁的注释足以说明非线性逻辑或隐藏约束。

### 命名规范

- 类名、方法名：PascalCase
- 变量名、参数名：camelCase
- 常量：PascalCase 或 UPPER_SNAKE_CASE
- 私有字段：camelCase（不使用下划线前缀）

### 代码组织

- 遵循项目规划书 2.1 节定义的模块目录结构。
- 每个文件只包含一个主要的类或接口。小的辅助类型可以放在同一文件。
- 不要引入不必要的抽象层。三个相似的 switch-case 分支不需要重构为策略模式。遵循规划书的模块划分，不要自行创建新的模块。

### Harmony Patch

- Harmony patch 类统一放在 `Source/` 目录下，命名 `Patch_<TargetClass>.cs`。
- 每个 patch 类只处理一个目标的 patch，用 `[HarmonyPatch]` attribute 明确标注。
- Prefix/Postfix 方法命名清晰（如 `Prefix_StorytellerTick`）。

### RimWorld 兼容

- 不使用 C# 原生 async/await（Unity Mono 运行时支持不完整）。
- 存档序列化必须经过 `ExposeData()`，遵循规划书 3.6 节的序列化规则。
- 所有在游戏主线程外部的回调使用 `AIChatServiceAsync.ExecuteOnMainThread()` 投递。
- Mod 兼容性：通过 `PromptInjector` 对外暴露接口，不直接操作其他 Mod 的数据。

### 安全与错误处理

- **绝不硬编码 API Key、密码或敏感信息**。所有凭证从 Mod Settings 读取。
- HTTP 层错误按规划书 3.5.1 节分类处理，分类标签（`failure_reason`）必须精确。
- 降级路径必须通畅：HTTP 失败 → TCP 重传 → 降级弹窗 → 原版接管。任何节点都不能让游戏卡死或静默失效。

### 必要的debug
- 由于验证阶段Agent无法独立完成，需要用户打开游戏并进行必要操作，才能在游戏的控制台查看日志，因此**必须在关键节点输出必要的debug信息**以便用户反馈问题。debug信息需尽量清晰且简短地确认当前函数被调用、关键变量的值等。

### 统一日志规范

- 所有输出到游戏控制台的日志必须带有统一的前缀`[StoryMaker]`。

---

## 关键架构要点速查

| 主题 | 位置 | 核心决策 |
|------|------|---------|
| TCP 滑动窗口调度 | 3.1 节 | ACK 指针 + 请求锁 + 现实时间计时器 + R_max 重传 |
| StorytellerDef 替换 | 3.1.7 节 | `ParentName="BaseStoryteller"`，Harmony prefix patch `Storyteller.StorytellerTick()` |
| LLM 点数控制 | 3.2.2 节 | `intensity_multiplier` 0.5~2.0，原版计算 × LLM 乘数 |
| 快照字段 | 3.2.1 节 | 整体化原则，`ISnapshotField` 可扩展接口 |
| 四层 Guard | 3.3 节 | Parse → Schema → Event → EmptyPlan，每层重试上限 1 次 |
| HTTP 错误处理 | 3.5.1 节 | 双层重试（HTTP 层 + TCP 层），借鉴 RimChat 体系 |
| 存档序列化 | 3.6 节 | ExposeData + 原子性 + contextVersion 保护 |
| 多语言 | 3.2.4 节 | 多套提示词模板，自动检测玩家语言 |
| 交互式对话 | 3.9 节 | Phase 5+，预留接口 |

---

## 开发流程

1. **开工前：** 通读对应阶段的规划书内容，确认无待定问题未解决。
2. **开发中：** 遵守模块边界，使用 RimSage 查源码，参考 RimChat 的处理方式。
3. **遇到障碍：** 停止 → 分析 → 告知用户 → 一起决策。
4. **阶段完成：** 对照规划书的里程碑 checklist 逐项验证。
5. **跨阶段事项：** 查看第 6.2 节确认当前阶段需要解决的问题。
