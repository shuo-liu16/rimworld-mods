using HarmonyLib;
using Verse;

namespace Shuo.AutoClean
{
    /// <summary>
    /// 挂在整个游戏的主 tick 上，而不是某个地图或建筑上，
    /// 这样无论玩家在看哪个地图、选中什么，计时都不会断。
    /// </summary>
    [HarmonyPatch(typeof(TickManager), nameof(TickManager.DoSingleTick))]
    public static class Patch_TickManager_DoSingleTick
    {
        public static void Postfix()
        {
            try
            {
                AutoCleanUtility.Tick();
            }
            catch (System.Exception e)
            {
                // 任何异常都不允许冒泡回游戏主循环，否则会污染整个 tick 链。
                SafeLog.Report("主循环钩子", e);
            }
        }
    }
}
