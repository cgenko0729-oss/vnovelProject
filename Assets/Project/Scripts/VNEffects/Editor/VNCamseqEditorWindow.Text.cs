using System.Collections.Generic;
using System.Globalization;
using System.Text;
using DG.Tweening;
using DG.Tweening.Core.Easing;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects.EditorTools
{
    public partial class VNCamseqEditorWindow
    {
        // ==================================================================
        // 预览插值（与运行时同一套公式）
        // ==================================================================

        /// <summary>
        /// 某一刻的镜头状态。<c>zoom</c> 是 ZoomRoot 的倍率（背景跟着它），
        /// <c>charScale</c> 是立绘在此之上的**额外**倍率（camseq 的 mode: 决定）。
        /// 0 视为 1——旧代码里有几处直接 <c>new CamState{offset=..,zoom=1}</c>，
        /// 不兜底的话立绘会被画成 0 尺寸直接消失。
        /// </summary>
        struct CamState
        {
            public Vector2 offset;
            public float zoom;
            public float charScale;
            public float CharScale => charScale <= 0.0001f ? 1f : charScale;
        }

        Vector2 PreviewPoint(Waypoint w)
        {
            switch (w.type)
            {
                case PointType.Anchor: return AnchorPositions[Mathf.Clamp(w.anchorIndex, 0, 8)];
                case PointType.Coords: return w.coords;
                case PointType.Character:
                {
                    var stage = Object.FindFirstObjectByType<VNStage>();
                    var def = FindCharDef(w.charId, stage);
                    float height = CharacterHeight(stage, def);
                    Vector2 offset = def != null ? def.positionOffset : Vector2.zero;
                    var basePos = new Vector2(SlotX[Mathf.Clamp(w.slotIndex, 0, 2)], -60f) + offset;
                    float frac = PartFracs[Mathf.Clamp(w.partIndex, 0, PartFracs.Length - 1)];
                    return basePos + new Vector2(0f, height * frac);
                }
            }
            return Vector2.zero;
        }

        /// <summary>
        /// 按下标取镜头状态：<c>stay</c> 点沿用**前面最近一个真点位**的位置与 zoom
        /// （与运行时 CamseqCo 里 lastPoint/lastZoom 那套是同一条规则）。
        /// </summary>
        CamState TargetState(int index)
        {
            int i = Mathf.Clamp(index, 0, _points.Count - 1);
            while (i > 0 && _points[i].type == PointType.Stay) i--;
            return TargetState(_points[i]);
        }

        CamState TargetState(Waypoint w)
        {
            float zoom = Mathf.Max(0.1f, w.zoom);
            // 容器倍率与立绘额外倍率都走运行时那两个静态方法，避免预览和实机各算一套
            float cz = VNCamera.ContainerZoomFor(_zoomMode, zoom);
            return new CamState
            {
                zoom = cz,
                charScale = VNCamera.CharacterScaleFor(_zoomMode, zoom),
                // mode:char 背景纹丝不动（连 overscan 级的平移都不给），与 VNCamera.Cut 一致
                offset = _zoomMode == VNCamZoomMode.Char ? Vector2.zero
                    : VNCamera.ComputeOffset(PreviewPoint(w), cz, CanvasHalf, Overscan, true),
            };
        }

        /// <summary>时间轴上一段的来源（轨道按它上色，拖动时决定改哪个字段）</summary>
        enum SegKind { Move, Xfade, Hold, StartFade, EndFade }

        /// <summary>预览时间轴上的一段：补间段沿缓动移动；叠化段镜头瞬切、旧画面淡出</summary>
        struct Segment
        {
            public CamState target;
            public float duration;
            public SegKind kind;
            public int pointIndex;   // 来源路径点下标；收尾叠化 = -1
            public Ease ease;        // 补间段缓动（叠化 / hold 段无意义）

            public bool isFade => kind == SegKind.Xfade
                                  || kind == SegKind.StartFade || kind == SegKind.EndFade;
            public bool isHold => kind == SegKind.Hold;
        }

        /// <summary>
        /// 把 开场fade + 路径点(xfade 覆盖) + 收尾fade 展开成时间轴段列表。
        /// 组内缓动默认与运行时一致：叠化段把连续补间点分成独立组，
        /// 每组 首 InSine / 中 Linear / 末 OutSine（单段 InOutSine）。
        /// start:cut 无需特殊段——首点本来就是时长 0 的瞬切（运行时并入 bg 转场）。
        /// </summary>
        List<Segment> BuildSegments()
        {
            var segs = new List<Segment>();

            int start = 0;
            if (_points.Count > 0 && _startMode == StartMode.Fade)
            {
                segs.Add(new Segment
                {
                    target = TargetState(0),
                    duration = Mathf.Max(0.05f, _startFade),
                    kind = SegKind.StartFade,
                    pointIndex = 0,
                });
                AddHoldSegment(segs, 0);
                start = 1;
            }
            for (int i = start; i < _points.Count; i++)
            {
                var w = _points[i];
                if (w.fade > 0.001f)
                    segs.Add(new Segment
                    {
                        target = TargetState(i), duration = w.fade,
                        kind = SegKind.Xfade, pointIndex = i,
                    });
                else
                    segs.Add(new Segment
                    {
                        target = TargetState(i), duration = Mathf.Max(0f, w.duration),
                        kind = SegKind.Move, pointIndex = i,
                    });
                AddHoldSegment(segs, i);
            }
            if (_endFade)
            {
                segs.Add(new Segment
                {
                    target = new CamState { offset = Vector2.zero, zoom = 1f },
                    duration = Mathf.Max(0.05f, _endFadeDur),
                    kind = SegKind.EndFade,
                    pointIndex = -1,
                });
            }

            // 按叠化段切组，组内分配默认缓动
            int g = 0;
            while (g < segs.Count)
            {
                if (segs[g].isFade) { g++; continue; }
                int gEnd = g;
                while (gEnd < segs.Count && !segs[gEnd].isFade) gEnd++;

                int firstMove = -1, lastMove = -1, moveCount = 0;
                for (int k = g; k < gEnd; k++)
                {
                    // hold 段只是停顿，不算"移动段"——否则默认缓动的首/末会算错，
                    // 与运行时 BuildSegment（只看 duration 字段）对不上
                    if (!segs[k].isHold && segs[k].duration > 0.001f)
                    {
                        if (firstMove < 0) firstMove = k;
                        lastMove = k;
                        moveCount++;
                    }
                }
                for (int k = g; k < gEnd; k++)
                {
                    var s = segs[k];
                    if (s.isHold) continue;
                    int pi = s.pointIndex;
                    if (pi >= 0 && _points[pi].easeIndex > 0 &&
                        System.Enum.TryParse(EaseNames[_points[pi].easeIndex], true, out Ease custom))
                        s.ease = custom;
                    else
                        s.ease = moveCount <= 1 ? Ease.InOutSine
                            : k == firstMove ? Ease.InSine
                            : k == lastMove ? Ease.OutSine
                            : Ease.Linear;
                    segs[k] = s;
                }
                g = gEnd;
            }
            return segs;
        }

        /// <summary>
        /// 该点带 hold（或 shake）就补一段「停在原地」的时间轴段
        /// （运行时是 Sequence 里的 Interval）。
        /// 停顿 = max(hold, 震动时长)，与运行时 VNCamera.BuildSegment 同一条规则——
        /// 这里算短了，预览进度条就会和实机对不上。震动本身不在预览里模拟。
        /// </summary>
        void AddHoldSegment(List<Segment> segs, int index)
        {
            var w = _points[index];
            float stall = w.hold;
            if (VNShakeSpec.TryParse(w.shake, out VNShakeSpec spec) && spec.Valid)
                stall = Mathf.Max(stall, spec.duration);
            if (stall <= 0.001f) return;
            segs.Add(new Segment
            {
                target = TargetState(index), duration = stall,
                kind = SegKind.Hold, pointIndex = index,
            });
        }

        /// <summary>
        /// 镜头「刚到达」第 index 个点的时刻 = 该点移动 / 叠化段的结束时间（不含它的 hold）。
        /// 这一刻缓动已走到 1，画面恰好就是该点的取景。找不到返回 -1
        /// </summary>
        float ArrivalTime(int index)
        {
            float t = 0f;
            foreach (var s in BuildSegments())
            {
                t += Mathf.Max(0f, s.duration);
                if (s.pointIndex == index && !s.isHold && s.kind != SegKind.EndFade) return t;
            }
            return -1f;
        }

        /// <summary>
        /// 进度条跳到该点（开关 _scrubFollow）。场景预览每帧按 _scrub 摆镜头，
        /// 所以只改 _scrub 就够，Game 视图会在下一次 Update 跟上。
        /// 正在播放时选中 = 要开始改它了，停掉播放，不然播放头下一帧又跑走
        /// </summary>
        void JumpScrubToPoint(int index)
        {
            if (!_scrubFollow || index < 0 || index >= _points.Count) return;
            float t = ArrivalTime(index);
            if (t < 0f) return;
            _playing = false;
            _scrub = t;
            Repaint();
        }

        float TotalDuration()
        {
            float t = 0f;
            foreach (var s in BuildSegments()) t += s.duration;
            return t;
        }

        /// <summary>某时刻的预览状态：镜头状态 + 叠化中的旧画面（画布上画橙色残框）</summary>
        struct PreviewState
        {
            public CamState state;
            public bool fading;
            public CamState fadeFrom;
            public float ghostAlpha;   // 旧画面剩余不透明度（按运行时 InOutSine 淡出）
        }

        PreviewState PreviewAtTime(float time)
        {
            var prev = new CamState { offset = Vector2.zero, zoom = 1f };
            var ps = new PreviewState { state = prev };
            if (_points.Count == 0) return ps;

            float t = time;
            foreach (var s in BuildSegments())
            {
                if (s.duration <= 0.001f)
                {
                    prev = s.target; // 瞬切
                    ps.state = prev;
                    continue;
                }
                if (t >= s.duration)
                {
                    t -= s.duration;
                    prev = s.target;
                    ps.state = prev;
                    continue;
                }
                if (s.isHold)
                {
                    ps.state = s.target;   // 停在原地
                    return ps;
                }
                if (s.isFade)
                {
                    // 叠化段：镜头开段即瞬切到目标，旧画面 InOutSine 淡出
                    float eased = EaseManager.Evaluate(Ease.InOutSine, null, t, s.duration, 1.70158f, 0f);
                    ps.state = s.target;
                    ps.fading = true;
                    ps.fadeFrom = prev;
                    ps.ghostAlpha = 1f - eased;
                    return ps;
                }
                float k = EaseManager.Evaluate(s.ease, null, t, s.duration, 1.70158f, 0f);
                ps.state = new CamState
                {
                    offset = Vector2.LerpUnclamped(prev.offset, s.target.offset, k),
                    zoom = Mathf.LerpUnclamped(prev.zoom, s.target.zoom, k),
                    charScale = Mathf.LerpUnclamped(prev.CharScale, s.target.CharScale, k),
                };
                return ps;
            }
            return ps;
        }

        // ==================================================================
        // 文本生成 / 解析
        // ==================================================================

        string PointToken(Waypoint w)
        {
            switch (w.type)
            {
                case PointType.Stay:
                    return VNCamWaypointDef.StayToken;
                case PointType.Anchor:
                    return AnchorTokens[Mathf.Clamp(w.anchorIndex, 0, 8)];
                case PointType.Character:
                    return w.partIndex > 0 ? $"{w.charId}:{PartTokens[w.partIndex]}" : w.charId;
                default:
                    return string.Format(CultureInfo.InvariantCulture,
                        "{0:0.#},{1:0.#}", w.coords.x, w.coords.y);
            }
        }

        string GenerateText()
        {
            var sb = new StringBuilder("camseq");
            if (_zoomMode != VNCamZoomMode.Both)
                sb.Append(" mode:").Append(_zoomMode.ToString().ToLower());
            if (_startMode == StartMode.Cut)
            {
                sb.Append(" start:cut");
            }
            else if (_startMode == StartMode.Fade)
            {
                sb.Append(" start:fade");
                if (Mathf.Abs(_startFade - 0.6f) > 0.001f)
                    sb.Append(" startfade:")
                      .Append(_startFade.ToString("0.##", CultureInfo.InvariantCulture));
            }
            if (_endFade)
            {
                sb.Append(" end:fade");
                if (Mathf.Abs(_endFadeDur - 0.6f) > 0.001f)
                    sb.Append(" endfade:")
                      .Append(_endFadeDur.ToString("0.##", CultureInfo.InvariantCulture));
            }
            sb.Append('\n');

            foreach (var w in _points)
            {
                sb.Append("> ").Append(PointToken(w));
                // stay 行没有 zoom（沿用上一个点），数字位前移：唯一的数字就是时长。
                // 这里多写一个 zoom 出去，运行时会把它当成时长，整条运镜的节奏就错了
                if (w.type != PointType.Stay)
                    sb.Append(' ').Append(w.zoom.ToString("0.##", CultureInfo.InvariantCulture));
                sb.Append(' ').Append(w.duration.ToString("0.##", CultureInfo.InvariantCulture));
                if (w.easeIndex > 0) sb.Append(" ease:").Append(EaseNames[w.easeIndex]);
                if (w.fade > 0.001f)
                    sb.Append(" xfade:").Append(w.fade.ToString("0.##", CultureInfo.InvariantCulture));
                if (w.hold > 0.001f)
                    sb.Append(" hold:").Append(w.hold.ToString("0.##", CultureInfo.InvariantCulture));
                if (!string.IsNullOrEmpty(w.shake)) sb.Append(" shake:").Append(w.shake);
                sb.Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>silent = 绑定载入时用，不弹通知也不因空序列而放弃（允许清成 0 个点）</summary>
        void ParseText(bool silent = false) => ParseTextFrom(_pasteText, silent);

        /// <summary>撤销恢复走这个重载：不碰下方那个粘贴框的内容</summary>
        void ParseTextFrom(string text, bool silent)
        {
            var commands = VNScriptParser.Parse(text);
            VNScriptCommand camseq = null;
            foreach (var c in commands)
                if (c.keyword == "camseq" && c.camPoints != null &&
                    (silent || c.camPoints.Count > 0))
                {
                    camseq = c;
                    break;
                }
            if (camseq == null)
            {
                if (!silent) ShowNotification(new GUIContent("没有找到含路径点的 camseq 块"));
                return;
            }

            // camseq 级 start:/end: 选项
            string startKw = camseq.Kw("start");
            _startMode = startKw == "cut" ? StartMode.Cut
                       : startKw == "fade" ? StartMode.Fade : StartMode.None;
            _startFade = camseq.KwF("startfade", 0.6f);
            _endFade = camseq.Kw("end") == "fade";
            _endFadeDur = camseq.KwF("endfade", 0.6f);
            string modeKw = camseq.Kw("mode");
            if (string.IsNullOrEmpty(modeKw) ||
                !System.Enum.TryParse(modeKw, true, out _zoomMode))
                _zoomMode = VNCamZoomMode.Both;

            _points.Clear();
            foreach (var def in camseq.camPoints)
            {
                var w = new Waypoint
                {
                    zoom = def.zoom, duration = def.duration,
                    fade = def.fade, hold = def.hold, shake = def.shake ?? "",
                };

                int anchor = System.Array.IndexOf(AnchorTokens, def.point.ToLower());
                if (def.point.ToLower() == "center" || def.point.ToLower() == "origin"
                    || def.point.ToLower() == "reset") anchor = 4;

                if (VNCamWaypointDef.IsStay(def.point))
                {
                    // 原地：没有自己的点位与 zoom，其余字段（ease/hold/shake）照常走下面
                    w.type = PointType.Stay;
                }
                else if (anchor >= 0)
                {
                    w.type = PointType.Anchor;
                    w.anchorIndex = anchor;
                }
                else if (def.point.Contains(","))
                {
                    w.type = PointType.Coords;
                    var parts = def.point.Split(',');
                    float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out w.coords.x);
                    if (parts.Length > 1)
                        float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out w.coords.y);
                }
                else
                {
                    w.type = PointType.Character;
                    int colon = def.point.IndexOf(':');
                    w.charId = colon > 0 ? def.point.Substring(0, colon) : def.point;
                    if (colon > 0)
                    {
                        int part = System.Array.IndexOf(PartTokens, def.point.Substring(colon + 1).ToLower());
                        w.partIndex = Mathf.Max(0, part);
                    }
                }

                if (!string.IsNullOrEmpty(def.ease))
                {
                    for (int i = 1; i < EaseNames.Length; i++)
                        if (string.Equals(EaseNames[i], def.ease, System.StringComparison.OrdinalIgnoreCase))
                        {
                            w.easeIndex = i;
                            break;
                        }
                }
                _points.Add(w);
            }
            _scrub = 0f;
            if (!silent) ShowNotification(new GUIContent($"已载入 {_points.Count} 个路径点"));
        }

        // ==================================================================
        // 场景查询
        // ==================================================================

        // ---- 内容库来源：VNGameConfig 优先，留空才回退场景组件 ----
        // 与运行时 VNGameConfig.ApplyList 的覆盖语义一致。编辑期场景里的
        // characters/backgrounds 常年落后于配置资产（配置只在运行时才合并进 VNStage），
        // 只读场景会让配置里登记的角色查不到定义 → 画布退回 fallback 的 880/offset0，
        // sizeScale≠1 的角色（小雪 2.1、亚里沙 1.9）在画布上大小、脚底位置全错。
        static VNGameConfig _configCache;

        static VNGameConfig Config
        {
            get
            {
                if (_configCache == null)
                {
                    var cfg = AssetDatabase.LoadAssetAtPath<VNGameConfig>(VNGameConfig.AssetPath);
                    _configCache = cfg != null
                        ? cfg : Resources.Load<VNGameConfig>(VNGameConfig.ResourcesName);
                }
                return _configCache;
            }
        }

        static List<T> PickLibrary<T>(List<T> fromConfig, List<T> fromScene) =>
            fromConfig != null && fromConfig.Count > 0 ? fromConfig : fromScene;

        /// <summary>角色定义：取错就画错大小（高度 = characterHeight × sizeScale）</summary>
        static VNCharacterDef FindCharDef(string id, VNStage stage)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var list = PickLibrary(Config != null ? Config.characters : null,
                stage != null ? stage.characters : null);
            return list?.Find(c => c != null && c.id == id);
        }

        /// <summary>立绘显示高度（与 VNStage.HeightFor 同一条公式）</summary>
        static float CharacterHeight(VNStage stage, VNCharacterDef def)
        {
            float baseHeight = stage != null ? stage.characterHeight : 880f;
            return baseHeight * (def != null ? Mathf.Max(0.05f, def.sizeScale) : 1f);
        }

        string[] CharacterIds()
        {
            var stage = Object.FindFirstObjectByType<VNStage>();
            var list = PickLibrary(Config != null ? Config.characters : null,
                stage != null ? stage.characters : null);
            if (list == null) return new string[0];
            var ids = new List<string>();
            foreach (var c in list)
                if (c != null && !string.IsNullOrEmpty(c.id)) ids.Add(c.id);
            return ids.ToArray();
        }

        /// <summary>
        /// 画布底图，三级回退：
        ///   ① 工具栏手动指定的背景（分支里推算不准时的兜底）
        ///   ② 绑定行推算出的背景 / CG（默认，切行自动跟着换）
        ///   ③ 场景里 VNStage 当前挂着的那张（没绑定剧本时的老行为）
        /// </summary>
        Sprite CanvasBackdrop()
        {
            var stage = Object.FindFirstObjectByType<VNStage>();

            if (!string.IsNullOrEmpty(_bgOverrideId))
            {
                var lib = PickLibrary(Config != null ? Config.backgrounds : null,
                    stage != null ? stage.backgrounds : null);
                if (lib != null)
                    foreach (var b in lib)
                        if (b != null && b.id == _bgOverrideId) return b.sprite;
            }

            var info = RowStage;
            if (info != null && info.backdrop != null) return info.backdrop;

            if (stage != null && stage.backgroundImage != null && stage.backgroundImage.sprite != null)
                return stage.backgroundImage.sprite;
            return null;
        }

        string[] BackgroundIds()
        {
            var stage = Object.FindFirstObjectByType<VNStage>();
            var lib = PickLibrary(Config != null ? Config.backgrounds : null,
                stage != null ? stage.backgrounds : null);
            if (lib == null) return new string[0];
            var ids = new List<string>();
            foreach (var b in lib)
                if (b != null && !string.IsNullOrEmpty(b.id)) ids.Add(b.id);
            return ids.ToArray();
        }
    }
}
