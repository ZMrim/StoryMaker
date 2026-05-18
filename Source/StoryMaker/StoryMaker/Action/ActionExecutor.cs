using System;
using System.Collections.Generic;
using RimWorld;
using StoryMaker.Response;
using StoryMaker.Snapshot;
using Verse;

namespace StoryMaker.Action;

// 事件执行分发器：应用 intensity_multiplier，调用生命周期钩子，执行事件
public static class ActionExecutor
{
    // 生命周期钩子（Phase 5 TTS mod 使用）
    public static Func<PlannedEvent, bool> OnEventWillExecute;
    public static Action<PlannedEvent> OnEventExecuted;
    public static Action<PlannedEvent> OnEventExecutionFailed;

    // 执行失败的事件，下周期作为 deviation_report 反馈给 LLM
    public static List<DeviationEntry> FailedEvents = new();

    public static bool Execute(PlannedEvent evt)
    {
        if (evt == null) return false;

        Log.Message($"[StoryMaker] ActionExecutor: 执行 {evt.event_type} @ tick {evt.scheduled_tick}");

        // 触发前钩子
        bool? hookResult = OnEventWillExecute?.Invoke(evt);
        if (hookResult == false)
        {
            Log.Message($"[StoryMaker]   钩子阻止执行: {evt.event_type}");
            return false;
        }

        // 显示 narration_text（如有）
        if (!string.IsNullOrEmpty(evt.narration_text))
        {
            DisplayNarrationLetter(evt);
        }

        // 执行事件
        bool success = ActionRegistry.Execute(evt);

        // 触发后钩子 + 失败记录
        if (success)
        {
            OnEventExecuted?.Invoke(evt);
            Log.Message($"[StoryMaker]   执行成功: {evt.event_type}");
        }
        else
        {
            OnEventExecutionFailed?.Invoke(evt);
            FailedEvents.Add(new DeviationEntry
            {
                eventId = evt.event_id,
                eventType = evt.event_type,
                failReason = "ActionExecutor 执行返回 false"
            });
            Log.Warning($"[StoryMaker]   执行失败: {evt.event_type}");
        }

        return success;
    }

    // 弹出并清空失败事件列表（下周期作为 deviation_report）
    public static List<DeviationEntry> PopFailedEvents()
    {
        var result = new List<DeviationEntry>(FailedEvents);
        FailedEvents.Clear();
        return result;
    }

    // 追加失败事件（读档时从过期事件生成 deviation_report）
    public static void AddFailedEvent(string eventId, string eventType, string reason)
    {
        FailedEvents.Add(new DeviationEntry
        {
            eventId = eventId,
            eventType = eventType,
            failReason = reason
        });
    }

    // 存档序列化（由 StoryMakerExpose 调用）
    public static void ExposeData()
    {
        Scribe_Collections.Look(ref FailedEvents, "failedEvents", LookMode.Deep);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
            FailedEvents ??= new List<DeviationEntry>();
    }

    private static void DisplayNarrationLetter(PlannedEvent evt)
    {
        try
        {
            Find.LetterStack.ReceiveLetter(
                label: "StoryMaker_NarrationLetter".Translate(evt.event_type),
                text: evt.narration_text,
                textLetterDef: LetterDefOf.NeutralEvent,
                lookTargets: null
            );
        }
        catch (Exception ex)
        {
            Log.Warning($"[StoryMaker] 无法显示叙事 Letter: {ex.Message}");
        }
    }
}
