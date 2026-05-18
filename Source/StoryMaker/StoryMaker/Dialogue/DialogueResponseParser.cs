using System.Collections.Generic;
using StoryMaker.Response;

namespace StoryMaker.Dialogue;

// 对话回复解析：dialogue_text + immediate_event + 可选事件字段
public static class DialogueResponseParser
{
    public static DialogueParseResult Parse(string rawResponse)
    {
        var result = new DialogueParseResult();

        // 提取干净 JSON
        string cleanJson = JsonExtractor.ExtractCleanJson(rawResponse);
        if (string.IsNullOrWhiteSpace(cleanJson) || !cleanJson.TrimStart().StartsWith("{"))
        {
            result.Error = "PARSE_NOT_JSON";
            result.ErrorDetail = "回复不是合法的 JSON 格式";
            return result;
        }

        var response = new DialogueResponse();

        // dialogue_text（必需）
        response.dialogue_text = JsonExtractor.ExtractString(cleanJson, "dialogue_text");
        if (string.IsNullOrEmpty(response.dialogue_text))
        {
            result.Error = "MISSING_DIALOGUE_TEXT";
            result.ErrorDetail = "dialogue_text 不能为空";
            return result;
        }

        // immediate_event（必需）
        bool? immEvent = JsonExtractor.ExtractBool(cleanJson, "immediate_event");
        if (!immEvent.HasValue)
        {
            result.Error = "MISSING_IMMEDIATE_EVENT";
            result.ErrorDetail = "immediate_event 字段缺失或不是布尔值";
            return result;
        }
        response.immediate_event = immEvent.Value;

        // 如果有事件，解析事件字段
        if (response.immediate_event)
        {
            response.event_type = JsonExtractor.ExtractString(cleanJson, "event_type");
            if (string.IsNullOrEmpty(response.event_type))
            {
                result.Error = "MISSING_EVENT_TYPE";
                result.ErrorDetail = "immediate_event 为 true 时，event_type 不能为空";
                return result;
            }

            response.narrative_context = JsonExtractor.ExtractString(cleanJson, "narrative_context") ?? "";

            // 解析 parameters
            string paramsJson = JsonExtractor.ExtractObject(cleanJson, "parameters");
            response.parameters = JsonExtractor.ExtractKeyValuePairs(paramsJson);
        }

        result.IsSuccess = true;
        result.Response = response;
        return result;
    }
}

public class DialogueParseResult
{
    public bool IsSuccess;
    public DialogueResponse Response;
    public string Error;
    public string ErrorDetail;
}
