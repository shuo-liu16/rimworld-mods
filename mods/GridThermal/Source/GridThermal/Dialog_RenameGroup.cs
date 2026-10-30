using System;
using Verse;
using UnityEngine;

namespace Shuo.GridThermal
{
    /// <summary>
    /// 通用分组改名对话框，温控组和电力组共用。
    ///
    /// 没用 RimWorld 自带的 Dialog_Rename：它吃的是 IRenameable，而分组只是一串字符串，
    /// 不是 Thing，套不上。这里用两个委托把「取当前名」和「尝试改名」交给调用方，
    /// 重名/空名由调用方返回 false，窗口保持打开让人继续改。
    /// </summary>
    public class Dialog_RenameGroup : Window
    {
        private const string FieldName = "gridThermalGroupName";

        private readonly string title;
        private readonly Func<string, bool> tryRename;
        private string curName;
        private bool submitting;

        public override Vector2 InitialSize
        {
            get { return new Vector2(380f, 150f); }
        }

        public Dialog_RenameGroup(string title, string currentName, Func<string, bool> tryRename)
        {
            this.title = string.IsNullOrEmpty(title) ? "分组名称" : title;
            this.curName = currentName ?? "";
            this.tryRename = tryRename;

            doCloseX = true;
            // 不让基类自己关窗口：回车要先过改名逻辑，重名时窗口必须留着。
            closeOnAccept = false;
            closeOnCancel = true;
            absorbInputAroundWindow = true;
        }

        public override void PostOpen()
        {
            base.PostOpen();
            GUI.SetNextControlName(FieldName);
            GUI.FocusControl(FieldName);
        }

        public override void OnAcceptKeyPressed()
        {
            base.OnAcceptKeyPressed();
            TrySubmit();
        }

        private void TrySubmit()
        {
            // 焦点在输入框时，Window 的 Enter 分支和 DoWindowContents 会各读一次同一个事件，
            // 用这个标志保证只提交一次。
            if (submitting)
                return;

            submitting = true;
            if (tryRename == null || tryRename(curName))
                Close();
            else
                submitting = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            // 窗口里抛异常会打断整段 GUI 绘制，输入框可能卡在半截状态；出问题就关掉窗口。
            try
            {
                Widgets.Label(new Rect(0f, 0f, inRect.width, 24f), title);

                // 每帧都要重新登记控件名，否则 PostOpen 里的 FocusControl 找不到目标，
                // 打开窗口还得手点一下输入框才能打字。
                GUI.SetNextControlName(FieldName);
                curName = Widgets.TextField(new Rect(0f, 28f, inRect.width, 30f), curName ?? "");

                bool acceptByKey = Event.current.type == EventType.KeyDown
                    && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);

                if (Widgets.ButtonText(new Rect(0f, inRect.height - 32f, 120f, 30f), "确定") || acceptByKey)
                {
                    TrySubmit();
                    return;
                }

                if (Widgets.ButtonText(new Rect(128f, inRect.height - 32f, 120f, 30f), "取消"))
                    Close();
            }
            catch (Exception e)
            {
                SafeLog.Report("分组改名窗口", e);
                Close();
            }
        }
    }
}
