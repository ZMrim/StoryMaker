using RimWorld;
using StoryMaker.Dialogue;
using UnityEngine;
using Verse;

namespace StoryMaker.UI;

// 对话窗口：聊天气泡风格，玩家居右（绿色），叙事者居左（深色）
// 对话时强制暂停游戏，提供沉浸式聊天体验
[StaticConstructorOnStartup]
public class StoryMakerDialogueWindow : Window
{
    // 全局单例：同一时间只允许一个对话窗口
    private static StoryMakerDialogueWindow currentInstance;

    // 打开对话窗口（防重复 + 防跨存档残留）
    public static void Open()
    {
        // 检测跨存档，清除旧对话历史
        DialogueHandler.EnsureHistoryCleared();

        // 清除可能因返回主菜单而残留的过期单例
        if (currentInstance != null && !Find.WindowStack.IsOpen(typeof(StoryMakerDialogueWindow)))
            currentInstance = null;
        if (currentInstance != null) return;
        var window = new StoryMakerDialogueWindow();
        Find.WindowStack.Add(window);
    }

    private Vector2 scrollPosition;
    private string inputText = "";
    private float lastHistoryCount;
    // 头像缓存
    private Texture2D cachedAvatar;
    private string cachedAvatarPath;
    private const float AvatarSize = 36f;
    private const float AvatarGap = 6f;
    private const float MaxBubbleWidthRatio = 0.78f;

    // 气泡颜色
    private static readonly Color PlayerBubbleColor = new(0.22f, 0.42f, 0.28f, 1f);     // 玩家 — 深绿
    private static readonly Color NarratorBubbleColor = new(0.18f, 0.18f, 0.22f, 1f);    // 叙事者 — 暗色
    private static readonly Color PlayerTextColor = new(0.85f, 0.92f, 0.78f);             // 玩家文字 — 浅绿白
    private static readonly Color NarratorTextColor = new(0.82f, 0.82f, 0.85f);           // 叙事者文字 — 浅灰
    private static readonly Color SystemTextColor = new(0.50f, 0.80f, 0.90f);             // 系统消息 — 青色

    public override Vector2 InitialSize => new(560f, 560f);

    public StoryMakerDialogueWindow()
    {
        layer = WindowLayer.Dialog;
        doCloseX = true;
        doCloseButton = false;
        closeOnAccept = false;
        closeOnCancel = false;
        forcePause = true;
        preventCameraMotion = false;
        absorbInputAroundWindow = false;
        onlyOneOfTypeAllowed = false;
        draggable = true;
        resizeable = true;
        optionalTitle = "StoryMaker_Dialogue_Title".Translate();
    }

    public override void PostOpen()
    {
        base.PostOpen();
        currentInstance = this;
    }

    public override void PostClose()
    {
        base.PostClose();
        currentInstance = null;
    }

    public override void WindowUpdate()
    {
        base.WindowUpdate();
        // 每帧检查 LLM 与 TTS 超时
        DialogueHandler.CheckTimeout();
        DialogueHandler.CheckTTSTimeout();
    }

    public override void DoWindowContents(Rect inRect)
    {
        var contentRect = inRect.ContractedBy(10f);

        // 整体背景
        Widgets.DrawBoxSolid(inRect, new Color(0.10f, 0.10f, 0.13f));
        Widgets.DrawBox(inRect, 1);

        // ── 消息区域 ──
        float inputHeight = 50f;
        float gap = 8f;
        float messagesHeight = contentRect.height - inputHeight - gap;
        var messagesRect = new Rect(contentRect.x, contentRect.y, contentRect.width, messagesHeight);
        DrawMessages(messagesRect);

        // 分隔线
        float lineY = messagesRect.yMax + gap / 2f;
        GUI.color = new Color(0.3f, 0.3f, 0.35f);
        Widgets.DrawLineHorizontal(contentRect.x, lineY, contentRect.width);
        GUI.color = Color.white;

        // ── 输入区域 ──
        float inputY = lineY + gap / 2f;
        var inputRect = new Rect(contentRect.x, inputY, contentRect.width, inputHeight);
        DrawInputArea(inputRect);
    }

    private void DrawMessages(Rect rect)
    {
        var history = DialogueHandler.History;
        if (history == null || history.Count == 0)
        {
            Text.Font = GameFont.Small;
            GUI.color = new Color(0.4f, 0.4f, 0.45f);
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, "StoryMaker_Dialogue_Placeholder".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            lastHistoryCount = 0;
            return;
        }

        // 计算总内容高度
        float viewWidth = rect.width - 16f;
        float totalHeight = 10f;
        var heights = new float[history.Count];
        for (int i = 0; i < history.Count; i++)
        {
            heights[i] = CalculateEntryHeight(history[i], viewWidth);
            totalHeight += heights[i] + 12f;
        }
        totalHeight += 10f;

        float viewHeight = Mathf.Max(totalHeight, rect.height);
        var viewRect = new Rect(0, 0, viewWidth, viewHeight);

        // 自动滚动
        if (history.Count > lastHistoryCount)
        {
            scrollPosition.y = float.MaxValue;
            lastHistoryCount = history.Count;
        }

        Widgets.BeginScrollView(rect, ref scrollPosition, viewRect);

        float y = 10f;
        for (int i = 0; i < history.Count; i++)
        {
            y += DrawEntryBubble(new Rect(0, y, viewWidth, heights[i]), history[i], viewWidth) + 20f;
        }

        Widgets.EndScrollView();
    }

    private float CalculateEntryHeight(DialogueEntry entry, float viewWidth)
    {
        float maxBubbleWidth = viewWidth * MaxBubbleWidthRatio;
        float textWidth = maxBubbleWidth - 20f;  // bubble 内 padding

        Text.Font = GameFont.Small;
        float playerH = Text.CalcHeight(entry.playerText ?? "", textWidth);
        float narratorH = Text.CalcHeight(entry.narratorText ?? "", textWidth);
        float h = playerH + narratorH + 34f;  // padding + gap (extra for spacing)

        if (entry.hadEvent)
        {
            string eventLine = "StoryMaker_Dialogue_EventTriggered".Translate(entry.triggeredEventType);
            float eventH = Text.CalcHeight(eventLine, viewWidth - 20f);
            h += eventH + 6f;
        }

        return h;
    }

    private float DrawEntryBubble(Rect rect, DialogueEntry entry, float viewWidth)
    {
        float maxBubbleWidth = viewWidth * MaxBubbleWidthRatio;
        float bubbleWidth;
        float textX, bubbleX;

        // ── 玩家消息（右对齐，绿色气泡）──
        if (!string.IsNullOrEmpty(entry.playerText))
        {
            float textW = Mathf.Min(Text.CalcSize(entry.playerText).x + 20f, maxBubbleWidth);
            bubbleWidth = textW;
            bubbleX = rect.x + viewWidth - bubbleWidth;
            textX = bubbleX + 10f;

            float playerH = Text.CalcHeight(entry.playerText, bubbleWidth - 20f);
            var bubbleRect = new Rect(bubbleX, rect.y, bubbleWidth, playerH + 16f);
            Widgets.DrawBoxSolid(bubbleRect, PlayerBubbleColor);
            Widgets.DrawBox(bubbleRect, 1);

            GUI.color = PlayerTextColor;
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(textX, rect.y + 8f, bubbleWidth - 20f, playerH), entry.playerText);
            GUI.color = Color.white;

            rect.y += playerH + 22f;
        }

        // ── 叙事者消息（左对齐，深色气泡）──
        if (!string.IsNullOrEmpty(entry.narratorText))
        {
            // 尝试加载头像
            TryLoadAvatar();

            float avatarOffset = 0f;
            if (cachedAvatar != null)
            {
                avatarOffset = AvatarSize + AvatarGap;
                var avatarRect = new Rect(rect.x, rect.y, AvatarSize, AvatarSize);
                GUI.DrawTexture(avatarRect, cachedAvatar, ScaleMode.ScaleToFit);
            }

            float textW = Mathf.Min(Text.CalcSize(entry.narratorText).x + 20f, maxBubbleWidth - avatarOffset);
            bubbleWidth = textW;
            bubbleX = rect.x + avatarOffset;
            textX = bubbleX + 10f;

            float narratorH = Text.CalcHeight(entry.narratorText, bubbleWidth - 20f);
            var bubbleRect = new Rect(bubbleX, rect.y, bubbleWidth, narratorH + 16f);
            Widgets.DrawBoxSolid(bubbleRect, NarratorBubbleColor);
            Widgets.DrawBox(bubbleRect, 1);

            GUI.color = NarratorTextColor;
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(textX, rect.y + 8f, bubbleWidth - 20f, narratorH), entry.narratorText);
            GUI.color = Color.white;

            rect.y += narratorH + 22f;
        }

        // ── 事件提示 ──
        if (entry.hadEvent)
        {
            string eventLine = "StoryMaker_Dialogue_EventTriggered".Translate(entry.triggeredEventType);
            float eventH = Text.CalcHeight(eventLine, viewWidth - 20f);
            GUI.color = SystemTextColor;
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(rect.x + 6f, rect.y, viewWidth - 20f, eventH), eventLine);
            GUI.color = Color.white;
            rect.y += eventH + 4f;
        }

        return rect.y - (rect.y - CalculateEntryHeight(entry, viewWidth));  // 返回实际使用的高度
    }

    private void DrawInputArea(Rect rect)
    {
        float btnWidth = 70f;
        float btnGap = 8f;
        var textRect = new Rect(rect.x, rect.y + 4f, rect.width - btnWidth - btnGap, 28f);
        var btnRect = new Rect(rect.x + rect.width - btnWidth, rect.y + 4f, btnWidth, 28f);

        // 输入框背景
        Widgets.DrawBoxSolid(new Rect(textRect.x - 2f, textRect.y - 2f, textRect.width + 4f, textRect.height + 4f),
            new Color(0.15f, 0.15f, 0.18f));

        GUI.SetNextControlName("DialogueInput");
        inputText = Widgets.TextField(textRect, inputText);

        // 发送按钮
        bool hasText = !string.IsNullOrWhiteSpace(inputText);
        bool waiting = DialogueHandler.IsWaitingForResponse;
        GUI.color = hasText && !waiting ? new Color(0.35f, 0.65f, 0.45f) : new Color(0.25f, 0.25f, 0.28f);
        bool waitingTTS = DialogueHandler.IsWaitingForTTS;
        string btnLabel;
        if (waitingTTS)
            btnLabel = "StoryMaker_Dialogue_Speaking".Translate();
        else if (waiting)
            btnLabel = "StoryMaker_Dialogue_Thinking".Translate();
        else
            btnLabel = "StoryMaker_Dialogue_Send".Translate();
        if (Widgets.ButtonText(btnRect, btnLabel) && hasText && !waiting)
        {
            DialogueHandler.SendMessage(inputText);
            inputText = "";
        }
        GUI.color = Color.white;

        // 回车发送
        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return
            && GUI.GetNameOfFocusedControl() == "DialogueInput" && hasText && !waiting)
        {
            DialogueHandler.SendMessage(inputText);
            inputText = "";
            Event.current.Use();
        }
    }

    // 加载叙事者头像（带缓存，避免每帧读磁盘）
    private void TryLoadAvatar()
    {
        string path = global::StoryMaker.StoryMaker.Instance?.Settings?.avatarPath ?? "";
        if (string.IsNullOrWhiteSpace(path))
        {
            cachedAvatar = null;
            cachedAvatarPath = "";
            return;
        }
        if (path == cachedAvatarPath && cachedAvatar != null) return;  // 已缓存

        cachedAvatarPath = path;
        if (!System.IO.File.Exists(path))
        {
            cachedAvatar = null;
            return;
        }
        try
        {
            byte[] data = System.IO.File.ReadAllBytes(path);
            cachedAvatar = new Texture2D(1, 1);
            cachedAvatar.LoadImage(data);  // PNG → Texture2D
        }
        catch (System.Exception ex)
        {
            Verse.Log.Warning($"[StoryMaker] 加载头像失败 ({path}): {ex.Message}");
            cachedAvatar = null;
        }
    }

}
