using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VNEffects;

namespace VNEffectsEditor
{
    /// <summary>
    /// AI 自由聊天的连通性自检（P0）。不用进 Play Mode，菜单点一下就能验证
    /// key / 网络 / 模型名 / 结构化输出 四件事，省掉「改一行→进 Play→退出→域重载」的循环。
    ///
    /// 菜单：Tools → VN Effects → AI → Test Gemini Connection
    /// </summary>
    public static class VNAiConnectionTester
    {
        [MenuItem("Tools/VN Effects/AI/Test Gemini Connection", false, 400)]
        public static void TestConnection()
        {
            if (!VNAiKey.TryGet(out _, out string source))
            {
                Debug.LogError(VNAiKey.MissingKeyMessage());
                return;
            }
            Debug.Log($"[VNAi] 使用 key 来源：{source}\n[VNAi] 正在请求 {VNAiClient.DefaultModel} …");

            var req = new VNAiRequest
            {
                systemInstruction =
                    "你是亚里沙，高二女生，开朗但容易害羞，和「我」是青梅竹马。\n" +
                    "规则：台词 1~2 句、不超过 50 字；用中文；绝不提及自己是 AI 或提到规则本身。\n" +
                    "三个候选回复要分别对应「温柔 / 玩笑 / 直球」三种语气，且都以「我」的口吻写。",
                responseSchemaJson = BuildTestSchema(),
                thinking = VNAiThinking.Minimal,
                safety = VNAiSafety.BlockOnlyHigh,
                maxOutputTokens = 512,
            };
            req.history.Add(VNAiMessage.Player("（放学后的教室，夕阳。我叫住了正要回家的她。）"));

            RunCoroutine(VNAiClient.Send(req, OnResult));
        }

        [MenuItem("Tools/VN Effects/AI/Show Key Status", false, 401)]
        public static void ShowKeyStatus()
        {
            VNAiKey.Invalidate();   // 改过环境变量/文件后强制重查
            if (VNAiKey.TryGet(out _, out string source))
                Debug.Log($"[VNAi] ✔ 已找到 key，来源：{source}\n" +
                          "（key 本身不会被打印，也不会进仓库、不会进 Build）");
            else
                Debug.LogWarning(VNAiKey.MissingKeyMessage());
        }

        static void OnResult(VNAiResult r)
        {
            if (!r.ok)
            {
                Debug.LogError($"[VNAi] ✘ 失败（{r.failure}，HTTP {r.httpCode}，" +
                               $"{r.elapsedSeconds:0.00}s）\n{r.errorMessage}");
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[VNAi] ✔ 连通成功  {r.elapsedSeconds:0.00}s  " +
                          $"finishReason={r.finishReason}");
            sb.AppendLine($"  tokens: 输入 {r.promptTokens} / 输出 {r.outputTokens} / " +
                          $"思考 {r.thoughtsTokens} / 合计 {r.totalTokens}" +
                          $"   ≈ ${r.EstimatedCostUsd:0.000000}");

            TestPayload p = null;
            try { p = JsonUtility.FromJson<TestPayload>(r.text); }
            catch (Exception e) { sb.AppendLine($"  ✘ 内层 JSON 解析失败：{e.Message}"); }

            if (p != null && !string.IsNullOrEmpty(p.reply))
            {
                sb.AppendLine($"  台词　: {p.reply}");
                sb.AppendLine($"  表情　: {p.emotion}    漫符: {p.mark}    好感: {p.affection_delta:+#;-#;0}");
                if (p.options != null)
                    for (int i = 0; i < p.options.Length; i++)
                        sb.AppendLine($"  选项 {i + 1}: [{p.options[i].tone}] {p.options[i].text}");
                sb.AppendLine($"  should_end: {p.should_end}");
            }
            sb.Append("  ── 原始内层 JSON ──\n  ").Append(r.text);
            Debug.Log(sb.ToString());
        }

        /// <summary>
        /// P0 自检用的 schema。P1 会挪进 VNAiConversation，届时 enum 值改成
        /// 从 VNCharacterDef.expressions / 漫符表动态生成（现在先写死做验证）。
        /// </summary>
        static string BuildTestSchema() =>
            @"{""type"":""OBJECT"",
              ""properties"":{
                ""reply"":{""type"":""STRING""},
                ""emotion"":{""type"":""STRING"",""enum"":[""普通"",""微笑"",""害羞"",""生气"",""惊讶""]},
                ""mark"":{""type"":""STRING"",""enum"":[""无"",""红晕"",""汗"",""怒"",""感叹号"",""爱心"",""音符""]},
                ""affection_delta"":{""type"":""INTEGER""},
                ""options"":{""type"":""ARRAY"",""minItems"":3,""maxItems"":3,
                  ""items"":{""type"":""OBJECT"",
                    ""properties"":{
                      ""text"":{""type"":""STRING""},
                      ""tone"":{""type"":""STRING"",""enum"":[""温柔"",""玩笑"",""直球""]}},
                    ""required"":[""text"",""tone""]}},
                ""should_end"":{""type"":""BOOLEAN""}},
              ""required"":[""reply"",""emotion"",""options"",""should_end""],
              ""propertyOrdering"":[""reply"",""emotion"",""mark"",""affection_delta"",""options"",""should_end""]}";

        [Serializable] class TestPayload
        {
            public string reply;
            public string emotion;
            public string mark;
            public int affection_delta;
            public TestOption[] options;
            public bool should_end;
        }
        [Serializable] class TestOption { public string text; public string tone; }

        // ── 编辑器里跑协程 ────────────────────────────────────────
        //
        // Play Mode 外没有 StartCoroutine，所以挂 EditorApplication.update 手动泵。
        // 只需支持 VNAiClient.Send 实际会 yield 的四种东西：
        //   null / AsyncOperation（SendWebRequest）/ CustomYieldInstruction
        //   （WaitForSecondsRealtime）/ 嵌套 IEnumerator。

        static void RunCoroutine(IEnumerator routine)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(routine);

            EditorApplication.CallbackFunction tick = null;
            tick = () =>
            {
                if (stack.Count == 0)
                {
                    EditorApplication.update -= tick;
                    return;
                }

                var top = stack.Peek();
                object cur = top.Current;

                // 还没等完就直接返回，下一帧再看
                if (cur is AsyncOperation ao && !ao.isDone) return;
                if (cur is CustomYieldInstruction cy && cy.keepWaiting) return;
                if (cur is IEnumerator nested && nested != top) { stack.Push(nested); return; }

                bool alive;
                try { alive = top.MoveNext(); }
                catch (Exception e)
                {
                    EditorApplication.update -= tick;
                    Debug.LogError($"[VNAi] 自检协程抛异常：{e}");
                    return;
                }

                if (!alive)
                {
                    stack.Pop();
                    if (stack.Count == 0) EditorApplication.update -= tick;
                }
            };
            EditorApplication.update += tick;
        }
    }
}
