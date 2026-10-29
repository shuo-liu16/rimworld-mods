using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Shuo.WorkSorter
{
    /// <summary>
    /// 全局设置（跟 Mod 走，不跟存档走）。
    /// 每份存档相关的状态在 WorkSorterGameComponent 里。
    /// </summary>
    public class WorkSorterSettings : ModSettings
    {
        public bool autoApply = true;
        public int intervalDays = 1;
        public int maxTopPerPawn = 3;
        public bool takeOverDisabled = false;
        public bool notifyOnApply = false;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref autoApply, "autoApply", true);
            Scribe_Values.Look(ref intervalDays, "intervalDays", 1);
            Scribe_Values.Look(ref maxTopPerPawn, "maxTopPerPawn", 3);
            Scribe_Values.Look(ref takeOverDisabled, "takeOverDisabled", false);
            Scribe_Values.Look(ref notifyOnApply, "notifyOnApply", false);

            // 手改过配置文件、或者换版本带进来越界值时，界面上的 +/- 会卡在边界
            // 出不去（比如已是 99，加号无效）。读进来就夹紧一次。
            if (intervalDays < 1) intervalDays = 1;
            if (intervalDays > 7) intervalDays = 7;
            if (maxTopPerPawn < 1) maxTopPerPawn = 1;
            if (maxTopPerPawn > 8) maxTopPerPawn = 8;
        }
    }

    public class WorkSorterMod : Mod
    {
        public static WorkSorterSettings Settings;

        public WorkSorterMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<WorkSorterSettings>();

            try
            {
                new Harmony("shuo.worksorter").PatchAll();
                Log.Message("[WorkSorter] 已加载，工作优先级自动排序已就绪。");
            }
            catch (Exception e)
            {
                SafeLog.Report("Harmony 补丁", e);
            }
        }

        public override string SettingsCategory()
        {
            return "工作优先级排序";
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            try
            {
                DrawSettings(inRect);
            }
            catch (Exception e)
            {
                SafeLog.Report("设置页绘制", e);
                Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 48f),
                    "设置页渲染出错，已跳过。游戏不受影响，详情见 Player.log。");
            }
        }

        private void DrawSettings(Rect inRect)
        {
            Listing_Standard list = new Listing_Standard();
            list.Begin(inRect);

            list.CheckboxLabeled("启用定时自动排序", ref Settings.autoApply,
                "每隔若干天按当前情况重排一次。关掉后只能用底栏「工作」页里的按钮手动应用。");

            DrawStepRow(list, "刷新间隔", ref Settings.intervalDays, 1, 7, " 天",
                "太频繁会让小人来回换活干，1 天是够用的节奏。");

            DrawStepRow(list, "每人第一档上限", ref Settings.maxTopPerPawn, 1, 8, " 项",
                "限制一个人最多能占几个「1」。防止某个全能选手把所有活都排成最高优先。");

            list.GapLine();

            list.CheckboxLabeled("接管未启用的工作", ref Settings.takeOverDisabled,
                "默认关闭。开启后，原本被你手动关掉（优先级空白）的工作也会被重新排上。慎用。");

            list.CheckboxLabeled("应用后弹出提示", ref Settings.notifyOnApply,
                "关闭时静默应用，只在日志里留一行记录。");

            list.GapLine();

            list.Label("说明：排序只改「优先级数字」，不会增加或删除工作类型。");
            list.Label("所有改动都可以在工作页里逐格锁定，锁定的格子永不被自动覆盖。");

            list.End();
        }

        private static void DrawStepRow(Listing_Standard list, string label, ref int value,
            int min, int max, string unit, string tip)
        {
            value = Mathf.Clamp(value, min, max);

            list.Label(label + "：" + value + unit);
            Rect row = list.GetRect(30f);
            float bw = 80f;
            if (Widgets.ButtonText(new Rect(row.x, row.y, bw, 28f), "−"))
                value = Mathf.Max(min, value - 1);
            if (Widgets.ButtonText(new Rect(row.x + bw + 6f, row.y, bw, 28f), "+"))
                value = Mathf.Min(max, value + 1);

            if (!tip.NullOrEmpty())
                TooltipHandler.TipRegion(row, tip);
        }
    }

    /// <summary>
    /// 把存档级组件挂到 Game 上。Game.FinalizeInit 在新开局和读档结束时都会跑，
    /// 在这一步之前 Add 进去，组件就会跟着这次初始化一起被 Scribe。
    /// </summary>
    [HarmonyPatch(typeof(Game), nameof(Game.FinalizeInit))]
    internal static class Patch_Game_FinalizeInit
    {
        private static void Prefix(Game __instance)
        {
            try
            {
                if (__instance.components == null)
                    return;
                if (__instance.GetComponent<WorkSorterGameComponent>() == null)
                    __instance.components.Add(new WorkSorterGameComponent());
            }
            catch (Exception e)
            {
                SafeLog.Report("注册存档组件", e);
            }
        }
    }
}
