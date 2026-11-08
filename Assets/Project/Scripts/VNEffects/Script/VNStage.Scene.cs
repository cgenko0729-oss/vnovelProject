using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    public partial class VNStage
    {
        // ------------------------------------------------------------------
        // 台词
        // ------------------------------------------------------------------

        /// <summary>说一句话：注册角色自动高亮+切表情+头像；否则名字原样显示（旁白）</summary>
        public void Say(string speaker, string expr, string text, bool followVoice = false)
        {
            StopSpeaking();
            var c = Get(speaker);
            if (c != null)
            {
                if (!string.IsNullOrEmpty(expr)) ApplyExpression(c, expr);
                if (speakerHighlight != null) speakerHighlight.SetSpeaker(c.fx);
                // 头像跟随说话者：优先本句表情，否则用角色当前表情的头像
                dialogue.SetPortrait(
                    c.def.GetPortrait(string.IsNullOrEmpty(expr) ? c.expression : expr),
                    c.def.portraitScale, c.def.portraitOffset);
                dialogue.SetSpeakerStyle(c.def); // 名牌配色跟着说话者走
                dialogue.Say(c.def.LocalizedDisplayName, text);
                c.mouth?.BeginSpeaking(followVoice, dialogue, vnAudio);
            }
            else
            {
                if (speakerHighlight != null && string.IsNullOrEmpty(speaker) == false)
                    speakerHighlight.ClearSpeaker();
                dialogue.SetPortrait(null); // 旁白/未注册角色不显示头像
                dialogue.SetSpeakerStyle(null); // 名牌配色回默认底色推算
                dialogue.Say(speaker, text); // speaker 为空 = 无名牌旁白
            }
        }

        /// <summary>强制所有在场角色恢复闭嘴；新台词、退场、读档和停止剧本都会调用。</summary>
        public void StopSpeaking()
        {
            foreach (var kv in _active)
                kv.Value.mouth?.ForceClosed();
        }

        /// <summary>对话头像全局开关（剧本 portrait on/off，进存档快照）</summary>
        public void SetPortraitEnabled(bool on)
        {
            _portraitOff = !on;
            if (dialogue != null) dialogue.SetPortraitEnabled(on);
        }

        bool _portraitOff;

        // ------------------------------------------------------------------
        // 背景
        // ------------------------------------------------------------------

        /// <summary>当前背景 id（存档用）</summary>
        public string CurrentBackgroundId { get; private set; }

        /// <summary>
        /// 切换背景（可选全屏转场），返回可等待的 Sequence（无转场时为 null）。
        /// onCovered：转场盖住画面瞬间额外执行的动作
        /// （camseq start:cut 的首镜头瞬切走这里，与换图同帧，睁眼即是新视角）。
        /// CG 显示期间只更新底层背景记录，不动画面；cg off 时按新背景恢复。
        /// </summary>
        public Sequence SetBackground(string id, string transitionName, int line = 0,
            System.Action onCovered = null, bool viaBlack = false)
        {
            var entry = backgrounds.Find(b => b.id == id);
            if (entry == null || entry.sprite == null)
            {
                Debug.LogError($"[VNScript] 第 {line} 行：未注册的背景「{id}」（检查 VNStage.backgrounds）");
                return null;
            }

            CurrentBackgroundId = id;
            if (CurrentCgId != null) return null; // CG 盖着：只记账，画面等 cg off 再换
            return SwapStageImage(entry.sprite, transitionName, line, onCovered, viaBlack);
        }

        /// <summary>舞台主图切换（背景与 CG 共用）：可选全屏转场，含直接背景转场快路径</summary>
        Sequence SwapStageImage(Sprite sprite, string transitionName, int line,
            System.Action onCovered = null, bool viaBlack = false)
        {
            if (!string.IsNullOrEmpty(transitionName) && transition != null)
            {
                var type = VNScriptParser.ParseEnum(transitionName, VNTransition.NoiseDissolve, line);
                bool canDirect = !viaBlack && sprite != null &&
                    backgroundImage != null && backgroundImage.sprite != null;
                // 直接过渡两条路：卷页/碎裂/水波/墨染有专用几何 shader（更漂亮），
                // 其余图案走贴图模式。两条都不经过纯色 —— viaBlack 才回到「先盖住再散开」。
                if (canDirect && VNScreenTransition.SupportsDirectBackground(type))
                {
                    var directSequence = transition.PlayBackground(type, backgroundImage, sprite, () =>
                    {
                        if (toneMatch != null) toneMatch.MatchTo(sprite);
                        onCovered?.Invoke();
                    });
                    if (directSequence != null) return directSequence;
                    // 直接转场 Shader 不可用时安全退回原全屏转场，确保画面仍会切换。
                }
                else if (canDirect && VNScreenTransition.SupportsPatternBackground(type))
                {
                    var patternSequence = transition.PlayBackgroundPattern(type, backgroundImage, sprite, () =>
                    {
                        if (toneMatch != null) toneMatch.MatchTo(sprite);
                        onCovered?.Invoke();
                    });
                    if (patternSequence != null) return patternSequence;
                }
                return transition.Play(type, () =>
                {
                    ApplyBackground(sprite);
                    onCovered?.Invoke();
                });
            }

            ApplyBackground(sprite);
            return null;
        }

        void ApplyBackground(Sprite sprite)
        {
            if (backgroundImage != null) backgroundImage.sprite = sprite;
            if (toneMatch != null && sprite != null) toneMatch.MatchTo(sprite);
            // 换图不停滚动（还在车上就该继续滚），但偏移归零：
            // 新图从头开始流，不会莫名其妙从半张图的位置接上
            if (bgScroll != null) bgScroll.ResetOffset();
        }

        /// <summary>bgscroll 命令入口。on = false 时其余参数忽略（只管缓停）</summary>
        public void SetBackgroundScroll(bool on, float? speed, float? dirDeg,
            VNScrollMode? mode, float? fade, int line = 0)
        {
            if (bgScroll == null)
            {
                Debug.LogWarning($"[VNScript] 第 {line} 行：场景里没有背景滚动组件" +
                                 "（VNBackgroundScroll），bgscroll 无效果");
                return;
            }
            if (on) bgScroll.StartScroll(speed, dirDeg, mode, fade);
            else bgScroll.StopScroll(fade);
        }

        // ------------------------------------------------------------------
        // CG（一枚绘）
        // ------------------------------------------------------------------

        /// <summary>当前显示中的 CG id（null = 没有 CG，进存档快照）</summary>
        public string CurrentCgId { get; private set; }

        bool _cgKeepChars;                 // 本次 CG 是否保留立绘（chars:keep）
        bool _cgKeepFx;                    // 本次 CG 是否保留环境特效（fx:keep）
        bool _cgFxPaused;                  // 环境特效当前被 CG 暂停中
        readonly List<string> _cgPausedFx = new List<string>(); // 被暂停的 fx 名（恢复用）
        string _cgSavedWeatherId = "";                          // 被暂停的天气 id（恢复用）
        CanvasGroup _charLayerGroup;       // 立绘层整体显隐（保持 GO 活跃，状态无损）

        /// <summary>CG 期间被暂停的环境特效集合（演出类 fx 如黑边/滤镜/心跳不受影响）</summary>
        static readonly string[] CgAmbientFxNames =
            { "godrays", "clouds", "haze", "shimmer", "meteor", "skycloud" };

        public bool CgKeepChars => _cgKeepChars;
        public bool CgKeepFx => _cgKeepFx;

        /// <summary>
        /// 显示 CG：默认隐藏立绘层 + 暂停环境特效（keepChars/keepFx 按需保留），
        /// 并记入全局鉴赏解锁。CG 之间可直接切换（差分/连续演出）。
        /// instant 供读档/调试重建静默摆台。
        /// </summary>
        public Sequence ShowCg(string id, string transitionName, bool keepChars, bool keepFx,
            int line = 0, bool instant = false, bool viaBlack = false, string fit = null)
        {
            var entry = cgLibrary.Find(c => c.id == id);
            if (entry == null || entry.sprite == null)
            {
                Debug.LogError($"[VNScript] 第 {line} 行：未注册的 CG「{id}」（检查 VNStage.cgLibrary）");
                return null;
            }

            // fit: 剧本覆盖 > 条目登记；写错的值警告后按条目走
            var mode = entry.fit;
            if (!string.IsNullOrEmpty(fit) && !VNCgFit.TryParse(fit, out mode))
            {
                Debug.LogWarning($"[VNScript] 第 {line} 行：未知的 fit:「{fit}」" +
                                 $"（可用 {string.Join("/", VNCgFit.ScriptNames)}），按条目设置显示");
                mode = entry.fit;
                fit = null;
            }

            VNCgUnlocks.Unlock(id);
            CurrentCgId = id;
            _cgKeepChars = keepChars;
            _cgKeepFx = keepFx;
            _cgFit = string.IsNullOrEmpty(fit) ? "" : fit;

            FadeCharLayer(keepChars ? 1f : 0f, instant);
            if (!keepFx) PauseCgAmbientFx(instant);
            else if (_cgFxPaused) ResumeCgAmbientFx(instant); // cg A → cg B fx:keep 的切换

            var sprite = VNCgFit.Resolve(entry.sprite, mode, StageImageSize(),
                                         backgroundImage != null ? backgroundImage.sprite : null);
            return SwapStageImage(sprite, instant ? null : transitionName, line, null, viaBlack);
        }

        /// <summary>剧本 fit: 覆盖值（空 = 跟条目登记走；进存档）</summary>
        string _cgFit = "";
        public string CgFit => _cgFit;

        /// <summary>舞台主图的实际尺寸（含防露边溢出），合成 CG 按它出图，比例才对得上</summary>
        Vector2 StageImageSize()
        {
            if (backgroundImage == null) return new Vector2(1920f, 1080f);
            var r = backgroundImage.rectTransform.rect;
            return new Vector2(Mathf.Abs(r.width), Mathf.Abs(r.height));
        }

        /// <summary>关闭 CG：恢复立绘层、环境特效与底层背景</summary>
        public Sequence HideCg(string transitionName, int line = 0, bool viaBlack = false)
        {
            if (CurrentCgId == null)
            {
                Debug.LogWarning($"[VNScript] 第 {line} 行：cg off 时没有显示中的 CG");
                return null;
            }

            CurrentCgId = null;
            FadeCharLayer(1f, false);
            if (_cgFxPaused) ResumeCgAmbientFx(false);

            Sprite bgSprite = null;
            if (!string.IsNullOrEmpty(CurrentBackgroundId))
                bgSprite = backgrounds.Find(b => b.id == CurrentBackgroundId)?.sprite;
            return SwapStageImage(bgSprite, transitionName, line, null, viaBlack);
        }

        void PauseCgAmbientFx(bool instant)
        {
            if (_cgFxPaused) return;
            _cgFxPaused = true;
            _cgPausedFx.Clear();
            foreach (var name in CgAmbientFxNames)
            {
                if (_fxStates.TryGetValue(name, out bool on) && on)
                {
                    _cgPausedFx.Add(name);
                    Fx(name, "off");
                }
            }
            _cgSavedWeatherId = weather != null ? weather.CurrentId : "";
            if (weather != null && !string.IsNullOrEmpty(_cgSavedWeatherId))
                weather.SetWeatherId("", instant ? 0.01f : 0.6f);
        }

        void ResumeCgAmbientFx(bool instant)
        {
            if (!_cgFxPaused) return;
            _cgFxPaused = false;
            foreach (var name in _cgPausedFx) Fx(name, "on");
            _cgPausedFx.Clear();
            if (weather != null && !string.IsNullOrEmpty(_cgSavedWeatherId))
                ApplyWeather(_cgSavedWeatherId, instant ? 0.01f : 0.6f);
            _cgSavedWeatherId = "";
        }

        /// <summary>
        /// 立绘层整体淡入淡出：走 CanvasGroup 而非 SetActive，
        /// GO 保持活跃（口型/眨眼协程、悬浮 Tween 都不中断），恢复零成本。
        /// </summary>
        void FadeCharLayer(float alpha, bool instant)
        {
            if (_charLayerGroup == null && characterLayer != null)
            {
                _charLayerGroup = characterLayer.GetComponent<CanvasGroup>();
                if (_charLayerGroup == null)
                    _charLayerGroup = characterLayer.gameObject.AddComponent<CanvasGroup>();
            }
            if (_charLayerGroup == null) return;
            _charLayerGroup.DOKill();
            if (instant) _charLayerGroup.alpha = alpha;
            else _charLayerGroup.DOFade(alpha, 0.35f)
                                .SetLink(_charLayerGroup.gameObject);
        }

        /// <summary>把 CG 状态恢复到"无 CG"的中性值（读档/清场用，不做动画不碰画面）</summary>
        void ResetCgState()
        {
            CurrentCgId = null;
            _cgKeepChars = false;
            _cgKeepFx = false;
            _cgFit = "";
            _cgFxPaused = false;
            _cgPausedFx.Clear();
            _cgSavedWeatherId = "";
            FadeCharLayer(1f, true);
        }
    }
}
