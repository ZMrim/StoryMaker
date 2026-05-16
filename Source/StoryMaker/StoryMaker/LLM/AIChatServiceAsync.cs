using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using Verse;
using StoryMaker.Core;
using StoryMaker.Schedule;

namespace StoryMaker;

/// <summary>
/// MonoBehaviour 单例 + 协程 + 回调队列 的 LLM 异步通信层。
/// 参照 RimChat 的 AIChatServiceAsync 设计。
/// </summary>
public class AIChatServiceAsync : MonoBehaviour
{
    private static AIChatServiceAsync instance;

    public static AIChatServiceAsync Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("StoryMakerAsyncService");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<AIChatServiceAsync>();
            }
            return instance;
        }
    }

    // 回调队列：在 Update() 中统一执行，保证主线程安全
    private readonly Queue<System.Action> callbackQueue = new();
    private readonly object queueLock = new();

    public void EnqueueCallback(System.Action action)
    {
        lock (queueLock)
        {
            callbackQueue.Enqueue(action);
        }
    }

    void Update()
    {
        // 实时超时检测（不受游戏暂停影响）
        EventScheduler.CheckRealtimeTimeout();

        while (true)
        {
            System.Action action;
            lock (queueLock)
            {
                if (callbackQueue.Count == 0) break;
                action = callbackQueue.Dequeue();
            }
            action?.Invoke();
        }
    }

    /// <summary>
    /// 发送 LLM 请求。onSuccess 参数为 LLM 原始响应体字符串。
    /// onError 参数为 (errorMessage, failureReason)。
    /// </summary>
    public void SendRequest(
        List<ChatMessage> messages,
        System.Action<string> onSuccess,
        System.Action<string, string> onError)
    {
        StartCoroutine(ProcessRequest(messages, onSuccess, onError, attempt: 0));
    }

    private IEnumerator ProcessRequest(
        List<ChatMessage> messages,
        Action<string> onSuccess,
        Action<string, string> onError,
        int attempt)
    {
        var settings = StoryMaker.Instance?.Settings;
        if (settings == null)
        {
            EnqueueCallback(() => onError?.Invoke("StoryMaker Settings 不可用", "internal_error"));
            yield break;
        }

        if (!settings.IsApiConfigured())
        {
            EnqueueCallback(() => onError?.Invoke("API Key 或模型名称未配置，请先打开 Mod 设置填写。", "config_incomplete"));
            yield break;
        }

        string url = settings.GetEffectiveEndpoint();
        if (string.IsNullOrWhiteSpace(url))
        {
            EnqueueCallback(() => onError?.Invoke("LLM endpoint URL 为空，请检查 Provider 和自定义 URL 配置。", "config_incomplete"));
            yield break;
        }

        int requestContextVersion = StoryMakerState.Instance?.contextVersion ?? 0;
        string jsonBody = BuildRequestBody(messages, settings.modelName);

        Stopwatch stopwatch = Stopwatch.StartNew();

        using (var request = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            string trimmedApiKey = (settings.apiKey ?? "").Trim();
            if (!string.IsNullOrEmpty(trimmedApiKey))
                request.SetRequestHeader("Authorization", $"Bearer {trimmedApiKey}");

            // Provider 特有的额外 HTTP 头
            var extraHeaders = settings.provider.GetExtraHeaders();
            if (extraHeaders != null)
                foreach (var h in extraHeaders)
                    request.SetRequestHeader(h.Key, h.Value);

            request.timeout = (int)settings.timeoutSeconds;

            var operation = request.SendWebRequest();

            // 轮询等待完成
            while (!operation.isDone)
            {
                // 检查 contextVersion：读档则丢弃回调，Abort 请求
                if (!IsContextVersionCurrent(requestContextVersion))
                {
                    request.Abort();
                    Log.Message("[StoryMaker] HTTP 请求因 contextVersion 变化（读档/新游戏）被丢弃。");
                    yield break;
                }
                yield return null;
            }

            stopwatch.Stop();

            // 请求完成后再次校验 contextVersion
            if (!IsContextVersionCurrent(requestContextVersion))
            {
                Log.Message("[StoryMaker] HTTP 请求完成但 contextVersion 已变化，丢弃回调。");
                yield break;
            }

            // HTTP 成功
            if (request.result == UnityWebRequest.Result.Success)
            {
                string responseBody = request.downloadHandler.text ?? "";
                Log.Message($"[StoryMaker] HTTP 200，耗时 {stopwatch.Elapsed.TotalSeconds:F1}s");
                EnqueueCallback(() => onSuccess?.Invoke(responseBody));
                yield break;
            }

            // HTTP 错误分类
            string failureReason = ResolveFailureReason(request);
            string errorText = BuildRequestErrorText(request);

            Log.Message($"[StoryMaker] HTTP 错误: failure_reason={failureReason}, 详情={errorText}");

            // HTTP 层重试：5xx 和 429 冷却后重试 1 次（不消耗 TCP 层 R_max）
            if (attempt == 0 && IsRetryableHttpError(request.responseCode))
            {
                Log.Message("[StoryMaker] HTTP 层重试（5xx/429），等待 2 秒…");
                yield return new WaitForSeconds(2f);
                StartCoroutine(ProcessRequest(messages, onSuccess, onError, attempt: 1));
                yield break;
            }

            // 不可重试的错误（如 401/404）或重试已用尽
            EnqueueCallback(() => onError?.Invoke(errorText, failureReason));
        }
    }

    private string BuildRequestBody(List<ChatMessage> messages, string model)
    {
        var settings = StoryMaker.Instance?.Settings;
        var sb = new StringBuilder();
        sb.Append("{\"model\":\"");
        sb.Append(EscapeJson(model));
        sb.Append("\",\"messages\":[");
        for (int i = 0; i < messages.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append("{\"role\":\"");
            sb.Append(EscapeJson(messages[i].role));
            sb.Append("\",\"content\":\"");
            sb.Append(EscapeJson(messages[i].content));
            sb.Append("\"}");
        }
        sb.Append("],\"temperature\":0.7,\"max_tokens\":4096");

        // 统一思考模式适配
        sb.Append(ThinkingModeAdapter.GetThinkingJson(
            settings?.provider ?? AIProvider.DeepSeek,
            settings?.enableThinking ?? true,
            settings?.customBaseUrl ?? ""));

        sb.Append('}');
        return sb.ToString();
    }

    private static string EscapeJson(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
    }

    private static bool IsContextVersionCurrent(int version)
    {
        int current = StoryMakerState.Instance?.contextVersion ?? 0;
        return version == current;
    }

    // ---- HTTP 错误分类（参照 RimChat） ----

    private static string ResolveFailureReason(UnityWebRequest request)
    {
        if (request == null)
            return "request_error";

        switch (request.result)
        {
            case UnityWebRequest.Result.ConnectionError:
                return LooksLikeTimeout(request.error) ? "timeout" : "connection_error";
            case UnityWebRequest.Result.ProtocolError:
                return $"http_{request.responseCode}";
            case UnityWebRequest.Result.DataProcessingError:
                return "data_processing_error";
            default:
                return "request_error";
        }
    }

    private static string BuildRequestErrorText(UnityWebRequest request)
    {
        if (request == null)
            return "Unknown request error.";

        string body = request.downloadHandler?.text ?? "";
        string preview = string.IsNullOrWhiteSpace(body) ? "" : $" body={body.Trim()}";
        return $"HTTP {request.responseCode}: {request.error}{preview}".Trim();
    }

    private static bool LooksLikeTimeout(string error)
    {
        string normalized = (error ?? "").ToLowerInvariant();
        return normalized.Contains("timeout") || normalized.Contains("timed out");
    }

    private static bool IsRetryableHttpError(long responseCode)
    {
        return responseCode == 429 || (responseCode >= 500 && responseCode < 600);
    }
}
