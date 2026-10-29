using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Shuo.WorkSorter
{
    /// <summary>
    /// 基地需求评估：把「地图上到底堆了多少事」换算成 0..1 的权重。
    /// 这一步是整套排序里唯一读地图状态的部分，所以指标必须便宜：
    /// 只用 listerThings / designationManager 里现成的列表长度，不做遍历统计。
    /// </summary>
    internal static class WorkSorterDemand
    {
        // 积压到这个数量就算「满负荷」。取对数压缩，1 个和 100 个不至于差 100 倍。
        private const float HaulFull = 30f;
        private const float ConstructionFull = 25f;
        private const float MiningFull = 40f;
        private const float PlantCutFull = 30f;
        private const float HuntFull = 8f;
        private const float CleanFull = 80f;
        private const float PatientFull = 4f;
        private const float HarvestFull = 40f;

        public static Dictionary<WorkTypeDef, DemandEntry> Build(Map map, List<WorkTypeDef> workTypes)
        {
            Dictionary<WorkTypeDef, DemandEntry> result =
                new Dictionary<WorkTypeDef, DemandEntry>();

            for (int i = 0; i < workTypes.Count; i++)
            {
                WorkTypeDef w = workTypes[i];
                DemandEntry de = new DemandEntry();
                de.workType = w;
                // 原版的 naturalPriority 本身就是设计者给的工作重要度排序
                // （消防 1400 最高，研究 100 最低），直接拿来当基线，
                // 顺带自动支持其它 Mod 新增的工作类型。
                de.baseImportance = Mathf.Clamp01(w.naturalPriority / 1400f);

                MeasureBacklog(map, w, de);
                de.demand = de.baseImportance + de.backlog * 0.55f;
                result[w] = de;
            }

            return result;
        }

        private static void MeasureBacklog(Map map, WorkTypeDef w, DemandEntry de)
        {
            int count = 0;
            float full = 1f;
            string label = null;

            switch (w.defName)
            {
                case "Hauling":
                    label = "待搬运物品";
                    full = HaulFull;
                    if (map.listerHaulables != null)
                    {
                        ICollection<Thing> things = map.listerHaulables.ThingsPotentiallyNeedingHauling();
                        if (things != null)
                            count = things.Count;
                    }
                    break;

                case "Construction":
                    label = "待建 / 待拆";
                    full = ConstructionFull;
                    count = CountThings(map, ThingRequestGroup.Blueprint)
                          + CountThings(map, ThingRequestGroup.BuildingFrame)
                          + CountDesig(map, DesignationDefOf.Deconstruct)
                          + CountDesig(map, DesignationDefOf.Uninstall);
                    break;

                case "Mining":
                    label = "采矿标记";
                    full = MiningFull;
                    count = CountDesig(map, DesignationDefOf.Mine)
                          + CountDesig(map, DesignationDefOf.MineVein);
                    break;

                case "PlantCutting":
                    label = "砍伐 / 采收标记";
                    full = PlantCutFull;
                    count = CountDesig(map, DesignationDefOf.CutPlant)
                          + CountDesig(map, DesignationDefOf.HarvestPlant);
                    break;

                case "Hunting":
                    label = "狩猎标记";
                    full = HuntFull;
                    count = CountDesig(map, DesignationDefOf.Hunt);
                    break;

                case "Cleaning":
                    label = "污渍";
                    full = CleanFull;
                    count = CountThings(map, ThingRequestGroup.Filth);
                    break;

                case "Doctor":
                    label = "伤员";
                    full = PatientFull;
                    count = CountPatients(map);
                    break;

                case "Growing":
                    label = "成熟待收作物";
                    full = HarvestFull;
                    count = CountThings(map, ThingRequestGroup.HarvestablePlant);
                    break;

                case "Research":
                    label = "进行中的研究";
                    if (Find.ResearchManager != null && Find.ResearchManager.AnyProjectIsAvailable)
                    {
                        de.backlog = 0.5f;
                        // 研究的 naturalPriority 只有 100（原版认为它最不紧急），
                        // 但基地稳定下来总得有人推进。把基线抬到和有活可干的中等
                        // 工作一个水平，否则研究永远没人排第一档。
                        de.baseImportance = Mathf.Max(de.baseImportance, 0.45f);
                    }
                    de.backlogCount = 0;
                    de.backlogLabel = label;
                    return;

                default:
                    // 测不出来的工作（烹饪、艺术、社交…）不额外加权，只吃固有序位。
                    de.backlog = 0f;
                    de.backlogCount = 0;
                    de.backlogLabel = null;
                    return;
            }

            de.backlogCount = count;
            de.backlogLabel = label;
            de.backlog = LogScale(count, full);
        }

        private static float LogScale(int count, float full)
        {
            if (count <= 0)
                return 0f;
            return Mathf.Clamp01(Mathf.Log(1f + count) / Mathf.Log(1f + full));
        }

        private static int CountThings(Map map, ThingRequestGroup group)
        {
            if (map == null || map.listerThings == null)
                return 0;
            List<Thing> list = map.listerThings.ThingsInGroup(group);
            return list == null ? 0 : list.Count;
        }

        private static int CountDesig(Map map, DesignationDef def)
        {
            if (map == null || map.designationManager == null || def == null)
                return 0;

            // DefMap 的索引器在「一条标记都没有」时返回 null，不能直接 .Count。
            List<Designation> list = map.designationManager.designationsByDef[def];
            return list == null ? 0 : list.Count;
        }

        /// <summary>
        /// 需要治疗或已经倒地的人数。只数自己人：囚犯和敌人由看守/医生
        /// 按原版逻辑处理，不该影响殖民地自己的工作排序。
        /// </summary>
        private static int CountPatients(Map map)
        {
            if (map == null)
                return 0;

            int n = 0;
            IReadOnlyList<Pawn> all = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < all.Count; i++)
            {
                Pawn p = all[i];
                if (p == null || p.Dead || p.Faction != Faction.OfPlayer)
                    continue;
                if (p.RaceProps == null || !p.RaceProps.Humanlike)
                    continue;

                if (p.Downed)
                {
                    n++;
                    continue;
                }

                if (p.health != null && p.health.hediffSet != null)
                {
                    foreach (Hediff h in p.health.hediffSet.GetHediffsTendable())
                    {
                        if (h != null)
                        {
                            n++;
                            break;
                        }
                    }
                }
            }
            return n;
        }
    }
}
