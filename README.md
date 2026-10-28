# RimWorld 自用模组合集

两个给《环世界》写的小模组，解决我自己玩的时候最烦的三件事：**电看不见、温度调不过来、基地总得有人扫地**。

适配 **RimWorld 1.7**（含 Waylith 引擎的新版本）。个人自制自用，觉得有用直接拿去。

| 模组 | 一句话 | 依赖 | 入口 |
|---|---|---|---|
| [电网与温控](mods/GridThermal/README.md) | 全图电网负载一览 + 加热/制冷设备分组控温 | 无 | 底栏「电网」 |
| [自动清扫](mods/AutoClean/README.md) | 每天定时清除居住区污渍 | Harmony | 选项 → Mod 设置 |

---

## 1. Mod 介绍

### 电网与温控（Grid & Thermal Console）

底栏新增一个「电网」标签页，两件事：

**电力网络** —— 把全图每一张独立电网列成一张表：发电、耗电、净输出、储能（当前/容量）、电量百分比、状态（正常 / 放电中 / 缺电）。顶部一行是全基地汇总。

**温控分组** —— 选中任意加热器或制冷器，把它归入一个分组；同组设备共用一套设定，在面板上一键调温。两种模式：

- **统一**：所有设备同一个目标温度。冷库、单间用这个。
- **季节**：热季只开制冷设备、冷季只开加热设备，反季那类**直接断电**（不是把温度设成极端值，是真的关掉电源，待机功耗也省了）。

温度按钮是 `[-5] [-1] 数值 [+1] [+5]`，冷库想从 21 直接压到 -5 也就几下。

### 自动清扫（Auto Clean）

每天在你设定的时刻（默认 06:00），自动清除居住区内的污渍——血迹、泥、动物粪、碎石堆都算。

不需要小人跑过去，不消耗资源，也不需要你记得按什么。时刻、范围（只清居住区 / 全图）、是否弹提示，都在 Mod 设置里。另有一个「立即清扫一次」用于测试。

清扫是**分批**做的（每次 300 处，跨多帧），所以大扫除之后不会因为同一帧删掉上千个实体而卡一下。

---

## 2. 解决什么问题

| 原版的麻烦 | 这个模组怎么处理 |
|---|---|
| 想知道「哪个电网快撑不住了」，只能挨个点电线，点开还只能看那一个网 | 一张表列出所有电网，缺电和放电中直接标色 |
| 十几个加热器制冷器各设各的温度，改一次要挨个点；漏一台就是局部过冷/过热 | 分组后一次调完整组，解散或移出会自动还原原温度 |
| 夏天加热器、冬天空调还在偷偷吃待机功率 | 季节模式直接断电，只保留当季需要的那一类设备 |
| 基地一忙起来就满地血和泥，扫地工永远不够，心情还掉 | 每天定时自动清，范围限定居住区，不影响矿区野外 |

设计上尽量不碰原版数值：温控走的是原版 `CompTempControl`，只改「目标温度」和「电源开关」，所以断电、EMP、故障停机、过热保护这些原版行为全部保留。

---

## 3. 如何使用

### 环境

- RimWorld **1.7**（`1.7.4990` 及以上）
- 自动清扫需要 **Harmony**（创意工坊订阅即可，`About.xml` 里已声明依赖与加载顺序）

### 安装

**创意工坊**：订阅后在游戏内 Mod 列表启用，重启游戏。

**本地**：把 `mods/GridThermal`、`mods/AutoClean` 两个文件夹整个放进

```
...\Steam\steamapps\common\RimWorld\Mods\
```

然后进游戏 → 选项 → Mod 设置 → 勾选启用 → 重启。

> 首次给已有的存档加 Mod，建议先备份存档。这两个 Mod 都不改地图生成、不改派系，正常情况下是安全的，但备份永远是对的。

### 上手

**电网与温控**

1. 底栏点「电网」（排在「建筑师」右边）。
2. 「电力网络」页直接看表；「温控分组」页管理分组。
3. 建组：选中一台加热器/制冷器 → 点 gizmo 栏的「温控分组：未分组」→ 选已有组或新建。
4. 回面板调模式与温度。点「选中」可以一次选中组内所有设备。

**自动清扫**

1. 选项 → Mod 设置 → 自动清扫。
2. 设定时刻、是否只清居住区。
3. 先点「立即清扫一次」验证能用，再等定时生效。

### 排错

游戏日志在 `%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Player.log`，搜 `GridThermal` 或 `AutoClean`：

- 有 `[GridThermal] 已加载…` / `[AutoClean] 已加载…` → Mod 正常加载。
- 完全没有这两行 → Mod 没被加载，检查文件夹层级和 `About.xml`，不是代码问题。
- 出现 `出错` 字样 → 附带完整堆栈，提到 Issue 里。

---

## 4. 项目结构

```
riamworldmod/
├─ mods/
│  ├─ GridThermal/                 电网与温控
│  │  ├─ About/                    About.xml + Preview.png
│  │  ├─ Assemblies/               编译产物（游戏读这个）
│  │  ├─ Defs/                     底栏按钮定义
│  │  ├─ Patches/                  给 Heater/Cooler 挂自定义组件
│  │  ├─ Languages/                中文翻译
│  │  ├─ Source/GridThermal/       C# 源码与 csproj
│  │  └─ README.md                 该模组的详细说明
│  └─ AutoClean/                   自动清扫（结构同上，无 Defs/Patches）
├─ tools/
│  ├─ gen-assets.py                绕过 NuGet restore 的构建辅助
│  ├─ make-preview.py              生成创意工坊封面
│  └─ ApiDump/                     读取游戏程序集元数据的小工具（查 API 用）
├─ docs/
│  ├─ modding-notes.md             RimWorld Mod 开发笔记（含 1.7 踩坑记录）
│  └─ publish.md                   GitHub 与创意工坊发布步骤
├─ build.bat                       一键重编译所有 Mod
├─ deploy.bat                      同步到本机游戏 Mods 目录
└─ LICENSE
```

### 构建

需要 .NET SDK 10 与 Python 3（`gen-assets.py` 只用标准库）。

```bat
build.bat     :: 编译两个 Mod，产物落在 mods\<Mod>\Assemblies\
deploy.bat    :: 把 mods\* 同步到游戏 Mods 目录（按需改脚本里的路径）
```

> 为什么要有 `gen-assets.py`：本机 `LOCALAPPDATA` 解析异常，导致 `dotnet build` 的 restore 阶段必挂。项目本身零 NuGet 依赖，所以直接手写一份空的 `project.assets.json` 跳过还原即可。细节见 [docs/modding-notes.md](docs/modding-notes.md)。

### 技术要点

- 目标框架 `net10.0`，引用游戏根目录的 `Assembly-CSharp.dll`、`Waylith.dll`、`UnityStubs.dll`（全部 `Private=false`，不打包进 Assemblies）。
- 异常边界：tick 钩子、OnGUI 绘制、建筑生成、设置页都有 try/catch，出错只记日志，不冒泡进游戏主循环。
- 性能：面板数据 0.5 秒重算一次、只绘制可视行；tick 路径上每台设备每 4 秒一次比较，偏离才写值。

---

## 5. 如何贡献

欢迎提 Issue，也欢迎直接改。

**报 Bug** 请带上：

1. 游戏版本（主菜单右下角，或 `Version.txt`）
2. 完整 Mod 列表（`ModsConfig.xml` 里的 `activeMods`）
3. `Player.log` 里相关片段（搜 `GridThermal` / `AutoClean`）
4. 复现步骤：做了什么、期望什么、实际什么

**提 PR**：

1. Fork → 新建分支（`fix/xxx`、`feat/xxx`）
2. 改完跑 `build.bat`，必须 0 错误
3. 不要提交 `obj/`、`bin/`（已在 `.gitignore`）
4. 新代码沿用现有约定：命名空间 `Shuo.<ModName>`、入口处加异常兜底、注释写「为什么」而不是「做了什么」

**本地调试**：

- 把 `deploy.bat` 里的目标路径改成你的 RimWorld 安装位置
- 改完 XML/代码 → 关游戏 → `deploy.bat` → 重开。RimWorld 不热重载 Mod

---

## 6. 免责声明

- 这两个模组是**个人自制自用**，按「现状」提供，不保证适用于所有 Mod 组合，也不对存档损坏、成就失效等后果负责。
- **可以自取**：代码以 MIT 协议开源，你可以自由使用、修改、二次分发，甚至直接并进自己的整合包，不需要问我。
- 二次分发时保留 `LICENSE` 与作者信息即可；如果你修了 bug，欢迎回推一份。
- 本项目与 Ludeon Studios 无隶属关系，RimWorld 及其素材版权归 Ludeon 所有。
- 反馈渠道：**GitHub Issues**（推荐，能贴日志）或**创意工坊页面留言**。

如果这两个模组帮你省了点事，那它们的目的就达到了。
