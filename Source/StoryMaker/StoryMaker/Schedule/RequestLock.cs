using UnityEngine;
using Verse;

namespace StoryMaker.Schedule;

// 正式请求锁 + 暂停冻结计时器。
// 替代 Phase 3 中 EventScheduler 的 static bool 简易锁。
// 由 AIChatServiceAsync.Update() 驱动实时超时检测，EventScheduler.OnTick() 驱动缓冲耗尽检测。
public class RequestLock
{
    public bool IsLocked { get; private set; }
    public int RetransmitRemaining { get; private set; }

    private float lockedRealtime;       // 上锁时的现实时间
    private float elapsedPaused;        // 累计暂停时间
    private float lastUpdateRealtime;   // 上一帧的现实时间
    private float timeoutSeconds;       // 超时阈值（现实秒）

    // 事件通知（由 EventScheduler 订阅）
    public event System.Action OnRetransmitRequested;
    public event System.Action<string> OnDegradationRequested;

    public void Lock(int retransmitMax, float timeoutSec)
    {
        IsLocked = true;
        RetransmitRemaining = retransmitMax;
        timeoutSeconds = timeoutSec;
        ResetTimer();
        Log.Message($"[StoryMaker] RequestLock: 上锁, R_max={retransmitMax}, T={timeoutSec}s");
    }

    public void Unlock()
    {
        if (IsLocked)
            Log.Message("[StoryMaker] RequestLock: 解锁");
        IsLocked = false;
        RetransmitRemaining = 0;
    }

    // 消耗一次重传次数，返回 true 表示可以重传，false 表示重传次数用尽
    public bool ConsumeRetransmit()
    {
        if (RetransmitRemaining > 0)
        {
            RetransmitRemaining--;
            ResetTimer();
            Log.Message($"[StoryMaker] RequestLock: 消耗重传, 剩余={RetransmitRemaining}");
            return true;
        }
        return false;
    }

    // 重置计时器（重传或新请求时调用）
    public void ResetTimer()
    {
        lockedRealtime = Time.realtimeSinceStartup;
        elapsedPaused = 0f;
        lastUpdateRealtime = lockedRealtime;
    }

    // 实时超时检测——由 AIChatServiceAsync.Update() 每帧调用，不受游戏暂停影响
    // 返回 true 表示已触发超时
    public bool CheckRealtimeTimeout()
    {
        if (!IsLocked) return false;

        float now = Time.realtimeSinceStartup;

        // 暂停时冻结计时器
        if (Find.TickManager != null && Find.TickManager.Paused)
        {
            elapsedPaused += now - lastUpdateRealtime;
            lastUpdateRealtime = now;
            return false;
        }

        lastUpdateRealtime = now;
        float effectiveElapsed = now - lockedRealtime - elapsedPaused;
        return effectiveElapsed >= timeoutSeconds;
    }

    // 处理实时超时——由 EventScheduler 在检测到超时后调用
    public void HandleRealtimeTimeout()
    {
        if (!IsLocked) return;

        float effectiveElapsed = Time.realtimeSinceStartup - lockedRealtime - elapsedPaused;
        string reason = $"超时 {effectiveElapsed:F1}s";

        if (ConsumeRetransmit())
        {
            OnRetransmitRequested?.Invoke();
        }
        else
        {
            Unlock();
            OnDegradationRequested?.Invoke(reason + "，重传次数用尽");
        }
    }

    // 处理缓冲耗尽——由 EventScheduler.OnTick 在 tick 追上时调用
    public void HandleBufferExhaustion(int curTick, int requestSentAtTick, int bufferTicks)
    {
        if (!IsLocked) return;

        string reason = $"缓冲耗尽 {curTick - requestSentAtTick} ticks (B={bufferTicks})";

        if (ConsumeRetransmit())
        {
            OnRetransmitRequested?.Invoke();
        }
        else
        {
            Unlock();
            OnDegradationRequested?.Invoke(reason + "，重传次数用尽");
        }
    }
}
