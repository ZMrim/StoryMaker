using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Verse;

namespace StoryMaker;

public static class DebugLogger
{
    private static string sessionDir;
    private static int seqNumber;
    private static string sessionStartTime;

    // 汇总统计
    private static int totalRequests;
    private static int totalRetries;
    private static int totalGuardCorrections;
    private static int totalHttpErrors;
    private static readonly Dictionary<string, int> httpErrorBreakdown = new();

    public static bool IsEnabled => StoryMaker.Instance?.Settings?.debugMode ?? false;

    private static void EnsureSessionDir()
    {
        if (sessionDir != null) return;

        string logRoot = Path.Combine(
            Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath)
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Ludeon Studios", "RimWorld by Ludeon Studios"),
            "StoryMaker_DebugLogs");

        Directory.CreateDirectory(logRoot);

        sessionStartTime = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        sessionDir = Path.Combine(logRoot, $"session_{sessionStartTime}");
        Directory.CreateDirectory(sessionDir);
        seqNumber = 0;

        Log.Message($"[StoryMaker] Debug 日志会话目录: {sessionDir}");
    }

    public static int LogRequest(List<ChatMessage> messages)
    {
        if (!IsEnabled) return 0;

        EnsureSessionDir();
        seqNumber++;
        totalRequests++;

        string filename = $"{seqNumber:D4}_request.md";
        string path = Path.Combine(sessionDir, filename);

        var sb = new StringBuilder();
        sb.AppendLine($"# 请求 #{seqNumber}");
        sb.AppendLine($"时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        foreach (var msg in messages)
        {
            sb.AppendLine($"## {msg.role}");
            sb.AppendLine(msg.content);
            sb.AppendLine();
        }

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        Log.Message($"[StoryMaker] Debug 请求已保存: {filename}");

        return seqNumber;
    }

    public static void LogResponse(int seq, string responseBody, long httpCode, long elapsedMs, int attemptCount, string parsedJson = null)
    {
        if (!IsEnabled) return;
        EnsureSessionDir();

        string filename = $"{seq:D4}_response.md";
        string path = Path.Combine(sessionDir, filename);

        var sb = new StringBuilder();
        sb.AppendLine($"# 响应 #{seq}");
        sb.AppendLine($"时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"HTTP 状态码: {httpCode}");
        sb.AppendLine($"耗时: {elapsedMs}ms");
        sb.AppendLine($"请求尝试次数: {attemptCount}");
        sb.AppendLine();
        sb.AppendLine("## 原始响应");
        sb.AppendLine();
        sb.AppendLine("```json");
        sb.AppendLine(responseBody);
        sb.AppendLine("```");

        if (!string.IsNullOrEmpty(parsedJson))
        {
            sb.AppendLine();
            sb.AppendLine("## 解析结果");
            sb.AppendLine();
            sb.AppendLine("```json");
            sb.AppendLine(parsedJson);
            sb.AppendLine("```");
        }

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        Log.Message($"[StoryMaker] Debug 响应已保存: {filename}");
    }

    public static void LogError(int seq, string errorMessage, string failureReason)
    {
        if (!IsEnabled) return;
        EnsureSessionDir();

        totalHttpErrors++;
        string key = failureReason ?? "unknown";
        if (httpErrorBreakdown.ContainsKey(key))
            httpErrorBreakdown[key]++;
        else
            httpErrorBreakdown[key] = 1;

        string filename = $"{seq:D4}_error.md";
        string path = Path.Combine(sessionDir, filename);

        var sb = new StringBuilder();
        sb.AppendLine($"# 错误 #{seq}");
        sb.AppendLine($"时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"failure_reason: {failureReason}");
        sb.AppendLine();
        sb.AppendLine("## 错误信息");
        sb.AppendLine(errorMessage);

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        Log.Message($"[StoryMaker] Debug 错误已保存: {filename}");
    }

    public static void WriteSummary()
    {
        if (!IsEnabled || sessionDir == null) return;

        string path = Path.Combine(sessionDir, "summary.json");
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine($"  \"session_start\": \"{sessionStartTime}\",");
        sb.AppendLine($"  \"total_requests\": {totalRequests},");
        sb.AppendLine($"  \"total_retries\": {totalRetries},");
        sb.AppendLine($"  \"total_guard_corrections\": {totalGuardCorrections},");
        sb.AppendLine($"  \"total_http_errors\": {totalHttpErrors},");
        sb.AppendLine("  \"http_error_breakdown\": {");
        bool first = true;
        foreach (var kv in httpErrorBreakdown)
        {
            string comma = first ? "" : ",";
            sb.AppendLine($"    \"{kv.Key}\": {kv.Value}{comma}");
            first = false;
        }
        sb.AppendLine("  }");
        sb.AppendLine("}");

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        Log.Message($"[StoryMaker] Debug 会话摘要已保存: summary.json");
    }
}
