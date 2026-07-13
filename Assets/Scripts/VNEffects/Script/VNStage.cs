using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 舞台管理器：剧本命令 → 现有 VNEffects API 的落地层。
    /// 负责：角色运行时生成（完整特效组件栈）/ 表情切换 / 背景切换 /
    /// 台词分发（联动说话者高亮）/ fx 开关分发。
    /// 场景生成器（Create Script Demo Scene）会自动连线全部引用。
    /// </summary>
    public class VNStage : MonoBehaviour
    {
        [Header("角色与背景库")]
        public List<VNCharacterDef> characters = new List<VNCharacterDef>();

        [System.Serializable]
        public class BackgroundEntry
        {
            public string id;
            public Sprite sprite;
        }
        public List<BackgroundEntry> backgrounds = new List<BackgroundEntry>();

        [Header("舞台引用（生成器自动连线）")]
        public RectTransform characterLayer; // LayerFront
        public Image backgroundImage;
        public VNImageEffectController backgroundFx;
        public VNDialogueBox dialogue;
        public VNScreenTransition transition;
        public VNWeatherController weather;
        public VNMoodGrading mood;
        public VNCamera vnCamera;
        public VNScreenShake screenShake;
        public VNDutchAngle dutchAngle;
        public VNHeartbeat heartbeat;
        public VNSakuraBurst sakura;
        public VNFakeDoF fakeDoF;
        public VNCloudShadows cloudShadows;
        public VNGodRays godRays;
        public VNHeatHaze heatHaze;
        public VNVignetteFocus vignetteFocus;
        public VNSpeakerHighlight speakerHighlight;
        public VNToneMatch toneMatch;

        [Header("角色生成参数")]
        public float characterHeight = 880f;

        public class ActiveCharacter
        {
            public VNCharacterDef def;
            public GameObject go;
            public Image image;
            public RectTransform rect;
            public VNImageEffectController fx;
            public VNEntranceAnimator animator;
            public VNCharacterEmotes emotes;
            public string expression;
        }

        readonly Dictionary<string, ActiveCharacter> _active =
            new Dictionary<string, ActiveCharacter>();

        public ActiveCharacter Get(string id) =>
            id != null && _active.TryGetValue(id, out var c) ? c : null;

        // ------------------------------------------------------------------
        // 角色
        // ------------------------------------------------------------------

        static Vector2 SlotPosition(string at)
        {
            switch (at)
            {
                case "left": return new Vector2(-380f, -60f);
                case "right": return new Vector2(380f, -60f);
                case "center": return new Vector2(0f, -60f);
                default:
                    // 支持直接写数字 = 横向像素坐标
                    if (float.TryParse(at, out float x)) return new Vector2(x, -60f);
                    return new Vector2(0f, -60f);
            }
        }

        /// <summary>角色登场（已在场则换位置/表情并重播出场）</summary>
        public Sequence Show(string id, string at, string expr, string presetName, int line = 0)
        {
            var def = characters.Find(c => c.id == id);
            if (def == null)
            {
                Debug.LogError($"[VNScript] 第 {line} 行：未注册的角色「{id}」（检查 VNStage.characters）");
                return null;
            }

            var c = Get(id) ?? CreateCharacter(def);
            if (!string.IsNullOrEmpty(at))
            {
                var pos = SlotPosition(at);
                c.rect.anchoredPosition = pos;
                // 关键：同步各组件缓存的"基准位"，否则出场动画会把角色重置回旧位置
                c.animator.SetBasePosition(pos);
                c.emotes.SetBasePosition(pos);
            }
            ApplyExpression(c, expr);

            var preset = VNScriptParser.ParseEnum(presetName, VNEntrancePreset.DissolveGlow, line);
            var seq = c.animator.PlayEntrance(preset);
            seq.OnComplete(() => c.animator.StartIdleEffects());
            return seq;
        }

        /// <summary>角色退场（style: dissolve / fade），完成后销毁</summary>
        public Sequence Hide(string id, string style, int line = 0)
        {
            var c = Get(id);
            if (c == null)
            {
                Debug.LogWarning($"[VNScript] 第 {line} 行：hide 的角色「{id}」不在场上");
                return null;
            }
            _active.Remove(id);
            RefreshRegistries();

            var seq = style == "dissolve" ? c.animator.PlayExitDissolve() : c.animator.PlayExitFade();
            seq.OnComplete(() => Destroy(c.go));
            return seq;
        }

        /// <summary>情绪演出动作</summary>
        public Sequence Emote(string id, string emoteName, int line = 0)
        {
            var c = Get(id);
            if (c == null)
            {
                Debug.LogWarning($"[VNScript] 第 {line} 行：emote 的角色「{id}」不在场上");
                return null;
            }
            switch (emoteName)
            {
                case "Surprise": return c.emotes.Surprise();
                case "Angry": return c.emotes.Angry();
                case "Shy": return c.emotes.Shy();
                case "Dejected": return c.emotes.Dejected();
                case "Recover": return c.emotes.Recover();
                case "Nod": return c.emotes.Nod();
                case "HeadShake": return c.emotes.HeadShake();
                default:
                    Debug.LogWarning($"[VNScript] 第 {line} 行：未知情绪动作「{emoteName}」");
                    return null;
            }
        }

        /// <summary>切换表情立绘（P0 为瞬间切换）</summary>
        public void SetExpression(string id, string expr)
        {
            var c = Get(id);
            if (c != null) ApplyExpression(c, expr);
        }

        void ApplyExpression(ActiveCharacter c, string expr)
        {
            var sprite = c.def.GetSprite(expr);
            if (sprite == null || sprite == c.image.sprite) return;
            c.image.sprite = sprite;
            c.expression = expr;
            // 不同表情图宽高比可能不同 → 以固定高度重算宽度
            float aspect = sprite.rect.width / sprite.rect.height;
            c.rect.sizeDelta = new Vector2(characterHeight * aspect, characterHeight);
        }

        /// <summary>运行时生成完整的角色特效组件栈</summary>
        ActiveCharacter CreateCharacter(VNCharacterDef def)
        {
            var go = new GameObject($"Char_{def.id}",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(characterLayer, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, -60f);

            var img = go.GetComponent<Image>();
            img.preserveAspect = true;
            img.raycastTarget = false;
            var sprite = def.GetSprite(null);
            img.sprite = sprite;
            if (sprite != null)
            {
                float aspect = sprite.rect.width / sprite.rect.height;
                rect.sizeDelta = new Vector2(characterHeight * aspect, characterHeight);
            }

            var c = new ActiveCharacter
            {
                def = def,
                go = go,
                image = img,
                rect = rect,
                fx = go.AddComponent<VNImageEffectController>(),
            };
            go.AddComponent<VNGlowBackdrop>();
            c.animator = go.AddComponent<VNEntranceAnimator>();
            c.emotes = go.AddComponent<VNCharacterEmotes>();
            go.AddComponent<VNFootShadow>();

            _active[def.id] = c;
            if (speakerHighlight != null) speakerHighlight.Register(c.fx);
            RefreshRegistries();
            return c;
        }

        /// <summary>在场角色变化后，刷新色调匹配等注册表</summary>
        void RefreshRegistries()
        {
            if (toneMatch != null)
            {
                var list = new List<VNImageEffectController>();
                foreach (var kv in _active) list.Add(kv.Value.fx);
                toneMatch.characters = list.ToArray();
                if (backgroundImage != null && backgroundImage.sprite != null)
                    toneMatch.MatchTo(backgroundImage.sprite);
            }
        }

        // ------------------------------------------------------------------
        // 台词
        // ------------------------------------------------------------------

        /// <summary>说一句话：注册角色自动高亮+切表情；否则名字原样显示（旁白）</summary>
        public void Say(string speaker, string expr, string text)
        {
            var c = Get(speaker);
            if (c != null)
            {
                if (!string.IsNullOrEmpty(expr)) ApplyExpression(c, expr);
                if (speakerHighlight != null) speakerHighlight.SetSpeaker(c.fx);
                dialogue.Say(c.def.displayName, text);
            }
            else
            {
                if (speakerHighlight != null && string.IsNullOrEmpty(speaker) == false)
                    speakerHighlight.ClearSpeaker();
                dialogue.Say(speaker, text); // speaker 为空 = 无名牌旁白
            }
        }

        // ------------------------------------------------------------------
        // 背景
        // ------------------------------------------------------------------

        /// <summary>切换背景（可选全屏转场），返回可等待的 Sequence（无转场时为 null）</summary>
        public Sequence SetBackground(string id, string transitionName, int line = 0)
        {
            var entry = backgrounds.Find(b => b.id == id);
            if (entry == null || entry.sprite == null)
            {
                Debug.LogError($"[VNScript] 第 {line} 行：未注册的背景「{id}」（检查 VNStage.backgrounds）");
                return null;
            }

            if (!string.IsNullOrEmpty(transitionName) && transition != null)
            {
                var type = VNScriptParser.ParseEnum(transitionName, VNTransition.NoiseDissolve, line);
                return transition.Play(type, () => ApplyBackground(entry.sprite));
            }

            ApplyBackground(entry.sprite);
            return null;
        }

        void ApplyBackground(Sprite sprite)
        {
            if (backgroundImage != null) backgroundImage.sprite = sprite;
            if (toneMatch != null) toneMatch.MatchTo(sprite);
        }

        // ------------------------------------------------------------------
        // fx 开关分发
        // ------------------------------------------------------------------

        /// <summary>fx 命令：fx godrays on / fx dof off / fx focus 亚里沙 / fx heartbeat on …</summary>
        public void Fx(string name, string arg, int line = 0)
        {
            bool on = arg == "on" || arg == "true" || string.IsNullOrEmpty(arg);
            switch (name)
            {
                case "godrays":
                    if (godRays == null) break;
                    if (on) godRays.Show(); else godRays.Hide();
                    break;
                case "dof":
                    if (fakeDoF != null) fakeDoF.SetFocus(on);
                    break;
                case "clouds":
                    if (cloudShadows == null) break;
                    if (on) cloudShadows.Show(); else cloudShadows.Hide();
                    break;
                case "haze":
                    if (heatHaze != null) heatHaze.SetActive(on);
                    break;
                case "shimmer":
                    if (backgroundFx == null) break;
                    if (on)
                    {
                        backgroundFx.SetWaterShimmer(0f);
                        backgroundFx.DOShimmerAmount(0.85f, 1f);
                    }
                    else backgroundFx.DOShimmerAmount(0f, 0.8f);
                    break;
                case "heartbeat":
                    if (heartbeat == null) break;
                    if (on) heartbeat.StartBeat(); else heartbeat.StopBeat();
                    break;
                case "dutch":
                    if (dutchAngle == null) break;
                    if (on) dutchAngle.SetTilt(dutchAngle.angle); else dutchAngle.Clear();
                    break;
                case "focus":
                    if (vignetteFocus == null) break;
                    if (arg == "off" || string.IsNullOrEmpty(arg)) vignetteFocus.ClearFocus();
                    else
                    {
                        var c = Get(arg);
                        if (c != null) vignetteFocus.FocusOn(c.fx.Rect);
                        else Debug.LogWarning($"[VNScript] 第 {line} 行：fx focus 的角色「{arg}」不在场上");
                    }
                    break;
                default:
                    Debug.LogWarning($"[VNScript] 第 {line} 行：未知 fx「{name}」");
                    break;
            }
        }
    }
}
