using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Shuo.WorkSorter
{
    /// <summary>
    /// 跟随存档的状态：哪些格子被玩家锁了、上次自动刷新是什么时候。
    /// 放在 GameComponent 里而不是 Mod 设置里，是为了让「锁定」跟存档走——
    /// 换个殖民地不该继承上一局的锁定。
    /// </summary>
    public class WorkSorterGameComponent : GameComponent
    {
        // 锁定用 long 而不是字符串：表格每帧要查几百次，
        // 拼 "12345:Growing" 这种 key 会持续制造垃圾。
        private HashSet<long> lockedPawns = new HashSet<long>();
        private HashSet<long> lockedCells = new HashSet<long>();

        // 整列锁定（按 WorkTypeDef 的 defNameHash）。有些工作是玩家想亲手管的，
        // 逐格锁太麻烦，锁一列更合适。
        private HashSet<long> lockedWorkTypes = new HashSet<long>();

        public int lastApplyTick = -1;
        public int lastChanged;

        /// <summary>
        /// 玩家是否已经手动应用过一次。装好 Mod 之后不该第二天就悄悄把
        /// 现有排班全改了——必须先亲眼看过预览、按下「应用全部」，
        /// 自动排序才开始按周期接手。
        /// </summary>
        public bool appliedOnce;

        public WorkSorterGameComponent()
        {
        }

        public static WorkSorterGameComponent Instance
        {
            get
            {
                Game g = Verse.Current.Game;
                if (g == null)
                    return null;
                return g.GetComponent<WorkSorterGameComponent>();
            }
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref lockedPawns, "lockedPawns", LookMode.Value);
            Scribe_Collections.Look(ref lockedCells, "lockedCells", LookMode.Value);
            Scribe_Collections.Look(ref lockedWorkTypes, "lockedWorkTypes", LookMode.Value);
            Scribe_Values.Look(ref lastApplyTick, "lastApplyTick", -1);
            Scribe_Values.Look(ref lastChanged, "lastChanged", 0);
            Scribe_Values.Look(ref appliedOnce, "appliedOnce", false);

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (lockedPawns == null) lockedPawns = new HashSet<long>();
                if (lockedCells == null) lockedCells = new HashSet<long>();
                if (lockedWorkTypes == null) lockedWorkTypes = new HashSet<long>();
            }
        }

        public override void LoadedGame()
        {
            // 读档后从这一刻重新计时，不要一进游戏就把玩家手调的表覆盖掉。
            if (lastApplyTick < 0)
                lastApplyTick = Find.TickManager.TicksGame;
        }

        private static long CellKey(Pawn p, WorkTypeDef w)
        {
            // 低 32 位放 defName 的稳定哈希而不是 shortHash：shortHash 只有 16 位，
            // 工作类型一多（尤其装了一堆 Mod）撞车的概率不低，撞了就会误锁别人的格子。
            return ((long)(uint)p.thingIDNumber << 32) | (uint)w.defNameHash;
        }

        public bool IsPawnLocked(Pawn p)
        {
            return p != null && lockedPawns.Contains(p.thingIDNumber);
        }

        public void TogglePawnLock(Pawn p)
        {
            if (p == null) return;
            if (!lockedPawns.Remove(p.thingIDNumber))
                lockedPawns.Add(p.thingIDNumber);
        }

        public bool IsCellLocked(Pawn p, WorkTypeDef w)
        {
            return p != null && w != null && lockedCells.Contains(CellKey(p, w));
        }

        public void LockCell(Pawn p, WorkTypeDef w)
        {
            if (p != null && w != null)
                lockedCells.Add(CellKey(p, w));
        }

        public void UnlockCell(Pawn p, WorkTypeDef w)
        {
            if (p != null && w != null)
                lockedCells.Remove(CellKey(p, w));
        }

        public bool IsWorkTypeLocked(WorkTypeDef w)
        {
            return w != null && lockedWorkTypes.Contains((long)(uint)w.defNameHash);
        }

        public void ToggleWorkTypeLock(WorkTypeDef w)
        {
            if (w == null) return;
            long key = (long)(uint)w.defNameHash;
            if (!lockedWorkTypes.Remove(key))
                lockedWorkTypes.Add(key);
        }

        public override void GameComponentTick()
        {
            try
            {
                TickAuto();
            }
            catch (Exception e)
            {
                SafeLog.Report("自动刷新", e);
            }
        }

        private void TickAuto()
        {
            WorkSorterSettings s = WorkSorterMod.Settings;
            if (s == null || !s.autoApply)
                return;

            // 没被手动应用过之前，自动排序一律不接管（见 appliedOnce 的注释）。
            if (!appliedOnce)
                return;

            // 每 2048 tick（约 34 秒）检查一次就够了。真正的时间判断在下面，
            // 这里只是避免每帧都去读 TicksGame 和设置。
            int now = Find.TickManager.TicksGame;
            if ((now & 0x7FF) != 0)
                return;

            if (lastApplyTick < 0)
            {
                lastApplyTick = now;
                return;
            }

            long interval = (long)Mathf.Max(1, s.intervalDays) * 60000L;
            if ((long)now - lastApplyTick < interval)
                return;

            // 分基地和主基地各有各的人，都要排。星图阶段 Find.Maps 里没有
            // 玩家定居点，循环自然什么都不做，不会白白消耗一次刷新窗口。
            int changed = 0;
            int homes = 0;
            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                Map m = maps[i];
                if (m == null || !m.IsPlayerHome)
                    continue;
                homes++;
                changed += WorkSorterApplier.ApplyAll(m, false, false);
            }

            if (homes == 0)
                return;

            lastApplyTick = now;
            lastChanged = changed;

            if (changed > 0)
            {
                Log.Message("[WorkSorter] 自动刷新完成：调整 " + changed + " 格工作优先级。");
                if (s.notifyOnApply)
                    Messages.Message("工作优先级已自动更新：" + changed + " 格。",
                        MessageTypeDefOf.NeutralEvent, false);
            }
        }
    }
}
