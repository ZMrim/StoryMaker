using System.Collections.Generic;
using StoryMaker.Response;
using Verse;

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
            response.parameters = ParseParameters(paramsJson);
        }

        result.IsSuccess = true;
        result.Response = response;
        return result;
    }

    private static Dictionary<string, string> ParseParameters(string paramsJson)
    {
        var parameters = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(paramsJson)) return parameters;

        int pos = 1;
        while (pos < paramsJson.Length - 1)
        {
            while (pos < paramsJson.Length && char.IsWhiteSpace(paramsJson[pos]))
                pos++;
            if (pos >= paramsJson.Length - 1) break;

            if (paramsJson[pos] != '"') break;
            string key = ReadQuotedString(paramsJson, pos, out int keyEnd);
            if (key == null) break;
            pos = keyEnd;

            int colonIdx = paramsJson.IndexOf(':', pos);
            if (colonIdx < 0) break;
            pos = colonIdx + 1;

            while (pos < paramsJson.Length && char.IsWhiteSpace(paramsJson[pos]))
                pos++;

            string value;
            if (pos < paramsJson.Length && paramsJson[pos] == '"')
                value = ReadQuotedString(paramsJson, pos, out pos);
            else if (pos < paramsJson.Length && (char.IsDigit(paramsJson[pos]) || paramsJson[pos] == '-'))
            {
                int valEnd = pos;
                while (valEnd < paramsJson.Length && (char.IsDigit(paramsJson[valEnd]) || paramsJson[valEnd] == '-' || paramsJson[valEnd] == '.'))
                    valEnd++;
                value = paramsJson.Substring(pos, valEnd - pos);
                pos = valEnd;
            }
            else { pos++; continue; }

            if (key != null && value != null)
                parameters[key] = value;

            while (pos < paramsJson.Length && (char.IsWhiteSpace(paramsJson[pos]) || paramsJson[pos] == ','))
                pos++;
        }

        return parameters;
    }

    private static string ReadQuotedString(string s, int start, out int endPos)
    {
        endPos = start;
        if (start >= s.Length || s[start] != '"') return null;

        var sb = new System.Text.StringBuilder();
        int i = start + 1;
        while (i < s.Length)
        {
            char c = s[i];
            if (c == '\\' && i + 1 < s.Length)
            {
                switch (s[i + 1])
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    default: sb.Append(s[i + 1]); break;
                }
                i += 2;
            }
            else if (c == '"') { endPos = i + 1; return sb.ToString(); }
            else { sb.Append(c); i++; }
        }
        endPos = i;
        return sb.ToString();
    }
}

public class DialogueParseResult
{
    public bool IsSuccess;
    public DialogueResponse Response;
    public string Error;
    public string ErrorDetail;
}
