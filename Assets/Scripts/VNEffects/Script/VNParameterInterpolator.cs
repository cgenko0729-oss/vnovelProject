using System;
using System.Collections.Generic;
using System.Text;

namespace VNEffects
{
    /// <summary>把文本中的 ${参数名} 替换为当前 call 的只读参数值（单层、不递归）。</summary>
    public static class VNParameterInterpolator
    {
        public static string Interpolate(
            string source,
            IReadOnlyDictionary<string, string> values,
            Action<string> missing = null)
        {
            if (string.IsNullOrEmpty(source) || values == null) return source;
            int marker = source.IndexOf("${", StringComparison.Ordinal);
            if (marker < 0) return source;

            var result = new StringBuilder(source.Length + 16);
            int cursor = 0;
            while (marker >= 0)
            {
                result.Append(source, cursor, marker - cursor);
                int close = source.IndexOf('}', marker + 2);
                if (close < 0)
                {
                    result.Append(source, marker, source.Length - marker);
                    return result.ToString();
                }

                string name = source.Substring(marker + 2, close - marker - 2);
                string value;
                if (name.Length == 0 || !values.TryGetValue(name, out value))
                {
                    if (missing != null) missing(name);
                    result.Append(source, marker, close - marker + 1);
                }
                else
                {
                    result.Append(value);
                }

                cursor = close + 1;
                marker = source.IndexOf("${", cursor, StringComparison.Ordinal);
            }
            result.Append(source, cursor, source.Length - cursor);
            return result.ToString();
        }
    }
}
