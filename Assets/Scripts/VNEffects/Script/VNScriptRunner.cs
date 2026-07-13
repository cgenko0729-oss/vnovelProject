using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.InputSystem;

namespace VNEffects
{
    /// <summary>
    /// 剧本解释器（P0）：逐条执行 VNScriptParser 解析出的命令。
    ///   - 默认同步执行（等待演出完成才继续），行尾 @ = 异步不等待
    ///   - 台词行：等打字机播完 + 玩家点击/Enter/空格推进（打字中按键 = 催促全文）
    ///   - wait 命令 = 分镜停顿
    /// 用法：Inspector 指定 stage 与 script（TextAsset），Play On Start 自动播放。
    /// </summary>
    public class VNScriptRunner : MonoBehaviour
    {
        [Tooltip("舞台管理器")]
        public VNStage stage;

        [Tooltip("剧本文件（.vn.txt）")]
        public TextAsset script;

        [Tooltip("启动时自动播放")]
        public bool playOnStart = true;

        List<VNScriptCommand> _commands;
        int _index;
        bool _running;
        bool _advance;
        Coroutine _co;

        public bool IsRunning => _running;
        public int CurrentLine =>
            _running && _index > 0 && _index <= _commands.Count ? _commands[_index - 1].line : 0;

        void Start()
        {
            if (playOnStart && script != null) Play(script);
        }

        public void Play(TextAsset asset) => Play(asset.text);

        public void Play(string source)
        {
            Stop();
            _commands = VNScriptParser.Parse(source);
            _index = 0;
            _co = StartCoroutine(Run());
        }

        public void Stop()
        {
            if (_co != null) StopCoroutine(_co);
            _co = null;
            _running = false;
        }

        void Update()
        {
            if (!_running) return;
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            bool pressed =
                (kb != null && (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame))
                || (mouse != null && mouse.leftButton.wasPressedThisFrame);
            if (!pressed) return;

            if (stage != null && stage.dialogue != null && stage.dialogue.IsTyping)
                stage.dialogue.CompleteTyping(); // 打字中 = 催促
            else
                _advance = true;                 // 已显示完 = 推进
        }

        IEnumerator Run()
        {
            _running = true;
            while (_index < _commands.Count)
            {
                var cmd = _commands[_index++];
                IEnumerator co = null;
                try
                {
                    co = Dispatch(cmd);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[VNScript] 第 {cmd.line} 行执行出错（{cmd.keyword}）：{e.Message}");
                }
                if (co == null) continue;
                if (cmd.isAsync) StartCoroutine(co);
                else yield return StartCoroutine(co);
            }
            _running = false;
            Debug.Log("[VNScript] 剧本播放结束");
        }

        // ------------------------------------------------------------------
        // 命令分发
        // ------------------------------------------------------------------

        IEnumerator Dispatch(VNScriptCommand cmd)
        {
            switch (cmd.keyword)
            {
                case "say":
                    return SayCo(cmd);

                case "wait":
                    return WaitCo(cmd.ArgF(0, 0.5f));

                case "bg":
                    return WaitTween(stage.SetBackground(cmd.Arg(0), cmd.Kw("transition"), cmd.line));

                case "show":
                    return WaitTween(stage.Show(cmd.Arg(0), cmd.Kw("at"),
                        cmd.Kw("expr"), cmd.Kw("with"), cmd.line));

                case "hide":
                    return WaitTween(stage.Hide(cmd.Arg(0), cmd.Kw("with", "fade"), cmd.line));

                case "emote":
                    return WaitTween(stage.Emote(cmd.Arg(0), cmd.Arg(1), cmd.line));

                case "weather":
                    stage.weather?.SetWeather(
                        VNScriptParser.ParseEnum(cmd.Arg(0), VNWeather.None, cmd.line));
                    return null;

                case "mood":
                    stage.mood?.SetMood(
                        VNScriptParser.ParseEnum(cmd.Arg(0), VNMood.Neutral, cmd.line));
                    return null;

                case "shake":
                {
                    var level = cmd.Arg(0, "medium").ToLower();
                    var l = level == "light" ? VNShakeLevel.Light
                          : level == "heavy" ? VNShakeLevel.Heavy
                          : VNShakeLevel.Medium;
                    stage.screenShake?.Shake(l);
                    return null;
                }

                case "camera":
                    return CameraCo(cmd);

                case "transition":
                    if (stage.transition == null) return null;
                    return WaitTween(stage.transition.Play(
                        VNScriptParser.ParseEnum(cmd.Arg(0), VNTransition.NoiseDissolve, cmd.line)));

                case "sakura":
                    stage.sakura?.Play();
                    return null;

                case "fx":
                    stage.Fx(cmd.Arg(0), cmd.Arg(1), cmd.line);
                    return null;

                // ---- P1 预留：解析通过但暂不执行 ----
                case "label":
                case "jump":
                case "flag":
                case "if":
                case "choice":
                    Debug.LogWarning($"[VNScript] 第 {cmd.line} 行：「{cmd.keyword}」将在 P1 分支系统中实现，已跳过");
                    return null;

                default:
                    Debug.LogWarning($"[VNScript] 第 {cmd.line} 行：未知命令「{cmd.keyword}」");
                    return null;
            }
        }

        // ------------------------------------------------------------------
        // 等待原语
        // ------------------------------------------------------------------

        IEnumerator SayCo(VNScriptCommand cmd)
        {
            stage.Say(cmd.speaker, cmd.expression, cmd.text);
            yield return null; // 让打字机先启动
            while (stage.dialogue != null && stage.dialogue.IsTyping)
                yield return null;
            _advance = false;
            while (!_advance)
                yield return null;
            _advance = false;
        }

        static IEnumerator WaitCo(float seconds)
        {
            yield return new WaitForSeconds(seconds);
        }

        static IEnumerator WaitTween(Tween t)
        {
            if (t == null) yield break;
            yield return t.WaitForCompletion();
        }

        IEnumerator CameraCo(VNScriptCommand cmd)
        {
            if (stage.vnCamera == null) yield break;
            string move = cmd.Arg(0, "reset").ToLower();
            Tween t = null;
            switch (move)
            {
                case "pushin":
                    t = stage.vnCamera.PushIn(cmd.ArgF(1, 1.06f), cmd.ArgF(2, 4f), FocusOf(cmd));
                    break;
                case "snapzoom":
                    t = stage.vnCamera.SnapZoom(cmd.ArgF(1, 1.12f), 0.16f, FocusOf(cmd), stage.screenShake);
                    break;
                case "pan":
                {
                    // camera pan 亚里沙 / camera pan 380
                    Vector2 target;
                    var c = stage.Get(cmd.Arg(1));
                    if (c != null) target = c.rect.anchoredPosition;
                    else target = new Vector2(cmd.ArgF(1, 0f), 0f);
                    t = stage.vnCamera.Pan(target, 0.6f, cmd.ArgF(2, 1.2f));
                    break;
                }
                case "dolly":
                    t = stage.vnCamera.DollyZoom(cmd.ArgF(1, 1.3f), cmd.ArgF(2, 3f));
                    break;
                case "reset":
                    t = stage.vnCamera.ResetCamera(cmd.ArgF(1, 1f));
                    break;
                default:
                    Debug.LogWarning($"[VNScript] 第 {cmd.line} 行：未知运镜「{move}」");
                    break;
            }
            if (t != null) yield return t.WaitForCompletion();
        }

        /// <summary>camera 命令的 focus:角色id 参数 → 该角色的画布坐标</summary>
        Vector2? FocusOf(VNScriptCommand cmd)
        {
            var id = cmd.Kw("focus");
            if (string.IsNullOrEmpty(id)) return null;
            var c = stage.Get(id);
            return c != null ? c.rect.anchoredPosition : (Vector2?)null;
        }
    }
}
