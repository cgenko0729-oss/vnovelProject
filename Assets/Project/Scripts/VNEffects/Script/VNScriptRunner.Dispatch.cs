using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace VNEffects
{
    public partial class VNScriptRunner
    {
        // ------------------------------------------------------------------
        // 主循环
        // ------------------------------------------------------------------

        IEnumerator Run()
        {
            _running = true;
            while (_index < _commands.Count)
            {
                // 编辑器调试暂停：卡在两条命令之间；单步 = 放行一条后自动卡回来
                while (_debugPaused)
                {
                    if (_debugStepRequested) { _debugStepRequested = false; break; }
                    yield return null;
                }

                var cmd = ResolveParameters(_commands[_index++]);
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
            if (_callStack.Count > 0)
            {
                VNCallFrame frame = _callStack[_callStack.Count - 1];
                Debug.LogError($"[VNScript] 子程序播放到文件末尾但没有 return（call 第 {frame.sourceLine} 行），" +
                               "已停止并清空调用栈");
                _callStack.Clear();
                _currentParameters.Clear();
            }
            _running = false;
            SetSkip(false);
            _voicePendingForNextSay = false;
            stage?.StopSpeaking();
            Debug.Log("[VNScript] 剧本播放结束");
        }

        IEnumerator Dispatch(VNScriptCommand cmd)
        {
            switch (cmd.keyword)
            {
                case "say":
                    _currentSayIndex = _index - 1;
                    return SayCo(cmd);

                case "wait":
                    return WaitCo(cmd.ArgF(0, 0.5f));

                case "bg":
                    // via:black = 回到老的「先被纯色盖住再散开」；不写 = 新图直接过渡出来
                    return WaitTween(stage.SetBackground(
                        cmd.Arg(0), cmd.Kw("transition"), cmd.line, PrecutFor(cmd),
                        cmd.Kw("via") == "black"));

                case "cg":
                    // cg <id> [transition:Type] [chars:keep] [fx:keep] [via:black] [fit:模式] / cg off [...]
                    if (cmd.Arg(0) == "off")
                        return WaitTween(stage.HideCg(cmd.Kw("transition"), cmd.line,
                            cmd.Kw("via") == "black"));
                    return WaitTween(stage.ShowCg(cmd.Arg(0), cmd.Kw("transition"),
                        cmd.Kw("chars") == "keep", cmd.Kw("fx") == "keep", cmd.line,
                        false, cmd.Kw("via") == "black", cmd.Kw("fit")));

                case "show":
                    // show <角色> [at:] [expr:] [with:预设] [from:方向] [dur:秒]
                    return WaitTween(stage.Show(cmd.Arg(0), cmd.Kw("at"),
                        cmd.Kw("expr"), cmd.Kw("with"),
                        cmd.Kw("from"), cmd.KwF("dur", 0f), cmd.line));

                case "hide":
                    // hide <角色> [with:预设] [to:方向] [dur:秒]
                    return WaitTween(stage.Hide(cmd.Arg(0), cmd.Kw("with", "fade"),
                        cmd.Kw("to"), cmd.KwF("dur", 0f), cmd.line));

                case "emote":
                    return WaitTween(stage.Emote(cmd.Arg(0), cmd.Arg(1), cmd.line));

                case "mark":
                    // mark <角色> <符号|clear> [keep|off] [pos:x,y] [size:1.2] [dur:1.2]
                    return WaitTween(stage.Mark(cmd.Arg(0), cmd.Arg(1), cmd.Arg(2),
                        ParseMarkPos(cmd.Kw("pos"), cmd.line),
                        cmd.KwF("size", 1f), cmd.KwF("dur", 1.1f), cmd.line));

                case "imprint":
                {
                    // imprint <角色> <痕迹id|clear> [pos:x,y] [size:] [life:秒] [rot:度]
                    // 立绘痕迹（掌印等）：pos 是立绘归一化坐标 (0,0)=立绘中心，
                    // 与部位框/markAnchor 同一套。临时演出，会自己褪色消失，不进存档
                    Vector2 ipos = ParseMarkPos(cmd.Kw("pos"), cmd.line) ?? Vector2.zero;
                    stage.Imprint(cmd.Arg(1), cmd.Arg(0), ipos,
                        cmd.KwF("size", 1f), cmd.KwF("life", 0f), cmd.KwF("rot", 0f),
                        null, cmd.line);
                    return null;
                }

                case "overlay":
                    // overlay <角色> <层id|clear> [强度 0~1] [time:秒]
                    // 情绪叠加层（潮红/汗/泪）；层在 VNCharacterDef.overlays 登记。
                    // 强度省略 = 1；瞬发不等待（要等就自己接 wait）
                    stage.SetOverlay(cmd.Arg(0), cmd.Arg(1), cmd.ArgF(2, 1f),
                        cmd.KwF("time", 0.35f), cmd.line);
                    return null;

                case "weather":
                {
                    // weather <id> [density:] [wind:] [speed:] [size:]
                    //   id：petals/sakura/落樱 · maple/枫叶 · ginkgo/银杏 · leaves/落叶 ·
                    //       bamboo/竹叶 · Rain · Snow · Fireflies · none
                    //       （也可以是 VNGameConfig 飘落天气库里登记的自定义 id）
                    //   覆盖参数留空 = 用资产里的值；wind 可为负（向左吹）
                    float wind = cmd.KwF("wind", float.NaN);
                    stage.SetWeather(cmd.Arg(0), cmd.KwF("density", 0f),
                        float.IsNaN(wind) ? 0f : wind, !float.IsNaN(wind),
                        cmd.KwF("speed", 0f), cmd.KwF("size", 0f));
                    return null;
                }

                case "liquid":
                    // liquid splash|spray|click|wet|dry|cover [on|off] [x:] [y:] [type:] …
                    stage.Liquid(cmd.Arg(0, "splash"), ParseLiquidArgs(cmd), cmd.line);
                    return null;

                case "mood":
                    // 走 VNStage 包装：Memory（回忆）色调自动联动电影黑边
                    stage.SetMood(
                        VNScriptParser.ParseEnum(cmd.Arg(0), VNMood.Neutral, cmd.line));
                    return null;

                case "letterbox":
                    // letterbox on|off [height:130] [time:0.7]
                    stage.SetLetterbox(cmd.Arg(0, "on") != "off",
                        cmd.KwF("height", -1f), cmd.KwF("time", -1f));
                    return null;

                case "reset":
                    if (cmd.Arg(0) == "effects" || cmd.Arg(0) == "all")
                        stage.ResetEffects();
                    else
                        Debug.LogWarning($"[VNScript] 第 {cmd.line} 行：reset 用法为「reset effects」");
                    return null;

                case "bgscroll":
                {
                    // bgscroll on|off [speed:] [dir:] [mode:repeat|mirror] [time:]
                    bool on = cmd.Arg(0, "on") != "off";
                    float? speed = cmd.kwargs.ContainsKey("speed")
                        ? cmd.KwF("speed", VNBackgroundScroll.DefaultSpeed) : (float?)null;
                    float? dir = VNBackgroundScroll.ParseDirection(cmd.Kw("dir"));
                    if (dir == null && !string.IsNullOrEmpty(cmd.Kw("dir")))
                        Debug.LogWarning($"[VNScript] 第 {cmd.line} 行：bgscroll dir" +
                                         $"「{cmd.Kw("dir")}」认不出，应为 left/right/up/down 或角度");
                    var mode = VNBackgroundScroll.ParseMode(cmd.Kw("mode"));
                    if (mode == null && !string.IsNullOrEmpty(cmd.Kw("mode")))
                        Debug.LogWarning($"[VNScript] 第 {cmd.line} 行：bgscroll mode" +
                                         $"「{cmd.Kw("mode")}」认不出，应为 repeat 或 mirror");
                    float? fade = cmd.kwargs.ContainsKey("time")
                        ? cmd.KwF("time", VNBackgroundScroll.DefaultFade) : (float?)null;
                    stage.SetBackgroundScroll(on, speed, dir, mode, fade, cmd.line);
                    return null;
                }

                case "shake":
                {
                    var level = cmd.Arg(0, "medium").ToLower();
                    var l = level == "light" ? VNShakeLevel.Light
                          : level == "heavy" ? VNShakeLevel.Heavy
                          : VNShakeLevel.Medium;
                    stage.screenShake?.Shake(l);
                    return null;
                }

                case "portrait":
                    // portrait on / portrait off：对话头像全局开关
                    stage.SetPortraitEnabled(cmd.Arg(0, "on") != "off");
                    return null;

                case "ui":
                    // ui dialogue|choice <皮肤id|default>：切换对话框/选项面板皮肤
                    // ui name <样式|default>：切换名字（说话人）的装饰样式
                    stage.SetUiSkin(cmd.Arg(0), cmd.Arg(1, "default"), cmd.line);
                    return null;

                case "sns":
                    return SnsCo(cmd);

                case "camera":
                    return CameraCo(cmd);

                case "camseq":
                    return CamseqCo(cmd);

                case "camcut":
                {
                    // camcut <目标点> [zoom]
                    var p = stage.ResolveCamPoint(cmd.Arg(0), cmd.line);
                    if (p.HasValue)
                        stage.vnCamera?.Cut(p.Value, cmd.ArgF(1, 1.5f));
                    return null;
                }

                case "camto":
                {
                    // camto <目标点> [zoom] [秒] [ease:名]
                    var p = stage.ResolveCamPoint(cmd.Arg(0), cmd.line);
                    if (!p.HasValue || stage.vnCamera == null) return null;
                    return WaitTween(stage.vnCamera.GoTo(p.Value,
                        cmd.ArgF(1, 1.4f), cmd.ArgF(2, 0.8f),
                        ParseEase(cmd.Kw("ease"), Ease.InOutSine)));
                }

                case "transition":
                    if (stage.transition == null) return null;
                    return WaitTween(stage.transition.Play(
                        VNScriptParser.ParseEnum(cmd.Arg(0), VNTransition.NoiseDissolve, cmd.line)));

                case "interlude":
                {
                    // interlude <过场id> [time:秒]
                    // 快进时整段跳过（连语音都不放）：章节卡本来就是给正常速度看的，
                    // 而 1.5 秒的固定停留在 SKIP 里是纯粹的卡顿。
                    if (_skip) return null;
                    VNInterludeDef interlude = FindInterlude(cmd.Arg(0), cmd.line);
                    if (interlude == null || stage.interlude == null) return null;
                    return stage.interlude.PlayCo(interlude, cmd.KwF("time", -1f),
                        stage.vnAudio);
                }

                case "tutorial":
                {
                    // tutorial <教程id> [force:on]
                    // 默认「看过就跳过」（记录是全局的，读旧档不会重看）；
                    // force:on 强制重看，帮助菜单/作者点名讲解用。
                    // 快进时整段跳过：教学是给正常速度看的，SKIP 里只会是干扰。
                    if (_skip) return null;
                    var player = stage.tutorial != null ? stage.tutorial : VNTutorialPlayer.Instance;
                    if (player == null) return null;
                    var def = FindTutorial(cmd.Arg(0), cmd.line);
                    if (def == null) return null;
                    string forceArg = cmd.Kw("force");
                    bool force = !string.IsNullOrEmpty(forceArg) &&
                                 forceArg != "off" && forceArg != "false" && forceArg != "0";
                    return player.PlayCo(def, force);
                }

                case "sakura":
                    stage.sakura?.Play();
                    return null;

                case "move":
                    // move 亚里沙 left 0.6
                    return WaitTween(stage.Move(cmd.Arg(0), cmd.Arg(1, "center"),
                        cmd.ArgF(2, 0.6f), cmd.line));

                case "bgm":
                {
                    // bgm play 黄昏之歌 [fade:2] [vol:0.6] / bgm stop [fade:3]
                    string sub = cmd.Arg(0, "play");
                    float fade = 1.5f;
                    if (float.TryParse(cmd.Kw("fade"), out float f)) fade = f;
                    if (sub == "stop") stage.vnAudio?.StopBgm(fade);
                    else if (sub == "play")
                        stage.vnAudio?.PlayBgm(cmd.Arg(1), fade, cmd.KwF("vol", 1f), cmd.line);
                    else Debug.LogWarning($"[VNScript] 第 {cmd.line} 行：bgm 用法为 bgm play <id> 或 bgm stop");
                    return null;
                }

                case "se":
                {
                    // se 雨声 loop [vol:0.5] / se 心跳 [vol:0.3] / se stop 雨声
                    if (cmd.Arg(0) == "stop")
                        stage.vnAudio?.StopSe(cmd.Arg(1));
                    else
                        stage.vnAudio?.PlaySe(cmd.Arg(0), cmd.args.Contains("loop"),
                            cmd.KwF("vol", 1f), cmd.line);
                    return null;
                }

                case "voice":
                    _voicePendingForNextSay = stage.vnAudio != null &&
                        stage.vnAudio.PlayVoice(cmd.Arg(0), cmd.KwF("vol", 1f), cmd.line);
                    return null;

                case "volume":
                    stage.vnAudio?.SetVolume(cmd.Arg(0), cmd.ArgF(1, 1f), cmd.line);
                    return null;

                case "fx":
                    stage.Fx(cmd.Arg(0), cmd.Arg(1), cmd.line);
                    return null;

                // ---- P1 分支系统 ----
                case "label":
                    return null; // 只是位置标记

                case "jump":
                    JumpTo(cmd.Arg(0), cmd.line);
                    return null;

                case "call":
                    if (string.IsNullOrEmpty(cmd.Arg(0)))
                        Debug.LogError($"[VNScript] 第 {cmd.line} 行：call 缺少目标 label");
                    else
                        CallTo(cmd);
                    return null;

                case "return":
                    ReturnFromCall(cmd.line, cmd.isAsync);
                    return null;

                case "params":
                    return null; // 声明已在 call 进入目标时绑定；运行到此处无需再执行

                case "chapter":
                    if (string.IsNullOrEmpty(cmd.Arg(0)))
                        Debug.LogError($"[VNScript] 第 {cmd.line} 行：chapter 缺少章节文件名");
                    else
                        SwitchChapter(cmd.Arg(0), cmd.line);
                    return null;

                case "flag":
                    // flag 名字 / flag 名字 3 / flag 名字 +1 / flag 名字 rand:1-100
                    ApplyFlagCommand(cmd, false);
                    return null;

                case "if":
                {
                    // 从最后一个 jump 向前重组条件，因此独立 if 可安全使用空格与括号。
                    int jumpIndex = -1;
                    for (int i = cmd.args.Count - 2; i >= 1; i--)
                        if (cmd.args[i] == "jump")
                        {
                            jumpIndex = i;
                            break;
                        }
                    if (jumpIndex <= 0 || jumpIndex + 1 >= cmd.args.Count)
                    {
                        Debug.LogWarning($"[VNScript] 第 {cmd.line} 行：if 语法应为「if 条件表达式 jump 标签」");
                        return null;
                    }
                    string cond = string.Join(" ", cmd.args.GetRange(0, jumpIndex));
                    if (VNFlags.Evaluate(cond, cmd.line))
                        JumpTo(cmd.args[jumpIndex + 1], cmd.line);
                    return null;
                }

                case "stat":
                    // stat 名字 +5 / stat 名字 -3 / stat 名字 500（按 VNStatDef 钳制 + 飘字）
                    _statsHud?.Apply(cmd.Arg(0), cmd.Arg(1), false, cmd.line);
                    return null;

                case "time":
                    // time set <月份> [remain:N] / time pass [months:N] [refill:off|属性名]
                    ApplyTimeCommand(cmd, false);
                    return null;

                case "choice":
                    return ChoiceCo(cmd);

                case "event":
                    return EventCo(cmd);

                case "quest":
                    // quest start|stage|done|fail <id> [阶段]
                    _questLog?.Apply(cmd.Arg(0, "start"), cmd.Arg(1),
                        (int)cmd.ArgF(2, 0f), false, cmd.line);
                    return null;
                case "hideHUD":
                {
                    ParseHideHudArgs(cmd, out var uiParts, out bool uiHide, out bool uiLock);
                    SetUiHidden(uiParts, uiHide, uiLock);
                    return null;
                }

                default:
                    Debug.LogWarning($"[VNScript] 第 {cmd.line} 行：未知命令「{cmd.keyword}」");
                    return null;
            }
        }
    }
}
