using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Shuo.WorkSorter
{
    /// <summary>
    /// 需求侧的原始测量结果。这一层可以缓存——它只跟地图状态有关，
    /// 不随「谁排第几」变化。缓存它是因为其中「伤员数」要遍历全图 pawn，
    /// 面板每 4 秒重建一次预览时没必要重算。
    /// </summary>
    internal sealed class RawDemand
    {
        public float baseImportance;
        public int backlogCount;
        public string backlogLabel;
        public float backlog;
    }

    /// <summary>
    /// 基地需求评估：把「地图上到底堆了多少事」换算成 0..1 的权重。
    /// 指标必须便宜：只用 listerThings / designationManager 里现成的列表长度，
    /// 唯一需要遍历的是伤员统计，所以整张表按 25 秒缓存一次。
    /// </summary>
    internal static class WorkSorterDemand
    {
        private const int CacheTicks = 1500;   // 25 秒

        // 积压到这个数量就算「满负荷」。取对数压缩，1 个和 100 个不至于差 100 倍。
        private const float HaulFull = 30f;
        private const float ConstructionFull = 25f;
        private const float MiningFull = 40f;
        private const float PlantCutFull = 30f;
        private const float HuntFull = 8f;
        private const float CleanFull = 80f;
        private const float PatientFull = 4f;
        private const float HarvestFull = 40f;

        private static Dictionary<WorkTypeDef, RawDemand> cache;
        private static Game cacheGame;
        private static int cacheMapId = -1;
        private static int cacheTick = int.MinValue;

        public static void InvalidateCache()
        {
            cache = null;
            cacheGame = null;
            cacheMapId = -1;
            cacheTick = int.MinValue;
        }

        public static Dictionary<WorkTypeDef, DemandEntry> Build(Map map, List<WorkTypeDef> workTypes)
        {
            Dictionary<WorkTypeDef, RawDemand> raw = MeasureCached(map, workTypes);

            // 每次克隆成新的 DemandEntry：下游会往 de 里写 capableCount 和最终的
            // demand，直接复用缓存对象会把这些值带进下一次计算。
            Dictionary<WorkTypeDef, DemandEntry> result =
                new Dictionary<WorkTypeDef, DemandEntry>(workTypes.Count);

            for (int i = 0; i < workTypes.Count; i++)
            {
                WorkTypeDef w = workTypes[i];
                RawDemand rd;
                if (!raw.TryGetValue(w, out rd))
                    continue;

                DemandEntry de = new DemandEntry();
                de.workType = w;
                de.baseImportance = rd.baseImportance;
                de.backlogCount = rd.backlogCount;
                de.backlogLabel = rd.backlogLabel;
                de.backlog = rd.backlog;
                de.demand = de.baseImportance + de.backlog * 0.55f;

                result[w] = de;
            }

            return result;
        }

        private static Dictionary<WorkTypeDef, RawDemand> MeasureCached(Map map, List<WorkTypeDef> workTypes)
        {
            int now = Find.TickManager.TicksGame;
            long age = (long)now - cacheTick;

            if (cache != null
                && ReferenceEquals(cacheGame, Verse.Current.Game)
                && cacheMapId == map.uniqueID
                && cache.Count == workTypes.Count
                && age >= 0 && age < CacheTicks)
                return cache;

            Dictionary<WorkTypeDef, RawDemand> result =
                new Dictionary<WorkTypeDef, RawDemand>(workTypes.Count);

            for (int i = 0; i < workTypes.Count; i++)
                result[workTypes[i]] = Measure(map, workTypes[i]);

            cache = result;
            cacheGame = Verse.Current.Game;
            cacheMapId = map.uniqueID;
            cacheTick = now;
            return result;
        }

        private static RawDemand Measure(Map map, WorkTypeDef w)
        {
            RawDemand rd = new RawDemand();

            // 原版的 naturalPriority 本身就是设计者给的工作重要度排序
            // （消防 1400 最高，研究 100 最低），直接拿来当基线，
            // 顺带自动支持其它 Mod 新增的工作类型。
            rd.baseImportance = Mathf.Clamp01(w.naturalPriority / 1400f);

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
                        rd.backlog = 0.5f;
                        // 研究的 naturalPriority 只有 100（原版认为它最不紧急），
                        // 但基地稳定下来总得有人推进。把基线抬到和有活可干的中等
                        // 工作一个水平，否则研究永远没人排第一档。
                        rd.baseImportance = Mathf.Max(rd.baseImportance, 0.45f);
                    }
                    rd.backlogCount = 0;
                    rd.backlogLabel = label;
                    return rd;

                default:
                    // 测不出来的工作（烹饪、艺术、社交…）不额外加权，只吃固有序位。
                    rd.backlog = 0f;
                    rd.backlogCount = 0;
                    rd.backlogLabel = null;
                    return rd;
            }

            rd.backlogCount = count;
            rd.backlogLabel = label;
            rd.backlog = LogScale(count, full);
            return rd;
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
