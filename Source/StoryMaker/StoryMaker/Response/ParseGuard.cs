namespace StoryMaker.Response;

// 第1层 Guard：验证回复是否为合法 JSON
public static class ParseGuard
{
    public static GuardResult Validate(string rawResponse)
    {
        // 检查空响应
        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            return GuardResult.Fail("PARSE_EMPTY_RESPONSE",
                "LLM 回复为空", new System.Collections.Generic.List<string> { "raw_response" });
        }

        // 检查是否包含有效的 JSON 对象
        string cleanJson = JsonExtractor.ExtractCleanJson(rawResponse);
        if (string.IsNullOrWhiteSpace(cleanJson))
        {
            return GuardResult.Fail("PARSE_NOT_JSON",
                "回复中未找到有效的 JSON 对象（首字符 { 末字符 }）",
                new System.Collections.Generic.List<string> { "raw_response" });
        }

        // 检查 JSON 基本结构：至少包含 { 和 }
        if (!cleanJson.StartsWith("{") || !cleanJson.EndsWith("}"))
        {
            return GuardResult.Fail("PARSE_NOT_JSON",
                "提取的 JSON 不是完整对象结构",
                new System.Collections.Generic.List<string> { "clean_json" });
        }

        return GuardResult.Pass();
    }
}
