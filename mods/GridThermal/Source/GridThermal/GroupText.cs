namespace Shuo.GridThermal
{
    /// <summary>
    /// 分组名的长度约束与显示截断。温控组和电力组共用一份规则，
    /// 免得两个类里各写一遍、哪天改了一边忘了另一边。
    /// </summary>
    internal static class GroupText
    {
        /// <summary>允许保存的最长组名。</summary>
        public const int MaxNameLength = 24;

        /// <summary>
        /// 规范化用户输入：去首尾空白、压到上限以内。
        ///
        /// 截断时避开代理对：`Length` 数的是 UTF-16 单元，一个 emoji 占两个。
        /// 正好切在中间会得到半个字符，显示成方块，存档里也会留一串无效码位。
        /// </summary>
        public static string ClampInput(string name)
        {
            if (name == null)
                return "";

            string s = name.Trim();
            if (s.Length <= MaxNameLength)
                return s;

            int cut = MaxNameLength;
            if (char.IsHighSurrogate(s[cut - 1]))
                cut--;
            return s.Substring(0, cut);
        }

        /// <summary>列宽有限，超长时截断并补省略号（同样避开代理对）。</summary>
        public static string ForColumn(string name, int max)
        {
            if (string.IsNullOrEmpty(name))
                return "";
            if (name.Length <= max)
                return name;

            int cut = max;
            if (char.IsHighSurrogate(name[cut - 1]))
                cut--;
            return name.Substring(0, cut) + "…";
        }
    }
}
