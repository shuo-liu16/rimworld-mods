using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Shuo.GridThermal
{
    /// <summary>
    /// 程序集加载后会走一次静态构造，做两件事：
    /// 1. 打一条锚点日志（有它才说明 Mod 真被加载了）；
    /// 2. 给所有带 CompTempControl 的 ThingDef 自动挂上分组组件。
    ///
    /// 第 2 点是为了覆盖「所有能升降温的设备」——原版和绝大多数 Mod 的温控建筑
    /// 都靠 CompTempControl 实现，所以不必为每个 Mod 单独写 Patch。
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class Bootstrap
    {
        static Bootstrap()
        {
            Log.Message("[GridThermal] 已加载：电网面板 + 温控分组。");

            try
            {
                AutoAttachToTempDevices();
            }
            catch (Exception e)
            {
                // 自动挂载失败也不影响 Heater / Cooler 的 XML 补丁已经生效的那部分。
                SafeLog.Report("自动挂载温控组件", e);
            }

            try
            {
                AutoAttachToPowerDevices();
            }
            catch (Exception e)
            {
                SafeLog.Report("自动挂载电力分组组件", e);
            }
        }

        /// <summary>
        /// 给所有会发电、会耗电、会储电的建筑挂上电力分组组件。
        ///
        /// 判定按 compClass 而不是 CompProperties 的类型：1.7 里 CompPowerTrader 的
        /// 属性类就叫 CompProperties_Power，电线等也用同一个类，光看属性类分不出来；
        /// 而 IsAssignableFrom 还能覆盖第三方 Mod 继承 CompPowerTrader 的设备。
        /// </summary>
        private static void AutoAttachToPowerDevices()
        {
            int added = 0;
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefs)
            {
                if (def.category != ThingCategory.Building)
                    continue;

                List<CompProperties> comps = def.comps;
                if (comps == null || comps.Count == 0)
                    continue;

                bool hasPower = false;
                bool hasOurs = false;
                for (int i = 0; i < comps.Count; i++)
                {
                    CompProperties cp = comps[i];
                    if (cp == null || cp.compClass == null)
                        continue;
                    if (cp.compClass == typeof(CompPowerGroup))
                    {
                        hasOurs = true;
                    }
                    else if (typeof(CompPowerTrader).IsAssignableFrom(cp.compClass)
                        || typeof(CompPowerBattery).IsAssignableFrom(cp.compClass))
                    {
                        hasPower = true;
                    }
                }

                if (!hasPower || hasOurs)
                    continue;

                comps.Add(new CompProperties_PowerGroup());
                added++;
            }

            if (added > 0)
                Log.Message("[GridThermal] 已为 " + added + " 种电力设施自动挂载分组组件。");
        }

        private static void AutoAttachToTempDevices()
        {
            int added = 0;
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefs)
            {
                List<CompProperties> comps = def.comps;
                if (comps == null || comps.Count == 0)
                    continue;

                bool hasTempControl = false;
                bool hasOurs = false;
                for (int i = 0; i < comps.Count; i++)
                {
                    CompProperties cp = comps[i];
                    if (cp == null)
                        continue;
                    if (cp is CompProperties_TempControl)
                        hasTempControl = true;
                    else if (cp.compClass == typeof(CompThermalGroup))
                        hasOurs = true;
                }

                if (!hasTempControl || hasOurs)
                    continue;

                comps.Add(new CompProperties_ThermalGroup());
                added++;
            }

            if (added > 0)
                Log.Message("[GridThermal] 已为 " + added + " 种温控设备自动挂载分组组件。");
        }
    }
}
