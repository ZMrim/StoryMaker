using System.Collections.Generic;
using Verse;

namespace StoryMaker.Dialogue;

// LLM 对话回复
public class DialogueResponse
{
    public string dialogue_text;
    public bool immediate_event;
    public string event_type;
    public Dictionary<string, string> parameters;
    public string narrative_context;

    public bool HasEvent => immediate_event && !string.IsNullOrEmpty(event_type);
}

// 对话历史条目（用于 Prompt 上下文 + 存档序列化）
public class DialogueEntry : IExposable
{
    public string playerText;
    public string narratorText;
    public bool hadEvent;
    public string triggeredEventType;  // 此轮对话触发的事件类型（如 TraderCaravanArrival）
    public int gameTick;

    public DialogueEntry() { }

    public void ExposeData()
    {
        Scribe_Values.Look(ref playerText, "playerText");
        Scribe_Values.Look(ref narratorText, "narratorText");
        Scribe_Values.Look(ref hadEvent, "hadEvent");
        Scribe_Values.Look(ref triggeredEventType, "triggeredEventType");
        Scribe_Values.Look(ref gameTick, "gameTick");
    }
}
