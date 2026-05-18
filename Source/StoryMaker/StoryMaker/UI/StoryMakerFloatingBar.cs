using System.Linq;
using RimWorld;
using StoryMaker.Dialogue;
using UnityEngine;
using Verse;

namespace StoryMaker.UI;

// 半透明浮动条：显示最近一条叙事者对话，可拖动、可调大小。
// 通过底部选项卡按钮切换显示/隐藏。
[StaticConstructorOnStartup]
public class StoryMakerFloatingBar : Window
{
    private static readonly Color bgColor = new(0.1f, 0.1f, 0.1f, 0.6f);

    // 跟踪是否已打开（用于底部选项卡按钮判断）
    public static bool CurrentlyOpen => currentInstance != null;
    private static StoryMakerFloatingBar currentInstance;

    public static void CloseIfOpen()
    {
        currentInstance?.Close();
    }

    public override Vector2 InitialSize => new(420f, 80f);

    public StoryMakerFloatingBar()
    {
        layer = WindowLayer.GameUI;
        draggable = true;
        resizeable = true;
        doWindowBackground = false;
        doCloseButton = false;
        doCloseX = false;
        preventCameraMotion = false;
        absorbInputAroundWindow = false;
        onlyOneOfTypeAllowed = true;
        soundAppear = null;
        soundClose = null;
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

    public override void DoWindowContents(Rect inRect)
    {
        // 背景
        Widgets.DrawBoxSolid(inRect, bgColor);

        var innerRect = inRect.ContractedBy(6f);

        // 最近一条对话
        var history = DialogueHandler.History;
        if (history != null && history.Count > 0)
        {
            var last = history.Last();
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            Widgets.Label(innerRect.TopPart(0.3f), "StoryMaker_FloatingBar_Label".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Widgets.Label(innerRect.BottomPart(0.7f), last.narratorText);
        }
        else
        {
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.5f, 0.5f, 0.5f);
            Widgets.Label(innerRect, "StoryMaker_FloatingBar_Empty".Translate());
            GUI.color = Color.white;
        }

        // 展开按钮（右下角）
        Text.Font = GameFont.Tiny;
        float btnW = 60f;
        float btnH = 24f;
        var btnRect = new Rect(inRect.width - btnW - 10f, inRect.height - btnH - 6f, btnW, btnH);
        if (Widgets.ButtonText(btnRect, "StoryMaker_FloatingBar_Expand".Translate()))
        {
            StoryMakerDialogueWindow.Open();
        }
    }
}
