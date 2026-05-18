using System.Collections.Generic;
using RimWorld;
using Verse;

namespace StoryMaker.Response;

// ── LLM 回复顶层结构 ──

public class LLMResponse
{
    public PlanRange plan_range;
    public bool empty_plan;
    public string narrative_summary;
    public List<PlannedEvent> events;

    public bool HasEvents => events != null && events.Count > 0;
}

public class PlanRange
{
    public int from_tick;
    public int to_tick;
}

public class PlannedEvent : IExposable
{
    public string event_id;
    public int scheduled_tick;
    public int generated_at_world_tick;  // LLM 生成时的世界 tick，用于读档过期检测
    public string event_type;
    public Dictionary<string, string> parameters;
    public string narration_text;
    public string narrative_context;

    // 运行时解析的 IncidentDef（不序列化，读档后通过 ResolveDef() 恢复）
    [Unsaved(false)]
    public IncidentDef resolvedDef;

    // 将 event_type 字符串解析为 IncidentDef 引用。入队前必须调用。
    // 返回 true 表示解析成功，false 表示 def 不存在或 worker 为空。
    public bool ResolveDef()
    {
        resolvedDef = DefDatabase<IncidentDef>.GetNamed(event_type, false);
        return resolvedDef?.Worker != null;
    }

    // 便捷访问方法
    public bool HasParam(string key) => parameters != null && parameters.ContainsKey(key);

    public string GetStringParam(string key, string defaultValue = null)
    {
        if (parameters != null && parameters.TryGetValue(key, out var val))
            return val;
        return defaultValue;
    }

    public float GetFloatParam(string key, float defaultValue = 0f)
    {
        if (parameters != null && parameters.TryGetValue(key, out var val))
        {
            if (float.TryParse(val, out var result))
                return result;
        }
        return defaultValue;
    }

    public int GetIntParam(string key, int defaultValue = 0)
    {
        if (parameters != null && parameters.TryGetValue(key, out var val))
        {
            if (int.TryParse(val, out var result))
                return result;
        }
        return defaultValue;
    }

    public void ExposeData()
    {
        Scribe_Values.Look(ref event_id, "event_id");
        Scribe_Values.Look(ref scheduled_tick, "scheduled_tick");
        Scribe_Values.Look(ref generated_at_world_tick, "generated_at_world_tick");
        Scribe_Values.Look(ref event_type, "event_type");
        Scribe_Values.Look(ref narration_text, "narration_text");
        Scribe_Values.Look(ref narrative_context, "narrative_context");

        // Dictionary<string, string> → 两个平行 List<string>
        List<string> paramKeys = null;
        List<string> paramValues = null;
        if (Scribe.mode == LoadSaveMode.Saving && parameters != null)
        {
            paramKeys = new List<string>(parameters.Keys);
            paramValues = new List<string>(parameters.Values);
        }
        Scribe_Collections.Look(ref paramKeys, "paramKeys", LookMode.Value);
        Scribe_Collections.Look(ref paramValues, "paramValues", LookMode.Value);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            parameters = new Dictionary<string, string>();
            if (paramKeys != null && paramValues != null)
            {
                int count = paramKeys.Count < paramValues.Count ? paramKeys.Count : paramValues.Count;
                for (int i = 0; i < count; i++)
                    parameters[paramKeys[i]] = paramValues[i];
            }
        }
    }
}

// ── Guard 统一结果 ──

public class GuardResult
{
    public bool IsValid;
    public string ViolationTag;       // 如 "PARSE_NOT_JSON", "MISSING_PLAN_RANGE"
    public string ViolationDetail;    // 人类可读描述
    public List<string> ViolatedFields;

    public bool NeedsCorrection => !IsValid;

    public static GuardResult Pass()
    {
        return new GuardResult { IsValid = true };
    }

    public static GuardResult Fail(string tag, string detail, List<string> fields = null)
    {
        return new GuardResult
        {
            IsValid = false,
            ViolationTag = tag,
            ViolationDetail = detail,
            ViolatedFields = fields ?? new List<string>()
        };
    }
}

// ── 综合解析结果 ──

public class ParseResult
{
    public LLMResponse Response;
    public bool IsSuccess;

    // 各层 Guard 结果
    public GuardResult ParseGuardResult;
    public GuardResult SchemaGuardResult;
    public GuardResult EventGuardResult;

    // 被 EventGuard 移除的无效事件索引
    public List<int> RemovedEventIndices;

    // 是否需要进行修正重试
    public bool NeedsCorrection =>
        (ParseGuardResult != null && ParseGuardResult.NeedsCorrection) ||
        (SchemaGuardResult != null && SchemaGuardResult.NeedsCorrection) ||
        (EventGuardResult != null && EventGuardResult.NeedsCorrection);

    // 最先失败的 Guard 结果（用于生成修正消息）
    public GuardResult FirstViolation
    {
        get
        {
            if (ParseGuardResult != null && ParseGuardResult.NeedsCorrection)
                return ParseGuardResult;
            if (SchemaGuardResult != null && SchemaGuardResult.NeedsCorrection)
                return SchemaGuardResult;
            if (EventGuardResult != null && EventGuardResult.NeedsCorrection)
                return EventGuardResult;
            return null;
        }
    }
}
