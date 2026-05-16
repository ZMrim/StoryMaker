using System.Collections.Generic;
using StoryMaker.Response;
using Verse;

namespace StoryMaker.Schedule;

// 按 scheduled_tick 升序排列的优先队列
public class EventQueue
{
    private List<PlannedEvent> events = new();

    public int Count => events.Count;

    // 批量插入并保持升序
    public void InsertAll(List<PlannedEvent> newEvents)
    {
        if (newEvents == null || newEvents.Count == 0) return;
        events.AddRange(newEvents);
        events.Sort((a, b) => a.scheduled_tick.CompareTo(b.scheduled_tick));
        Log.Message($"[StoryMaker] EventQueue: 插入 {newEvents.Count} 个事件, 当前队列深度={events.Count}");
    }

    // 查看队首（最近即将触发的事件），不弹出
    public PlannedEvent Peek()
    {
        if (events.Count == 0) return null;
        return events[0];
    }

    // 弹出队首
    public PlannedEvent Pop()
    {
        if (events.Count == 0) return null;
        var evt = events[0];
        events.RemoveAt(0);
        return evt;
    }

    // 清空队列
    public void Clear()
    {
        int count = events.Count;
        events.Clear();
        if (count > 0)
            Log.Message($"[StoryMaker] EventQueue: 清空 {count} 个事件");
    }

    // 获取全部事件（只读，用于调试/序列化）
    public List<PlannedEvent> GetAll()
    {
        return new List<PlannedEvent>(events);
    }

    // 存档序列化（由 StoryMakerExpose 调用）
    public void ExposeData()
    {
        Scribe_Collections.Look(ref events, "eventQueueItems", LookMode.Deep);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            events ??= new List<PlannedEvent>();
            events.Sort((a, b) => a.scheduled_tick.CompareTo(b.scheduled_tick));
        }
    }
}
