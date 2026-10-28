using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Shuo.AutoClean
{
    public class AutoCleanSettings : ModSettings
    {
        public bool enabled = true;
        public int hour = 6;
        public bool homeOnly = true;
        public bool notify = true;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref enabled, "enabled", true);
            Scribe_Values.Look(ref hour, "hour", 6);
            Scribe_Values.Look(ref homeOnly, "homeOnly", true);
            Scribe_Values.Look(ref notify, "notify", true);
            // 手改配置文件或旧版本存下来的越界值会让整点判断永远不成立。
            hour = Mathf.Clamp(hour, 0, 23);
        }
    }

    /// <summary>
    /// 每天到点清扫一次。用 ticks 记录上次清扫时间，避免同一小时内重复触发。
    /// 清扫本身是分帧完成的：一次性删掉上千个污渍会让同一帧卡顿，
    /// 所以先入队，每次检查只处理一小批。
    /// </summary>
    public static class AutoCleanUtility
    {
        private const int TicksPerDay = 60000;
        /// <summary>每轮处理的污渍上限。256 tick 一轮，1000 个污渍约 15 秒清完。</summary>
        private const int MaxPerPass = 300;

        private static readonly Dictionary<int, int> lastCleanTickByMap = new Dictionary<int, int>();
        private static readonly Dictionary<int, Queue<Thing>> pendingByMap = new Dictionary<int, Queue<Thing>>();

        // 静态状态会跨存档残留。读档后旧地图的 Thing 引用已经失效，
        // 继续拿它们 Destroy 会在已卸载的地图上操作，可能刷红字。
        private static Game lastGame;

        public static void Tick()
        {
            TickManager tm = Find.TickManager;
            if (tm == null)
                return;

            if (!ReferenceEquals(Current.Game, lastGame))
            {
                lastGame = Current.Game;
                pendingByMap.Clear();
                lastCleanTickByMap.Clear();
            }

            // 主循环每秒调用 60 次（4 倍速更多），先用位与做最廉价的早退：
            // 每 256 tick（约 4 秒）才真正检查一次。游戏内一小时是 2500 tick，
            // 所以这个粒度绝不会漏掉整点窗口。
            if ((tm.TicksGame & 0xFF) != 0)
                return;

            AutoCleanSettings s = AutoCleanMod.Settings;
            if (s != null && s.enabled)
                CheckSchedule(s);

            // 关开关不应该把清到一半的队列冻住，所以 DrainPending 不受 enabled 影响。
            // 放在后面：到点那一刻入队的污渍，同一次检查就能先清掉第一批。
            DrainPending();
        }

        private static void CheckSchedule(AutoCleanSettings s)
        {
            List<Map> maps = Find.Maps;
            if (maps == null)
                return;

            for (int i = 0; i < maps.Count; i++)
            {
                // 逐地图兜异常：一张地图出问题不该让其它殖民地当天不扫。
                try
                {
                    Map map = maps[i];
                    if (map == null || !map.IsPlayerHome)
                        continue;
                    if (GenLocalDate.HourOfDay(map) != s.hour)
                        continue;

                    int last;
                    lastCleanTickByMap.TryGetValue(map.uniqueID, out last);
                    int diff = GenTicks.TicksAbs - last;
                    // diff < 0 说明读了一个时间更早的存档，静态记录失效，按「没清过」处理。
                    if (diff >= 0 && diff < TicksPerDay - 3000)
                        continue;

                    lastCleanTickByMap[map.uniqueID] = GenTicks.TicksAbs;
                    int queued = EnqueueFilth(map, s.homeOnly);
                    if (queued > 0 && s.notify && Current.Game != null)
                    {
                        string where = map.info != null && map.info.parent != null
                            ? map.info.parent.LabelCap.ToString()
                            : "殖民地";
                        Messages.Message(
                            "自动清扫：" + where + " 开始清理 " + queued + " 处污渍。",
                            MessageTypeDefOf.NeutralEvent,
                            false);
                    }
                }
                catch (System.Exception e)
                {
                    SafeLog.Report("排程检查", e);
                }
            }
        }

        private static void DrainPending()
        {
            if (pendingByMap.Count == 0)
                return;

            List<int> finished = null;
            foreach (KeyValuePair<int, Queue<Thing>> kv in pendingByMap)
            {
                Queue<Thing> q = kv.Value;
                int processed = 0;
                while (q.Count > 0 && processed < MaxPerPass)
                {
                    Thing t = q.Dequeue();
                    processed++;
                    // 单个污渍销毁失败（例如已被别的 Mod 拿走）不应该中止整批。
                    try
                    {
                        if (t != null && !t.Destroyed && t.Spawned)
                            t.Destroy(DestroyMode.Vanish);
                    }
                    catch (System.Exception e)
                    {
                        SafeLog.Report("销毁污渍", e);
                    }
                }
                if (q.Count == 0)
                {
                    if (finished == null)
                        finished = new List<int>();
                    finished.Add(kv.Key);
                }
            }

            if (finished != null)
            {
                for (int i = 0; i < finished.Count; i++)
                    pendingByMap.Remove(finished[i]);
            }
        }

        private static Queue<Thing> QueueFor(Map map)
        {
            Queue<Thing> q;
            if (!pendingByMap.TryGetValue(map.uniqueID, out q))
            {
                q = new Queue<Thing>();
                pendingByMap[map.uniqueID] = q;
            }
            return q;
        }

        private static int EnqueueFilth(Map map, bool homeOnly)
        {
            List<Thing> filth = map.listerThings.ThingsInGroup(ThingRequestGroup.Filth);
            if (filth.Count == 0)
                return 0;

            Queue<Thing> q = QueueFor(map);
            int added = 0;
            for (int i = 0; i < filth.Count; i++)
            {
                Thing t = filth[i];
                if (t == null || t.Destroyed || !t.Spawned)
                    continue;
                if (homeOnly && !map.areaManager.Home[t.Position])
                    continue;
                q.Enqueue(t);
                added++;
            }
            return added;
        }

        /// <summary>设置页里「立即清扫一次」用，直接清完，不分批。</summary>
        public static int CleanNow()
        {
            if (Current.Game == null || Find.Maps == null)
                return 0;

            AutoCleanSettings s = AutoCleanMod.Settings;
            bool homeOnly = s == null || s.homeOnly;
            int total = 0;
            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                Map map = maps[i];
                if (map == null || !map.IsPlayerHome)
                    continue;
                total += CleanFilthNow(map, homeOnly);
                lastCleanTickByMap[map.uniqueID] = GenTicks.TicksAbs;
            }
            return total;
        }

        private static int CleanFilthNow(Map map, bool homeOnly)
        {
            List<Thing> filth = map.listerThings.ThingsInGroup(ThingRequestGroup.Filth);
            if (filth.Count == 0)
                return 0;

            // 必须复制一份再删，否则会边遍历边改集合。
            List<Thing> snapshot = new List<Thing>(filth);
            int removed = 0;
            for (int i = 0; i < snapshot.Count; i++)
            {
                Thing t = snapshot[i];
                if (t == null || t.Destroyed || !t.Spawned)
                    continue;
                try
                {
                    if (homeOnly && !map.areaManager.Home[t.Position])
                        continue;
                    t.Destroy(DestroyMode.Vanish);
                    removed++;
                }
                catch (System.Exception e)
                {
                    SafeLog.Report("立即清扫", e);
                }
            }
            return removed;
        }
    }
}
