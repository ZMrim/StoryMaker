using System.Collections.Generic;
using Verse;

namespace StoryMaker;

public class StoryMakerSettings : ModSettings
{
    // TCP 协议参数
    public int bufferDays = 2;
    public int planWindowDays = 5;
    public float timeoutSeconds = 60f;
    public int maxRetransmissions = 2;

    // 玩家个性化
    public string playerPersonality = "";

    // 死亡记录种族白名单（defName 列表，如 Human, Ratkin 等）
    public List<string> deathRaceWhitelist = new() { "Human" };

    // 调试选项
    public bool lowTokenMode = false;
    public bool debugMode = false;

    public int BufferTicks => bufferDays * 60000;
    public int PlanWindowTicks => planWindowDays * 60000;

    public override void ExposeData()
    {
        Scribe_Values.Look(ref bufferDays, "bufferDays", 2);
        Scribe_Values.Look(ref planWindowDays, "planWindowDays", 5);
        Scribe_Values.Look(ref timeoutSeconds, "timeoutSeconds", 60f);
        Scribe_Values.Look(ref maxRetransmissions, "maxRetransmissions", 2);
        Scribe_Values.Look(ref playerPersonality, "playerPersonality", "");
        Scribe_Collections.Look(ref deathRaceWhitelist, "deathRaceWhitelist", LookMode.Value);
        Scribe_Values.Look(ref lowTokenMode, "lowTokenMode", false);
        Scribe_Values.Look(ref debugMode, "debugMode", false);
    }
}
