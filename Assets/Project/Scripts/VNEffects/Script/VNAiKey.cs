using System;
using System.IO;
using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// Gemini API Key 读取（三级回退）。
    ///
    /// ★★ 本模式定位为「本地开发 / 自用」，key 绝不打包进游戏、绝不进仓库。★★
    ///    仓库是公开的，.gitignore 已挡掉 /*ApiKey*.txt 与 /GeminiAiApiKey.txt。
    ///    将来若要发行给玩家，**不要**沿用这套文件读取——必须改成
    ///    玩家自填（PlayerPrefs）或自建中转服务器，否则 key 会随包泄漏。
    ///    正因如此，Build 出来的包里这里永远读不到 key（下面 #if 挡掉了），
    ///    这是刻意的：宁可功能不可用，也不能让 key 有机会进包。
    ///
    /// 回退顺序（先命中先用）：
    ///   1. 环境变量 GEMINI_API_KEY                       ← 最安全，推荐
    ///   2. 仓库外   &lt;项目上级目录&gt;/GeminiAiApiKey.txt   ← 次选，物理隔离
    ///   3. 仓库内   &lt;项目根&gt;/GeminiAiApiKey.txt         ← 最方便，靠 .gitignore 兜底
    ///
    /// 读到的 key 只缓存在内存里，任何日志/报错都不会打印它（只打印来源）。
    /// </summary>
    public static class VNAiKey
    {
        public const string EnvVarName = "GEMINI_API_KEY";
        public const string FileName = "GeminiAiApiKey.txt";

        static string _cached;
        static string _cachedSource;
        static bool _resolved;

        /// <summary>是否已经能拿到 key（不会暴露 key 本身）</summary>
        public static bool HasKey => TryGet(out _, out _);

        /// <summary>key 的来源描述（"环境变量" / 文件路径），用于报错提示，不含 key</summary>
        public static string Source
        {
            get { TryGet(out _, out string src); return src; }
        }

        /// <summary>
        /// 取 key。拿不到时返回 false，并给出人话提示（reason 可直接显示给开发者）。
        /// </summary>
        public static bool TryGet(out string key, out string source)
        {
            if (_resolved)
            {
                key = _cached;
                source = _cachedSource;
                return !string.IsNullOrEmpty(key);
            }

            _resolved = true;
            _cached = null;
            _cachedSource = null;

#if UNITY_EDITOR || VN_AI_ALLOW_LOCAL_KEY
            // 1) 环境变量
            try
            {
                string env = Environment.GetEnvironmentVariable(EnvVarName);
                if (!string.IsNullOrWhiteSpace(env))
                {
                    _cached = Clean(env);
                    _cachedSource = "环境变量 " + EnvVarName;
                }
            }
            catch (Exception e)
            {
                // 某些平台读环境变量会抛权限异常，不致命，继续走文件
                Debug.LogWarning($"[VNAi] 读环境变量 {EnvVarName} 失败：{e.GetType().Name}");
            }

            // 2) / 3) 文件
            if (string.IsNullOrEmpty(_cached))
            {
                foreach (string path in CandidatePaths())
                {
                    string v = ReadFileSafe(path);
                    if (string.IsNullOrEmpty(v)) continue;
                    _cached = v;
                    _cachedSource = path;
                    break;
                }
            }
#endif

            key = _cached;
            source = _cachedSource;
            return !string.IsNullOrEmpty(key);
        }

        /// <summary>拿不到 key 时给开发者看的完整指引（绝不含 key）</summary>
        public static string MissingKeyMessage()
        {
#if UNITY_EDITOR || VN_AI_ALLOW_LOCAL_KEY
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[VNAi] 找不到 Gemini API Key。按以下任一方式配置（越靠前越安全）：");
            sb.AppendLine($"  1. 设环境变量 {EnvVarName}=<你的key>（设完要重启 Unity 才生效）");
            int i = 2;
            foreach (string path in CandidatePaths())
                sb.AppendLine($"  {i++}. 建文件 {path}（内容就是一行 key）");
            sb.Append("注意：仓库是公开的，key 文件已被 .gitignore 挡掉，别手动 git add -f。");
            return sb.ToString();
#else
            return "[VNAi] Build 版本不读取本地 key 文件（防止 key 进包）。" +
                   "AI 自由聊天是开发期功能；要发行请改用玩家自填 key 或自建中转服务器。";
#endif
        }

        /// <summary>清缓存，下次重新查找（改了环境变量/换了文件后在编辑器里调）</summary>
        public static void Invalidate()
        {
            _resolved = false;
            _cached = null;
            _cachedSource = null;
        }

        /// <summary>候选文件路径：先仓库外，再仓库内</summary>
        static System.Collections.Generic.IEnumerable<string> CandidatePaths()
        {
            // Application.dataPath = <项目根>/Assets
            string projectRoot = null;
            try { projectRoot = Directory.GetParent(Application.dataPath)?.FullName; }
            catch { }
            if (string.IsNullOrEmpty(projectRoot)) yield break;

            string outside = null;
            try { outside = Directory.GetParent(projectRoot)?.FullName; }
            catch { }

            if (!string.IsNullOrEmpty(outside))
                yield return Path.Combine(outside, FileName);   // 仓库外，推荐
            yield return Path.Combine(projectRoot, FileName);   // 仓库内，靠 .gitignore
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
