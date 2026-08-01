namespace VNEffects
{
    /// <summary>剧本位置地址：本文件 label，或“文件::label”。</summary>
    public static class VNStoryAddress
    {
        public static bool TryParse(
            string address,
            out string file,
            out string label,
            out string error)
        {
            file = null;
            label = null;
            error = null;
            if (string.IsNullOrWhiteSpace(address))
            {
                error = "跳转地址不能为空";
                return false;
            }

            string value = address.Trim();
            int separator = value.IndexOf("::", System.StringComparison.Ordinal);
            if (separator < 0)
            {
                label = value;
                return true;
            }
            if (value.IndexOf("::", separator + 2, System.StringComparison.Ordinal) >= 0)
            {
                error = "限定地址只能包含一个 ::";
                return false;
            }

            file = value.Substring(0, separator).Trim();
            label = value.Substring(separator + 2).Trim();
            if (file.Length == 0 || label.Length == 0)
            {
                error = "限定地址必须写成“文件::label”";
                return false;
            }
            return true;
        }

        public static string NormalizeFile(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;
            string normalized = name.Trim();
            if (normalized.EndsWith(".txt", System.StringComparison.OrdinalIgnoreCase))
                normalized = normalized.Substring(0, normalized.Length - 4);
            if (normalized.EndsWith(".vn", System.StringComparison.OrdinalIgnoreCase))
                normalized = normalized.Substring(0, normalized.Length - 3);
            return normalized.ToLowerInvariant();
        }
    }
}
