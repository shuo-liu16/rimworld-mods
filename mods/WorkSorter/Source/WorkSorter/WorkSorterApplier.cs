using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Shuo.WorkSorter
{
    /// <summary>
    /// 把算好的方案写回游戏。所有写操作都收在这里，方便统一做边界检查：
    /// 死亡 / 未初始化 / 被原版禁用 / 被玩家锁定 的情况一律跳过。
    /// </summary>
    public static class WorkSorterApplier
    {
        /// <summary>
        /// 重算并立即应用。返回实际改动的格子数。
        /// userInitiated 区分「玩家亲手按的」和「定时器触发的」——
        /// 只有前者才会把定时自动排序解锁。
        /// </summary>
        public static int ApplyAll(Map map, bool userInitiated, bool notify)
        {
            if (map == null)
                return 0;

            WorkSorterPlan plan = WorkSorterEngine.Build(map);
            return ApplyPlan(plan, userInitiated, notify);
        }

        public static int ApplyPlan(WorkSorterPlan plan, bool userInitiated, bool notify)
        {
            if (plan == null)
                return 0;

            WorkSorterSettings s = WorkSorterMod.Settings;
            bool takeOverDisabled = s != null && s.takeOverDisabled;
            WorkSorterGameComponent comp = WorkSorterGameComponent.Instance;

            int changed = 0;

            for (int pi = 0; pi < plan.pawns.Count; pi++)
            {
                Pawn p = plan.pawns[pi];
                if (p == null || p.Dead || p.workSettings == null)
                    continue;

                if (comp != null && comp.IsPawnLocked(p))
                    continue;

                Dictionary<WorkTypeDef, WorkSorterCell> row;
                if (!plan.cells.TryGetValue(p, out row))
                    continue;

                try
                {
                    // 老存档里新加入的小人可能还没初始化工作表，这里顺手补上。
                    p.workSettings.EnableAndInitializeIfNotAlreadyInitialized();

                    foreach (WorkSorterCell c in row.Values)
                    {
                        if (c.suggested <= 0)
                            continue;
                        if (p.WorkTypeIsDisabled(c.workType))
                            continue;
                        if (comp != null && comp.IsWorkTypeLocked(c.workType))
                            continue;
                        if (comp != null && comp.IsCellLocked(p, c.workType))
                            continue;

                        int cur = p.workSettings.GetPriority(c.workType);
                        if (cur == c.suggested)
                            continue;

                        // priority 0 = 这一项没启用。分不清是「从没启用」还是
                        // 「玩家特意关的」，所以默认一律不动——宁可少做，不要擅自
                        // 把玩家关掉的活重新打开。
                        if (cur <= 0 && !takeOverDisabled)
                            continue;

                        p.workSettings.SetPriority(c.workType, c.suggested);
                        c.current = c.suggested;
                        changed++;
                    }
                }
                catch (Exception e)
                {
                    // 异常处理里再抛异常会直接冒到游戏主循环，所以取名字也要兜住。
                    string who = "?";
                    try { who = p.LabelShort; } catch { }
                    SafeLog.Report("写入 " + who, e);
                }
            }

            if (notify && changed > 0 && Verse.Current.Game != null)
                Messages.Message("工作优先级已更新：" + changed + " 格。",
                    MessageTypeDefOf.NeutralEvent, false);

            // 玩家亲自按过「应用全部」了，之后才允许定时自动排序接手。
            if (comp != null && userInitiated)
                comp.appliedOnce = true;

            return changed;
        }

        /// <summary>
        /// 单独写一格。用于玩家在面板里点格子——点了就写，并顺带锁定。
        /// </summary>
        public static bool ApplyCell(Pawn p, WorkTypeDef w, int priority)
        {
            if (p == null || w == null || p.workSettings == null || p.Dead)
                return false;
            if (p.WorkTypeIsDisabled(w))
                return false;

            int pr = Mathf.Clamp(priority, 1, WorkSorterEngine.MaxPriority);
            try
            {
                p.workSettings.EnableAndInitializeIfNotAlreadyInitialized();
                p.workSettings.SetPriority(w, pr);
                return true;
            }
            catch (Exception e)
            {
                SafeLog.Report("写入单格", e);
                return false;
            }
        }
    }
}
