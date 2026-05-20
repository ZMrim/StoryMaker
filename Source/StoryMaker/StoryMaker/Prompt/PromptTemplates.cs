using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Verse;

namespace StoryMaker;

public static class PromptTemplates
{
    public static string ModRootDir { get; set; }

    private static string cachedCn, cachedCnLow;
    private static string cachedDialogueCn;
    private static Dictionary<string, string> cachedDifficulty;
    private static Dictionary<string, string> cachedDensity;

    // 获取玩家游戏语言的可读名称，用于提示 LLM 回复语言
    public static string GetPlayerLanguageName()
    {
        string native = LanguageDatabase.activeLanguage?.FriendlyNameNative;
        if (!string.IsNullOrEmpty(native)) return native;
        return "English";
    }

    public static string GetDialogueSystemPrompt()
    {
        if (cachedDialogueCn == null)
            cachedDialogueCn = LoadTemplate("DialoguePrompt_CN.txt");
        return cachedDialogueCn;
    }

    public static string GetSystemPrompt(bool lowToken)
    {
        if (lowToken)
        {
            if (cachedCnLow == null)
                cachedCnLow = LoadTemplate("SystemPrompt_CN_LowToken.txt");
            return cachedCnLow;
        }
        if (cachedCn == null)
            cachedCn = LoadTemplate("SystemPrompt_CN.txt");
        return cachedCn;
    }

    // ---- 难度 / 密度提示词片段 ----

    public static string GetDifficultyPrompt(string level)
    {
        if (cachedDifficulty == null) LoadDifficultyFiles();
        return cachedDifficulty.TryGetValue(level, out string content) ? content : "";
    }

    public static string GetDensityPrompt(string level)
    {
        if (cachedDensity == null) LoadDensityFiles();
        return cachedDensity.TryGetValue(level, out string content) ? content : "";
    }

    private static void LoadDifficultyFiles()
    {
        cachedDifficulty = new Dictionary<string, string>();
        foreach (string level in Enum.GetNames(typeof(DifficultyLevel)))
        {
            string content = LoadDataFile($"Difficulty_{level}.txt");
            if (!string.IsNullOrWhiteSpace(content))
                cachedDifficulty[level] = content;
        }
    }

    private static void LoadDensityFiles()
    {
        cachedDensity = new Dictionary<string, string>();
        foreach (string level in Enum.GetNames(typeof(DensityLevel)))
        {
            string content = LoadDataFile($"Density_{level}.txt");
            if (!string.IsNullOrWhiteSpace(content))
                cachedDensity[level] = content;
        }
    }

    private static string LoadDataFile(string filename)
    {
        if (string.IsNullOrWhiteSpace(ModRootDir)) return "";
        string path = Path.Combine(ModRootDir, "Resources", "PromptData", "CN", filename);
        if (!File.Exists(path)) return "";
        return File.ReadAllText(path, Encoding.UTF8);
    }

    // ----

    private static string LoadTemplate(string filename)
    {
        if (!string.IsNullOrEmpty(ModRootDir))
        {
            string path = Path.Combine(ModRootDir, "Resources", "PromptTemplates", filename);
            if (File.Exists(path))
                return File.ReadAllText(path, Encoding.UTF8);
            Log.Warning($"[StoryMaker] 模板文件不存在: {path}，使用内置回退模板。");
        }
        return GetFallbackTemplate();
    }

    private static string GetFallbackTemplate()
    {
        return @"你是 RimWorld 的 AI 叙事者。根据殖民地状态规划游戏事件。

## 字段
colony: name(殖民地名),population(人数),average_mood(心情0~1),food_days(食物天数),total_wealth(财富)
environment: season(季节),biome(生物群系),current_temperature(温度°C)
faction_relations: [{name(派系名),relation(关系-100~100)}]
recent_events: [{type(事件名),category(类别)}]
recent_deaths: [{pawn_name(角色名)}]
deviation_report: 失败事件列表
request_range: {from_tick,to_tick} 规划时间范围

{categories}

{event_list}

{event_details}

{personality}

## 回复格式
{""plan_range"":{""from_tick"":int,""to_tick"":int},""empty_plan"":bool,""narrative_summary"":""简述"",""events"":[{""event_id"":""id"",""scheduled_tick"":int,""event_type"":""类型"",""parameters"":{""faction"":""派系"",""intensity_multiplier"":0.5~2.0},""narration_text"":""旁白"",""narrative_context"":""上下文""}]}

## 规则
scheduled_tick必为2500整数倍。勿在文本中提数值。每窗口0~4事件。empty_plan可声明空窗口。

{injections}";
    }
}
