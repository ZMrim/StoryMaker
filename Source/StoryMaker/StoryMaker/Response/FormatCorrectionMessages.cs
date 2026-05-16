using System.Text;

namespace StoryMaker.Response;

// 为每个违规标签生成对应的修正提示词，追加到对话历史中发送给 LLM 修正。
// 参考 RimChat 的双语标签前缀模式：每条修正消息以唯一违规标签开头。
public static class FormatCorrectionMessages
{
    public static string Build(GuardResult violation)
    {
        if (violation == null || !violation.NeedsCorrection)
            return null;

        return violation.ViolationTag switch
        {
            "PARSE_EMPTY_RESPONSE" => BuildParseError("回复为空"),
            "PARSE_NOT_JSON" => BuildParseError("未找到有效 JSON"),
            "PARSE_DESERIALIZE_FAILED" => BuildParseError("JSON 结构不完整"),
            "SCHEMA_VIOLATION" => BuildSchemaError(violation),
            "EVENT_VIOLATION_ALL_INVALID" => BuildEventError(violation),
            _ => BuildGenericError(violation)
        };
    }

    private static string BuildParseError(string reason)
    {
        var sb = new StringBuilder();
        sb.AppendLine("STORYMAKER_PARSE_ERROR");
        sb.AppendLine($"你的上一条回复无法解析：{reason}。");
        sb.AppendLine("请严格输出一个 JSON 对象，首字符 { 末字符 }，不要附加 markdown、自然语言说明或代码块标记。");
        sb.AppendLine();
        sb.AppendLine("你应当回复的 JSON 结构示例：");
        sb.AppendLine("{");
        sb.AppendLine("  \"plan_range\": { \"from_tick\": <整数>, \"to_tick\": <整数> },");
        sb.AppendLine("  \"empty_plan\": false,");
        sb.AppendLine("  \"narrative_summary\": \"<本窗口叙事意图简述>\",");
        sb.AppendLine("  \"events\": [");
        sb.AppendLine("    {");
        sb.AppendLine("      \"event_id\": \"<唯一ID>\",");
        sb.AppendLine("      \"scheduled_tick\": <整数, 2500倍数>,");
        sb.AppendLine("      \"event_type\": \"<事件defName>\",");
        sb.AppendLine("      \"parameters\": { \"faction\": \"<派系名>\", \"intensity_multiplier\": 0.8 },");
        sb.AppendLine("      \"narration_text\": \"<叙事文本>\",");
        sb.AppendLine("      \"narrative_context\": \"<叙事上下文>\"");
        sb.AppendLine("    }");
        sb.AppendLine("  ]");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string BuildSchemaError(GuardResult violation)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"STORYMAKER_SCHEMA_VIOLATION");
        sb.AppendLine($"你的上一条回复缺少必要字段或字段类型不正确。");
        sb.AppendLine();
        sb.AppendLine($"具体问题：");
        foreach (var field in violation.ViolatedFields)
            sb.AppendLine($"  - {field}");
        sb.AppendLine();
        sb.AppendLine("请修正后重新输出完整的 JSON 对象。首字符 { 末字符 }。");
        return sb.ToString();
    }

    private static string BuildEventError(GuardResult violation)
    {
        var sb = new StringBuilder();
        sb.AppendLine("STORYMAKER_EVENT_VIOLATION");
        sb.AppendLine($"事件列表中全部事件校验失败：");
        sb.AppendLine();
        foreach (var error in violation.ViolatedFields)
            sb.AppendLine($"  - {error}");
        sb.AppendLine();
        sb.AppendLine("每个事件必须包含: event_id, scheduled_tick(在plan_range范围内且为2500倍数), event_type(系统提示词中列出的事件类型)。");
        sb.AppendLine("可选字段: parameters(faction/intensity_multiplier/raid_strategy/trader_kind), narration_text, narrative_context。");
        sb.AppendLine();
        sb.AppendLine("请修正后重新输出完整的 JSON 对象。");
        return sb.ToString();
    }

    private static string BuildGenericError(GuardResult violation)
    {
        return $"STORYMAKER_{violation.ViolationTag}\n你的上一条回复存在问题：{violation.ViolationDetail}\n请修正后重新输出完整的 JSON 对象。";
    }
}
