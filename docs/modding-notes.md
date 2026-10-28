# 环世界（RimWorld）Mod 制作准备指南

> 整理时间：2026-10-28　｜　目标：从零建立可开工的 Mod 开发环境与学习路线
> 适用对象：会一点编程、没做过 RimWorld Mod 的人

---

## 0. 先搞清楚现在是什么版本（2026 年 10 月）

这是今年最重要的一条变化，先看它，因为选错分支会让后面所有工作白做。

| 项目 | 现状 |
|---|---|
| 稳定分支 | **1.6**（Steam 主分支，玩家默认版本） |
| 测试分支 | **1.7**，在 Steam「属性 → 测试版 → unstable」里可选，2026-10-12 公布 |
| 1.7 的核心变化 | 游戏引擎从 **Unity 换成 Ludeon 自研的 Waylith 引擎**，官方称性能提升 2~4 倍 |
| 存档 | 1.6 ↔ 1.7 **双向兼容** |
| 老 Mod | 官方说法：绝大多数 Mod 无需作者改动即可运行；黄字「可能不支持」只是版本号变了 |
| 例外 | 依赖 **Prepatcher** 框架的 Mod 会失效，要等 Prepatcher 自身适配 |
| 对写代码的 Modder | 需要重编译：**net10.0**、去掉 Unity DLL 引用、改引 `Assembly-CSharp.dll` + `UnityStubs.dll` + `Waylith.dll`、**Harmony 3.0**（会做类型校验，老 patch 可能直接被拒） |
| 新工具 | Frame Profiler、`-mod=` 命令行启动参数（只加载指定 Mod，方便隔离排查）、GLSL 着色器 |

**本机实况（2026-10-28 实测）**：`F:\SteamLibrary\steamapps\common\RimWorld`，版本 **1.7.4990 rev1069**（即 unstable 分支），所以下面的示例全部按 1.7 写。`Mods/` 原本为空，创意工坊里有一批 Mod；**扩展包未安装**（`Data/` 下只有 Core），所以不要用 Royalty/Ideology/Biotech/Anomaly 的内容做示例，会直接红字。Harmony（`brrainz.harmony`）已在启用列表里，写 C# 的依赖不用再自己装。

### 你需要做的版本决策

| 你的 Mod 类型 | 建议 |
|---|---|
| **纯 XML**（新武器 / 家具 / 物品 / 种族数值 / 派系 / 剧本） | 不用管版本，直接写。两版通吃，`supportedVersions` 同时写 1.6 和 1.7 |
| **XML + 少量 C#**（自定义 Comp / Job / 事件） | 按 **1.7 / net10.0** 起项目，日常在 unstable 分支测。1.7 是未来，现在按老框架搭等于出生即过时 |
| **大量 Harmony 补丁** | 同上按 1.7 走，但注意 Harmony 3 的类型校验会拒掉一批老写法，优先用 Postfix 而不是替换型 Prefix |
| 只想先玩着学 | 留在 1.6 主分支，跟 1.6 教程走，Def 写法完全通用 |

> 结论：**XML 内容先做，代码后补**。这样无论 1.7 什么时候转正，你的成果都不作废。

---

## 1. 三条学习主线（按这个顺序看，别乱跳）

### 第一站：社区 Modding Wiki（最适合新手，讲「为什么」）
<https://rimworldmodding.wiki.gg/>

只有 17 页，但结构极清晰，从概念到动手：
1. **Basic Concepts** — 代码环境 vs XML 环境、Def 是什么、Patch 是什么、Assembly 是什么
2. **Mod Folder Basics** — 文件夹结构、哪些目录名大小写敏感
3. **First XML Steps** — 开发软件、第一个 Def、About.xml

关键心智模型（原文意思）：**Def 是菜谱，不是菜**。游戏里你看到的枪是 `ThingDef` 生成的 `Thing`，你改的是菜谱。

### 第二站：官方 Wiki 的 Modding Tutorials（当手册查，不要通读）
<https://rimworldwiki.com/wiki/Modding_Tutorials>

完整目录（我按用途重新分组了）：

**必看基础**
- `Mod Folder Structure` / `About.xml`
- `Defs`、`MayRequire`（跨 Mod 依赖的版本守卫）、`PatchOperations`（Patch 的 6 种操作：Patch/Replace/Insert/Add/Remove/Attribute）
- `Textures`、`Sounds`、`Localization`、`Research Projects`

**照着抄一遍的实战教程**
- `Basic Melee Weapon`、`Basic Ranged Weapon`、`Basic Plant`、`Custom Drug`
- （Custom Animal / Simple Building / Custom Workbench / Custom Faction 标记为「即将推出」，暂时只能靠拆原版 XML）

**进阶 C#**
- `Decompiling Source Code`（学会反编译，这是 Modder 最重要的技能）
- `Setting up a Solution`、`Application Startup`、`Hello World`
- `Linking XML and C#`、`Writing Custom Code`
- `Custom Comp Classes`、`ThingComp`、`Components`、`Def Mod Extensions`
- `Alter Code at Runtime with Harmony`
- `Mod settings`、`ExposeData`（存档持久化）、`Debug Actions`
- `The big ass list of useful classes`（真名，很好用）

**排错**
- `Testing Mods`、`Config Errors`、`Troubleshooting mods`

### 第三站：中文资料（入门快，但注意时效）
| 资源 | 说明 |
|---|---|
| [Rimworld 中文维基 · MOD 教程](https://rimworld.huijiwiki.com/wiki/MOD%E6%95%99%E7%A8%8B/XML%E6%96%87%E4%BB%B6%E4%BB%B6%E7%BB%93%E6%9E%84) | 翻译自官方 wiki，XML 结构、继承（Name/ParentName/Abstract）讲得最清楚 |
| [B 站《Rimworld 的 MOD 教程》专栏系列](https://www.bilibili.com/read/readlist/rl11884) | 1→5 讲：基础结构 / Defs / 独立 Mod / 扩展 Mod / 工作清单。例子接地气（发光墙、动力爪），但基于老版本 |
| [CSDN · Mod 开发指南](https://blog.csdn.net/weixin_30776273/article/details/99030234) | 质量参差，只建议当补充 |

⚠️ 中文资料普遍停留在 1.3~1.5，**Def 标签有增删**。看到不确定的标签，去 `Core/Defs` 里搜原版怎么写的，比信教程可靠。

---

## 2. 工具清单

### 必备（XML 阶段够用）
| 工具 | 用途 | 备注 |
|---|---|---|
| **VS Code** | 写 XML / 看大量 Def | 装 `XML` 插件 + `RimWorld XML` 相关高亮；全局搜索 `Core/Defs` 是你的主要学习方式 |
| **dnSpy / ILSpy** | 反编译 `Assembly-CSharp.dll` 看游戏源码 | **最重要的工具**。想知道某个字段干嘛的，直接看源码 |
| **GIMP / Photoshop / Aseprite** | 贴图 | 必须是 **PNG**，别用 jpg |
| **RimSort** 或 **RimPy** | 加载顺序管理、依赖检查 | 测试多 Mod 组合时的救命工具 |

### 写 C# 才需要
| 工具 | 说明 |
|---|---|
| **.NET 10 SDK** | 1.7 的目标框架是 `net10.0`（注意 `.0` 不能省） |
| **Visual Studio 2026 / Rider 2026** | VS2022 及更老版本**不支持 .NET 10**，别装错 |
| **Harmony** | 通过 Steam 订阅即可，**绝对不要把 `0Harmony.dll` 打包进自己的 Assemblies** |
| **Git** | 从第一天就用。Def 改错导致游戏起不来是日常 |

### 项目模板（省掉 90% 的脚手架工作）
- <https://github.com/Anarcho/RimModTemplate> — .NET 模板，含 About.xml、LoadFolders.xml、DefOf 模式、Comp/Hediff/IncidentWorker 示例、构建后自动启动游戏测试
  ```bash
  git clone https://github.com/Anarcho/RimModTemplate.git
  cd RimModTemplate && dotnet new --install .
  # 之后在 Mods 目录下 dotnet new rimworldmod
  ```
  注意：它默认是 .NET Framework 4.8.1 目标，用于 1.7 需自己改成 `net10.0` 并换引用。

---

## 3. 目录结构（背下来，写错一个字母就不加载）

```
RimWorld/
└─ Mods/
   └─ YourModName/                 ← 名字随意
      ├─ About/                    ← 唯一必需的文件夹
      │  ├─ About.xml
      │  └─ Preview.png            ← 建议 512×512（创意工坊封面）
      ├─ Defs/                     ← 新增内容（可分子文件夹）
      │  ├─ ThingDefs/
      │  ├─ ResearchProjectDefs/
      │  └─ ...
      ├─ Patches/                  ← 修改已有内容（XML Patch）
      ├─ Assemblies/               ← 编译出的 .dll
      ├─ Textures/YourModName/     ← 必须 PNG，且要套一层自己的子文件夹
      ├─ Sounds/YourModName/       ← 推荐 .ogg
      ├─ Languages/ChineseSimplified/ChineseSimplified.xml
      └─ LoadFolders.xml           ← 多版本 / 多 DLC 条件加载
```

除了 `YourModName`，其余文件夹名**大小写敏感**，写错就是「Mod 加载了但内容不出现」。

`About.xml` 最小可用版本：

```xml
<?xml version="1.0" encoding="utf-8"?>
<ModMetaData>
  <name>My First Mod</name>
  <author>YourName</author>
  <packageId>yourname.myfirstmod</packageId>   <!-- 全小写，别带空格 -->
  <supportedVersions>
    <li>1.6</li>
    <li>1.7</li>
  </supportedVersions>
  <description>一句话说明这个 Mod 干什么。</description>
</ModMetaData>
```

真正的工作目录在 `Steam/steamapps/common/RimWorld/Mods/`，创意工坊下载的 Mod 在 `steamapps/workshop/content/294100/`（**拆别人的 Mod 学写法就看这里**）。

---

## 4. 核心概念：一张图就够

```
你的 Mod
├── Defs/     → 新增「菜谱」            ← 不写代码的 90% 工作在这
├── Patches/  → 改原版菜谱的某几行      ← 只改数值/加配方，不要复制整个 Def
└── Assemblies/ → 新增「做菜的动作」    ← Def 做不到的行为才需要
        └── Harmony → 改别人已有的动作（前置/后置）
```

三条铁律，新手 80% 的坑都在这：
1. **能 Patch 就别复制整个 Def**。复制原版 Def 再改，会和任何改同一 Def 的 Mod 直接冲突。
2. **能继承就别重写**（`ParentName` + `Abstract="True"`），和 CSS 继承一个道理。
3. **能 Postfix 就别 Prefix**。带 `return false` 的 Prefix 会跳过原方法，也会挡掉别人的 Patch，是兼容性杀手。

---

## 5. 建议的三个练习（按顺序，每个 1~2 小时）

**① 改数值（验证环境）**
做一个 Patch：把原版栓动步枪射程 +13。目标是跑通「放进 Mods → 启动 → 开发者模式 → 生效」这条链路。

```xml
<Patch>
  <Operation Class="PatchOperationReplace">
    <xpath>/Defs/ThingDef[defName="Gun_BoltActionRifle"]/verbs/li/range</xpath>
    <value><range>50</range></value>
  </Operation>
</Patch>
```

> ⚠️ **这是本文档最重要的一条实战教训**：网上几乎所有教程（包括中文维基和 B 站那套）都让你去改 `statBases/RangedWeapon_Range`。但在 1.7 的 `Core/Defs` 里搜这个字段，**一个都没有**——射程早就改成每个 verb 自己的 `<range>` 了（原版栓动步枪是 36.9）。照着老教程写，Patch 会静默失败：不报错、也不生效。
> 所以规则是：**写 Patch 之前，先在 `Data/Core/Defs` 里搜到那个 def 的真实 XML，照着它的层级写 xpath。**

**② 新内容（掌握 Def）**
做一个新远程武器：复制 `Core/Defs/ThingDefs_Misc/Weapons_Guns.xml` 里一把枪的整段，改 `defName` / `label` / 数值 / 贴图路径 / `researchPrerequisites`，再去 `Recipes` 加配方、`ResearchProjectDefs` 加研究项。跑通后你就理解了 Def 之间的引用网。

**③ 加行为（进 C#）**
写一个 `ThingComp`（比如「每 250 tick 自动给周围小人加一点心情」），挂到②的武器或一个新建筑上。这一步会逼你学会：反编译找基类 → 建项目 → 编译进 Assemblies → `Class="YourNamespace.YourComp"` 挂载。

---

## 6. 调试（这一节决定你能不能坚持下去）

- **开开发者模式**：选项 → 开发者工具。右上角有报错按钮，红字点开能看到堆栈。
- **日志位置**：`%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Player.log`。Mod 加载期崩溃时游戏界面什么都不显示，答案全在这个文件里。
- **Def 报错**：「Could not find def」= defName 拼错或文件不在 `Defs/` 下；「XML error」= 标签没闭合 / 属性拼错，报错会给出文件和行号。
- **Harmony 没生效**：设 `HarmonyInstance.DEBUG = true`，看桌面生成的日志；或者方法太小被 JIT 内联了。
- **1.7 专用**：用 `-mod=` 启动参数只加载你自己的 Mod，快速确认是不是被别的 Mod 干扰；用 Frame Profiler 看自己的 tick 开销。
- **习惯**：改 XML 前先备份或 `git commit`；一次只改一个变量；崩溃先看 Player.log 最后 50 行。

---

## 7. 发布前检查

- [ ] `packageId` 唯一且全小写（格式 `作者名.mod名`）
- [ ] `supportedVersions` 填对；1.7 测试过就加 `1.7`
- [ ] Mod 依赖写在 `<modDependencies>`，加载顺序写在 `<loadAfter>` / `<loadBefore>`
- [ ] 用 `MayRequire` 处理「只有装了某 DLC 才加载」的内容，避免红字
- [ ] 多语言：`Languages/` 下至少留 `ChineseSimplified`，硬编码中文 label 是不好的习惯
- [ ] 贴图路径与文件名大小写完全一致
- [ ] 上传创意工坊后，用自己的 Mod + 常见大型 Mod 组合再测一次

---

## 8. 本机环境实测结果

| 项目 | 状态 |
|---|---|
| 游戏路径 | `F:\SteamLibrary\steamapps\common\RimWorld` |
| 版本 | **1.7.4990 rev1069**（unstable 分支，Waylith 引擎已就位：`Waylith.dll`、`Waylith.UnityCompat.dll`、`UnityStubs.dll` 都在根目录） |
| 参照程序集 | 1.7 把 `Assembly-CSharp.dll` 放在了**安装根目录**，不再是 `RimWorldWin64_Data/Managed/` |
| 本地 Mod 目录 | `...\RimWorld\Mods\`（原来为空，现已放入测试 Mod） |
| 创意工坊目录 | `F:\SteamLibrary\steamapps\workshop\content\294100`（有一批已订阅 Mod，可拆开当范例） |
| 扩展包 | **未安装**（`Data/` 只有 Core），写示例时避开 DLC 内容 |
| Harmony | 已启用（`brrainz.harmony`），C# patch 依赖已满足 |
| .NET SDK | **已安装 10.0.403**（`C:\Users\Shuo-\.dotnet`，用户级安装，无需管理员） |
| Python | 3.13 可用（可以用来自动校验 XML、批量改 Def） |

### 已经替你搭好的骨架

```
E:\Desktop-files\py\riamworldmod\
├─ RimWorld-Mod制作准备指南.md      ← 本文档
├─ deploy.bat                       ← 一键同步所有 Mod 到游戏目录（robocopy）
├─ tools\
│  ├─ gen-assets.py                 ← 绕过 NuGet restore 的构建辅助
│  └─ ApiDump\                      ← 自己写的程序集元数据查看器（替代反编译器）
└─ mods\
   ├─ HelloRimWorld\                ← XML 最小练习：Patch + Def + 翻译
   └─ GridThermal\                  ← 正式项目：电网负载面板 + 温控分组（C#）
      ├─ About\ Defs\ Patches\ Languages\
      ├─ Assemblies\GridThermal.dll ← 编译产物
      └─ Source\GridThermal\        ← 源码（游戏不读）
```

两个 Mod 都已写进 `ModsConfig.xml` 的启用列表（原文件备份为 `ModsConfig.xml.bak`），启动游戏即可看到。

**验证清单**
1. 启动游戏 → 主菜单 Mods 列表里能找到 **Hello RimWorld**，且没有红字。
2. 开发者模式 → 打开「生成物品」→ 搜 `HelloCrystal` → 名称应显示为「测试水晶」（说明 Languages 生效）。
3. 随便找把栓动步枪 → 信息页射程应为 **50**（原版 36.9，说明 Patch 生效）。
4. 打开 `Player.log`，搜 `HelloCrystal`，不应出现 `Could not find` 之类报错。

> 改完 XML 后必须：**关闭游戏 → 双击 `deploy.bat` → 重新启动游戏**。RimWorld 不会热重载 Mod 内容，游戏运行中改文件会被覆盖。

### 下一步

1. 启动游戏验一次：底栏应多出「电网」标签页（中文语言下），点开看两个子页。
2. 若报错，把 `Player.log` 里 `GridThermal` 附近的堆栈贴给我，我按日志修。
3. 想继续加功能的话，优先级建议：按房间取温（而非按格）→ 分组跟随时间表/日夜 → 电网历史曲线。

---

## 9. C# 开工手册（本机实测）

这一节全部是在你这台机器上踩出来的，照抄能省几小时。

### 工具链
- **.NET SDK 10.0.403**，装在 `C:\Users\Shuo-\.dotnet`（用户级，不需要管理员）。
- 编辑器用 VS Code 即可，`dotnet build` 命令行编译，不需要装 40 GB 的 Visual Studio。
- 没有 NuGet 工具也能反编译：本仓库的 `tools/ApiDump` 用 SDK 自带的 `System.Reflection.Metadata` 直接读程序集元数据，列出任意类的字段/属性/方法签名。用法：
  ```bash
  dotnet tools/ApiDump/bin/Debug/net10.0/ApiDump.dll \
    "F:/SteamLibrary/steamapps/common/RimWorld/Assembly-CSharp.dll" \
    RimWorld.PowerNet Verse.Map
  # 模糊找类名：加 --find:WindowDrawing
  ```

### 两个必须知道的坑

**坑 1：`dotnet build` 会报 `Value cannot be null. (Parameter 'path1')`**
不是你的项目有问题，是这台机器的 `LOCALAPPDATA` 环境变量为空，NuGet 解析用户目录时炸了。表现为任何项目（哪怕零依赖）都无法 restore，`dotnet tool install` 同样失败。
绕过方案：**不跑 restore**。用 `tools/gen-assets.py` 生成一份空的 `obj/project.assets.json`，然后：
```bash
python tools/gen-assets.py mods/GridThermal/Source/GridThermal net10.0
cd mods/GridThermal/Source/GridThermal
"C:\Users\Shuo-\.dotnet\dotnet.exe" build --no-restore
```
只要项目不引用任何 NuGet 包（我们引用的全是游戏本地的 DLL），这套流程完全够用。

**坑 2：1.7 的 `Window` 构造函数变了**
```
Window(IWindowDrawing)      // 1.7 新增，必须传绘制实现
```
老教程里 `class MyWindow : Window { public MyWindow() { ... } }` 的写法在 1.7 会编译失败。两条路：
- 传 `new DefaultWindowDrawing()`（`Verse` 命名空间下有这个实现）；
- **更省事**：不做独立窗口，改成 `MainTabWindow` 子类挂在底栏标签页上。`MainTabWindow` 仍有无参构造，UI 代码只写 `DoWindowContents`，不用碰绘制后端。本仓库的 `MainTabWindow_Console` 走的就是这条路。

### 编译配置要点
```xml
<TargetFramework>net10.0</TargetFramework>
<AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
<GenerateDependencyFile>false</GenerateDependencyFile>
<!-- 引用，全部 Private=false，绝不能打进 Assemblies -->
Assembly-CSharp.dll   Waylith.dll   UnityStubs.dll
```
1.7 的这三个 DLL 都在**游戏安装根目录**。

### 代码里最常查的几个入口
| 想做的事 | 入口 |
|---|---|
| 遍历全图电网 | `map.powerNetManager.AllNetsListForReading` |
| 单网发电/耗电 | 遍历 `net.powerComps`，按 `CompPowerTrader.PowerOutput` 正负分类 |
| 电池余量 | `CompPowerBattery.StoredEnergy` / `Props.storedEnergyMax` |
| 改机器目标温度 | `CompTempControl.TargetTemperature`（setter 会同步 gizmo 显示） |
| 机器所在格温度 | `Thing.AmbientTemperature` |
| 加底栏按钮 | `MainButtonDef` + `tabWindowClass` 指向你的 `MainTabWindow` |

## 10. 参考资料

- RimWorld Modding Wiki（新手主线）<https://rimworldmodding.wiki.gg/>
- 官方 Modding Tutorials 目录 <https://rimworldwiki.com/wiki/Modding_Tutorials>
- 官方 Harmony 教程 <https://www.rimworldwiki.com/wiki/Modding_Tutorials/Harmony>
- 1.7 / Waylith 官方公告中文转述 <https://keylol.com/forum.php?mod=viewthread&tid=1052348>
- 项目模板 <https://github.com/Anarcho/RimModTemplate>
- 中文维基 MOD 教程 <https://rimworld.huijiwiki.com/wiki/MOD教程/XML文件结构>
- B 站教程系列 <https://www.bilibili.com/read/readlist/rl11884>
