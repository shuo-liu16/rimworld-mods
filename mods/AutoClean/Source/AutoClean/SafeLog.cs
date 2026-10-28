using System;
using System.Collections.Generic;
using Verse;

namespace Shuo.AutoClean
{
    /// <summary>
    /// 统一的错误上报口。同一标签最多输出 3 次，避免高频路径刷屏。
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
            Log.Error("[AutoClean] " + tag + " 出错" + tail + ":\n" + e);
        }
    }
}
