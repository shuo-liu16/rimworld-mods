using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace Shuo.WorkSorter
{
    /// <summary>
    /// 一个「某人 × 某工作」的评分单元。所有字段都在重算时算好，
    /// 绘制阶段只读，不做任何计算。
    /// </summary>
    public sealed class WorkSorterCell
    {
        public Pawn pawn;
        public WorkTypeDef workType;

        public float fit;        // 综合适配度，0 起步，理论上可到 1.2
        public float skill01;    // 技能归一化 0..1
        public float passion01;  // 热情归一化 0..1
        public float speed01;    // 工作速度归一化 0..1
        public float cap01;      // 相关身体能力 0..1
        public float back;       // 职业背景加成 0..0.25

        public float avgSkill;   // 加权平均技能等级（0..20）
        public int passion;      // 0 无 / 1 有兴趣 / 2 狂热
        public float speedValue; // 实际工作速度倍率（1.0 = 100%）

        public float demand;     // 该工作当前的基地需求
        public int current;      // 游戏内现有优先级（0 = 未启用）
        public int suggested;    // 本模组建议的优先级（0 = 保持原样）
        public bool locked;

        public string reason;    // 悬浮提示里的分数明细
    }

    /// <summary>
    /// 一个工作类型的需求评估。
    /// </summary>
    public sealed class DemandEntry
    {
        public WorkTypeDef workType;
        public float baseImportance;  // 原版 naturalPriority / 1400
        public int backlogCount;      // 地图上待办数量
        public string backlogLabel;
        public float backlog;         // 待办归一化 0..1
        public int capableCount;      // 能做这活的人数
        public float demand;          // 综合需求
    }

    public sealed class WorkSorterPlan
    {
        public readonly List<Pawn> pawns = new List<Pawn>();
        public readonly List<WorkTypeDef> workTypes = new List<WorkTypeDef>();
        public readonly Dictionary<Pawn, Dictionary<WorkTypeDef, WorkSorterCell>> cells =
            new Dictionary<Pawn, Dictionary<WorkTypeDef, WorkSorterCell>>();
        public Dictionary<WorkTypeDef, DemandEntry> demands =
            new Dictionary<WorkTypeDef, DemandEntry>();

        public int builtTick;

        public WorkSorterCell Cell(Pawn p, WorkTypeDef w)
        {
            Dictionary<WorkTypeDef, WorkSorterCell> row;
            if (!cells.TryGetValue(p, out row))
                return null;
            WorkSorterCell c;
            return row.TryGetValue(w, out c) ? c : null;
        }
    }

    /// <summary>
    /// 评分与分配。纯计算，不写游戏状态——写回在 WorkSorterApplier 里。
    /// </summary>
    public static class WorkSorterEngine
    {
        // 各因子权重。加起来不到 1，剩下的空间留给背景加成。
        private const float WSkill = 0.40f;
        private const float WPassion = 0.18f;
        private const float WSpeed = 0.20f;
        private const float WCapacity = 0.15f;

        private const float MaxBackstoryBonus = 0.25f;

        public static WorkSorterPlan Build(Map map)
        {
            WorkSorterPlan plan = new WorkSorterPlan();
            plan.builtTick = Find.TickManager.TicksGame;

            // —— 1. 参评的殖民者 ——
            // FreeColonistsSpawned 含倒地者：病人也要有「就医 / 卧床」，不该被排除。
            // 奴隶单列一张表，但同样要排班——他们干的活和自由民没区别。
            // 机械族不在这里：它们的工作由机械师控制组管理，按 1-4 排会打架。
            AddPawns(plan, map.mapPawns.FreeColonistsSpawned);
            AddPawns(plan, map.mapPawns.SlavesOfColonySpawned);

            // —— 2. 参评的工作类型 ——
            // VisibleCurrently 会把当前环境下不该出现的列（DLC 未启用等）过滤掉，
            // 免得表格里出现一列没人能做的空工作。
            List<WorkTypeDef> all = DefDatabase<WorkTypeDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                WorkTypeDef w = all[i];
                if (!w.visible || !w.VisibleCurrently)
                    continue;
                plan.workTypes.Add(w);
            }
            plan.workTypes.Sort((a, b) => b.naturalPriority.CompareTo(a.naturalPriority));

            // —— 3. 需求表 ——
            plan.demands = WorkSorterDemand.Build(map, plan.workTypes);

            // —— 4. 逐格打分 ——
            for (int pi = 0; pi < plan.pawns.Count; pi++)
            {
                Pawn p = plan.pawns[pi];
                Dictionary<WorkTypeDef, WorkSorterCell> row = plan.cells[p];

                for (int wi = 0; wi < plan.workTypes.Count; wi++)
                {
                    WorkTypeDef w = plan.workTypes[wi];
                    DemandEntry de;
                    if (!plan.demands.TryGetValue(w, out de))
                        continue;

                    // 原版自己就认为这人干不了（技能被特性锁死、意识形态禁止、
                    // 背景禁止、身体缺陷导致不可胜任），那就不参与排序。
                    if (p.WorkTypeIsDisabled(w))
                        continue;

                    WorkSorterCell c = Score(p, w, de);
                    if (c == null)
                        continue;

                    row[w] = c;
                    de.capableCount++;
                }
            }

            // —— 5. 需求补上「稀缺度」：能干的人越少，越该由这几个人顶上 ——
            foreach (KeyValuePair<WorkTypeDef, DemandEntry> kv in plan.demands)
            {
                DemandEntry de = kv.Value;
                float scarcity = Mathf.Clamp01(1f - de.capableCount / 4f) * 0.25f;
                de.demand = de.baseImportance + de.backlog * 0.55f + scarcity;
            }

            // 把最终需求回填到格子上，供排序与提示使用
            foreach (Dictionary<WorkTypeDef, WorkSorterCell> row in plan.cells.Values)
            {
                foreach (WorkSorterCell c in row.Values)
                {
                    DemandEntry de;
                    if (plan.demands.TryGetValue(c.workType, out de))
                        c.demand = de.demand;
                }
            }

            // —— 6. 分配优先级 ——
            Assign(plan);

            // —— 7. 生成可读的解释 ——
            foreach (Dictionary<WorkTypeDef, WorkSorterCell> row in plan.cells.Values)
            {
                foreach (WorkSorterCell c in row.Values)
                {
                    DemandEntry de;
                    plan.demands.TryGetValue(c.workType, out de);
                    c.reason = BuildReason(c, de);
                }
            }

            return plan;
        }

        private static void AddPawns(WorkSorterPlan plan, List<Pawn> source)
        {
            if (source == null)
                return;

            for (int i = 0; i < source.Count; i++)
            {
                Pawn p = source[i];
                if (p == null || p.Dead || p.workSettings == null)
                    continue;
                if (plan.cells.ContainsKey(p))
                    continue;

                plan.pawns.Add(p);
                plan.cells[p] = new Dictionary<WorkTypeDef, WorkSorterCell>();
            }
        }

        /// <summary>
        /// 单人单工作的适配度。返回 null 表示这个人根本不该碰这活。
        /// </summary>
        private static WorkSorterCell Score(Pawn p, WorkTypeDef w, DemandEntry de)
        {
            if (p.skills == null)
                return null;

            WorkSorterCell c = new WorkSorterCell();
            c.pawn = p;
            c.workType = w;
            c.demand = de.demand;

            // 技能：多个相关技能按「有热情的那项权重更高」加权平均。
            // 这样"医疗 15 但无热情 + 社交 2 无热情"不会被平均成 8.5 那么好看。
            float weighted = 0f;
            float weight = 0f;
            int maxPassion = 0;

            if (w.relevantSkills != null)
            {
                for (int i = 0; i < w.relevantSkills.Count; i++)
                {
                    SkillRecord sr = p.skills.GetSkill(w.relevantSkills[i]);
                    if (sr == null)
                        continue;
                    if (sr.TotallyDisabled)
                        return null;

                    float wgt = 1f + 0.15f * (int)sr.passion;
                    weighted += sr.Level * wgt;
                    weight += wgt;

                    if ((int)sr.passion > maxPassion)
                        maxPassion = (int)sr.passion;
                }
            }

            // 无技能要求的工作（搬运、清洁）给一个中性底分，
            // 否则它们会因为 skill01 = 0 永远排在最后。
            c.avgSkill = weight > 0f ? weighted / weight : 6f;
            c.skill01 = Mathf.Clamp01(c.avgSkill / 18f);
            c.passion = maxPassion;
            c.passion01 = maxPassion >= 2 ? 1f : maxPassion == 1 ? 0.5f : 0f;

            c.speedValue = WorkSpeed(p, w);
            c.speed01 = Mathf.Clamp01((c.speedValue - 0.55f) / 1.35f);

            c.cap01 = CapacityFit(p, w);
            c.back = BackstoryFit(p, w);

            c.fit = WSkill * c.skill01
                  + WPassion * c.passion01
                  + WSpeed * c.speed01
                  + WCapacity * c.cap01
                  + c.back;

            c.current = p.workSettings.GetPriority(w);
            c.suggested = 0;
            return c;
        }

        /// <summary>
        /// 实际工作速度。优先用该工作对应的专属速度属性（施工、采矿、研究各有各的），
        /// 再和全局工作速度做几何平均——这样仿生、基因、伤病都能反映进来。
        /// </summary>
        private static float WorkSpeed(Pawn p, WorkTypeDef w)
        {
            float global = SafeStat(p, StatDefOf.WorkSpeedGlobal);
            StatDef specific = SpecificSpeedStat(w);
            if (specific == null)
                return global;

            float sp = SafeStat(p, specific);
            return Mathf.Sqrt(Mathf.Max(0.05f, sp * global));
        }

        private static StatDef SpecificSpeedStat(WorkTypeDef w)
        {
            WorkTags t = w.workTags;
            if ((t & WorkTags.Intellectual) != 0) return StatDefOf.ResearchSpeed;
            if ((t & WorkTags.Constructing) != 0) return StatDefOf.ConstructionSpeed;
            if ((t & WorkTags.Mining) != 0) return StatDefOf.MiningSpeed;
            if ((t & WorkTags.PlantWork) != 0) return StatDefOf.PlantWorkSpeed;
            if ((t & WorkTags.Cleaning) != 0) return StatDefOf.CleaningSpeed;
            if ((t & WorkTags.Caring) != 0) return StatDefOf.MedicalTendSpeed;
            if ((t & (WorkTags.Crafting | WorkTags.Artistic)) != 0) return StatDefOf.GeneralLaborSpeed;
            return null;
        }

        private static float SafeStat(Pawn p, StatDef def)
        {
            try
            {
                if (def == null)
                    return 1f;
                float v = p.GetStatValue(def);
                return v <= 0.01f ? 0.01f : v;
            }
            catch (Exception e)
            {
                SafeLog.Report("读取属性 " + (def == null ? "?" : def.defName), e);
                return 1f;
            }
        }

        /// <summary>
        /// 身体能力。按工作标签挑出真正用得到的能力取最小值：
        /// 手断了就别指望精细活，走不动就别指望搬运。
        /// </summary>
        private static float CapacityFit(Pawn p, WorkTypeDef w)
        {
            if (p.health == null || p.health.capacities == null)
                return 1f;

            WorkTags t = w.workTags;
            float f = 1f;

            if ((t & (WorkTags.ManualDumb | WorkTags.ManualSkilled | WorkTags.Crafting
                      | WorkTags.Cooking | WorkTags.Artistic)) != 0)
                f = Mathf.Min(f, Cap(p, PawnCapacityDefOf.Manipulation));

            if ((t & (WorkTags.Hauling | WorkTags.Cleaning | WorkTags.Constructing
                      | WorkTags.Mining | WorkTags.PlantWork)) != 0)
                f = Mathf.Min(f, Cap(p, PawnCapacityDefOf.Moving));

            if ((t & (WorkTags.Shooting | WorkTags.Hunting)) != 0)
                f = Mathf.Min(f, Cap(p, PawnCapacityDefOf.Sight));

            if ((t & WorkTags.Social) != 0)
                f = Mathf.Min(f, Cap(p, PawnCapacityDefOf.Talking));

            if ((t & WorkTags.Caring) != 0)
                f = Mathf.Min(f, Mathf.Min(Cap(p, PawnCapacityDefOf.Manipulation),
                                           Cap(p, PawnCapacityDefOf.Sight)));

            if ((t & WorkTags.Intellectual) != 0)
                f = Mathf.Min(f, Cap(p, PawnCapacityDefOf.Consciousness));

            return Mathf.Clamp01(f);
        }

        private static float Cap(Pawn p, PawnCapacityDef def)
        {
            if (def == null)
                return 1f;
            return Mathf.Clamp01(p.health.capacities.GetLevel(def));
        }

        /// <summary>
        /// 职业背景里的技能加成。原版创建小人时就按背景给技能，
        /// 所以「成年职业是矿工」这条信息值得单独加一点分，
        /// 否则一个 20 级厨师被拉去挖矿会被算成和矿工一样好。
        /// </summary>
        private static float BackstoryFit(Pawn p, WorkTypeDef w)
        {
            if (p.story == null || w.relevantSkills == null || w.relevantSkills.Count == 0)
                return 0f;

            float sum = 0f;
            List<BackstoryDef> all = p.story.AllBackstories;
            if (all == null)
                return 0f;

            for (int i = 0; i < all.Count; i++)
            {
                BackstoryDef bs = all[i];
                if (bs == null || bs.skillGains == null)
                    continue;
                for (int j = 0; j < bs.skillGains.Count; j++)
                {
                    SkillGain g = bs.skillGains[j];
                    if (g == null || g.skill == null)
                        continue;
                    if (w.relevantSkills.Contains(g.skill))
                        sum += g.amount;
                }
            }

            return Mathf.Clamp01(sum / 12f) * MaxBackstoryBonus;
        }

        /// <summary>
        /// 两阶段分配。
        /// 阶段一：按需求从高到低，替每个工作挑出最合适的一到三个人排第一档。
        /// 阶段二：剩下的工作按各人适配度排成 2/3/4 档，形成「最爱做的事先做」的顺序。
        /// 这样既保证关键岗位有人，又不会所有人都把同一件事当作第一优先。
        /// </summary>
        private static void Assign(WorkSorterPlan plan)
        {
            WorkSorterSettings s = WorkSorterMod.Settings;
            int maxTopPerPawn = s == null ? 3 : Mathf.Clamp(s.maxTopPerPawn, 1, 8);

            Dictionary<Pawn, int> topCount = new Dictionary<Pawn, int>();
            for (int i = 0; i < plan.pawns.Count; i++)
                topCount[plan.pawns[i]] = 0;

            List<DemandEntry> byDemand = new List<DemandEntry>(plan.demands.Values);
            byDemand.Sort((a, b) => b.demand.CompareTo(a.demand));

            for (int di = 0; di < byDemand.Count; di++)
            {
                DemandEntry de = byDemand[di];
                if (de.capableCount <= 0)
                    continue;

                List<WorkSorterCell> cands = new List<WorkSorterCell>();
                for (int pi = 0; pi < plan.pawns.Count; pi++)
                {
                    WorkSorterCell c = plan.Cell(plan.pawns[pi], de.workType);
                    if (c != null)
                        cands.Add(c);
                }
                if (cands.Count == 0)
                    continue;

                cands.Sort((a, b) => b.fit.CompareTo(a.fit));

                // 消防、就医、卧床这三类是原版 alwaysStartActive 的工作：
                // 它们本来就该全员第一档，不占「三个名额」。
                if (de.workType.alwaysStartActive)
                {
                    for (int i = 0; i < cands.Count; i++)
                        cands[i].suggested = 1;
                    continue;
                }

                int slots = SlotsFor(de, cands.Count);
                int done = 0;
                for (int i = 0; i < cands.Count && done < slots; i++)
                {
                    WorkSorterCell c = cands[i];
                    int had;
                    topCount.TryGetValue(c.pawn, out had);
                    if (had >= maxTopPerPawn)
                        continue;

                    c.suggested = 1;
                    topCount[c.pawn] = had + 1;
                    done++;
                }

                // 覆盖保证：关键工作一个候选都没排上（都被名额限制挡掉），
                // 也要硬塞给最合适的那个人，避免出现「没人做饭」这种局面。
                if (done == 0 && de.baseImportance >= 0.75f)
                {
                    WorkSorterCell c = cands[0];
                    int had;
                    topCount.TryGetValue(c.pawn, out had);
                    c.suggested = 1;
                    topCount[c.pawn] = had + 1;
                }
            }

            // 阶段二：每个人自己剩下的工作按组合分分档
            for (int pi = 0; pi < plan.pawns.Count; pi++)
            {
                Pawn p = plan.pawns[pi];
                Dictionary<WorkTypeDef, WorkSorterCell> row = plan.cells[p];

                List<WorkSorterCell> rest = new List<WorkSorterCell>();
                foreach (WorkSorterCell c in row.Values)
                {
                    if (c.suggested == 0)
                        rest.Add(c);
                }
                if (rest.Count == 0)
                    continue;

                rest.Sort((a, b) => SortScore(b).CompareTo(SortScore(a)));

                int n = rest.Count;
                for (int i = 0; i < n; i++)
                {
                    WorkSorterCell c = rest[i];
                    float frac = n <= 1 ? 0f : (float)i / (n - 1);

                    int pr;
                    if (n <= 2) pr = 2;                 // 会做的活就那么两三样，别压到最低档
                    else if (frac < 0.40f) pr = 2;
                    else if (frac < 0.80f) pr = 3;
                    else pr = 4;

                    // 完全不适配的工作直接扔到最低档，不占中间档位
                    if (c.fit < 0.16f)
                        pr = 4;

                    c.suggested = Mathf.Clamp(pr, 1, Pawn_WorkSettings.LowestPriority);
                }
            }
        }

        private static float SortScore(WorkSorterCell c)
        {
            float demandNorm = Mathf.Clamp01(c.demand / 1.6f);
            return c.fit * 0.75f + demandNorm * 0.25f;
        }

        private static int SlotsFor(DemandEntry de, int capable)
        {
            if (capable <= 0)
                return 0;

            // 关键岗位（医生、看守、驯养…）配两个人，防止主负责人受伤后没人接班
            if (de.baseImportance >= 0.75f)
                return Mathf.Min(2, capable);

            int s = Mathf.RoundToInt(capable * 0.30f * Mathf.Clamp(de.demand, 0.1f, 1.6f));

            // 需求不够高的活不设第一档：没人排 1，大家在 2/3/4 里顺手做。
            // 否则「艺术」这种冷门工作也会硬拉一个人专门去雕石头。
            if (s < 1)
                return de.demand >= 0.45f ? 1 : 0;

            return Mathf.Clamp(s, 1, Mathf.Min(3, capable));
        }

        private static string BuildReason(WorkSorterCell c, DemandEntry de)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(c.workType.labelShort).Append("　适配 ").Append(c.fit.ToString("0.00"));

            if (c.workType.relevantSkills != null && c.workType.relevantSkills.Count > 0)
                sb.Append("\n技能 ").Append(c.avgSkill.ToString("0.0")).Append(" 级 → ").Append(c.skill01.ToString("0.00"));
            else
                sb.Append("\n技能 无要求（按中性值计）");

            sb.Append("\n热情 ").Append(PassionName(c.passion)).Append(" → ").Append(c.passion01.ToString("0.00"));
            sb.Append("\n速度 ").Append((c.speedValue * 100f).ToString("0")).Append("% → ").Append(c.speed01.ToString("0.00"));
            sb.Append("\n能力 ").Append((c.cap01 * 100f).ToString("0")).Append("% → ").Append(c.cap01.ToString("0.00"));
            if (c.back > 0f)
                sb.Append("\n背景 +").Append(c.back.ToString("0.00"));

            sb.Append("\n需求 ").Append(c.demand.ToString("0.00"))
              .Append("　固有序位 ").Append(c.workType.naturalPriority).Append("/1400");
            if (de != null && de.backlogCount > 0)
                sb.Append("\n待办 ").Append(de.backlogLabel).Append(" ").Append(de.backlogCount)
                  .Append(" → ").Append(de.backlog.ToString("0.00"));

            sb.Append("\n现在 ").Append(c.current <= 0 ? "未启用" : c.current.ToString())
              .Append("　建议 ").Append(c.suggested <= 0 ? "保持原样" : c.suggested.ToString());

            return sb.ToString();
        }

        private static string PassionName(int p)
        {
            if (p >= 2) return "狂热（有火）";
            if (p == 1) return "有兴趣";
            return "无";
        }
    }
}
