using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Shuo.WorkSorter
{
    /// <summary>
    /// 底栏「工作」标签页：一张 工作 × 殖民者 的表，格子里是建议的优先级。
    /// 先在表上预览，觉得合适再「应用全部」；单独点某一格会立刻生效并锁定它。
    /// </summary>
    public class MainTabWindow_WorkSorter : MainTabWindow
    {
        private const float RowH = 24f;
        private const float NameW = 118f;
        private const float HeadH = 46f;
        private const int RebuildTicks = 250;   // 约 4 秒重算一次预览，够用又不浪费

        private WorkSorterPlan plan;
        private int planTick = -1;
        private int planMapId = -1;
        private Game planGame;
        private int pendingChanges;
        private Vector2 scroll = Vector2.zero;

        // 每帧取一次存档组件就够了。Game.GetComponent<T>() 内部是遍历 components
        // 列表，如果每个格子各取一次，200 格 × 60 帧就是每秒上万次遍历。
        private WorkSorterGameComponent comp;

        public override Vector2 RequestedTabSize
        {
            get { return new Vector2(1020f, 520f); }
        }

        public override void DoWindowContents(Rect inRect)
        {
            try
            {
                Draw(inRect);
            }
            catch (Exception e)
            {
                SafeLog.Report("面板绘制", e);
                Widgets.Label(inRect, "工作排序面板出错，已跳过本帧绘制。详情见 Player.log。");
            }
        }

        public override void PostClose()
        {
            // 关掉面板就把预览丢掉，下次打开重新按最新情况算。
            plan = null;
            planGame = null;
            planTick = -1;
        }

        private void Draw(Rect inRect)
        {
            comp = WorkSorterGameComponent.Instance;
            EnsurePlan();

            float y = inRect.y;
            DrawToolbar(new Rect(inRect.x, y, inRect.width, 32f));
            y += 38f;

            Rect head = new Rect(inRect.x, y, inRect.width, HeadH);
            y += HeadH;
            DrawHeader(head);

            float footerH = 44f;
            Rect body = new Rect(inRect.x, y, inRect.width, Mathf.Max(60f, inRect.height - (y - inRect.y) - footerH));
            DrawBody(body);

            DrawFooter(new Rect(inRect.x, inRect.height - footerH + inRect.y - 2f, inRect.width, footerH));
        }

        private void DrawToolbar(Rect bar)
        {
            float x = bar.x;

            if (Widgets.ButtonText(new Rect(x, bar.y, 92f, 30f), "重新计算"))
            {
                // 玩家主动要求重算时，连带把需求侧的缓存也丢掉，
                // 否则刚搬完货还会看到 25 秒前的「待搬运 43」。
                WorkSorterDemand.InvalidateCache();
                plan = null;
                planTick = -1;
                EnsurePlan();
            }
            x += 98f;

            WorkSorterSettings s = WorkSorterMod.Settings;

            if (s != null && Widgets.ButtonText(new Rect(x, bar.y, 92f, 30f), "应用全部"))
            {
                int n = plan == null ? 0 : WorkSorterApplier.ApplyPlan(plan, true, true);
                if (n == 0)
                    Messages.Message("没有需要改动的格子。", MessageTypeDefOf.RejectInput, false);
                planTick = -1;
            }
            x += 98f;

            if (s != null)
            {
                string autoLabel = s.autoApply ? "自动：开" : "自动：关";
                if (Widgets.ButtonText(new Rect(x, bar.y, 92f, 30f), autoLabel))
                    s.autoApply = !s.autoApply;
                x += 98f;

                if (Widgets.ButtonText(new Rect(x, bar.y, 92f, 30f), "间隔 " + s.intervalDays + " 天"))
                {
                    s.intervalDays = s.intervalDays >= 7 ? 1 : s.intervalDays + 1;
                }
                x += 98f;

                TooltipHandler.TipRegion(new Rect(bar.x + 98f, bar.y, 92f, 30f),
                    "应用全部：把当前预览写回游戏。已锁定的格子会被跳过。");
                TooltipHandler.TipRegion(new Rect(bar.x + 196f, bar.y, 92f, 30f),
                    "自动排序开关。开启后每隔若干天自动重排一次（需先手动应用过一次）。");
                TooltipHandler.TipRegion(new Rect(bar.x + 294f, bar.y, 92f, 30f),
                    "两次自动重排的间隔天数。点一下加一天，到 7 之后回到 1。");
            }

            // 右侧状态
            string stat = plan == null
                ? ""
                : "待改动 " + pendingChanges + " 格 / 共 " + (plan.pawns.Count * plan.workTypes.Count) + " 格";

            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(new Rect(bar.x, bar.y, bar.width - 4f, 30f), stat);
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawHeader(Rect head)
        {
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(head.x, head.y, NameW - 6f, 20f), "工作 \\ 殖民者");
            Text.Font = GameFont.Small;

            if (plan == null || plan.pawns.Count == 0)
                return;

            float colW = ColumnWidth(head.width);

            for (int i = 0; i < plan.pawns.Count; i++)
            {
                Pawn p = plan.pawns[i];
                Rect r = new Rect(head.x + NameW + i * colW, head.y, colW - 2f, 34f);

                bool locked = comp != null && comp.IsPawnLocked(p);
                if (locked)
                {
                    Color old = GUI.color;
                    GUI.color = new Color(0.35f, 0.55f, 0.9f, 0.35f);
                    Widgets.DrawBoxSolid(r, GUI.color);
                    GUI.color = old;
                }

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.UpperCenter;
                Widgets.Label(r, ShortName(p));
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;

                TooltipHandler.TipRegion(r, p.Name.ToStringFull + (locked
                    ? "\n\n已锁定：这个人的一切优先级都不会被自动改动。点击取消锁定。"
                    : "\n\n点击锁定这个人，自动排序将完全跳过它。"));

                if (Widgets.ButtonInvisible(r) && comp != null)
                    comp.TogglePawnLock(p);
            }
        }

        private void DrawBody(Rect body)
        {
            if (plan == null || plan.pawns.Count == 0 || plan.workTypes.Count == 0)
            {
                Widgets.Label(body, "当前地图没有可排序的殖民者。");
                return;
            }

            float colW = ColumnWidth(body.width);
            float contentW = NameW + colW * plan.pawns.Count;
            float contentH = plan.workTypes.Count * RowH + 4f;

            Rect view = new Rect(0f, 0f, Mathf.Max(contentW, body.width - 16f), contentH);
            Widgets.BeginScrollView(body, ref scroll, view);

            // EndScrollView 必须无条件执行：半路抛异常会留下一个没关闭的裁剪区，
            // 之后整块窗口的绘制都会错位。用 finally 兜住。
            try
            {
                DrawRows(colW, view, body);
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }

        private void DrawRows(float colW, Rect view, Rect body)
        {
            float visTop = scroll.y;
            float visBottom = scroll.y + body.height;

            for (int ri = 0; ri < plan.workTypes.Count; ri++)
            {
                float rowY = ri * RowH;
                // 可视区裁剪：滚出屏幕的行连 Widget 都不建
                if (rowY + RowH < visTop || rowY > visBottom)
                    continue;

                WorkTypeDef w = plan.workTypes[ri];

                Rect nameRect = new Rect(0f, rowY, NameW - 6f, RowH);
                if (ri % 2 == 0)
                    Widgets.DrawBoxSolid(new Rect(0f, rowY, view.width, RowH), new Color(0f, 0f, 0f, 0.04f));

                Text.Font = GameFont.Tiny;
                Widgets.Label(nameRect, WorkSorterEngine.WorkLabel(w));
                Text.Font = GameFont.Small;

                for (int ci = 0; ci < plan.pawns.Count; ci++)
                {
                    WorkSorterCell c = plan.Cell(plan.pawns[ci], w);
                    Rect cr = new Rect(NameW + ci * colW, rowY + 1f, colW - 2f, RowH - 3f);

                    if (c == null)
                    {
                        Widgets.DrawBoxSolid(cr, new Color(0.25f, 0.25f, 0.25f, 0.35f));
                        TooltipHandler.TipRegion(cr, "这个人做不了这项工作（被特性、背景或意识形态限制）。");
                        continue;
                    }

                    DrawCell(cr, c);
                }
            }
        }

        private void DrawCell(Rect rect, WorkSorterCell c)
        {
            c.locked = comp != null && comp.IsCellLocked(c.pawn, c.workType);

            // 锁定后这一格以游戏内实际值为准——继续显示"建议值"会让人以为
            // 还没生效，实际上锁定就是不打算再改了。
            int shown = c.locked ? c.current : c.suggested;
            Color bg = ColorFor(shown);

            // 锁定 = 深色粗框；建议与现状不同 = 细框，表示"应用后会变"。
            // 用 DrawBoxSolidWithOutline 一次画完背景和边框，比先铺底色再画
            // 一遍 Widgets.DrawBox 少一次四边矩形绘制——两百个格子省得出来。
            Color outline;
            int outlineW;
            if (c.locked)
            {
                outline = new Color(0.15f, 0.3f, 0.7f, 0.9f);
                outlineW = 2;
            }
            else if (c.suggested > 0 && c.suggested != c.current)
            {
                outline = new Color(0.1f, 0.1f, 0.1f, 0.45f);
                outlineW = 1;
            }
            else
            {
                outline = Color.clear;
                outlineW = 0;
            }

            if (outlineW > 0)
                Widgets.DrawBoxSolidWithOutline(rect, bg, outline, outlineW);
            else
                Widgets.DrawBoxSolid(rect, bg);

            TextAnchor oldAnchor = Text.Anchor;
            GameFont oldFont = Text.Font;
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Small;

            GUI.color = new Color(0.08f, 0.08f, 0.08f);
            Widgets.Label(rect, shown <= 0 ? "·" : shown.ToString());
            GUI.color = Color.white;

            Text.Font = oldFont;
            Text.Anchor = oldAnchor;

            string tip = c.reason ?? "";
            if (c.locked)
                tip += "\n\n已锁定：自动排序不会改动这一格。点击解锁。";

            TooltipHandler.TipRegion(rect, tip);
            if (Widgets.ButtonInvisible(rect))
                OnCellClick(c);
        }

        private void DrawFooter(Rect r)
        {
            string hint;

            if (comp == null)
            {
                // 组件没挂上说明 Harmony 补丁没生效。面板还能用（能看建议值），
                // 但写回和锁定都不可用，得让玩家知道是配置问题而不是按钮坏了。
                hint = "存档组件未注册（Harmony 补丁可能没打上）：本面板只能预览，应用与锁定不可用。\n"
                     + "检查 Mod 列表里 Harmony 是否启用，并在加载顺序上排在前面。";
            }
            else
            {
                hint = "1 最高 · 4 最低　｜　点格子 = 立即应用并锁定，再点一次解锁　｜　点表头 = 锁定整人　｜　蓝框 = 已锁定，细框 = 应用后会变";
                if (!comp.appliedOnce)
                    hint += "\n自动排序尚未启动：先点一次「应用全部」，之后才会按间隔自己重排。";
            }

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(r, hint);
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }

        private void OnCellClick(WorkSorterCell c)
        {
            if (comp == null)
            {
                Messages.Message("存档组件未注册，无法写入。请检查 Harmony 是否启用。",
                    MessageTypeDefOf.RejectInput, false);
                return;
            }

            if (comp.IsCellLocked(c.pawn, c.workType))
            {
                comp.UnlockCell(c.pawn, c.workType);
                c.locked = false;
                planTick = -1;
                return;
            }

            int pr = c.suggested > 0 ? c.suggested : c.current;
            if (pr <= 0)
            {
                Messages.Message("这一格没有可写入的优先级。", MessageTypeDefOf.RejectInput, false);
                return;
            }

            if (WorkSorterApplier.ApplyCell(c.pawn, c.workType, pr))
            {
                comp.LockCell(c.pawn, c.workType);
                c.locked = true;
                c.current = pr;
                pendingChanges = CountPending();
            }
        }

        private void EnsurePlan()
        {
            Game game = Verse.Current.Game;
            Map map = Find.CurrentMap;
            if (map == null || game == null)
            {
                plan = null;
                return;
            }

            int now = Find.TickManager.TicksGame;

            // 读档后地图 uniqueID 不会变，但 Pawn 对象已经全是新的。
            // 只比 mapId 会让 plan 里留着一堆上一局的死人引用，
            // 点上去就写进了已经不在地图上的小人。必须连 Game 引用一起比。
            bool needRebuild = plan == null
                               || !ReferenceEquals(game, planGame)
                               || map.uniqueID != planMapId
                               || planTick < 0
                               || (long)now - planTick > RebuildTicks;

            if (!needRebuild)
                return;

            plan = WorkSorterEngine.Build(map);
            planTick = now;
            planMapId = map.uniqueID;
            planGame = game;
            pendingChanges = CountPending();
        }

        private int CountPending()
        {
            if (plan == null)
                return 0;

            int n = 0;
            foreach (Dictionary<WorkTypeDef, WorkSorterCell> row in plan.cells.Values)
            {
                foreach (WorkSorterCell c in row.Values)
                {
                    if (c.suggested <= 0 || c.suggested == c.current)
                        continue;
                    // 锁定的格子不会被改，不该算进「待改动」里，否则数字永远归不了零。
                    if (comp != null && comp.IsCellLocked(c.pawn, c.workType))
                        continue;
                    n++;
                }
            }
            return n;
        }

        private float ColumnWidth(float totalWidth)
        {
            int n = plan == null || plan.pawns.Count == 0 ? 1 : plan.pawns.Count;
            float avail = totalWidth - NameW - 22f;
            return Mathf.Clamp(avail / n, 30f, 68f);
        }

        private static string ShortName(Pawn p)
        {
            if (p == null || p.Name == null)
                return "?";
            string s = p.Name.ToStringShort;
            if (string.IsNullOrEmpty(s))
                return "?";
            if (s.Length > 5)
                s = s.Substring(0, 5) + "…";
            return s;
        }

        private static Color ColorFor(int priority)
        {
            switch (priority)
            {
                case 1: return new Color(0.44f, 0.76f, 0.44f);
                case 2: return new Color(0.78f, 0.84f, 0.50f);
                case 3: return new Color(0.93f, 0.78f, 0.50f);
                case 4: return new Color(0.86f, 0.62f, 0.58f);
                default: return new Color(0.55f, 0.55f, 0.55f, 0.5f);
            }
        }
    }
}
