using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using VNEffects;

namespace VNEffectsEditor
{
    /// <summary>
    /// AI 自由聊天的编辑器自检。**不用进 Play Mode**，省掉「改一行 → 进 Play →
    /// 退出 → 域重载」的循环。
    ///
    /// 两级诊断，出问题时能立刻分清是谁的锅：
    ///   Test Gemini Connection  只发一句纯文本 → 只验 key / 网络 / 模型名
    ///   Test Persona Talk       跑 3 轮真实对话 → 验 VNAiConversation 的
    ///                           提示词组装、schema 生成、响应解析、历史裁剪
    /// 前者过、后者挂 = 问题在我们的逻辑层，不是网络。
    ///
    /// 菜单：Tools → VN Effects → AI → …
    /// </summary>
    public static class VNAiConnectionTester
    {
        // ──────────────── 一级：连通性 ────────────────

        [MenuItem("Tools/VN Effects/AI/Test Gemini Connection", false, 400)]
        public static void TestConnection()
        {
            if (!RequireKey(out string source)) return;
            Debug.Log($"[VNAi] key 来源：{source}\n[VNAi] 正在请求 {VNAiClient.DefaultModel} …");

            var req = new VNAiRequest
            {
                systemInstruction = "只回答问题本身，不要多余的话。",
                maxOutputTokens = 64,
            };
            req.history.Add(VNAiMessage.Player("用一句话说明你是谁。"));

            RunCoroutine(VNAiClient.Send(req, r =>
            {
                if (!r.ok) { LogFailure(r); return; }
                Debug.Log($"[VNAi] ✔ 网络通  {r.elapsedSeconds:0.00}s  " +
                          $"{r.totalTokens} tokens  ≈ ${r.EstimatedCostUsd:0.000000}\n  {r.text}");
            }));
        }

        [MenuItem("Tools/VN Effects/AI/Show Key Status", false, 401)]
        public static void ShowKeyStatus()
        {
            VNAiKey.Invalidate();   // 改过环境变量/文件后强制重查
            if (VNAiKey.TryGet(out _, out string source))
                Debug.Log($"[VNAi] ✔ 已找到 key，来源：{source}\n" +
                          "（key 不会被打印、不会进仓库、不会进 Build）");
            else
                Debug.LogWarning(VNAiKey.MissingKeyMessage());
        }

        // ──────────────── 二级：人格 + 多轮对话 ────────────────

        [MenuItem("Tools/VN Effects/AI/Test Persona Talk (3 turns)", false, 420)]
        public static void TestPersonaTalk()
        {
            if (!RequireKey(out _)) return;

            var persona = ResolvePersona();
            if (persona == null)
            {
                Debug.LogError("[VNAi] 工程里没有 VNAiPersonaDef 资产。" +
                               "先 Create → VN → AI Persona 建一套人格。");
                return;
            }

            var errors = persona.Validate();
            if (errors.Count > 0)
            {
                Debug.LogError($"[VNAi] 人格「{persona.id}」配置有问题，先修：\n  - " +
                               string.Join("\n  - ", errors));
                return;
            }

            Debug.Log($"[VNAi] 人格「{persona.id}」→ 角色「{persona.character.id}」" +
                      $"，模型 {persona.ResolveModel()}，开始 3 轮对话自检 …");
            RunCoroutine(TalkCo(persona, 3));
        }

        /// <summary>
        /// 跑 N 轮：每轮把上一轮的三个选项里**随机挑一个**当玩家回复，
        /// 验证多轮上下文是否连贯、历史裁剪有没有把对话搞断。
        /// </summary>
        static IEnumerator TalkCo(VNAiPersonaDef persona, int turns)
        {
            var convo = new VNAiConversation(persona);
            var log = new StringBuilder();
            double cost = 0;
            float totalTime = 0;
            string playerSaid = null;

            for (int i = 0; i < turns; i++)
            {
                var ctx = new VNAiContext
                {
                    playerName = "我",
                    place = "放学后的空教室，夕阳照进来",
                    affectionText = "好感 40 / 100，算是走得比较近的同学",
                    topic = i == 0 ? "随便聊聊今天发生的事" : null,
                    turnsLeft = turns - i,
                };

                var req = convo.BuildRequest(playerSaid, ctx);
                if (i == 0)
                    log.AppendLine($"── system prompt（{req.systemInstruction.Length} 字）──")
                       .AppendLine(req.systemInstruction)
                       .AppendLine("──────────────────────────").AppendLine();

                VNAiResult res = null;
                yield return VNAiClient.Send(req, r => res = r);
                while (res == null) yield return null;

                totalTime += res.elapsedSeconds;
                cost += res.EstimatedCostUsd;

                if (!res.ok)
                {
                    log.AppendLine($"第 {i + 1} 轮 ✘ {res.failure}：{res.errorMessage}");
                    log.AppendLine("→ 走兜底轮：" + convo.BuildFallbackTurn().reply);
                    Debug.LogError(log.ToString());
                    yield break;
                }

                if (!convo.TryParseTurn(res.text, out VNAiTurn turn, out string parseError))
                {
                    log.AppendLine($"第 {i + 1} 轮 ✘ 解析失败：{parseError}");
                    log.AppendLine("原始输出：" + res.text);
                    Debug.LogError(log.ToString());
                    yield break;
                }

                convo.RecordReply(turn);

                log.AppendLine($"【第 {i + 1} 轮】{res.elapsedSeconds:0.00}s  " +
                               $"{res.promptTokens}+{res.outputTokens} tokens");
                if (!string.IsNullOrEmpty(playerSaid)) log.AppendLine($"  我　: {playerSaid}");
                log.AppendLine($"  {persona.DisplayName}: {turn.reply}");
                log.AppendLine($"  表情: {turn.emotion}   漫符: {turn.mark ?? "（无）"}   " +
                               $"好感: {turn.affectionDelta:+#;-#;0}   收尾: {turn.shouldEnd}");
                for (int k = 0; k < turn.options.Count; k++)
                    log.AppendLine($"  选项{k + 1} [{turn.options[k].tone}] {turn.options[k].text}");

                // 随机挑一个当玩家回复，进入下一轮
                var picked = turn.options[UnityEngine.Random.Range(0, turn.options.Count)];
                convo.RecordPick(picked);
                playerSaid = picked.text;
                log.AppendLine($"  → 自检随机选了：[{picked.tone}] {picked.text}").AppendLine();

                if (turn.shouldEnd)
                {
                    log.AppendLine("（AI 认为话题已收尾，提前结束）");
                    break;
                }
            }

            log.AppendLine($"✔ 完成 {convo.TurnCount} 轮  合计 {totalTime:0.0}s  " +
                           $"≈ ${cost:0.0000}  玩家倾向：{string.Join("/", convo.pickedTones)}");
            Debug.Log("[VNAi] 人格对话自检\n" + log);
        }

        // ──────────────── 辅助 ────────────────

        static bool RequireKey(out string source)
        {
            if (VNAiKey.TryGet(out _, out source)) return true;
            Debug.LogError(VNAiKey.MissingKeyMessage());
            return false;
        }

        static void LogFailure(VNAiResult r) =>
            Debug.LogError($"[VNAi] ✘ 失败（{r.failure}，HTTP {r.httpCode}，" +
                           $"{r.elapsedSeconds:0.00}s）\n{r.errorMessage}");

        /// <summary>优先用 Project 窗口里选中的人格资产，否则取工程里第一个。</summary>
        static VNAiPersonaDef ResolvePersona()
        {
            if (Selection.activeObject is VNAiPersonaDef selected) return selected;

            var config = VNGameConfig.Active;
            if (config != null && config.aiPersonas != null)
                foreach (var p in config.aiPersonas)
                    if (p != null) return p;

            foreach (string guid in AssetDatabase.FindAssets("t:VNAiPersonaDef"))
            {
                var p = AssetDatabase.LoadAssetAtPath<VNAiPersonaDef>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (p != null) return p;
            }
            return null;
        }

        // 编辑器里跑协程：泵在 VNAiEditorCoroutine（与试聊台窗口共用同一份，
        // 那里记着「已耗尽子协程被无限重新压栈」那个坑）
        static void RunCoroutine(IEnumerator routine) => VNAiEditorCoroutine.Start(routine);
    }
}
