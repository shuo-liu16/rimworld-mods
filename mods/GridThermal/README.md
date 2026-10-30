# Grid & Thermal Console（电网与温控）

给 RimWorld 1.7 加一个底栏标签页：**集中查看电力负载 + 给加热器/制冷器分组控温 + 给电力设备分组开关**。

写这个 Mod 的起因是两个具体的烦点：
1. 原版只有点开某一根电线时才能看到**那一个**电网的数据，殖民地铺开以后想知道「哪个电网快撑不住了」得挨个点。
2. 十几个加热器、制冷器各自设温度，想让整个基地恒温 21°C 就得挨个调，漏一个就出现局部过冷/过热。

---

## 功能

### 电力网络
底栏「电网」（排在「建筑师」右边），点开进「电力网络」页，列出全图每一个独立电网：

| 列 | 含义 |
|---|---|
| 发电 | 该电网内所有 `CompPowerTrader.PowerOutput >= 0` 的机器输出之和 |
| 耗电 | 所有 `PowerOutput < 0` 的机器耗电之和（取正值） |
| 净 | 发电 − 耗电，正数蓝色语义、负数红字 |
| 储能 | 所有电池 `StoredEnergy` 之和 / `storedEnergyMax` 之和 |
| 电量 | 电池总余量百分比 |
| 状态 | 正常 / 放电中（在烧电池）/ 缺电（无电源或电池也空了） |

顶部一行是全图汇总。

> 为什么按 `PowerOutput` 自己求和，而不用 `PowerNet.CurrentEnergyGainRate()`：后者只反映发电侧，语义容易和「净增益」混淆。自己按正负号分类求和，发电/耗电两个数一定自洽，且和游戏内电力条显示的数值对得上。

### 温控分组
1. 选中任意一台**加热器**或**制冷器** → 出现按钮「温控分组：未分组」。
2. 点它 → 选已有分组，或「新建分组」（自动命名「温控组 1」「温控组 2」…）。
3. 切到底栏「电网」→「温控分组」页：每组一行，可调模式、目标温度（`-5` / `-1` / `+1` / `+5`）、冷季温度，点「选中」在场景里同时选中组内设备，点「解散」清除分组。

同组的机器共用同一套设定，由 Mod 周期性校准（原版 `CompTempControl` 的逻辑不变，所以断电、EMP、高温停机这些原版行为全都保留）。

> 校准挂在 `CompTickRare` 上而不是 `CompTick`——加热器和制冷器在 Core 里是 `tickerType=Rare`，只会走 Rare 分支。这一点踩过一次：最初写在 `CompTick` 里，编译通过但永远不执行。

#### 两种控温模式（点「模式」按钮切换）

| 模式 | 行为 | 适合 |
|---|---|---|
| **统一** | 所有设备用同一个目标温度 | 冷库、单一房间 |
| **季节** | 热季**只开制冷设备**（目标 = 热季温度）、加热器**直接断电**；冷季反过来 | 有明显四季的基地 |

**季节模式的「关掉」是真断电**：通过 `CompFlickable.SwitchIsOn` 关掉开关，不是把目标改成 −273。后者设备虽然不工作，但仍有待机功耗，而且目标栏会显示一个离谱的数字。

> 这里有个坑：**不能直接写 `CompPowerTrader.PowerOn = false`**。加热器和制冷器都带 `CompProperties_Flickable`，它的轮询会按 `wantSwitchOn`（默认 true）把电源又打开，等于白关。必须走 `CompFlickable.SwitchIsOn`，它同时更新 `wantSwitchOn` 和实际供电。

断电由 Mod 自己记账（`powerCutByMod`），所以：
- 换季恢复正常时，只会把**被 Mod 关掉的**设备重新通电，不会把你手动关掉的机器打开；
- 解散分组时也会把电源还原。

> 入组时还会记下这台机器原本的目标温度，解散或移出分组时还原——否则设备会停在分组期间的设定上，而 `ApplyTarget` 在未分组时是不管的。

**季节判定**用的是 `map.mapTemperature.SeasonalTemp`（季节平均温度），不是瞬时室外温度——后者一天之内波动太大，会导致设备频繁通断。

#### 典型配置

- **冷库**：单独一组，模式「统一」，目标 −5°C。全年恒温。
- **生活区（省心）**：制冷器 + 加热器同组，模式「统一」，目标 21°C。原版逻辑就是「高于目标制冷、低于目标加热」，天热空调介入、天冷加热器接手，本来就不会打架。
- **生活区（严格四季）**：模式「季节」，热季 24°C、冷季 20°C。夏天加热器彻底断电，冬天空调彻底断电，不会有任何待机功耗。

#### 能覆盖哪些设备

判断标准只有一个：**这台设备是否用原版的 `CompTempControl` 组件来实现温控**。

- **能**：原版 `Heater`、`Cooler`；以及绝大多数 Mod 的空调/暖气建筑（它们基本都复用 `CompTempControl`）。
  Mod 设备不需要你手动加补丁——Mod 启动时会自动扫描所有 `ThingDef`，凡是带 `CompTempControl` 的都挂上分组组件，并打一条日志：`已为 N 种温控设备自动挂载分组组件`。
- **不能**：不走 `CompTempControl` 的升降温方式——原版被动降温器（PassiveCooler）、篝火/蒸汽喷口的 `CompHeatPusher`、发电机和机器自身的发热、通风口。这些要么没有「目标温度」这个概念，要么根本不是建筑。

补充一点：原版里真正用 `CompTempControl` 的**只有 Heater 和 Cooler 两个**（我扫过 `Data/Core/Defs` 确认）。所以「覆盖所有设备」这件事，实际价值主要在兼容第三方 Mod 的温控建筑上。

对于类型认不出来的第三方设备（既不是 `Building_Cooler` 也不是 `Building_Heater` 的子类），季节模式不会去断它的电，只让它跟随当前季节的目标温度——宁可少控制，也不要用错方向。

#### 选中（仅温控页）

温控页每行右侧有「选中」按钮，一次选中组内全部加热器/制冷器（不切页，面板保留），可以直接在信息栏里看状态，或用原版按钮批量操作。

电力页没有这个按钮——那一路的设备数量动辄几十上百，全选反而没法用；想看某张电网的详情，直接点地图上的电缆即可。

### 电力分组

给发电与用电设备分组，整组一键开关，组名可以自己起。

1. 选中任意会发电或会耗电的建筑 → 出现按钮「电力分组：未分组」。
2. 点它 → 选已有分组，或「新建分组」（自动命名「电力组 1」「电力组 2」…）。
3. 切到底栏「电网」→「电力分组」页：每组一行。

| 列 | 含义 |
|---|---|
| 成员 | 组内建筑数量 |
| 发电 / 耗电 | 组内 `PowerOutput` 按正负号分别求和（关掉的设备计 0） |
| 储能 | 组内电池 `StoredEnergy` 之和 / `storedEnergyMax` 之和 |
| 状态 | 运行中 / 部分开启 / 已关闭 |
| 开关 | 全组一键开或关；只要还有一台开着，点它就是整组关闭 |
| 改名 | 自定义组名，最多 24 字，不允许重名（重名会提示并保持对话框打开） |
| 选中 | 一次选中组内全部建筑 |
| 解散 | 清空分组，不改变设备的开关状态 |

开关同样走 `CompFlickable.SwitchIsOn`，理由和温控那节完全一样——直接写 `PowerOn` 会被 flickable 的轮询覆盖回去。

**电池只参与统计，没有开关**：`CompPowerBattery` 不是 `CompPowerTrader`，没有电源开关可切。整组只有电池时，开关按钮显示「不可开关」。

组状态是**从成员推导**的，不额外存字段：全部开着 = 运行中，全关 = 已关闭，混合 = 部分开启。代价是多一次遍历，好处是你在场景里手动关掉其中一台，面板状态会立刻跟上，不会出现「面板说开着、实际是关的」。

> 挂载判定按 `compClass` 而不是属性类：1.7 里 `CompPowerTrader` 的属性类就叫 `CompProperties_Power`，电线等也用同一个类，光看属性类分不出来。用 `IsAssignableFrom` 还能覆盖第三方 Mod 继承 `CompPowerTrader` 的设备。电线（`PowerConduit`）不发电也不耗电，不在范围内。

电力分组和温控分组是两条独立的线：**温控管目标温度，电力管电源开关**。同一台加热器可以同时属于两个组。但注意两者都会动电源——温控组在季节模式下的断电，和电力组的「全部关闭」可能互相打架，所以同一台设备不建议同时用两种自动策略。


---

## 性能

面板只在打开时绘制，但 `DoWindowContents` 是每帧调用的（60fps），所以所有「算数据」的活儿都挪到了缓存里：

| 位置 | 策略 | 说明 |
|---|---|---|
| 电力表 | 15 tick（0.25s）重建 | 行文本、状态色、汇总行一次性算好存成字符串数组，绘制阶段直接读 |
| 温控分组 | 30 tick（0.5s）重建 | 全图 `AllThings` 扫一次，同时算好成员数/平均室温/目标温度的显示字符串并排好序 |
| 电力分组 | 30 tick（0.5s）重建 | 同一套缓存结构；统计时每台设备只取一次组件引用（`Flickable`/`PowerTrader`/`Battery`），把 7 次 `TryGetComp` 压到 3 次 |
| 点击 ±1 | 不走缓存 | 直接改 `target` 并刷新 `targetText`，下一帧立刻生效，不需要等缓存过期 |
| 建组 / 解散 | 主动 `Invalidate` | 避免新组有半秒延迟才出现 |

关键取舍：**绘制阶段零计算、零字符串拼接**。如果每帧现算，10 个电网 × 7 列就是 70 次字符串分配/帧，约 4000 次/秒，GC 压力会体现在长局掉帧上。现在稳态下只有滚动和控件绘制的固定开销。

`CompThermalGroup` 侧的开销也很低：加热器/制冷器是 `tickerType=Rare`，每 250 tick 才校准一次，且只在温度真的偏离时才写 setter（`Mathf.Approximately` 先判）。

## 健壮性与日志

面板是纯展示 + 少量写入，所以设计原则是：**任何一处出错都只影响它自己**。

| 边界 | 兜住之后的后果 |
|---|---|
| `DoWindowContents` | 本帧画一行错误提示，切到别的标签页、继续游戏都不受影响 |
| `PostSpawnSetup` | 建筑照常生成，只是这一次没校准温度（这里抛异常会打断地图加载，必须挡） |
| `ApplyTarget` | 单台机器失败，同组其它机器照常 |
| `CompGetGizmosExtra` | 少一个按钮，不影响原版的温度/断电按钮 |
| gizmo 回调（打开分组菜单） | 菜单不弹出，游戏继续 |

日志标签（`Player.log` 里搜 `GridThermal`）：

- `[GridThermal] 已加载：电网面板 + 温控分组。` —— 正常。**如果没有这一行，说明 Mod 压根没加载**，别在代码里找原因，去查 DLL 路径 / About.xml / 版本号。
- `[GridThermal] 已为 N 种电力设施自动挂载分组组件。` —— 电力分组的挂载数量。N 明显偏小（比如只有 2）说明判定没匹配上建筑。
- `[GridThermal] 面板绘制 出错` —— UI 层异常，会附堆栈和出错时的异常类型。
- `[GridThermal] 校准目标温度 出错` —— 某台机器的 `CompTempControl` 拿不到。
- `[GridThermal] 切换电力分组开关 出错` —— 某台设备既没有 `CompFlickable` 也没有 `CompPowerTrader`。

同一位置最多输出 3 条，之后静默（否则每帧一条会瞬间刷满日志、反而拖慢游戏）。要重新触发日志，重启游戏即可。

## 已知限制

- **室温是近似值**：取机器所在格的环境温度（`Thing.AmbientTemperature`）。嵌在墙里的制冷器，这个值可能读到室外那侧。要看准确室温点开房间看。
- 玩家如果手动用原版 gizmo 改了某台机器的温度，最多 250 tick（约 4 秒，取决于 `tickerType`）会被组设定覆盖回去。
- 分组名是字符串，解散后同名重建视为新组。
- 只处理 `Heater` / `Cooler` 两个原版 Def。被动降温器（PassiveCooler）没有电力也没有温控组件，不适用；其它 Mod 的温控建筑默认可通过给它加同一个 Comp 来接入。

---

## 目录结构

```
GridThermal/
├─ About/About.xml                       packageId = shuo.gridthermal
├─ Assemblies/GridThermal.dll            编译产物（游戏读这个）
├─ Defs/MainButtonDefs/                  底栏按钮
├─ Patches/01_ThermalGroupComp.xml       给 Heater / Cooler 加自定义 Comp
├─ Languages/ChineseSimplified/          按钮的中文名
└─ Source/GridThermal/                   C# 源码（游戏不读，可删）
   ├─ GridThermal.csproj
   ├─ CompThermalGroup.cs                Comp + 温控分组扫描逻辑
   ├─ CompPowerGroup.cs                  Comp + 电力分组扫描/改名/开关
   └─ MainTabWindow_Console.cs           三个页面的 UI
```

## 构建

```bash
# 一次性：生成 assets（本机 NuGet 解析异常时的绕过手段，见仓库根目录说明）
python ../../../tools/gen-assets.py . net10.0

# 每次改代码后
"C:\Users\Shuo-\.dotnet\dotnet.exe" build --no-restore
# 产物自动输出到 ../../Assemblies/GridThermal.dll

# 同步到游戏并重启 RimWorld
双击仓库根目录的 deploy.bat
```

目标框架 `net10.0`，引用游戏根目录的 `Assembly-CSharp.dll`、`Waylith.dll`、`UnityStubs.dll`（全部 `Private=false`，绝不能打进 Assemblies）。

## 调试

- 游戏日志：`%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Player.log`
- 搜 `GridThermal`：应有加载记录；出现 `Could not load class` 说明 DLL 没进 Assemblies 或命名空间写错。
- 搜 `Could not find comp class`：`Patches/01_ThermalGroupComp.xml` 里的 `Class` 全名不对。
- 底栏没有「电网」按钮：`MainButtonDefs` 的 XML 没被加载，或 `tabWindowClass` 全名不对。
