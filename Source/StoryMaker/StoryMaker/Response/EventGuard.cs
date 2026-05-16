using System.Collections.Generic;
using StoryMaker.Snapshot;
using Verse;

namespace StoryMaker.Response;

// 第3层 Guard：验证事件条目合法性
public static class EventGuard
{
    // 修正事件时，被移除的无效事件索引
    public static List<int> RemovedIndices { get; private set; }

    public static GuardResult Validate(LLMResponse response)
    {
        RemovedIndices = new List<int>();

        // 空计划或无事件——合法
        if (response.empty_plan || response.events == null || response.events.Count == 0)
            return GuardResult.Pass();

        var errors = new List<string>();
        var validEvents = new List<PlannedEvent>();

        for (int i = 0; i < response.events.Count; i++)
        {
            var evt = response.events[i];
            var evtErrors = ValidateEvent(evt, i, response.plan_range);

            if (evtErrors.Count > 0)
            {
                errors.AddRange(evtErrors);
                RemovedIndices.Add(i);
            }
            else
            {
                validEvents.Add(evt);
            }
        }

        // 移除无效事件
        response.events = validEvents;

        if (errors.Count > 0 && validEvents.Count == 0 && !response.empty_plan)
        {
            return GuardResult.Fail("EVENT_VIOLATION_ALL_INVALID",
                $"全部 {RemovedIndices.Count} 个事件校验失败，已移除:\n- {string.Join("\n- ", errors)}",
                errors);
        }

        if (errors.Count > 0)
        {
            // 部分事件无效，已移除——记录日志但不触发修正重试
            Log.Warning($"[StoryMaker] EventGuard: {RemovedIndices.Count} 个事件被移除，保留 {validEvents.Count} 个");
        }

        return GuardResult.Pass();
    }

    private static List<string> ValidateEvent(PlannedEvent evt, int index, PlanRange range)
    {
        var errors = new List<string>();

        // event_id 非空
        if (string.IsNullOrEmpty(evt.event_id))
            errors.Add($"events[{index}].event_id: 不能为空");

        // scheduled_tick 在规划范围内
        if (range != null)
        {
            if (evt.scheduled_tick < range.from_tick || evt.scheduled_tick > range.to_tick)
            {
                errors.Add($"events[{index}].scheduled_tick({evt.scheduled_tick}): 超出 plan_range [{range.from_tick}, {range.to_tick}]");
            }
        }
        else if (evt.scheduled_tick <= 0)
        {
            errors.Add($"events[{index}].scheduled_tick({evt.scheduled_tick}): 必须为正整数");
        }

        // scheduled_tick 为 2500 整数倍
        if (evt.scheduled_tick > 0 && evt.scheduled_tick % 2500 != 0)
            errors.Add($"events[{index}].scheduled_tick({evt.scheduled_tick}): 不是 2500 的整数倍");

        // event_type 在白名单中
        if (string.IsNullOrEmpty(evt.event_type))
        {
            errors.Add($"events[{index}].event_type: 不能为空");
        }
        else if (!IncidentWhitelist.IsWhitelisted(evt.event_type))
        {
            errors.Add($"events[{index}].event_type({evt.event_type}): 不在事件白名单中");
        }

        // narration_text 如果存在须为非空字符串
        // （parameters 中有 narration_text 键说明 LLM 提供了但可能为空——提取时 JsonExtractor 会返回 null 或空字符串）
        // 此检查已由 JsonExtractor 处理：空字符串 narration_text 会变成 null

        return errors;
    }
}
