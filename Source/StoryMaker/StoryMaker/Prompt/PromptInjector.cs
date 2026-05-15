using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using Verse;

namespace StoryMaker;

public class PromptInjectionEntry
{
    public string ModName;
    public string Category;
    public string Content;
}

// 用于 JsonUtility 反序列化注入配置文件
[Serializable]
public class PromptInjectionFile
{
    public string ModName;
    public string Category;
    public string Content;
}

public static class PromptInjector
{
    private static readonly List<PromptInjectionEntry> entries = new();
    private static bool configFilesLoaded;

    public static void Register(PromptInjectionEntry entry)
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.Content)) return;
        entries.Add(entry);
        Log.Message($"[StoryMaker] PromptInjector: 已注册来自 {entry.ModName} 的注入 ({entry.Category})");
    }

    // 从 Resources/PromptInjections/ 目录加载 .json 配置文件
    // 由 PromptBuilder 首次构建时自动调用
    public static void LoadFromConfigFiles()
    {
        if (configFilesLoaded) return;
        configFilesLoaded = true;

        string root = PromptTemplates.ModRootDir;
        if (string.IsNullOrEmpty(root)) return;

        string dir = Path.Combine(root, "Resources", "PromptInjections");
        if (!Directory.Exists(dir)) return;

        foreach (string file in Directory.GetFiles(dir, "*.json"))
        {
            try
            {
                string json = File.ReadAllText(file, Encoding.UTF8);
                var entry = JsonUtility.FromJson<PromptInjectionFile>(json);
                if (entry != null && !string.IsNullOrWhiteSpace(entry.Content))
                {
                    Register(new PromptInjectionEntry
                    {
                        ModName = entry.ModName ?? Path.GetFileNameWithoutExtension(file),
                        Category = entry.Category ?? "general",
                        Content = entry.Content
                    });
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[StoryMaker] PromptInjector: 解析注入文件失败 {file}: {e.Message}");
            }
        }
    }

    public static string BuildInjectedSection()
    {
        LoadFromConfigFiles();

        if (entries.Count == 0) return "";
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("## 外部注入信息");
        foreach (var entry in entries)
        {
            sb.AppendLine($"### {entry.Category}（来自模组：{entry.ModName}）");
            sb.AppendLine(entry.Content);
        }
        return sb.ToString();
    }
}
