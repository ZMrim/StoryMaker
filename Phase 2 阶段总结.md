# Phase 2 阶段总结：LLM 通信 + Prompt 构建

## 里程碑状态：✅ 完成

模组可配置 LLM 提供商和 API Key，构建三段式 Prompt（系统提示词 + 玩家个性化 + 快照 JSON），通过 HTTP 发送至 LLM，收到原始回复并记录到控制台和 Debug 日志。

---

## 1. 已验证的端到端流程

```
用户点击 [发送测试请求]
  → 配置完整性检查
  → SnapshotCollector.Collect() 采集快照
  → PromptBuilder.Build() 组装三段式 Prompt
  → AIChatServiceAsync.SendRequest() HTTP 发送
  → LLM 回复 → 控制台输出 + DebugLogger 保存
```

### 真实测试数据（硅基流动 DeepSeek-V3.2，低 Token 模式，思考关闭）

**请求：** system=3431 字符 + user=1024 字符，共 2 条消息，1670 prompt tokens

**LLM 回复：**
```json
{
  "plan_range": {
    "from_tick": 180031,
    "to_tick": 480031
  },
  "empty_plan": false,
  "narrative_summary": "只有两个人的小可怜虫，在沙漠里瑟瑟发抖。这点财富和食物，够谁吃的？看看那些敌对的派系，他们一定迫不及待想来看看你们了。是时候让生活热闹一点了。",
  "events": [
    {
      "event_id": "raid1",
      "scheduled_tick": 195000,
      "event_type": "RaidEnemy",
      "parameters": { "faction": "韦格斯恩", "intensity_multiplier": 0.6 },
      "narration_text": "雷达显示有几个不怀好意的身影正在接近。他们带着简陋的武器，但对付两个可怜虫，或许绰绰有余。",
      "narrative_context": "早期敌对派系试探性袭击，测试殖民地防御能力。"
    },
    {
      "event_id": "haze1",
      "scheduled_tick": 217500,
      "event_type": "NoxiousHaze",
      "parameters": { "intensity_multiplier": 1.0 },
      "narration_text": "一股有毒的浓雾无声无息地笼罩了殖民地。沙漠的风也无法将它吹散。",
      "narrative_context": "在沙漠环境中添加生存压力，干扰户外活动并可能引发健康问题。"
    },
    {
      "event_id": "disease1",
      "scheduled_tick": 245000,
      "event_type": "Disease_Flu",
      "parameters": { "intensity_multiplier": 1.3 },
      "narration_text": "其中一位殖民者开始咳嗽、发烧。在这种恶劣的环境和紧张的心情下，疾病总是来得特别快。",
      "narrative_context": "趁殖民地人口少、心情一般时引入疾病，削弱其劳动力。"
    },
    {
      "event_id": "trader1",
      "scheduled_tick": 280000,
      "event_type": "TraderCaravanArrival",
      "parameters": { "faction": "天行交易所", "intensity_multiplier": 1.0 },
      "narration_text": "一支商队缓缓驶入视野。他们带来了货物，也带来了希望。",
      "narrative_context": "提供贸易机会，但鉴于殖民地财富有限，可能加剧其资源匮乏感。"
    }
  ]
}
```

**消耗：** prompt=1670, completion=604, total=2274 tokens（思考模式关闭，无 reasoning_tokens）

### 验证结论

- 系统提示词引导效果良好：LLM 正确理解沙漠环境 + 2 人殖民地 + 多敌对派系
- 叙事风格适配：玩家的黑暗幽默性格被准确执行（"小可怜虫"、"好好享受"）
- 4 个事件类型全在白名单内，scheduled_tick 全为 2500 倍数
- intensity_multiplier 正确用于威胁类事件（RaidEnemy=0.6, Disease_Flu=1.3）
- 参数格式完整（faction, narration_text, narrative_context 全覆盖）

---

## 2. 文件产出清单（19 源文件 + 10 资源文件）

```
Source/StoryMaker/StoryMaker/
├── StoryMaker.cs                         ← Mod 入口 + Settings UI（滚动面板 + 测试按钮）
├── Core/
│   ├── StoryMakerSettings.cs             ← B/N/T/R_max + API配置 + enableThinking + 种族白名单
│   ├── StoryMakerState.cs                ← ack/permanentDegraded/contextVersion/requestLocked
│   └── StoryMakerExpose.cs               ← Scribe 序列化（含 contextVersion）
├── LLM/
│   ├── AIProviderDef.cs                  ← AIProvider 枚举 + ProviderDef 结构体
│   ├── AIProviderRegistry.cs             ← 5 个 Provider 预设注册表 [StaticConstructorOnStartup]
│   ├── ChatMessage.cs                    ← {role, content} 数据类
│   ├── AIChatServiceAsync.cs             ← MonoBehaviour 单例 + UnityWebRequest + 协程 + 回调队列
│   └── ThinkingModeAdapter.cs            ← 9 家提供商/平台的思考模式统一适配
├── Prompt/
│   ├── PromptInjector.cs                 ← 外接提示词注入（C# API + JSON 配置文件）
│   ├── PromptTemplates.cs                ← 多语言模板加载/缓存（普通 + 低Token 双模式）
│   └── PromptBuilder.cs                  ← 三段式 Prompt 组装 + 外部数据加载 + 语言自适应
├── Debug/
│   └── DebugLogger.cs                    ← session 目录管理 + 请求/回复/错误 .md 保存 + summary.json
├── Management/
│   └── StoryMakerManager.cs              ← SendTestRequest() + ResetDegradation()
├── Schedule/
│   └── EventScheduler.cs                 ← OnTick: gameStartAbsTick 检测 + contextVersion++ + 快照输出
└── Snapshot/
    ├── IncidentEventStack.cs             ← PopAllIncidents/PopAllDeaths 调试日志增强
    └── SnapshotFields.cs                 ← RecentEvents/RecentDeaths 低Token兼容修复

Resources/
├── PromptTemplates/
│   ├── SystemPrompt_CN.txt               ← 中文系统提示词模板（普通）
│   ├── SystemPrompt_EN.txt               ← 英文系统提示词模板（普通）
│   ├── SystemPrompt_CN_LowToken.txt      ← 中文系统提示词模板（低Token 精简）
│   └── SystemPrompt_EN_LowToken.txt      ← 英文系统提示词模板（低Token 精简）
└── PromptData/
    ├── CN/
    │   ├── Categories.txt                ← 10 个 category 中文说明
    │   ├── Categories_LowToken.txt       ← 10 个 category 精简中文说明
    │   └── KeyEvents.txt                 ← ~27 个重点事件中文说明
    └── EN/
        ├── Categories.txt                ← 10 个 category 英文说明
        ├── Categories_LowToken.txt       ← 10 个 category 精简英文说明
        └── KeyEvents.txt                 ← ~27 个重点事件英文说明
```

---

## 3. 与 Phase 1 的衔接

| Phase 1 基础设施 | Phase 2 使用方式 |
|-----------------|-----------------|
| `StoryMakerSettings` | 扩展 API Key / Provider / Model / CustomURL / enableThinking |
| `StoryMakerState` | 新增 `contextVersion` + `requestLocked`，读档时 `contextVersion++` |
| `SnapshotCollector.Collect()` | `SendTestRequest()` 调用，获取快照 |
| `SnapshotSerializer.ToJson()` | `PromptBuilder` 调用，生成快照 JSON |
| `IncidentWhitelist` | `PromptBuilder` 调用 `GetCategorizedEvents()` 生成事件清单 |
| `IncidentEventStack` | `SnapshotFields.RecentEvents` 调用 `PopAllIncidents()` 消费事件栈 |
| `EventScheduler.OnTick()` | 读档时 `contextVersion++`（保留事件栈不清空） |
| Mod Settings UI | 扩展滚动面板 + Provider 下拉 + 测试/恢复按钮 |

---

## 4. 核心架构决策

| # | 决策 | 结论 |
|---|------|------|
| 1 | HTTP 通信模式 | MonoBehaviour 单例 + UnityWebRequest + 协程 + `Update()` 回调队列。参照 RimChat 但不引入队列调度/优先级等复杂度 |
| 2 | 提示词外部化 | 系统提示词模板、category 说明、重点事件说明全部放入 `Resources/` 下的 .txt 文件，修改无需重编译 |
| 3 | 低 Token 双模板 | 普通模式用完整模板（~3400 字符），低 Token 模式用精简模板（~1700 字符） + 精简 category 描述 + 保留事件列表但省略重点事件说明 |
| 4 | 思考模式适配 | `ThinkingModeAdapter` 统一适配 5 家已知 Provider + 7 家自定义 URL 匹配方案（国内覆盖阿里/百度/火山/华为/腾讯，京东无 OpenAI 兼容端点跳过） |
| 5 | 玩家个性化位置 | 从 user 消息移到 system prompt，以 `## 玩家自定义叙事风格` 标注 |
| 6 | 事件栈生命周期 | 读档时**不清空**栈（Phase 1 的 `ClearAll()` 已移除），由每次快照 `PopAll` 自然消耗。跨存档污染留到 Phase 4 序列化根治 |
| 7 | 上下文版本保护 | `contextVersion` 在 `gameStartAbsTick` 变化时递增，HTTP 协程每帧和完成后校验，不匹配则 Abort 并丢弃回调 |

---

## 5. Phase 2 期间遇到的坑

| 坑 | 现象 | 修复 |
|----|------|------|
| Provider 注册表日志延迟 | 打开游戏不输出，打开 Settings 才输出 | `AIProviderRegistry` 加 `[StaticConstructorOnStartup]` |
| Settings 窗口内容溢出 | 测试按钮和死亡白名单不可见 | 用 `Widgets.BeginScrollView` 包裹全部内容，高度设为 1100px |
| 玩家个性化无法填写 | 输入框被挤出可视区 | 重新排列 UI 顺序，测试按钮置顶 |
| 提示词数据硬编码 | category/KeyEvent 修改需重编译 | 全部移到 `Resources/PromptData/` 外部 .txt 文件 |
| 跨存档事件丢失 | 读档后 `ClearAll()` 清空事件栈 | 移除 `ClearAll()`，保留 `contextVersion++` |
| `Dialog_MessageBox` 参数顺序 | CS1503: string → Action 转换失败 | 参数改为 `("文本", "确定", null)` |
| 硅基流动超时 | 开启思考模式后 60s 内未收到数据 | 关闭思考模式秒回；调大超时 120-180s；换 DeepSeek 官方 |
| `\n` 转义疑虑 | Prompt 内容中 `\n` 可能无法解析 | 确认是 JSON 标准转义，Phase 3 `JsonUtility` 自动还原 |

---

## 6. 已知限制（留到后续阶段）

| 限制 | 负责阶段 | 说明 |
|------|---------|------|
| 响应解析 | Phase 3 | 当前只记录原始回复，不解析 JSON。不同 provider 格式差异（markdown 包裹 vs 纯 JSON）需 `ResponseParser` 处理 |
| 事件队列 + 完整调度 | Phase 3/4 | 当前手动测试，无自动滑动窗口触发 |
| 存档序列化 | Phase 4 | `IncidentEventStack` 不序列化；读档后若开新游戏可能携带旧栈残留 |
| DebugLogger 耗时信息 | Phase 4 | `LogResponse` 的 `httpCode`/`elapsedMs` 由回调传硬编码值，实际值在协程内 |
| Guard 校验 | Phase 3 | `from_tick` 偏差（180037 vs 180057）未校验修正 |
| 多轮修正重试 | Phase 3 | HTTP 层已有 5xx/429 重试，但格式修正重试未接入 |

---

## 7. 项目规划书版本

`项目规划.md` Phase 2 任务清单全部可标记完成。需同步更新 `6.2 各阶段待定事项` 中 Phase 2 部分为空。
