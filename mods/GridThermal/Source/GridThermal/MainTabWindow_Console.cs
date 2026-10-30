using System.Collections.Generic;
using RimWorld;
using Verse;
using UnityEngine;

namespace Shuo.GridThermal
{
    /// <summary>
    /// 底栏「电网」标签页：左边看电网负载，右边管温控分组。
    /// OnGUI 每帧都会调用 DoWindowContents，所以凡是「算数据」的部分都做缓存，
    /// 绘制阶段只读结果、不做计算也不拼字符串。
    /// </summary>
    public class MainTabWindow_Console : MainTabWindow
    {
        private const float RowHeight = 28f;
        private const int PowerCacheTicks = 30;
        private const int MaxSelect = 200;

        private static readonly float[] ColX = { 0f, 110f, 200f, 290f, 400f, 520f, 620f };
        private static readonly string[] Headers = { "电网", "发电", "耗电", "净", "储能", "电量", "状态" };
        private static readonly Color ColorOk = new Color(0.6f, 0.9f, 0.6f);
        private static readonly Color ColorDrain = new Color(1f, 0.85f, 0.4f);
        private static readonly Color ColorShort = new Color(1f, 0.5f, 0.5f);
        // 纯电池组没有开关，「关闭」不是告警状态，别染成红色。
        private static readonly Color ColorNeutral = new Color(0.72f, 0.72f, 0.72f);

        private static readonly List<string[]> powerRows = new List<string[]>();
        private static readonly List<int> powerStates = new List<int>();
        private static string powerSummary = "";
        private static int powerCacheTick = int.MinValue;
        private static int powerCacheMapId = -1;
        private static Game lastGame;

        private int page;
        private Vector2 scrollPos = Vector2.zero;
        private Vector2 scrollPosPowerGroup = Vector2.zero;

        // 窗口高度按「当前页实际有几行」算，不写死。
        // 写死一个偏大的固定值，早期殖民地只有一两个电网时，多出来的高度
        // 就是整块窗口背景——底栏上方会撑出一个空白框。
        private Vector2 windowSize = new Vector2(960f, 300f);

        public override Vector2 RequestedTabSize
        {
            get { return windowSize; }
        }

        public override void PreOpen()
        {
            base.PreOpen();
            windowSize = ComputeWindowSize();
        }

        private Vector2 ComputeWindowSize()
        {
            float bodyH;

            try
            {
                Map map = Find.CurrentMap;

                if (page == 0)
                {
                    int rows = 0;
                    if (map != null)
                    {
                        EnsurePowerCache(map);
                        rows = powerRows.Count;
                    }
                    // 汇总行(32) + 表头(28) + 4 + 数据行 + 底部余量
                    bodyH = 32f + RowHeight + 4f + rows * RowHeight + 14f;
                }
                else if (page == 2)
                {
                    int rows = map == null ? 0 : PowerGroups.ScanSorted(map).Count;
                    // 顶部两行说明(46) + 表头(28) + 4 + 每组 32 + 底部余量（与温控页同构）
                    bodyH = 46f + RowHeight + 4f + rows * (RowHeight + 4f) + 14f;
                }
                else
                {
                    int rows = map == null ? 0 : ThermalGroups.ScanSorted(map).Count;
                    // 顶部两行说明(46) + 表头(28) + 4 + 每组 32 + 底部余量。
                    // 这里原来漏算了开头那 46px，导致组一多就少一行、看起来「显示不全」。
                    bodyH = 46f + RowHeight + 4f + rows * (RowHeight + 4f) + 14f;
                }
            }
            catch (System.Exception e)
            {
                SafeLog.Report("计算面板尺寸", e);
                bodyH = 280f;
            }

            // 下限 260 让内容少的时候不要太扁；上限 520 放宽容 14 组左右，
            // 再多的靠滚动——滚动条很窄，一超界看起来就像掉了几行。
            bodyH = Mathf.Clamp(bodyH, 260f, 520f);

            // 温控页内容到 x=816，960 宽右侧只剩 144，加宽到 1000 松一点。
            float w = Mathf.Min(1000f, UI.screenWidth - 40f);
            return new Vector2(w, 28f + 10f + bodyH + 8f);
        }

        /// <summary>
        /// 切页后内容行数会变，窗口跟着收放一次。差得多才动，
        /// 免得每帧 SetInitialSizeAndPosition 把窗口晃来晃去。
        /// </summary>
        private void ResizeToContent()
        {
            try
            {
                Vector2 size = ComputeWindowSize();
                if (Mathf.Abs(size.y - windowSize.y) < 1f && Mathf.Abs(size.x - windowSize.x) < 1f)
                    return;

                windowSize = size;
                SetInitialSizeAndPosition();
            }
            catch (System.Exception e)
            {
                SafeLog.Report("调整面板尺寸", e);
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            // OnGUI 里抛异常会打断整段 GUI 绘制，轻则面板花屏，重则输入失灵。
            try
            {
                DrawTabs(inRect);
            }
            catch (System.Exception e)
            {
                SafeLog.Report("面板绘制", e);
                DrawFault(inRect, e);
            }
        }

        private void DrawTabs(Rect inRect)
        {
            float tabH = 28f;
            if (Widgets.ButtonText(new Rect(inRect.x, inRect.y, 100f, tabH), "电力网络") && page != 0)
            {
                page = 0;
                scrollPos = Vector2.zero;
                ResizeToContent();
            }
            if (Widgets.ButtonText(new Rect(inRect.x + 106f, inRect.y, 100f, tabH), "温控分组") && page != 1)
            {
                page = 1;
                scrollPos = Vector2.zero;
                ResizeToContent();
            }
            if (Widgets.ButtonText(new Rect(inRect.x + 212f, inRect.y, 100f, tabH), "电力分组") && page != 2)
            {
                page = 2;
                scrollPosPowerGroup = Vector2.zero;
                ResizeToContent();
            }

            Rect body = new Rect(inRect.x, inRect.y + tabH + 10f, inRect.width,
                Mathf.Max(80f, inRect.height - tabH - 10f));
            if (page == 0)
                DrawPower(body);
            else if (page == 1)
                DrawThermal(body);
            else
                DrawPowerGroups(body);
        }

        private static void DrawFault(Rect inRect, System.Exception e)
        {
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 48f),
                "电网面板渲染出错，已跳过本帧绘制，游戏不受影响。详情见 Player.log。");
            Widgets.Label(new Rect(inRect.x, inRect.y + 50f, inRect.width, 48f),
                e.GetType().Name + ": " + e.Message);
        }

        private static void SelectMany(List<CompThermalGroup> members)
        {
            try
            {
                Find.Selector.ClearSelection();
                Map map = Find.CurrentMap;
                int n = 0;
                for (int i = 0; i < members.Count && n < MaxSelect; i++)
                {
                    Thing t = members[i].parent;
                    if (t == null || !t.Spawned || t.def == null || !t.def.selectable)
                        continue;
                    // 跨地图选中的单位会干扰当前视角，直接跳过。
                    if (t.Map != map)
                        continue;
                    Find.Selector.Select(t, false, false);
                    n++;
                }
            }
            catch (System.Exception e)
            {
                SafeLog.Report("选中温控设备", e);
            }
        }

        // ---------- 电力 ----------

        private static void EnsurePowerCache(Map map)
        {
            // 换存档后 map.uniqueID 可能撞车，直接作废旧缓存。
            if (!ReferenceEquals(Current.Game, lastGame))
            {
                lastGame = Current.Game;
                powerCacheTick = int.MinValue;
                powerCacheMapId = -1;
                powerRows.Clear();
                powerStates.Clear();
            }

            int now = Find.TickManager == null ? 0 : Find.TickManager.TicksGame;
            // 用 long 相减：powerCacheTick 的哨兵是 int.MinValue，
            // 直接 now - int.MinValue 会整型溢出成负数，导致缓存被误判为「刚算过」。
            if (map.uniqueID == powerCacheMapId && now >= powerCacheTick
                && (long)now - powerCacheTick < PowerCacheTicks)
                return;

            powerCacheTick = now;
            powerCacheMapId = map.uniqueID;
            powerRows.Clear();
            powerStates.Clear();

            List<PowerNet> nets = map.powerNetManager.AllNetsListForReading;
            float totalGen = 0f, totalUse = 0f, totalStore = 0f, totalCap = 0f;

            for (int i = 0; i < nets.Count; i++)
            {
                PowerNet net = nets[i];
                float gen = 0f, use = 0f, store = 0f, cap = 0f;

                List<CompPowerTrader> comps = net.powerComps;
                for (int j = 0; j < comps.Count; j++)
                {
                    CompPowerTrader c = comps[j];
                    if (c == null)
                        continue;
                    float p = c.PowerOutput;
                    if (p >= 0f)
                        gen += p;
                    else
                        use -= p;
                }

                List<CompPowerBattery> batteries = net.batteryComps;
                for (int j = 0; j < batteries.Count; j++)
                {
                    CompPowerBattery b = batteries[j];
                    if (b == null)
                        continue;
                    store += b.StoredEnergy;
                    cap += b.Props.storedEnergyMax;
                }

                totalGen += gen;
                totalUse += use;
                totalStore += store;
                totalCap += cap;

                int state = 0;
                if (gen <= 0f && use > 0f)
                    state = 2;
                else if (use > gen + 0.5f)
                    state = store > 0.5f ? 1 : 2;

                powerRows.Add(new[]
                {
                    "电网 " + (i + 1),
                    gen.ToString("0") + " W",
                    use.ToString("0") + " W",
                    (gen - use).ToString("+0;-0;0") + " W",
                    store.ToString("0") + " / " + cap.ToString("0"),
                    (cap > 0f ? store / cap * 100f : 0f).ToString("0") + " %",
                    state == 0 ? "正常" : (state == 1 ? "放电中" : "缺电")
                });
                powerStates.Add(state);
            }

            powerSummary =
                "总发电 " + totalGen.ToString("0") + " W    " +
                "总耗电 " + totalUse.ToString("0") + " W    " +
                "净输出 " + (totalGen - totalUse).ToString("+0;-0;0") + " W    " +
                "储能 " + totalStore.ToString("0") + " / " + totalCap.ToString("0") + "    " +
                "电量 " + (totalCap > 0f ? (totalStore / totalCap * 100f).ToString("0") : "0") + " %";
        }

        private void DrawPower(Rect r)
        {
            Map map = Find.CurrentMap;
            if (map == null)
            {
                Widgets.Label(r, "没有打开的地图。");
                return;
            }

            EnsurePowerCache(map);

            Widgets.Label(new Rect(r.x, r.y, r.width, 24f), powerSummary);
            Widgets.DrawLineHorizontal(r.x, r.y + 26f, r.width);

            Rect listRect = new Rect(r.x, r.y + 32f, r.width, Mathf.Max(60f, r.height - 32f));
            float viewH = Mathf.Max(listRect.height, powerRows.Count * RowHeight + 34f);
            Rect viewRect = new Rect(0f, 0f, listRect.width - 22f, viewH);

            Widgets.BeginScrollView(listRect, ref scrollPos, viewRect);

            for (int c = 0; c < Headers.Length; c++)
                Widgets.Label(new Rect(ColX[c], 0f, 110f, RowHeight), Headers[c]);
            Widgets.DrawLineHorizontal(0f, RowHeight, viewRect.width);

            float y = RowHeight + 4f;
            float visibleTop = scrollPos.y;
            float visibleBottom = scrollPos.y + listRect.height;
            for (int i = 0; i < powerRows.Count; i++)
            {
                // 只绘制可视区内的行：电网多的时候，滚出屏幕的行没有任何理由再调一遍 GUI。
                if (y + RowHeight < visibleTop || y > visibleBottom)
                {
                    y += RowHeight;
                    continue;
                }

                if (i % 2 == 1)
                    Widgets.DrawHighlight(new Rect(0f, y, viewRect.width, RowHeight));

                string[] row = powerRows[i];
                for (int c = 0; c < 6; c++)
                    Widgets.Label(new Rect(ColX[c], y, 120f, RowHeight), row[c]);

                int st = powerStates[i];
                Color old = GUI.color;
                GUI.color = st == 0 ? ColorOk : (st == 1 ? ColorDrain : ColorShort);
                Widgets.Label(new Rect(ColX[6], y, 70f, RowHeight), row[6]);
                GUI.color = old;

                y += RowHeight;
            }

            Widgets.EndScrollView();
        }

        // ---------- 温控 ----------

        private enum Action { None, Mode, Select, Disband }

        // 温控表列坐标（滚动视图内容坐标系）。
        // 温度单元格布局：[−5][−1] 21°C [+1][+5]，数值居中，符合“左减右加”的直觉。
        private const float ColGroup = 0f;
        private const float ColMembers = 134f;
        private const float ColRoom = 193f;
        private const float ColMode = 267f;
        private const float ColTarget = 335f;
        private const float ColAlt = 521f;
        private const float ColSelect = 712f;
        private const float ColDisband = 766f;
        private const float TempLabelOffset = 62f;

        // 季节判定结果缓存：只在这两个值变化时重建字符串，避免每帧拼字符串。
        private static bool seasonCachedWarm = true;
        private static string seasonCachedText = "";

        private static string SeasonHint(Map map)
        {
            bool warm = map.mapTemperature.SeasonalTemp >= CompThermalGroup.SeasonWarmThreshold;
            if (seasonCachedText.Length == 0 || warm != seasonCachedWarm)
            {
                seasonCachedWarm = warm;
                seasonCachedText = warm
                    ? "当前判定：热季　季节模式会关掉加热器，只让制冷设备工作"
                    : "当前判定：冷季　季节模式会关掉制冷器，只让加热设备工作";
            }
            return seasonCachedText;
        }

        private void DrawThermal(Rect r)
        {
            Map map = Find.CurrentMap;
            if (map == null)
            {
                Widgets.Label(r, "没有打开的地图。");
                return;
            }

            Widgets.Label(new Rect(r.x, r.y, r.width, 20f),
                "统一：所有设备同一个目标　季节：热季只开制冷、冷季只开制热，反季设备直接断电");
            Widgets.Label(new Rect(r.x, r.y + 20f, r.width, 20f), SeasonHint(map));

            List<KeyValuePair<string, ThermalGroupInfo>> groups = ThermalGroups.ScanSorted(map);
            Rect listRect = new Rect(r.x, r.y + 46f, r.width, Mathf.Max(60f, r.height - 46f));

            if (groups.Count == 0)
            {
                Widgets.Label(new Rect(r.x, r.y + 54f, r.width, 24f),
                    "还没有任何分组。选中一台加热器或制冷器，点「温控分组」建组。");
                return;
            }

            float viewH = Mathf.Max(listRect.height, groups.Count * (RowHeight + 4f) + 40f);
            Rect viewRect = new Rect(0f, 0f, listRect.width - 22f, viewH);
            Widgets.BeginScrollView(listRect, ref scrollPos, viewRect);

            Widgets.Label(new Rect(ColGroup, 0f, 130f, RowHeight), "分组");
            Widgets.Label(new Rect(ColMembers, 0f, 55f, RowHeight), "成员");
            Widgets.Label(new Rect(ColRoom, 0f, 70f, RowHeight), "室温");
            Widgets.Label(new Rect(ColMode, 0f, 64f, RowHeight), "模式");
            Widgets.Label(new Rect(ColTarget, 0f, 150f, RowHeight), "目标/热季");
            Widgets.Label(new Rect(ColAlt, 0f, 150f, RowHeight), "冷季");
            Widgets.DrawLineHorizontal(0f, RowHeight, viewRect.width);

            string key = null;
            Action act = Action.None;
            int tempDelta = 0;
            int altDelta = 0;

            float y = RowHeight + 4f;
            float visibleTop = scrollPos.y;
            float visibleBottom = scrollPos.y + listRect.height;
            for (int i = 0; i < groups.Count; i++)
            {
                string k = groups[i].Key;
                ThermalGroupInfo info = groups[i].Value;

                // 可视区裁剪：滚出屏幕的组不再构造控件。
                if (y + RowHeight + 4f < visibleTop || y > visibleBottom)
                {
                    y += RowHeight + 4f;
                    continue;
                }

                if (i % 2 == 1)
                    Widgets.DrawHighlight(new Rect(0f, y, viewRect.width, RowHeight + 4f));

                Widgets.Label(new Rect(ColGroup, y, 130f, RowHeight), k);
                Widgets.Label(new Rect(ColMembers, y, 55f, RowHeight), info.membersText);
                Widgets.Label(new Rect(ColRoom, y, 70f, RowHeight), info.avgText);

                if (Widgets.ButtonText(new Rect(ColMode, y + 2f, 64f, 24f), info.modeText))
                {
                    key = k;
                    act = Action.Mode;
                }

                int dt = TempCell(ColTarget, y, info.targetText);
                if (dt != 0)
                {
                    key = k;
                    tempDelta = dt;
                }

                if (info.mode == CompThermalGroup.ModeSeason)
                {
                    int da = TempCell(ColAlt, y, info.altText);
                    if (da != 0)
                    {
                        key = k;
                        altDelta = da;
                    }
                }
                else
                {
                    LabelCentered(new Rect(ColAlt + TempLabelOffset, y, 58f, RowHeight), "—");
                }

                if (Widgets.ButtonText(new Rect(ColSelect, y + 2f, 50f, 24f), "选中"))
                {
                    key = k;
                    act = Action.Select;
                }
                if (Widgets.ButtonText(new Rect(ColDisband, y + 2f, 50f, 24f), "解散"))
                {
                    key = k;
                    act = Action.Disband;
                }

                y += RowHeight + 4f;
            }

            Widgets.EndScrollView();

            if (key == null)
                return;

            ThermalGroupInfo target;
            if (!ThermalGroups.TryGet(map, key, out target))
                return;

            if (tempDelta != 0)
            {
                ThermalGroups.ApplySettings(target, target.target + tempDelta, target.altTarget, target.mode);
                return;
            }
            if (altDelta != 0)
            {
                ThermalGroups.ApplySettings(target, target.target, target.altTarget + altDelta, target.mode);
                return;
            }

            switch (act)
            {
                case Action.Mode:
                    // 只剩两种模式：统一 ↔ 季节
                    ThermalGroups.ApplySettings(target, target.target, target.altTarget,
                        target.mode == CompThermalGroup.ModeSeason
                            ? CompThermalGroup.ModeUniform
                            : CompThermalGroup.ModeSeason);
                    break;
                case Action.Select:
                    SelectMany(target.members);
                    break;
                case Action.Disband:
                    ThermalGroups.Disband(target);
                    // 解散后少一行，窗口跟着收，否则底栏上方会留一条空白。
                    ResizeToContent();
                    break;
            }
        }

        // ---------- 电力分组 ----------

        private const float ColPgName = 0f;
        private const float ColPgMembers = 186f;
        private const float ColPgGen = 244f;
        private const float ColPgUse = 316f;
        private const float ColPgStore = 392f;
        private const float ColPgState = 478f;
        private const float ColPgToggle = 540f;
        private const float ColPgRename = 622f;
        private const float ColPgSelect = 678f;
        private const float ColPgDisband = 734f;

        private enum PowerAction { None, Toggle, Rename, Select, Disband }

        private void DrawPowerGroups(Rect r)
        {
            Map map = Find.CurrentMap;
            if (map == null)
            {
                Widgets.Label(r, "没有打开的地图。");
                return;
            }

            Widgets.Label(new Rect(r.x, r.y, r.width, 20f),
                "把发电设备与用电设备归为一组，整组一键开关；组名可以自定义");
            Widgets.Label(new Rect(r.x, r.y + 20f, r.width, 20f),
                "开关走建筑自身的电源开关，效果与手动点开一样；电池只参与统计，不能断电");

            List<KeyValuePair<string, PowerGroupInfo>> groups = PowerGroups.ScanSorted(map);
            Rect listRect = new Rect(r.x, r.y + 46f, r.width, Mathf.Max(60f, r.height - 46f));

            if (groups.Count == 0)
            {
                Widgets.Label(new Rect(r.x, r.y + 54f, r.width, 24f),
                    "还没有任何电力分组。选中任意发电或用电设备，点「电力分组」建组。");
                return;
            }

            float viewH = Mathf.Max(listRect.height, groups.Count * (RowHeight + 4f) + 40f);
            Rect viewRect = new Rect(0f, 0f, listRect.width - 22f, viewH);
            Widgets.BeginScrollView(listRect, ref scrollPosPowerGroup, viewRect);

            Widgets.Label(new Rect(ColPgName, 0f, 180f, RowHeight), "分组");
            Widgets.Label(new Rect(ColPgMembers, 0f, 50f, RowHeight), "成员");
            Widgets.Label(new Rect(ColPgGen, 0f, 70f, RowHeight), "发电");
            Widgets.Label(new Rect(ColPgUse, 0f, 70f, RowHeight), "耗电");
            Widgets.Label(new Rect(ColPgStore, 0f, 80f, RowHeight), "储能");
            Widgets.Label(new Rect(ColPgState, 0f, 60f, RowHeight), "状态");
            Widgets.DrawLineHorizontal(0f, RowHeight, viewRect.width);

            string key = null;
            PowerAction act = PowerAction.None;

            float y = RowHeight + 4f;
            float visibleTop = scrollPosPowerGroup.y;
            float visibleBottom = scrollPosPowerGroup.y + listRect.height;
            for (int i = 0; i < groups.Count; i++)
            {
                string k = groups[i].Key;
                PowerGroupInfo info = groups[i].Value;

                // 滚出可视区的组不构造控件，组多的时候省一轮 GUI 调用。
                if (y + RowHeight + 4f < visibleTop || y > visibleBottom)
                {
                    y += RowHeight + 4f;
                    continue;
                }

                if (i % 2 == 1)
                    Widgets.DrawHighlight(new Rect(0f, y, viewRect.width, RowHeight + 4f));

                Widgets.Label(new Rect(ColPgName, y, 180f, RowHeight), info.nameText);
                Widgets.Label(new Rect(ColPgMembers, y, 50f, RowHeight), info.membersText);
                Widgets.Label(new Rect(ColPgGen, y, 70f, RowHeight), info.genText);
                Widgets.Label(new Rect(ColPgUse, y, 70f, RowHeight), info.useText);
                Widgets.Label(new Rect(ColPgStore, y, 80f, RowHeight), info.storeText);

                Color old = GUI.color;
                GUI.color = !info.anySwitchable
                    ? ColorNeutral
                    : (info.allOn ? ColorOk : (info.anyOn ? ColorDrain : ColorShort));
                Widgets.Label(new Rect(ColPgState, y, 60f, RowHeight), info.stateText);
                GUI.color = old;

                // 整组只有电池时没有开关可切，画一个按钮会让人以为点得动。
                if (info.anySwitchable)
                {
                    if (Widgets.ButtonText(new Rect(ColPgToggle, y + 2f, 76f, 24f), info.toggleText))
                    {
                        key = k;
                        act = PowerAction.Toggle;
                    }
                }
                else
                {
                    Color oldBtn = GUI.color;
                    GUI.color = ColorNeutral;
                    LabelCentered(new Rect(ColPgToggle, y, 76f, RowHeight), info.toggleText);
                    GUI.color = oldBtn;
                }
                if (Widgets.ButtonText(new Rect(ColPgRename, y + 2f, 50f, 24f), "改名"))
                {
                    key = k;
                    act = PowerAction.Rename;
                }
                if (Widgets.ButtonText(new Rect(ColPgSelect, y + 2f, 50f, 24f), "选中"))
                {
                    key = k;
                    act = PowerAction.Select;
                }
                if (Widgets.ButtonText(new Rect(ColPgDisband, y + 2f, 50f, 24f), "解散"))
                {
                    key = k;
                    act = PowerAction.Disband;
                }

                y += RowHeight + 4f;
            }

            Widgets.EndScrollView();

            if (key == null)
                return;

            PowerGroupInfo target;
            if (!PowerGroups.Scan(map).TryGetValue(key, out target))
                return;

            switch (act)
            {
                case PowerAction.Toggle:
                    // 只要还有开着的就整组关掉；全关状态点一下整组打开。
                    // 现场状态实时读，不用缓存快照——见 PowerGroups.AnyOn 的注释。
                    PowerGroups.SetPower(target, !PowerGroups.AnyOn(target));
                    break;
                case PowerAction.Rename:
                    Find.WindowStack.Add(new Dialog_RenamePowerGroup(target));
                    break;
                case PowerAction.Select:
                    SelectPowerMembers(target.members);
                    break;
                case PowerAction.Disband:
                    PowerGroups.Disband(target);
                    // 少了一行就该收一次窗口，否则底部会多出一条空白背景。
                    ResizeToContent();
                    break;
            }
        }

        private static void SelectPowerMembers(List<CompPowerGroup> members)
        {
            try
            {
                Find.Selector.ClearSelection();
                Map map = Find.CurrentMap;
                int n = 0;
                for (int i = 0; i < members.Count && n < MaxSelect; i++)
                {
                    Thing t = members[i].parent;
                    if (t == null || !t.Spawned || t.def == null || !t.def.selectable)
                        continue;
                    if (t.Map != map)
                        continue;
                    Find.Selector.Select(t, false, false);
                    n++;
                }
            }
            catch (System.Exception e)
            {
                SafeLog.Report("选中电力设备", e);
            }
        }

        /// <summary>一个温度单元格：[−5][−1] 数值 [+1][+5]，返回本次点击的增量（未点击为 0）。</summary>
        private static int TempCell(float x, float y, string text)
        {
            int result = 0;
            if (Widgets.ButtonText(new Rect(x, y + 2f, 28f, 24f), "-5"))
                result = -5;
            if (Widgets.ButtonText(new Rect(x + 31f, y + 2f, 28f, 24f), "-1"))
                result = -1;
            LabelCentered(new Rect(x + TempLabelOffset, y, 58f, RowHeight), text);
            if (Widgets.ButtonText(new Rect(x + 123f, y + 2f, 28f, 24f), "+1"))
                result = 1;
            if (Widgets.ButtonText(new Rect(x + 154f, y + 2f, 28f, 24f), "+5"))
                result = 5;
            return result;
        }

        /// <summary>居中的 Label。不显式设锚点的话会沿用上一次控件留下的对齐方式，导致数值贴边。</summary>
        private static void LabelCentered(Rect rect, string text)
        {
            TextAnchor old = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, text);
            Text.Anchor = old;
        }
    }
}
