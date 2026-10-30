using System.Collections.Generic;
using RimWorld;
using Verse;
using UnityEngine;

namespace Shuo.GridThermal
{
    public class CompProperties_PowerGroup : CompProperties
    {
        public CompProperties_PowerGroup()
        {
            compClass = typeof(CompPowerGroup);
        }
    }

    /// <summary>
    /// 电力设施的分组标记。同组设备可以从「电网」面板一键开/关，组名可自定义。
    ///
    /// 和温控分组是两条独立的线：温控组管的是目标温度，这里管的是电源开关。
    /// 同一台机器可以既在温控组里，又在电力组里，互不干扰。
    /// </summary>
    public class CompPowerGroup : ThingComp
    {
        public string group = "";

        public CompPowerTrader PowerTrader
        {
            get { return parent == null ? null : parent.TryGetComp<CompPowerTrader>(); }
        }

        public CompPowerBattery Battery
        {
            get { return parent == null ? null : parent.TryGetComp<CompPowerBattery>(); }
        }

        public CompFlickable Flickable
        {
            get { return parent == null ? null : parent.TryGetComp<CompFlickable>(); }
        }

        /// <summary>能不能真的断电。电池只参与统计，没有开关。</summary>
        public bool CanSwitch
        {
            get { return Flickable != null || PowerTrader != null; }
        }

        public bool IsOn
        {
            get
            {
                CompFlickable f = Flickable;
                if (f != null)
                    return f.SwitchIsOn;
                CompPowerTrader pt = PowerTrader;
                return pt == null || pt.PowerOn;
            }
        }

        /// <summary>
        /// 开关必须优先走 CompFlickable。
        /// 直接写 PowerOn 会被 flickable 的轮询按 wantSwitchOn（默认 true）又打开，等于白关。
        /// </summary>
        public void SetPower(bool on)
        {
            try
            {
                CompFlickable f = Flickable;
                if (f != null)
                {
                    if (f.SwitchIsOn != on)
                        f.SwitchIsOn = on;
                    return;
                }

                CompPowerTrader pt = PowerTrader;
                if (pt != null && pt.PowerOn != on)
                    pt.PowerOn = on;
            }
            catch (System.Exception e)
            {
                // 单台设备出问题不该让整组操作中断。
                SafeLog.Report("切换电力分组开关", e);
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref group, "powerGroup", "");
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            // 和温控组件一样用 List 当场构造：迭代器里的异常会冒到游戏 UI 层，兜不住。
            List<Gizmo> list = new List<Gizmo>();
            try
            {
                foreach (Gizmo g in base.CompGetGizmosExtra())
                    list.Add(g);

                list.Add(new Command_Action
                {
                    defaultLabel = group.NullOrEmpty() ? "电力分组：未分组" : "电力分组：" + group,
                    defaultDesc = "把发电与用电设备归入一组，之后可在「电网」面板里一键开关整组。",
                    icon = TexButton.Info,
                    action = delegate
                    {
                        try
                        {
                            OpenGroupMenu();
                        }
                        catch (System.Exception e)
                        {
                            SafeLog.Report("打开电力分组菜单", e);
                        }
                    }
                });
            }
            catch (System.Exception e)
            {
                SafeLog.Report("生成电力分组按钮", e);
            }
            return list;
        }

        private void OpenGroupMenu()
        {
            List<FloatMenuOption> opts = new List<FloatMenuOption>();
            Map map = parent == null ? null : parent.Map;

            foreach (KeyValuePair<string, PowerGroupInfo> kv in PowerGroups.Scan(map))
            {
                string g = kv.Key;
                PowerGroupInfo info = kv.Value;
                opts.Add(new FloatMenuOption(
                    g + "（" + info.members.Count + " 台）",
                    delegate { SetGroup(g); }));
            }

            opts.Add(new FloatMenuOption("移出分组", delegate { SetGroup(""); }));
            opts.Add(new FloatMenuOption("新建分组", delegate
            {
                SetGroup(PowerGroups.NextName(map));
            }));

            Find.WindowStack.Add(new FloatMenu(opts));
        }

        public void SetGroup(string newGroup)
        {
            group = newGroup ?? "";
            PowerGroups.Invalidate(parent == null ? null : parent.Map);
        }
    }

    public class PowerGroupInfo
    {
        public string name = "";
        public List<CompPowerGroup> members = new List<CompPowerGroup>();
        public Map map;

        public float gen;
        public float use;
        public float store;
        public float cap;
        public float net;

        public bool anyOn;
        public bool allOn;
        public bool anySwitchable;

        // 面板每帧都读，字符串在这里一次算好，绘制阶段不再拼接。
        public string nameText = "";      // 截断后的组名，长名字不会溢到下一列
        public string membersText = "0 台";
        public string genText = "0 W";
        public string useText = "0 W";
        public string storeText = "0 / 0";
        public string stateText = "已关闭";
        public string toggleText = "全部开启";

        public void Recalculate()
        {
            gen = 0f;
            use = 0f;
            store = 0f;
            cap = 0f;

            int total = 0;
            int onCount = 0;
            int switchable = 0;

            for (int i = 0; i < members.Count; i++)
            {
                CompPowerGroup m = members[i];
                if (m == null || m.parent == null || !m.parent.Spawned)
                    continue;

                total++;

                // 每台只取一次组件引用。CanSwitch / IsOn 内部各要两次 TryGetComp，
                // 组里几十台时会被放大成几百次查找；这里用局部变量把 7 次压到 3 次。
                CompFlickable flick = m.Flickable;
                CompPowerTrader pt = m.PowerTrader;
                CompPowerBattery bat = m.Battery;

                if (pt != null)
                {
                    float p = pt.PowerOutput;
                    if (p >= 0f)
                        gen += p;
                    else
                        use -= p;
                }

                if (bat != null)
                {
                    store += bat.StoredEnergy;
                    cap += bat.Props.storedEnergyMax;
                }

                if (flick != null || pt != null)
                {
                    switchable++;
                    if (flick != null ? flick.SwitchIsOn : pt.PowerOn)
                        onCount++;
                }
            }

            net = gen - use;
            anySwitchable = switchable > 0;
            anyOn = onCount > 0;
            allOn = switchable > 0 && onCount == switchable;

            // 组名最长 24 字，而这一列只有 180px：不截断会盖住「成员」列。
            nameText = Truncate(name, 12);
            membersText = total + " 台";
            genText = gen.ToString("0") + " W";
            useText = use.ToString("0") + " W";
            storeText = store.ToString("0") + " / " + cap.ToString("0");

            if (!anySwitchable)
            {
                stateText = "无开关";
                toggleText = "不可开关";
            }
            else if (allOn)
            {
                stateText = "运行中";
                toggleText = "全部关闭";
            }
            else if (anyOn)
            {
                stateText = "部分开启";
                toggleText = "全部开启";
            }
            else
            {
                stateText = "已关闭";
                toggleText = "全部开启";
            }
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s))
                return "";
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }
    }

    public static class PowerGroups
    {
        private const int CacheTicks = 30;
        private static readonly Dictionary<int, CacheEntry> cache = new Dictionary<int, CacheEntry>();
        private static Game lastGame;

        private class CacheEntry
        {
            public Dictionary<string, PowerGroupInfo> groups;
            public List<KeyValuePair<string, PowerGroupInfo>> sorted;
            public int tick;
        }

        private static readonly CacheEntry EmptyEntry = new CacheEntry
        {
            groups = new Dictionary<string, PowerGroupInfo>(),
            sorted = new List<KeyValuePair<string, PowerGroupInfo>>(),
            tick = 0
        };

        public static void Invalidate(Map map)
        {
            if (map != null)
                cache.Remove(map.uniqueID);
        }

        private static CacheEntry Ensure(Map map)
        {
            // 换存档后 map.uniqueID 可能撞车，缓存必须整体作废，否则会拿到上一局的 Comp 引用。
            if (!ReferenceEquals(Current.Game, lastGame))
            {
                lastGame = Current.Game;
                cache.Clear();
            }

            TickManager tm = Find.TickManager;
            if (tm == null)
                return EmptyEntry;
            int now = tm.TicksGame;

            CacheEntry entry;
            if (cache.TryGetValue(map.uniqueID, out entry) && now >= entry.tick && now - entry.tick < CacheTicks)
                return entry;

            Dictionary<string, PowerGroupInfo> result = new Dictionary<string, PowerGroupInfo>();

            List<Thing> things = map.listerThings.AllThings;
            for (int i = 0; i < things.Count; i++)
            {
                Thing t = things[i];
                if (t == null || t.def == null || !t.Spawned)
                    continue;

                CompPowerGroup c = t.TryGetComp<CompPowerGroup>();
                if (c == null || c.group.NullOrEmpty())
                    continue;

                PowerGroupInfo info;
                if (!result.TryGetValue(c.group, out info))
                {
                    info = new PowerGroupInfo();
                    info.name = c.group;
                    info.map = map;
                    result[c.group] = info;
                }
                info.members.Add(c);
            }

            List<KeyValuePair<string, PowerGroupInfo>> sorted =
                new List<KeyValuePair<string, PowerGroupInfo>>(result.Count);
            foreach (KeyValuePair<string, PowerGroupInfo> kv in result)
            {
                kv.Value.Recalculate();
                sorted.Add(kv);
            }
            // 组名唯一，所以 ordinal 比较就是稳定的全序，不需要额外的 tie-breaker。
            sorted.Sort(delegate (KeyValuePair<string, PowerGroupInfo> a, KeyValuePair<string, PowerGroupInfo> b)
            {
                return string.CompareOrdinal(a.Key, b.Key);
            });

            entry = new CacheEntry { groups = result, sorted = sorted, tick = now };
            cache[map.uniqueID] = entry;
            return entry;
        }

        public static Dictionary<string, PowerGroupInfo> Scan(Map map)
        {
            if (map == null)
                return new Dictionary<string, PowerGroupInfo>();
            return Ensure(map).groups;
        }

        public static List<KeyValuePair<string, PowerGroupInfo>> ScanSorted(Map map)
        {
            if (map == null)
                return new List<KeyValuePair<string, PowerGroupInfo>>();
            return Ensure(map).sorted;
        }

        public static string NextName(Map map)
        {
            Dictionary<string, PowerGroupInfo> existing = Scan(map);
            int i = 1;
            while (existing.ContainsKey("电力组 " + i))
                i++;
            return "电力组 " + i;
        }

        /// <summary>整组开/关。逐台 try/catch 在 SetPower 内部，一台失败不影响其它。</summary>
        public static void SetPower(PowerGroupInfo info, bool on)
        {
            if (info == null)
                return;

            for (int i = 0; i < info.members.Count; i++)
            {
                CompPowerGroup m = info.members[i];
                if (m == null || !m.CanSwitch)
                    continue;
                m.SetPower(on);
            }

            info.Recalculate();
            Invalidate(info.map);
        }

        public static void Disband(PowerGroupInfo info)
        {
            if (info == null)
                return;

            for (int i = 0; i < info.members.Count; i++)
            {
                CompPowerGroup m = info.members[i];
                if (m != null)
                    m.group = "";
            }

            Invalidate(info.map);
        }

        /// <summary>改名。成功返回 true；重名或空名返回 false，对话框保持打开。</summary>
        public static bool Rename(PowerGroupInfo info, string newName)
        {
            if (info == null)
                return true;

            newName = (newName ?? "").Trim();
            if (newName.Length == 0)
            {
                Messages.Message("分组名不能为空。", MessageTypeDefOf.RejectInput, false);
                return false;
            }
            if (newName.Length > 24)
                newName = newName.Substring(0, 24);
            if (newName == info.name)
                return true;

            Dictionary<string, PowerGroupInfo> existing = Scan(info.map);
            if (existing != null && existing.ContainsKey(newName))
            {
                Messages.Message("已经有同名分组：" + newName, MessageTypeDefOf.RejectInput, false);
                return false;
            }

            for (int i = 0; i < info.members.Count; i++)
            {
                CompPowerGroup m = info.members[i];
                if (m != null)
                    m.group = newName;
            }

            Invalidate(info.map);
            return true;
        }
    }

    /// <summary>
    /// 改名对话框。RimWorld 自带的 Dialog_Rename 只接受 IRenameable，
    /// 而分组不是 Thing，套不上；这里写个轻量版，回车即确认。
    /// </summary>
    public class Dialog_RenamePowerGroup : Window
    {
        private const string FieldName = "gridThermalPowerGroupName";

        private readonly PowerGroupInfo info;
        private string curName;
        private bool submitting;

        public override Vector2 InitialSize
        {
            get { return new Vector2(380f, 150f); }
        }

        public Dialog_RenamePowerGroup(PowerGroupInfo info)
        {
            this.info = info;
            curName = info == null ? "" : info.name;
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
            if (PowerGroups.Rename(info, curName))
                Close();
            else
                submitting = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            // 窗口里抛异常会打断整段 GUI 绘制，输入框可能卡在半截状态；出问题就关掉窗口。
            try
            {
                Widgets.Label(new Rect(0f, 0f, inRect.width, 24f), "分组名称");

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
            catch (System.Exception e)
            {
                SafeLog.Report("电力分组改名窗口", e);
                Close();
            }
        }
    }
}
