using System;
using System.Collections.Generic;
using Verse;

namespace Shuo.GridThermal
{
    /// <summary>
    /// 统一的错误上报口。
    /// 关键约束：同一个标签最多输出 3 次——tick 和 OnGUI 都是高频路径，
    /// 一次持续性错误如果不节流，会以每秒几十行的速度写日志，反而把游戏拖死。
    /// </summary>
    internal static class SafeLog
    {
        private const int MaxPerTag = 3;
        private static readonly Dictionary<string, int> counts = new Dictionary<string, int>();

        public static void Report(string tag, Exception e)
        {
            int n;
            counts.TryGetValue(tag, out n);
            n++;
            counts[tag] = n;

            if (n > MaxPerTag)
                return;

            string tail = n == MaxPerTag ? "（同一位置后续错误不再输出）" : "";
            Log.Error("[GridThermal] " + tag + " 出错" + tail + ":\n" + e);
        }
    }
}
