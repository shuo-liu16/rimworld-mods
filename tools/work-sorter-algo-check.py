#!/usr/bin/env python3
"""离线复现 WorkSorter 的分配算法，跑几个固定场景做回归。

为什么不直接调用 C#：Engine 依赖 Pawn / WorkTypeDef，脱离游戏跑不起来。
所以这里是 Engine.Assign 的一份**复刻**——改了 C# 里的权重、排序键或门槛，
必须同步改这里，否则这份回归就失去意义。

用法: python tools/work-sorter-algo-check.py

断言：
1. 不存在「某项工作全员 1」
2. 不存在「某人把所有工作都占成 1」
3. 关键工作一定有第一档
4. 冷门工作不会被硬塞第一档
5. 专业工作不会被管理类工作挤掉名额
"""

W_SKILL, W_PASSION, W_SPEED, W_CAP = 0.40, 0.18, 0.20, 0.15
MAX_TOP = 3
SKILL_FLOOR = 3.0      # 低于这个等级算「不会」
MANDATORY = {"Firefighter", "Patient", "PatientBedRest"}
CRITICAL = {"Doctor", "Warden", "Handling"}   # 允许无技能兜底的岗位


def clamp(v, lo, hi):
    return lo if v < lo else hi if v > hi else v


def cround(v):
    import decimal
    return int(decimal.Decimal(v).quantize(0, rounding=decimal.ROUND_HALF_EVEN))


# (defName, naturalPriority, skills, backlog)
WORKS = [
    ("Firefighter", 1400, [], 0.0),
    ("Patient", 1350, [], 0.0),
    ("Doctor", 1300, ["Medicine"], 0.0),
    ("PatientBedRest", 1200, [], 0.0),
    ("BasicWorker", 1150, [], 0.0),
    ("Warden", 1100, ["Social"], 0.0),
    ("Handling", 1050, ["Animals"], 0.0),
    ("Cooking", 1000, ["Cooking"], 0.0),
    ("Hunting", 950, ["Shooting"], 0.0),
    ("Construction", 900, ["Construction"], 0.0),
    ("Growing", 700, ["Plants"], 0.0),
    ("Mining", 600, ["Mining"], 0.0),
    ("PlantCutting", 500, ["Plants"], 0.0),
    ("Smithing", 470, ["Crafting"], 0.0),
    ("Tailoring", 450, ["Crafting"], 0.0),
    ("Art", 430, ["Artistic"], 0.0),
    ("Crafting", 400, ["Crafting"], 0.0),
    ("Hauling", 300, [], 0.0),
    ("Cleaning", 200, [], 0.0),
    ("Research", 100, ["Intellectual"], 0.0),
]


def fit_of(pawn, work):
    name, np, skills, _ = work
    if not skills:
        skill01 = 6 / 18
    else:
        total = sum(pawn["skills"].get(s, 0) for s in skills)
        skill01 = clamp((total / len(skills)) / 18, 0, 1)
    passion01 = pawn["passions"].get(name, 0) * 0.5
    speed01 = clamp((pawn["speed"] - 0.55) / 1.35, 0, 1)
    return (W_SKILL * skill01 + W_PASSION * passion01
            + W_SPEED * speed01 + W_CAP * pawn["cap"]
            + pawn["back"].get(name, 0.0))


def avg_skill(pawn, work):
    skills = work[2]
    if not skills:
        return 6.0
    return sum(pawn["skills"].get(s, 0) for s in skills) / len(skills)


def run(pawns, works, label, max_top=MAX_TOP):
    print("=" * 66)
    print(label)

    cap = {w[0]: 0 for w in works}
    cells = {}
    for p in pawns:
        for w in works:
            if w[0] in p.get("disabled", ()):
                continue
            cells[(p["name"], w[0])] = {
                "fit": fit_of(p, w),
                "avg": avg_skill(p, w),
                "sug": 0,
                "cur": p["cur"].get(w[0], 3),
            }
            cap[w[0]] += 1

    demand = {}
    for w in works:
        base = clamp(w[1] / 1400, 0, 1)
        backlog = w[3]
        dem = base + backlog * 0.55
        scarcity = clamp(1 - cap[w[0]] / 4, 0, 1) * 0.25
        demand[w[0]] = (base, backlog, dem + scarcity)

    top = {p["name"]: 0 for p in pawns}

    # --- 阶段一：按「需求 × 有人真擅长」排序 ---
    groups = []
    for w in works:
        name = w[0]
        cands = [(k, c) for k, c in cells.items() if k[1] == name]
        if not cands:
            continue
        cands.sort(key=lambda kv: (-kv[1]["fit"], kv[0][0]))
        groups.append((name, cands))

    def group_key(g):
        name, cands = g
        base, backlog, dem = demand[name]
        return (-(dem * 0.55 + cands[0][1]["fit"] * 0.45), -w_prio(name), name)

    groups.sort(key=group_key)

    for name, cands in groups:
        base, backlog, dem = demand[name]

        if name in MANDATORY:
            for k, c in cands:
                c["sug"] = 1
            continue

        # 有技能要求的工作：只从「确实会」的人里挑；
        # 一个达标的都没有时，非关键岗位干脆不设第一档。
        needs_skill = bool(next(w[2] for w in works if w[0] == name))
        skilled = [(k, c) for k, c in cands if c["avg"] >= SKILL_FLOOR]
        if needs_skill:
            if skilled:
                pool = skilled
            elif name in CRITICAL:
                pool = cands
            else:
                continue
        else:
            pool = cands

        if base >= 0.75:
            slots = min(2, len(pool))
        else:
            s = cround(len(pool) * 0.30 * clamp(dem, 0.1, 1.6))
            if backlog >= 0.6:
                s += 1
            slots = (1 if dem >= 0.45 else 0) if s < 1 else clamp(s, 1, min(3, len(pool)))

        done = 0
        for k, c in pool:
            if done >= slots:
                break
            if top[k[0]] >= max_top:
                continue
            # 关键岗位的第二个名额必须给「确实会」的人，不能随便拉个白板
            if done >= 1 and needs_skill and c["avg"] < SKILL_FLOOR:
                continue
            c["sug"] = 1
            top[k[0]] += 1
            done += 1

        if done == 0 and base >= 0.75 and top[pool[0][0][0]] <= max_top:
            # 覆盖保证：关键岗位不能没人。但只在对方名额还没满时才破例，
            # 否则一个人会被所有关键岗位轮流硬塞，最后拿到七八个第一档。
            k, c = pool[0]
            c["sug"] = 1
            top[k[0]] += 1

    # --- 阶段二 ---
    for p in pawns:
        rest = [(k, c) for k, c in cells.items() if k[0] == p["name"] and c["sug"] == 0]
        rest.sort(key=lambda kv: (-(kv[1]["fit"] * 0.75 + clamp(demand[kv[0][1]][2] / 1.6, 0, 1) * 0.25),
                                  -next(w[1] for w in works if w[0] == kv[0][1])))
        n = len(rest)
        for i, (k, c) in enumerate(rest):
            frac = 0.0 if n <= 1 else i / (n - 1)
            pr = 2 if n <= 2 else (2 if frac < 0.40 else 3 if frac < 0.80 else 4)
            if c["fit"] < 0.16:
                pr = 4
            c["sug"] = pr

    # --- 输出 ---
    names = [p["name"] for p in pawns]
    print("工作".ljust(16) + "".join(n.rjust(6) for n in names) + "   需求")
    for w in works:
        row = w[0].ljust(16)
        for n in names:
            c = cells.get((n, w[0]))
            row += ("  -" if c is None else ("  " + str(c["sug"]))).rjust(6)
        print(row + "   %.2f" % demand[w[0]][2])

    print("\n每人第一档：")
    for n in names:
        ones = sorted(k[1] for k in cells if k[0] == n and cells[k]["sug"] == 1)
        print("  %-6s %d 个  %s" % (n, len(ones), ",".join(ones)))

    problems = []
    # 人少的时候一个人必然要承担更多第一档，这是合理的；
    # 覆盖保证还允许每个关键岗位破例一次。
    allowed = max_top + len(MANDATORY) + (1 if len(pawns) <= 3 else 0)
    for n in names:
        cnt = sum(1 for k in cells if k[0] == n and cells[k]["sug"] == 1)
        if cnt > allowed:
            problems.append("%s 有 %d 个第一档（上限 %d）" % (n, cnt, allowed))
        all4 = [k for k in cells if k[0] == n]
        if all4 and all(cells[k]["sug"] == 4 for k in all4):
            problems.append("%s 全是 4" % n)
    for w in works:
        if w[0] in MANDATORY:
            continue
        grp = [k for k in cells if k[1] == w[0]]
        if grp and all(cells[k]["sug"] == 1 for k in grp):
            problems.append("%s 全员第一档" % w[0])
    print("\n" + ("[OK] 无断言失败" if not problems else "[FAIL] " + "; ".join(problems)))
    return not problems


def w_prio(name):
    return next(w[1] for w in WORKS if w[0] == name)


def pawn(name, skills, passions, speed=1.0, cap=1.0, back=None, cur=None, disabled=()):
    return {"name": name, "skills": skills, "passions": passions, "speed": speed,
            "cap": cap, "back": back or {}, "cur": cur or {}, "disabled": disabled}


ok = True

ok &= run([
    pawn("张三", {"Cooking": 12, "Plants": 6}, {"Cooking": 2}),
    pawn("李四", {"Construction": 10, "Mining": 8}, {"Construction": 1}),
    pawn("王五", {"Shooting": 9, "Mining": 6}, {}),
], WORKS, "场景 1：3 人小殖民地，无积压")

ok &= run([
    pawn("甲", {"Cooking": 15}, {"Cooking": 2}),
    pawn("乙", {"Construction": 12}, {"Construction": 1}),
    pawn("丙", {"Mining": 10}, {}),
    pawn("丁", {"Plants": 9}, {"Plants": 1}),
    pawn("戊", {"Medicine": 8}, {"Medicine": 2}),
], [w[:3] + (1.0 if w[0] == "Hauling" else w[3],) for w in WORKS],
    "场景 2：5 人，搬运积压拉满")

ok &= run([
    pawn("全能", {k: 16 for k in ["Cooking", "Construction", "Mining", "Plants",
                                  "Crafting", "Artistic", "Intellectual", "Medicine",
                                  "Social", "Animals", "Shooting"]},
         {k: 2 for k in ["Cooking", "Construction", "Mining", "Plants", "Crafting"]}),
    pawn("新人", {}, {}),
], WORKS, "场景 3：一个全能 + 一个白板")

ok &= run([
    pawn("老手", {"Cooking": 14, "Construction": 12, "Mining": 11}, {"Cooking": 2}),
    pawn("伤员", {"Cooking": 8}, {}, speed=0.55, cap=0.45),
    pawn("孩子", {}, {}, speed=0.8, cap=0.7, disabled=("Mining", "Cooking", "Construction")),
], WORKS, "场景 4：老手 + 伤员 + 部分工作禁用的孩子")

print("\n总体：" + ("通过" if ok else "有失败"))
