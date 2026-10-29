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
            // naturalPriority 可能重复（Mod 新增的工作类型常直接抄原版的值），
            // 不给 tie-breaker 的话表格行顺序每次重算都可能变。
            plan.workTypes.Sort((a, b) =>
            {
                int c = b.naturalPriority.CompareTo(a.naturalPriority);
                return c != 0 ? c : string.CompareOrdinal(a.defName, b.defName);
            });

            // —— 3. 需求表 ——
            plan.demands = WorkSorterDemand.Build(map, plan.workTypes);

            // —— 4. 逐格打分 ——
            for (int pi = 0; pi < plan.pawns.Count; pi++)
            {
                Pawn p = plan.pawns[pi];
                Dictionary<WorkTypeDef, WorkSorterCell> row = plan.cells[p];

                // 背景加成只跟这个人有关，先摊平成「技能 → 加成」的字典，
                // 免得每算一格都去遍历两段 backstory 的 skillGains。
                Dictionary<SkillDef, int> backstoryBonus = BackstorySkillMap(p);

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

                    WorkSorterCell c = Score(p, w, de, backstoryBonus);
                    if (c == null)
                        continue;

                    row[w] = c;
                    de.capableCount++;
                }
            }

            // —— 4.5 剔掉一个活都做不了的人 ——
            // 婴儿、长期昏迷、被基因锁死全部工作的人，在表里只会占一列空白。
            // 打分之后再过滤，因为「能不能做」要逐项问过才知道。
            for (int i = plan.pawns.Count - 1; i >= 0; i--)
            {
                Pawn p = plan.pawns[i];
                Dictionary<WorkTypeDef, WorkSorterCell> row;
                if (plan.cells.TryGetValue(p, out row) && row.Count > 0)
                    continue;
                plan.cells.Remove(p);
                plan.pawns.RemoveAt(i);
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
        private static WorkSorterCell Score(Pawn p, WorkTypeDef w, DemandEntry de,
            Dictionary<SkillDef, int> backstoryBonus)
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
            c.back = BackstoryFit(w, backstoryBonus);

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

        // 工作类型 → 专属速度属性的映射只跟 Def 有关，算一次就够。
        // 每次重建预览要算 200 格，这里省下 200 次位标志比较。
        private static readonly Dictionary<WorkTypeDef, StatDef> speedStatCache =
            new Dictionary<WorkTypeDef, StatDef>();

        private static StatDef SpecificSpeedStat(WorkTypeDef w)
        {
            StatDef cached;
            if (speedStatCache.TryGetValue(w, out cached))
                return cached;

            StatDef def = ResolveSpeedStat(w);
            speedStatCache[w] = def;
            return def;
        }

        private static StatDef ResolveSpeedStat(WorkTypeDef w)
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
        /// 把两段职业背景的 skillGains 摊平成「技能 → 总加成」。
        /// 原版创建小人时就按背景给技能，所以「成年职业是矿工」这条信息值得
        /// 单独加一点分，否则一个 20 级厨师被拉去挖矿会被算成和矿工一样好。
        /// 每个人只需要算一次，之后每格查表。
        /// </summary>
        private static Dictionary<SkillDef, int> BackstorySkillMap(Pawn p)
        {
            Dictionary<SkillDef, int> map = new Dictionary<SkillDef, int>();
            if (p.story == null)
                return map;

            List<BackstoryDef> all = p.story.AllBackstories;
            if (all == null)
                return map;

            for (int i = 0; i < all.Count; i++)
            {
                BackstoryDef bs = all[i];
                if (bs == null || bs.skillGains == null)
                    continue;

                for (int j = 0; j < bs.skillGains.Count; j++)
                {
                    SkillGain g = bs.skillGains[j];
                    if (g == null || g.skill == null || g.amount == 0)
                        continue;

                    int cur;
                    map.TryGetValue(g.skill, out cur);
                    map[g.skill] = cur + g.amount;
                }
            }

            return map;
        }

        private static float BackstoryFit(WorkTypeDef w, Dictionary<SkillDef, int> map)
        {
            if (w.relevantSkills == null || w.relevantSkills.Count == 0 || map.Count == 0)
                return 0f;

            int sum = 0;
            for (int i = 0; i < w.relevantSkills.Count; i++)
            {
                int v;
                if (map.TryGetValue(w.relevantSkills[i], out v))
                    sum += v;
            }

            return Mathf.Clamp01(sum / 12f) * MaxBackstoryBonus;
        }

        /// <summary>
        /// 允许写入的最低档（原版是 4）。读静态字段而不是写死，是为了兼容把
        /// 范围扩到 1-9 的那类 Mod；同时兜一个底：万一读到 0（静态构造没跑或
        /// 读取时机不对），所有格都会被 clamp 成 0，等于「这个人什么都不做」。
        /// </summary>
        public static int MaxPriority
        {
            get
            {
                int v = Pawn_WorkSettings.LowestPriority;
                return v < 4 ? 4 : v;
            }
        }

        // 低于这个技能等级就算「不会」。3 级是原版新手的水平，
        // 用来区分「学过」和「完全没碰过」。
        private const float SkillFloor = 3f;

        /// <summary>一个工作类型 + 它的候选人（已按适配度降序排好）。</summary>
        private sealed class WorkGroup
        {
            public DemandEntry demand;
            public readonly List<WorkSorterCell> cands = new List<WorkSorterCell>();
        }

        /// <summary>
        /// 两阶段分配。
        /// 阶段一：给每个工作挑出最合适的 1-3 人排第一档；
        /// 阶段二：剩下的工作按各人适配度排成 2/3/4 档。
        ///
        /// 阶段一的排序键是「需求」和「有没有人真的擅长」的加权，不能只看需求：
        /// 医生、看守、基础工作这类 naturalPriority 很高的岗位会先占满每个人的
        /// 名额，等排到烹饪、建造时已经没有位置了，结果厨师去做急救、矿工去
        /// 拨开关，真正该由专人干的事全掉到第二档。
        /// </summary>
        private static void Assign(WorkSorterPlan plan)
        {
            WorkSorterSettings s = WorkSorterMod.Settings;
            int maxTopPerPawn = s == null ? 3 : Mathf.Clamp(s.maxTopPerPawn, 1, 8);

            Dictionary<Pawn, int> topCount = new Dictionary<Pawn, int>();
            for (int i = 0; i < plan.pawns.Count; i++)
                topCount[plan.pawns[i]] = 0;

            // 1) 收集候选人
            List<WorkGroup> groups = new List<WorkGroup>();
            foreach (DemandEntry de in plan.demands.Values)
            {
                if (de.capableCount <= 0)
                    continue;

                WorkGroup g = new WorkGroup();
                g.demand = de;

                for (int pi = 0; pi < plan.pawns.Count; pi++)
                {
                    WorkSorterCell c = plan.Cell(plan.pawns[pi], de.workType);
                    if (c != null)
                        g.cands.Add(c);
                }
                if (g.cands.Count == 0)
                    continue;

                g.cands.Sort(CompareFit);
                groups.Add(g);
            }

            groups.Sort(CompareGroup);

            // 2) 逐项分配第一档
            for (int gi = 0; gi < groups.Count; gi++)
            {
                WorkGroup g = groups[gi];
                WorkTypeDef w = g.demand.workType;
                DemandEntry de = g.demand;

                // 消防、就医、卧床：原版就是「谁碰上谁做」，全员第一档，不占名额。
                if (IsMandatoryForEveryone(w))
                {
                    for (int i = 0; i < g.cands.Count; i++)
                        g.cands[i].suggested = 1;
                    continue;
                }

                bool needsSkill = w.relevantSkills != null && w.relevantSkills.Count > 0;
                bool critical = de.baseImportance >= 0.75f;

                // 有技能要求的工作，第一档只从「确实会」的人里挑。
                // 一个达标的都没有时，关键岗位（医生、看守、驯养）仍然要有人兜底，
                // 其它工作干脆不设第一档——等练出人来再说，别硬塞。
                List<WorkSorterCell> pool = g.cands;
                if (needsSkill)
                {
                    List<WorkSorterCell> skilled = new List<WorkSorterCell>();
                    for (int i = 0; i < g.cands.Count; i++)
                    {
                        if (g.cands[i].avgSkill >= SkillFloor)
                            skilled.Add(g.cands[i]);
                    }

                    if (skilled.Count > 0)
                        pool = skilled;
                    else if (!critical)
                        continue;
                }

                int slots = SlotsFor(de, pool.Count);
                int done = 0;

                for (int i = 0; i < pool.Count && done < slots; i++)
                {
                    WorkSorterCell c = pool[i];
                    int had;
                    topCount.TryGetValue(c.pawn, out had);
                    if (had >= maxTopPerPawn)
                        continue;

                    // 关键岗位的第二个人必须也沾边：医生配两个，不等于
                    // 随便拉一个不会医疗的人来当第二医生。
                    if (done >= 1 && needsSkill && c.avgSkill < SkillFloor)
                        continue;

                    c.suggested = 1;
                    topCount[c.pawn] = had + 1;
                    done++;
                }

                // 覆盖保证：关键岗位一个都没排上（名额满了 / 人选不够），
                // 破例塞给最合适的那个人——但每个人最多被破例一次，
                // 否则所有关键岗位会轮流硬塞同一个人。
                if (done == 0 && critical && pool.Count > 0)
                {
                    WorkSorterCell c = pool[0];
                    int had;
                    topCount.TryGetValue(c.pawn, out had);
                    if (had <= maxTopPerPawn)
                    {
                        c.suggested = 1;
                        topCount[c.pawn] = had + 1;
                    }
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

                rest.Sort(CompareSortScore);

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

                    c.suggested = Mathf.Clamp(pr, 1, MaxPriority);
                }
            }
        }

        /// <summary>
        /// 是否「谁碰上谁做」的工作。只认原版这三项：救火、就医、卧床。
        /// BasicWorker（放囚犯、拨开关）虽然也是 alwaysStartActive，但它有真实工作量，
        /// 该参与正常竞争——否则每人先白拿一个第一档名额，「每人最多 3 个 1」就废了。
        /// </summary>
        private static bool IsMandatoryForEveryone(WorkTypeDef w)
        {
            return w.defName == "Firefighter"
                || w.defName == "Patient"
                || w.defName == "PatientBedRest";
        }

        private static float SortScore(WorkSorterCell c)
        {
            float demandNorm = Mathf.Clamp01(c.demand / 1.6f);
            return c.fit * 0.75f + demandNorm * 0.25f;
        }

        // 下面几个比较器都带确定的 tie-breaker：List.Sort 不稳定，
        // 同分项按任意顺序进场会让每次重算产出不同的排班，
        // 玩家会看到优先级每隔几秒自己跳。

        /// <summary>
        /// 阶段一的排序：需求占 55%，「这项最强的候选人有多合适」占 45%。
        /// 只看需求的话，医生/看守/基础工作会先占满名额，专业岗全掉到第二档。
        /// </summary>
        private static int CompareGroup(WorkGroup a, WorkGroup b)
        {
            float sa = a.demand.demand * 0.55f + a.cands[0].fit * 0.45f;
            float sb = b.demand.demand * 0.55f + b.cands[0].fit * 0.45f;

            int c = sb.CompareTo(sa);
            if (c != 0)
                return c;
            c = b.demand.workType.naturalPriority.CompareTo(a.demand.workType.naturalPriority);
            if (c != 0)
                return c;
            return string.CompareOrdinal(a.demand.workType.defName, b.demand.workType.defName);
        }

        private static int CompareFit(WorkSorterCell a, WorkSorterCell b)
        {
            int c = b.fit.CompareTo(a.fit);
            if (c != 0)
                return c;
            return a.pawn.thingIDNumber.CompareTo(b.pawn.thingIDNumber);
        }

        private static int CompareSortScore(WorkSorterCell a, WorkSorterCell b)
        {
            int c = SortScore(b).CompareTo(SortScore(a));
            if (c != 0)
                return c;
            c = b.workType.naturalPriority.CompareTo(a.workType.naturalPriority);
            if (c != 0)
                return c;
            return string.CompareOrdinal(a.workType.defName, b.workType.defName);
        }

        private static int SlotsFor(DemandEntry de, int capable)
        {
            if (capable <= 0)
                return 0;

            // 关键岗位（医生、看守、驯养…）配两个人，防止主负责人受伤后没人接班
            if (de.baseImportance >= 0.75f)
                return Mathf.Min(2, capable);

            int s = Mathf.RoundToInt(capable * 0.30f * Mathf.Clamp(de.demand, 0.1f, 1.6f));

            // 积压过半（比如三十件货堆在仓库门口）时再多派一个人，
            // 否则「一万件待搬」和「三件待搬」拿到的人数一样。
            if (de.backlog >= 0.6f)
                s++;

            // 需求不够高的活不设第一档：没人排 1，大家在 2/3/4 里顺手做。
            // 否则「艺术」这种冷门工作也会硬拉一个人专门去雕石头。
            if (s < 1)
                return de.demand >= 0.45f ? 1 : 0;

            return Mathf.Clamp(s, 1, Mathf.Min(3, capable));
        }

        /// <summary>
        /// 工作类型的显示名。Mod 新增的工作类型可能没填 labelShort，
        /// 直接往 Widgets.Label 里塞 null 会抛 NRE，这里兜一层。
        /// </summary>
        public static string WorkLabel(WorkTypeDef w)
        {
            if (w == null)
                return "?";
            if (!string.IsNullOrEmpty(w.labelShort))
                return w.labelShort;
            if (!string.IsNullOrEmpty(w.defName))
                return w.defName;
            return "?";
        }

        private static string BuildReason(WorkSorterCell c, DemandEntry de)
        {
            StringBuilder sb = new StringBuilder(256);
            sb.Append(WorkLabel(c.workType)).Append("　适配 ").Append(c.fit.ToString("0.00"));

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
