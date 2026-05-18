using System.Collections.Generic;
using Verse;

namespace StoryMaker.Response;

public static class ResponseParser
{
    // 解析 LLM 原始回复为 LLMResponse。
    // 成功返回 ParseResult.IsSuccess=true，失败返回 ParseResult 包含失败原因。
    public static ParseResult Parse(string rawResponse)
    {
        var result = new ParseResult();

        // 第1层：ParseGuard —— JSON 合法性
        result.ParseGuardResult = ParseGuard.Validate(rawResponse);
        if (result.ParseGuardResult.NeedsCorrection)
        {
            Log.Warning($"[StoryMaker] ParseGuard 失败: {result.ParseGuardResult.ViolationTag} - {result.ParseGuardResult.ViolationDetail}");
            return result;
        }

        // 提取干净 JSON 并构建对象
        string cleanJson = JsonExtractor.ExtractCleanJson(rawResponse);
        var response = BuildResponse(cleanJson);
        if (response == null)
        {
            result.ParseGuardResult = GuardResult.Fail("PARSE_DESERIALIZE_FAILED",
                "JSON 结构不完整，无法提取必要字段");
            return result;
        }
        result.Response = response;

        // 第2层：SchemaGuard —— 必需字段校验
        result.SchemaGuardResult = SchemaGuard.Validate(response);
        if (result.SchemaGuardResult.NeedsCorrection)
        {
            Log.Warning($"[StoryMaker] SchemaGuard 失败: {result.SchemaGuardResult.ViolationTag} - {result.SchemaGuardResult.ViolationDetail}");
            return result;
        }

        // 第3层：EventGuard —— 事件字段校验
        result.EventGuardResult = EventGuard.Validate(response);
        result.RemovedEventIndices = EventGuard.RemovedIndices;
        if (result.EventGuardResult.NeedsCorrection)
        {
            Log.Warning($"[StoryMaker] EventGuard 发现问题: {result.EventGuardResult.ViolationTag}");
        }

        result.IsSuccess = !result.NeedsCorrection;
        return result;
    }

    private static LLMResponse BuildResponse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            var response = new LLMResponse();

            // plan_range 对象
            string planRangeJson = JsonExtractor.ExtractObject(json, "plan_range");
            if (planRangeJson != null)
            {
                response.plan_range = new PlanRange
                {
                    from_tick = JsonExtractor.ExtractObjectFieldInt(planRangeJson, "from_tick") ?? 0,
                    to_tick = JsonExtractor.ExtractObjectFieldInt(planRangeJson, "to_tick") ?? 0
                };
            }

            // empty_plan
            response.empty_plan = JsonExtractor.ExtractBool(json, "empty_plan") ?? false;

            // narrative_summary
            response.narrative_summary = JsonExtractor.ExtractString(json, "narrative_summary") ?? "";

            // events 数组
            response.events = BuildEvents(json);

            return response;
        }
        catch (System.Exception ex)
        {
            Log.Error($"[StoryMaker] ResponseParser.BuildResponse 异常: {ex.Message}");
            return null;
        }
    }

    private static List<PlannedEvent> BuildEvents(string json)
    {
        var events = new List<PlannedEvent>();
        string eventsArray = JsonExtractor.ExtractArray(json, "events");
        if (string.IsNullOrWhiteSpace(eventsArray)) return events;

        List<string> eventItems = JsonExtractor.SplitJsonArray(eventsArray);
        foreach (string itemJson in eventItems)
        {
            var evt = BuildSingleEvent(itemJson);
            if (evt != null)
                events.Add(evt);
        }

        return events;
    }

    private static PlannedEvent BuildSingleEvent(string eventJson)
    {
        if (string.IsNullOrWhiteSpace(eventJson)) return null;

        var evt = new PlannedEvent();

        evt.event_id = JsonExtractor.ExtractString(eventJson, "event_id") ?? "";
        evt.scheduled_tick = JsonExtractor.ExtractInt(eventJson, "scheduled_tick") ?? 0;
        evt.event_type = JsonExtractor.ExtractString(eventJson, "event_type") ?? "";
        evt.narration_text = JsonExtractor.ExtractString(eventJson, "narration_text");
        evt.narrative_context = JsonExtractor.ExtractString(eventJson, "narrative_context") ?? "";

        // parameters 是可变键值对，单独提取
        string paramsJson = JsonExtractor.ExtractObject(eventJson, "parameters");
        evt.parameters = JsonExtractor.ExtractKeyValuePairs(paramsJson);

        return evt;
    }
}
