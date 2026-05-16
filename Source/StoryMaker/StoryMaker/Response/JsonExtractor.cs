using System;
using System.Collections.Generic;
using System.Text;

namespace StoryMaker.Response;

// 轻量级 JSON 手动解析工具。因 Unity JsonUtility 不支持 Dictionary，
// 且 LLM 回复可能包裹 markdown 代码块，故采用手动解析。
// 参考 RimChat 的 ExtractFirstBalancedJsonObject / ExtractStringField 模式。
public static class JsonExtractor
{
    // ── 顶层：从 LLM 原始回复中提取干净的 JSON 字符串 ──

    public static string ExtractCleanJson(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        // 1. 尝试提取 markdown 代码块中的 JSON
        string[] fences = { "```json", "```" };
        foreach (var fence in fences)
        {
            int start = raw.IndexOf(fence, StringComparison.OrdinalIgnoreCase);
            if (start >= 0)
            {
                start += fence.Length;
                int end = raw.IndexOf("```", start, StringComparison.Ordinal);
                if (end > start)
                {
                    string inner = raw.Substring(start, end - start);
                    // 代码块内可能是 JSON 转义字符串（\n、\" 等），先还原转义
                    string unescaped = UnescapeJsonString(inner.Trim());
                    // 再从还原后的内容提取纯 JSON 对象
                    var json = ExtractFirstBalancedJson(unescaped);
                    if (!string.IsNullOrWhiteSpace(json))
                        return json;
                    // 如果还原后也没有 {，可能整个内容就是转义后的 JSON
                    json = ExtractFirstBalancedJson(inner.Trim());
                    if (!string.IsNullOrWhiteSpace(json))
                        return json;
                }
            }
        }

        // 2. 检查是否为 OpenAI 格式的包装 JSON（choices[0].message.content）
        string innerContent = ExtractString(raw, "content");
        if (!string.IsNullOrWhiteSpace(innerContent))
        {
            var nested = ExtractCleanJson(innerContent);  // 递归提取 content 中的 JSON
            if (!string.IsNullOrWhiteSpace(nested))
                return nested;
        }

        // 3. 提取第一个平衡的 JSON 对象
        return ExtractFirstBalancedJson(raw);
    }

    // 还原 JSON 字符串中的转义序列（\n、\"、\\ 等）
    private static string UnescapeJsonString(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                switch (s[i + 1])
                {
                    case 'n': sb.Append('\n'); i++; break;
                    case 'r': sb.Append('\r'); i++; break;
                    case 't': sb.Append('\t'); i++; break;
                    case '"': sb.Append('"'); i++; break;
                    case '\\': sb.Append('\\'); i++; break;
                    default: sb.Append(s[i]); break;
                }
            }
            else
            {
                sb.Append(s[i]);
            }
        }
        return sb.ToString();
    }

    private static string ExtractFirstBalancedJson(string raw)
    {
        int start = raw.IndexOf('{');
        if (start < 0) return null;

        bool inString = false;
        bool escaped = false;
        int depth = 0;

        for (int i = start; i < raw.Length; i++)
        {
            char c = raw[i];

            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (c == '\\' && inString)
            {
                escaped = true;
                continue;
            }

            if (c == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString) continue;

            if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                    return raw.Substring(start, i - start + 1);
            }
        }

        return null;
    }

    // ── 字段提取 ──

    public static string ExtractString(string json, string key)
    {
        string pattern = $"\"{key}\"";
        int keyIdx = json.IndexOf(pattern, StringComparison.Ordinal);
        if (keyIdx < 0) return null;

        int colonIdx = json.IndexOf(':', keyIdx + pattern.Length);
        if (colonIdx < 0) return null;

        int valueStart = SkipWhitespace(json, colonIdx + 1);
        if (valueStart >= json.Length || json[valueStart] != '"') return null;

        return ReadQuotedString(json, valueStart);
    }

    public static int? ExtractInt(string json, string key)
    {
        string s = ExtractNumberRaw(json, key);
        if (s == null) return null;
        if (int.TryParse(s, out int val)) return val;
        return null;
    }

    public static float? ExtractFloat(string json, string key)
    {
        string s = ExtractNumberRaw(json, key);
        if (s == null) return null;
        if (float.TryParse(s,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out float val)) return val;
        return null;
    }

    public static bool? ExtractBool(string json, string key)
    {
        string pattern = $"\"{key}\"";
        int keyIdx = json.IndexOf(pattern, StringComparison.Ordinal);
        if (keyIdx < 0) return null;

        int colonIdx = json.IndexOf(':', keyIdx + pattern.Length);
        if (colonIdx < 0) return null;

        int valueStart = SkipWhitespace(json, colonIdx + 1);
        if (valueStart >= json.Length) return null;

        string rest = json.Substring(valueStart);
        if (rest.StartsWith("true")) return true;
        if (rest.StartsWith("false")) return false;
        return null;
    }

    private static string ExtractNumberRaw(string json, string key)
    {
        string pattern = $"\"{key}\"";
        int keyIdx = json.IndexOf(pattern, StringComparison.Ordinal);
        if (keyIdx < 0) return null;

        int colonIdx = json.IndexOf(':', keyIdx + pattern.Length);
        if (colonIdx < 0) return null;

        int valueStart = SkipWhitespace(json, colonIdx + 1);
        if (valueStart >= json.Length) return null;

        // 读取连续的数字字符（含负号、小数点）
        int end = valueStart;
        while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '-' || json[end] == '.'))
            end++;

        if (end == valueStart) return null;
        return json.Substring(valueStart, end - valueStart);
    }

    // ── 嵌套对象提取 ──

    public static string ExtractObject(string json, string key)
    {
        string pattern = $"\"{key}\"";
        int keyIdx = json.IndexOf(pattern, StringComparison.Ordinal);
        if (keyIdx < 0) return null;

        int colonIdx = json.IndexOf(':', keyIdx + pattern.Length);
        if (colonIdx < 0) return null;

        int objStart = SkipWhitespace(json, colonIdx + 1);
        if (objStart >= json.Length || json[objStart] != '{') return null;

        return ExtractBalanced(json, objStart, '{', '}');
    }

    // ── 数组提取与拆分 ──

    public static string ExtractArray(string json, string key)
    {
        string pattern = $"\"{key}\"";
        int keyIdx = json.IndexOf(pattern, StringComparison.Ordinal);
        if (keyIdx < 0) return null;

        int colonIdx = json.IndexOf(':', keyIdx + pattern.Length);
        if (colonIdx < 0) return null;

        int arrStart = SkipWhitespace(json, colonIdx + 1);
        if (arrStart >= json.Length || json[arrStart] != '[') return null;

        return ExtractBalanced(json, arrStart, '[', ']');
    }

    // 将 "[{...},{...}]" 拆分为单独的 "{...}" 字符串列表
    public static List<string> SplitJsonArray(string arrayJson)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(arrayJson)) return result;

        int start = arrayJson.IndexOf('{');
        if (start < 0) return result;

        bool inString = false;
        bool escaped = false;
        int depth = 0;
        int itemStart = start;

        for (int i = start; i < arrayJson.Length; i++)
        {
            char c = arrayJson[i];

            if (escaped) { escaped = false; continue; }
            if (c == '\\' && inString) { escaped = true; continue; }
            if (c == '"') { inString = !inString; continue; }
            if (inString) continue;

            if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    result.Add(arrayJson.Substring(itemStart, i - itemStart + 1));
                    itemStart = arrayJson.IndexOf('{', i + 1);
                    if (itemStart < 0) break;
                    i = itemStart - 1;
                }
            }
        }

        return result;
    }

    // ── 对象内部字段提取（用于提取嵌套对象的字段，如 plan_range.from_tick）──

    public static string ExtractObjectField(string jsonObj, string key)
    {
        return ExtractString(jsonObj, key);
    }

    public static int? ExtractObjectFieldInt(string jsonObj, string key)
    {
        return ExtractInt(jsonObj, key);
    }

    public static float? ExtractObjectFieldFloat(string jsonObj, string key)
    {
        return ExtractFloat(jsonObj, key);
    }

    public static bool? ExtractObjectFieldBool(string jsonObj, string key)
    {
        return ExtractBool(jsonObj, key);
    }

    // ── 辅助方法 ──

    private static string ReadQuotedString(string json, int startQuote)
    {
        var sb = new StringBuilder();
        for (int i = startQuote + 1; i < json.Length; i++)
        {
            char c = json[i];
            if (c == '\\' && i + 1 < json.Length)
            {
                char next = json[i + 1];
                switch (next)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u': sb.Append('?'); i += 5; break;  // 跳过 u + 4 位 hex，i++ 后共跳 6 字符
                    default: sb.Append(next); break;
                }
                i++;
            }
            else if (c == '"')
            {
                return sb.ToString();
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    private static string ExtractBalanced(string json, int openIdx, char openChar, char closeChar)
    {
        bool inString = false;
        bool escaped = false;
        int depth = 0;

        for (int i = openIdx; i < json.Length; i++)
        {
            char c = json[i];

            if (escaped) { escaped = false; continue; }
            if (c == '\\' && inString) { escaped = true; continue; }
            if (c == '"') { inString = !inString; continue; }
            if (inString) continue;

            if (c == openChar) depth++;
            else if (c == closeChar)
            {
                depth--;
                if (depth == 0)
                    return json.Substring(openIdx, i - openIdx + 1);
            }
        }

        return null;
    }

    private static int SkipWhitespace(string s, int start)
    {
        while (start < s.Length && char.IsWhiteSpace(s[start]))
            start++;
        return start;
    }
}
