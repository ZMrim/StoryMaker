using StoryMaker.Response;

namespace StoryMaker.Action;

public interface IActionHandler
{
    string EventType { get; }                        // 对应 event_type 字符串（IncidentDef.defName）
    bool Execute(PlannedEvent evt);                  // 执行事件，返回成功/失败

    // Phase 5 预留
    bool IsAllowedInImmediateMode { get; }
    float MaxImmediatePointsMultiplier { get; }
}
