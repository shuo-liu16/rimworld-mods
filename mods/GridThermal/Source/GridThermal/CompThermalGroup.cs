using System.Collections.Generic;
using RimWorld;
using Verse;
using UnityEngine;

namespace Shuo.GridThermal
{
    public class CompProperties_ThermalGroup : CompProperties
    {
        public CompProperties_ThermalGroup()
        {
            compClass = typeof(CompThermalGroup);
        }
    }

    /// <summary>
    /// 挂在加热器 / 制冷器上的分组标记。
    /// 组内所有机器共享同一套设定，由「电网」面板集中调节。
    /// </summary>
    public class CompThermalGroup : ThingComp
    {
        public const int ModeUniform = 0;   // 所有设备同一个目标
        public const int ModeSeason = 2;    // 按季节切换：热季只开制冷，冷季只开制热
        // 历史遗留：1 是已删除的「死区」模式。保留常量只为兼容老存档的 mode=1，
        // 读到时按「统一」处理，不会出错。

        /// <summary>季节模式的分界：季节性均温高于此值算热季。</summary>
        public const float SeasonWarmThreshold = 15f;

        // 没有供电组件的温控设备退而求其次：把目标推到够不到的极端值让其闲置。
        public const float IdleHeaterTarget = -273f;
        public const float IdleCoolerTarget = 100f;

        public string group = "";
        public float targetTemp = 21f;   // 统一模式的目标；季节模式下为热季目标
        public float altTemp = 18f;      // 季节模式下的冷季目标
        public int mode = ModeUniform;

        // 入组前的原始目标温度，离组时还原。
        public float savedTarget = 21f;
        public bool hasSavedTarget;

        // 是否由本 Mod 切断了电源（季节模式的「闲置」）。玩家手动断电不会置这个标记，
        // 这样换季时不会把玩家自己关掉的机器又打开。
        public bool powerCutByMod;

        public CompTempControl TempControl
        {
            get { return parent == null ? null : parent.TryGetComp<CompTempControl>(); }
        }

        public CompPowerTrader PowerTrader
        {
            get { return parent == null ? null : parent.TryGetComp<CompPowerTrader>(); }
        }

        public bool IsCooler
        {
            get { return parent is Building_Cooler; }
        }

        public bool IsHeater
        {
            get { return parent is Building_Heater; }
        }

        private bool IsWarmSeason()
        {
            Map map = parent == null ? null : parent.Map;
            float seasonal = map == null ? SeasonWarmThreshold : map.mapTemperature.SeasonalTemp;
            return seasonal >= SeasonWarmThreshold;
        }

        /// <summary>季节模式下，这台设备是不是本季该停的那一类。</summary>
        private bool ShouldIdleNow()
        {
            if (mode != ModeSeason)
                return false;
            // 认不出类型的第三方设备不擅自断电，宁可少控制也不要猜错方向。
            if (!IsCooler && !IsHeater)
                return false;
            return IsWarmSeason() ? IsHeater : IsCooler;
        }

        /// <summary>本季该用的目标温度。</summary>
        public float EffectiveTarget()
        {
            if (mode == ModeSeason)
                return IsWarmSeason() ? targetTemp : altTemp;
            return targetTemp;   // 统一模式，以及老存档里的 mode=1
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            // 这里抛异常会打断地图加载，绝对不能漏出去。
            try
            {
                ApplyTarget();
            }
            catch (System.Exception e)
            {
                SafeLog.Report("建筑生成时校准", e);
            }
        }

        // 加热器 / 制冷器在 Core 里是 tickerType=Rare，
        // 走的是 CompTickRare，不是 CompTick。两个都覆盖，兼容被改过频率的 Mod 建筑。
        public override void CompTick()
        {
            base.CompTick();
            ApplyTarget();
        }

        public override void CompTickRare()
        {
            base.CompTickRare();
            ApplyTarget();
        }

        public void ApplyTarget()
        {
            if (group.NullOrEmpty())
                return;
            try
            {
                CompTempControl tc = TempControl;
                if (tc == null)
                    return;

                if (ShouldIdleNow())
                {
                    // 只有真要断电时才去取供电组件，正常供暖路径少一次组件查找。
                    CompPowerTrader pt = PowerTrader;
                    if (pt != null)
                    {
                        CutPower(pt);
                        return;
                    }

                    // 没有供电组件的设备只能靠极端目标值闲置。
                    float idle = IsCooler ? IdleCoolerTarget : IdleHeaterTarget;
                    if (!Mathf.Approximately(tc.TargetTemperature, idle))
                        tc.TargetTemperature = idle;
                    return;
                }

                // 本季该工作：先把目标设好，再恢复供电，避免短暂按旧目标运行。
                float want = EffectiveTarget();
                if (!Mathf.Approximately(tc.TargetTemperature, want))
                    tc.TargetTemperature = want;

                if (powerCutByMod)
                    RestorePower(PowerTrader);
            }
            catch (System.Exception e)
            {
                // 单台机器出问题不应该影响同组其它机器，也不该冒泡进 Thing.Tick。
                SafeLog.Report("校准目标温度", e);
            }
        }

        /// <summary>
        /// 断电必须走 CompFlickable。
        /// 直接写 PowerOn 是不行的：加热器/制冷器都带 CompProperties_Flickable，
        /// 它的轮询会按 wantSwitchOn（默认 true）把电源又打开，等于白关。
        /// </summary>
        private void CutPower(CompPowerTrader pt)
        {
            CompFlickable flick = parent == null ? null : parent.TryGetComp<CompFlickable>();
            if (flick != null)
            {
                if (flick.SwitchIsOn)
                {
                    flick.SwitchIsOn = false;
                    powerCutByMod = true;
                }
                return;
            }

            if (pt.PowerOn)
            {
                pt.PowerOn = false;
                powerCutByMod = true;
            }
        }

        /// <summary>只恢复「本 Mod 关掉的」电源，玩家手动关的不碰。</summary>
        private void RestorePower(CompPowerTrader pt)
        {
            if (!powerCutByMod)
                return;

            if (pt != null)
            {
                CompFlickable flick = parent == null ? null : parent.TryGetComp<CompFlickable>();
                if (flick != null)
                {
                    if (!flick.SwitchIsOn)
                        flick.SwitchIsOn = true;
                }
                else if (!pt.PowerOn)
                {
                    pt.PowerOn = true;
                }
            }
            powerCutByMod = false;
        }

        public void SetGroup(string newGroup, float target, float alt, int newMode)
        {
            bool wasEmpty = group.NullOrEmpty();
            bool leaving = newGroup.NullOrEmpty();

            // 入组时先记下这台机器原本的目标温度，离组时还回去。
            if (wasEmpty && !leaving)
                CaptureCurrentTarget();

            group = newGroup ?? "";
            targetTemp = target;
            altTemp = alt;
            mode = newMode;

            // 离组时必须显式还原：否则设备会停在季节模式的状态上（可能被断电、
            // 也可能目标被设成极端值），而 ApplyTarget 在 group 为空时直接 return，
            // 再也没人去修正它——表现就是「解散分组后这台机器永远不工作」。
            if (leaving)
                RestoreSavedTarget();
            else
                ApplyTarget();

            ThermalGroups.Invalidate(parent == null ? null : parent.Map);
        }

        private void CaptureCurrentTarget()
        {
            try
            {
                CompTempControl tc = TempControl;
                if (tc == null)
                    return;
                savedTarget = tc.TargetTemperature;
                hasSavedTarget = true;
            }
            catch (System.Exception e)
            {
                SafeLog.Report("记录原目标温度", e);
            }
        }

        private void RestoreSavedTarget()
        {
            try
            {
                CompTempControl tc = TempControl;
                CompPowerTrader pt = PowerTrader;

                if (tc != null)
                {
                    if (hasSavedTarget)
                        tc.TargetTemperature = savedTarget;
                    else if (tc.Props != null)
                        tc.TargetTemperature = tc.Props.defaultTargetTemperature;
                }

                RestorePower(pt);
            }
            catch (System.Exception e)
            {
                SafeLog.Report("还原目标温度", e);
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref group, "thermalGroup", "");
            Scribe_Values.Look(ref targetTemp, "thermalTargetTemp", 21f);
            Scribe_Values.Look(ref altTemp, "thermalAltTemp", 18f);
            Scribe_Values.Look(ref mode, "thermalMode", ModeUniform);
            Scribe_Values.Look(ref savedTarget, "thermalSavedTarget", 21f);
            Scribe_Values.Look(ref hasSavedTarget, "thermalHasSavedTarget", false);
            Scribe_Values.Look(ref powerCutByMod, "thermalPowerCut", false);
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            // 用 List 而不是 yield：迭代器里的异常会在调用方（游戏 UI）迭代时才抛出，
            // 那样就兜不住了。这里当场构造完，出问题当场记录。
            List<Gizmo> list = new List<Gizmo>();
            try
            {
                foreach (Gizmo g in base.CompGetGizmosExtra())
                    list.Add(g);

                list.Add(new Command_Action
                {
                    defaultLabel = group.NullOrEmpty() ? "温控分组：未分组" : "温控分组：" + group,
                    defaultDesc = "把同一区域的加热器与制冷器归入一组，之后可在「电网」面板里统一设定目标温度。",
                    icon = TexButton.Info,
                    action = delegate
                    {
                        try
                        {
                            OpenGroupMenu();
                        }
                        catch (System.Exception e)
                        {
                            SafeLog.Report("打开分组菜单", e);
                        }
                    }
                });
            }
            catch (System.Exception e)
            {
                SafeLog.Report("生成分组按钮", e);
            }
            return list;
        }

        private void OpenGroupMenu()
        {
            List<FloatMenuOption> opts = new List<FloatMenuOption>();
            Map map = parent == null ? null : parent.Map;
            int batch = CountSelectedTempDevices();
            string hint = batch > 1 ? "　→ " + batch + " 台选中" : "";

            foreach (KeyValuePair<string, ThermalGroupInfo> kv in ThermalGroups.Scan(map))
            {
                string g = kv.Key;
                ThermalGroupInfo info = kv.Value;
                opts.Add(new FloatMenuOption(
                    g + "（" + info.members.Count + " 台）" + hint,
                    delegate { ApplyGroupToSelection(g, info.target, info.altTarget, info.mode); }));
            }

            opts.Add(new FloatMenuOption("移出分组" + hint,
                delegate { ApplyGroupToSelection("", 21f, 18f, ModeUniform); }));
            opts.Add(new FloatMenuOption("新建分组" + hint, delegate
            {
                ApplyGroupToSelection(ThermalGroups.NextName(map), 21f, 18f, ModeUniform);
            }));

            Find.WindowStack.Add(new FloatMenu(opts));
        }

        /// <summary>当前选中的温控设备数量（至少算自己 1 台）。</summary>
        private int CountSelectedTempDevices()
        {
            try
            {
                List<object> sel = Find.Selector.SelectedObjects;
                if (sel == null)
                    return 1;

                int n = 0;
                for (int i = 0; i < sel.Count; i++)
                {
                    Thing t = sel[i] as Thing;
                    if (t == null || t.Map != parent.Map)
                        continue;
                    if (t.TryGetComp<CompThermalGroup>() != null)
                        n++;
                }
                return n < 1 ? 1 : n;
            }
            catch (System.Exception e)
            {
                SafeLog.Report("统计选中的温控设备", e);
                return 1;
            }
        }

        /// <summary>
        /// 多选时应用到所有选中的温控设备，单选时只改自己。
        /// RimWorld 的多选 gizmo 只画一次、也只回调一次，必须自己遍历选中集合。
        /// </summary>
        private void ApplyGroupToSelection(string groupName, float target, float alt, int mode)
        {
            int applied = 0;
            try
            {
                List<object> sel = Find.Selector.SelectedObjects;
                if (sel != null && sel.Count > 1)
                {
                    for (int i = 0; i < sel.Count; i++)
                    {
                        Thing t = sel[i] as Thing;
                        if (t == null || t.Map != parent.Map)
                            continue;
                        CompThermalGroup c = t.TryGetComp<CompThermalGroup>();
                        if (c == null)
                            continue;
                        c.SetGroup(groupName, target, alt, mode);
                        applied++;
                    }
                }
            }
            catch (System.Exception e)
            {
                SafeLog.Report("批量应用温控分组", e);
            }

            // 选中的东西里没有同类设备时，至少把当前这台改掉。
            if (applied == 0)
                SetGroup(groupName, target, alt, mode);
        }
    }

    public class ThermalGroupInfo
    {
        public string name = "";
        public Map map;

        public List<CompThermalGroup> members = new List<CompThermalGroup>();
        public float target = 21f;
        public float altTarget = 18f;
        public int mode;

        public float avgTemp;

        // 显示用字符串一并缓存：OnGUI 每帧都读，现场格式化会产生大量小对象。
        public string nameText = "";     // 截断后的组名，长名字不会溢到下一列
        public string membersText = "0 台";
        public string avgText = "0 °C";
        public string targetText = "21 °C";
        public string altText = "—";
        public string modeText = "统一";

        public void RecalculateDisplay()
        {
            float sum = 0f;
            int n = 0;
            for (int i = 0; i < members.Count; i++)
            {
                CompThermalGroup m = members[i];
                if (m.parent == null || !m.parent.Spawned)
                    continue;
                sum += m.parent.AmbientTemperature;
                n++;
            }
            avgTemp = n == 0 ? 0f : sum / n;

            // 温控页「分组」列只有 130px，10 个全角字约 120px，再长就压到「成员」列了。
            nameText = TruncateName(name, 10);
            membersText = members.Count + " 台";
            avgText = avgTemp.ToString("0") + " °C";
            RefreshTexts();
        }

        private static string TruncateName(string s, int max)
        {
            if (string.IsNullOrEmpty(s))
                return "";
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }

        public void RefreshTexts()
        {
            targetText = target.ToString("0") + " °C";
            altText = mode == CompThermalGroup.ModeSeason ? altTarget.ToString("0") + " °C" : "—";
            modeText = ModeName(mode);
        }

        public static string ModeName(int m)
        {
            return m == CompThermalGroup.ModeSeason ? "季节" : "统一";
        }
    }

    public static class ThermalGroups
    {
        // 面板每帧都会取数据，全图 AllThings 可能上千个对象。
        // 缓存 30 tick（半秒）：重算一次，绘制阶段只读。
        private const int CacheTicks = 30;
        private static readonly Dictionary<int, CacheEntry> cache = new Dictionary<int, CacheEntry>();
        private static Game lastGame;

        private class CacheEntry
        {
            public Dictionary<string, ThermalGroupInfo> groups;
            public List<KeyValuePair<string, ThermalGroupInfo>> sorted;
            public int tick;
        }

        private static readonly CacheEntry EmptyEntry = new CacheEntry
        {
            groups = new Dictionary<string, ThermalGroupInfo>(),
            sorted = new List<KeyValuePair<string, ThermalGroupInfo>>(),
            tick = 0
        };

        public static void Invalidate(Map map)
        {
            if (map != null)
                cache.Remove(map.uniqueID);
        }

        private static CacheEntry Ensure(Map map)
        {
            // 缓存跨存档会残留：新存档的 map.uniqueID 可能和旧的一样，
            // 命中旧条目就会拿到上一局的 Comp 引用。
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

            Dictionary<string, ThermalGroupInfo> result = new Dictionary<string, ThermalGroupInfo>();

            List<Thing> things = map.listerThings.AllThings;
            for (int i = 0; i < things.Count; i++)
            {
                Thing t = things[i];
                if (t == null || t.def == null || !t.Spawned)
                    continue;
                // 温控组件只会挂在建筑上；先按 category 过滤，省掉对满地的物品调 TryGetComp。
                if (t.def.category != ThingCategory.Building)
                    continue;
                CompThermalGroup c = t.TryGetComp<CompThermalGroup>();
                if (c == null || c.group.NullOrEmpty())
                    continue;

                ThermalGroupInfo info;
                if (!result.TryGetValue(c.group, out info))
                {
                    info = new ThermalGroupInfo();
                    info.name = c.group;
                    info.map = map;
                    info.target = c.targetTemp;
                    info.altTarget = c.altTemp;
                    info.mode = c.mode;
                    result[c.group] = info;
                }
                info.members.Add(c);
            }

            List<KeyValuePair<string, ThermalGroupInfo>> sorted =
                new List<KeyValuePair<string, ThermalGroupInfo>>(result.Count);
            foreach (KeyValuePair<string, ThermalGroupInfo> kv in result)
            {
                kv.Value.RecalculateDisplay();
                sorted.Add(kv);
            }
            sorted.Sort(delegate (KeyValuePair<string, ThermalGroupInfo> a, KeyValuePair<string, ThermalGroupInfo> b)
            {
                return string.CompareOrdinal(a.Key, b.Key);
            });

            entry = new CacheEntry { groups = result, sorted = sorted, tick = now };
            cache[map.uniqueID] = entry;
            return entry;
        }

        public static Dictionary<string, ThermalGroupInfo> Scan(Map map)
        {
            if (map == null)
                return new Dictionary<string, ThermalGroupInfo>();
            return Ensure(map).groups;
        }

        public static List<KeyValuePair<string, ThermalGroupInfo>> ScanSorted(Map map)
        {
            if (map == null)
                return new List<KeyValuePair<string, ThermalGroupInfo>>();
            return Ensure(map).sorted;
        }

        public static bool TryGet(Map map, string group, out ThermalGroupInfo info)
        {
            info = null;
            if (map == null)
                return false;
            return Ensure(map).groups.TryGetValue(group, out info);
        }

        public static string NextName(Map map)
        {
            Dictionary<string, ThermalGroupInfo> existing = Scan(map);
            int i = 1;
            while (existing.ContainsKey("温控组 " + i))
                i++;
            return "温控组 " + i;
        }

        /// <summary>整组写入设定。任何一个成员失败都不影响其它成员。</summary>
        public static void ApplySettings(ThermalGroupInfo info, float target, float alt, int mode)
        {
            target = Mathf.Clamp(target, -100f, 100f);
            alt = Mathf.Clamp(alt, -100f, 100f);
            info.target = target;
            info.altTarget = alt;
            info.mode = mode;
            info.RefreshTexts();

            for (int i = 0; i < info.members.Count; i++)
            {
                CompThermalGroup m = info.members[i];
                m.targetTemp = target;
                m.altTemp = alt;
                m.mode = mode;
                m.ApplyTarget();
            }
        }

        public static void Disband(ThermalGroupInfo info)
        {
            Map map = null;
            for (int i = 0; i < info.members.Count; i++)
            {
                CompThermalGroup m = info.members[i];
                if (map == null && m.parent != null)
                    map = m.parent.Map;
                m.SetGroup("", 21f, 18f, CompThermalGroup.ModeUniform);
            }
            Invalidate(map);
        }

        /// <summary>
        /// 改名。只动 group 这个标签，不重设温度——所以不走 SetGroup，
        /// 避免改名顺带触发 ApplyTarget 和「记录原目标温度」那套逻辑。
        /// </summary>
        public static bool Rename(ThermalGroupInfo info, string newName)
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

            Dictionary<string, ThermalGroupInfo> existing = Scan(info.map);
            if (existing != null && existing.ContainsKey(newName))
            {
                Messages.Message("已经有同名分组：" + newName, MessageTypeDefOf.RejectInput, false);
                return false;
            }

            for (int i = 0; i < info.members.Count; i++)
            {
                CompThermalGroup m = info.members[i];
                if (m != null)
                    m.group = newName;
            }

            Invalidate(info.map);
            return true;
        }
    }
}
