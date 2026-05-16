using System.Collections.Generic;

namespace StoryMaker.Response;

// 第2层 Guard：验证必需字段是否存在且类型正确
public static class SchemaGuard
{
    public static GuardResult Validate(LLMResponse response)
    {
        var errors = new List<string>();

        // plan_range 检查
        if (response.plan_range == null)
        {
            errors.Add("plan_range: 对象缺失");
        }
        else
        {
            if (response.plan_range.from_tick <= 0)
                errors.Add("plan_range.from_tick: 必须为正整数");

            if (response.plan_range.to_tick <= 0)
                errors.Add("plan_range.to_tick: 必须为正整数");

            if (response.plan_range.from_tick > 0 && response.plan_range.to_tick > 0
                && response.plan_range.from_tick >= response.plan_range.to_tick)
                errors.Add($"plan_range: from_tick({response.plan_range.from_tick}) 必须小于 to_tick({response.plan_range.to_tick})");
        }

        // events 数组检查（即使为空也必须是数组，但 null 且 empty_plan==false 视为错误）
        if (response.events == null && !response.empty_plan)
        {
            errors.Add("events: 数组缺失（empty_plan=false 时 events 不能为 null）");
        }

        // narrative_summary 检查
        if (string.IsNullOrEmpty(response.narrative_summary))
        {
            errors.Add("narrative_summary: 不能为空");
        }

        if (errors.Count > 0)
        {
            return GuardResult.Fail("SCHEMA_VIOLATION",
                $"缺少必要字段或字段类型不正确:\n- {string.Join("\n- ", errors)}",
                errors);
        }

        return GuardResult.Pass();
    }
}
