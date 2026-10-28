using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Shuo.AutoClean
{
    public class AutoCleanMod : Mod
    {
        public static AutoCleanSettings Settings;

        public AutoCleanMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<AutoCleanSettings>();

            // 补丁失败不应该让整个 Mod 加载失败——那样连设置页都进不去，
            // 反而更难排查。这里记一条错误，Mod 仍然可用（只是不自动清扫）。
            try
            {
                new Harmony("shuo.autoclean").PatchAll();
                Log.Message("[AutoClean] 已加载，每日自动清扫补丁挂接成功。");
            }
            catch (System.Exception e)
            {
                SafeLog.Report("Harmony 补丁", e);
            }
        }

        public override string SettingsCategory()
        {
            return "自动清扫";
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            // 设置页画挂了会连带整个选项界面异常，这里兜住并给出可读提示。
            try
            {
                DrawSettings(inRect);
            }
            catch (System.Exception e)
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

            list.CheckboxLabeled("启用每日自动清扫", ref Settings.enabled,
                "关闭后只保留手动清扫按钮。");

            list.GapLine();

            string hh = Settings.hour.ToString("00") + ":00";
            list.Label("每天清扫时刻：" + hh);
            Rect row = list.GetRect(30f);
            float bw = (row.width - 8f) / 2f;
            if (Widgets.ButtonText(new Rect(row.x, row.y, bw, 28f), "−1 小时"))
                Settings.hour = (Settings.hour + 23) % 24;
            if (Widgets.ButtonText(new Rect(row.x + bw + 8f, row.y, bw, 28f), "+1 小时"))
                Settings.hour = (Settings.hour + 1) % 24;

            list.GapLine();

            list.CheckboxLabeled("只清扫居住区", ref Settings.homeOnly,
                "勾选后只处理居住区内的污渍，矿区和野外保持原样。");
            list.CheckboxLabeled("清扫后弹出提示", ref Settings.notify,
                "关闭则静默清扫。");

            list.GapLine();

            if (list.ButtonText("立即清扫一次"))
            {
                int n = AutoCleanUtility.CleanNow();
                if (Current.Game != null)
                    Messages.Message("手动清扫完成，清除了 " + n + " 处污渍。", MessageTypeDefOf.NeutralEvent, false);
            }

            list.Label("说明：清扫直接移除污渍实体，不需要小人动手，也不消耗任何资源。");

            list.End();
        }
    }
}
