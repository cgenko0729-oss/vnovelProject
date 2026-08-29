using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// AI API Key 读取（**按供应商各一套**，三级回退）。
    ///
    /// ★★ 本模式定位为「本地开发 / 自用」，key 绝不打包进游戏、绝不进仓库。★★
    ///    仓库是公开的，.gitignore 已挡掉 /*ApiKey*.txt。
    ///    将来若要发行给玩家，**不要**沿用这套文件读取——必须改成
    ///    玩家自填（PlayerPrefs）或自建中转服务器，否则 key 会随包泄漏。
    ///    正因如此，Build 出来的包里这里永远读不到 key（下面 #if 挡掉了），
    ///    这是刻意的：宁可功能不可用，也不能让 key 有机会进包。
    ///
    /// 回退顺序（先命中先用，以 Gemini 为例，DeepSeek 同构）：
    ///   1. 环境变量 GEMINI_API_KEY                       ← 最安全，推荐
    ///   2. 仓库外   &lt;项目上级目录&gt;/GeminiAiApiKey.txt   ← 次选，物理隔离
    ///   3. 仓库内   &lt;项目根&gt;/GeminiAiApiKey.txt         ← 最方便，靠 .gitignore 兜底
    ///
    /// 名字从 VNAiProviders.EnvVarFor / KeyFileFor 取，加供应商不用改这个文件。
    /// 读到的 key 只缓存在内存里，任何日志/报错都不会打印它（只打印来源）。
    /// **两家的 key 分开缓存**——只配了一家时另一家照样能正确报「没找到 key」。
    /// </summary>
    public static class VNAiKey
    {
        // 兼容旧代码的常量（当时只有 Gemini 一家）。新代码请用 VNAiProviders.EnvVarFor()
        public const string EnvVarName = "GEMINI_API_KEY";
        public const string FileName = "GeminiAiApiKey.txt";

        class Entry
        {
            public string key;
            public string source;
            public bool resolved;
        }

        static readonly Dictionary<VNAiProvider, Entry> _cache = new Dictionary<VNAiProvider, Entry>();

        // ──────────────── 对外 ────────────────

        /// <summary>默认供应商是否已经能拿到 key（不会暴露 key 本身）</summary>
        public static bool HasKey => HasKeyFor(VNAiProviders.GlobalDefault);

        public static bool HasKeyFor(VNAiProvider provider) => TryGet(provider, out _, out _);

        /// <summary>默认供应商的 key 来源描述（"环境变量" / 文件路径），用于报错提示，不含 key</summary>
        public static string Source => SourceFor(VNAiProviders.GlobalDefault);

        public static string SourceFor(VNAiProvider provider)
        {
            TryGet(provider, out _, out string src);
            return src;
        }

        /// <summary>取默认供应商的 key。</summary>
        public static bool TryGet(out string key, out string source) =>
            TryGet(VNAiProviders.GlobalDefault, out key, out source);

        /// <summary>
        /// 取某一家的 key。拿不到时返回 false，source 为 null。
        /// </summary>
        public static bool TryGet(VNAiProvider provider, out string key, out string source)
        {
            if (!_cache.TryGetValue(provider, out Entry e))
            {
                e = new Entry();
                _cache[provider] = e;
            }

            if (!e.resolved)
            {
                e.resolved = true;
                e.key = null;
                e.source = null;

#if UNITY_EDITOR || VN_AI_ALLOW_LOCAL_KEY
                string envName = VNAiProviders.EnvVarFor(provider);

                // 1) 环境变量
                try
                {
                    string env = Environment.GetEnvironmentVariable(envName);
                    if (!string.IsNullOrWhiteSpace(env))
                    {
                        e.key = Clean(env);
                        e.source = "环境变量 " + envName;
                    }
                }
                catch (Exception ex)
                {
                    // 某些平台读环境变量会抛权限异常，不致命，继续走文件
                    Debug.LogWarning($"[VNAi] 读环境变量 {envName} 失败：{ex.GetType().Name}");
                }

                // 2) / 3) 文件
                if (string.IsNullOrEmpty(e.key))
                {
                    foreach (string path in CandidatePaths(provider))
                    {
                        string v = ReadFileSafe(path);
                        if (string.IsNullOrEmpty(v)) continue;
                        e.key = v;
                        e.source = path;
                        break;
                    }
                }
#endif
            }

            key = e.key;
            source = e.source;
            return !string.IsNullOrEmpty(key);
        }

        /// <summary>拿不到 key 时给开发者看的完整指引（绝不含 key）</summary>
        public static string MissingKeyMessage() => MissingKeyMessage(VNAiProviders.GlobalDefault);

        public static string MissingKeyMessage(VNAiProvider provider)
        {
#if UNITY_EDITOR || VN_AI_ALLOW_LOCAL_KEY
            string name = VNAiProviders.DisplayName(provider);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[VNAi] 找不到 {name} API Key。按以下任一方式配置（越靠前越安全）：");
            sb.AppendLine($"  1. 设环境变量 {VNAiProviders.EnvVarFor(provider)}=<你的key>（设完要重启 Unity 才生效）");
            int i = 2;
            foreach (string path in CandidatePaths(provider))
                sb.AppendLine($"  {i++}. 建文件 {path}（内容就是一行 key）");
            sb.Append("注意：仓库是公开的，key 文件已被 .gitignore 挡掉，别手动 git add -f。");
            return sb.ToString();
#else
            return "[VNAi] Build 版本不读取本地 key 文件（防止 key 进包）。" +
                   "AI 自由聊天是开发期功能；要发行请改用玩家自填 key 或自建中转服务器。";
#endif
        }

        /// <summary>清缓存，下次重新查找（改了环境变量/换了文件后在编辑器里调）</summary>
        public static void Invalidate() => _cache.Clear();

        /// <summary>候选文件路径：先仓库外，再仓库内</summary>
        static IEnumerable<string> CandidatePaths(VNAiProvider provider)
        {
            string fileName = VNAiProviders.KeyFileFor(provider);

            // Application.dataPath = <项目根>/Assets
            string projectRoot = null;
            try { projectRoot = Directory.GetParent(Application.dataPath)?.FullName; }
            catch { }
            if (string.IsNullOrEmpty(projectRoot)) yield break;

            string outside = null;
            try { outside = Directory.GetParent(projectRoot)?.FullName; }
            catch { }

            if (!string.IsNullOrEmpty(outside))
                yield return Path.Combine(outside, fileName);   // 仓库外，推荐
            yield return Path.Combine(projectRoot, fileName);   // 仓库内，靠 .gitignore
        }

        static string ReadFileSafe(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                return Clean(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[VNAi] 读取 key 文件失败：{path}（{e.GetType().Name}）");
                return null;
            }
        }

        /// <summary>
        /// 去掉首尾空白与换行。key 里混进 \r\n 会让请求头非法，
        /// Windows 上用记事本存的文件几乎必然带 \r\n，所以这一步不能省。
        /// </summary>
        static string Clean(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            string s = raw.Trim().Trim('﻿', '​'); // 顺带吃掉 BOM / 零宽空格
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }
    }
}
