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
        public VNChoicePanel choicePanel;
        public VNAudio vnAudio;

        [Tooltip("表情切换的交叉溶解时长（0 = 瞬间切换）")]
        public float expressionCrossfade = 0.25f;

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

        void Awake()
        {
            AutoWire();
        }

        /// <summary>
        /// 自动补线：Inspector 里为空的引用自动在场景中查找。
        /// 这样给 VNStage 加新字段后，旧场景不重新生成也能正常工作。
        /// </summary>
        void AutoWire()
        {
            if (dialogue == null) dialogue = FindFirstObjectByType<VNDialogueBox>();
            if (transition == null) transition = FindFirstObjectByType<VNScreenTransition>();
            if (weather == null) weather = FindFirstObjectByType<VNWeatherController>();
            if (mood == null) mood = FindFirstObjectByType<VNMoodGrading>();
            if (vnCamera == null) vnCamera = FindFirstObjectByType<VNCamera>();
            if (screenShake == null) screenShake = FindFirstObjectByType<VNScreenShake>();
            if (dutchAngle == null) dutchAngle = FindFirstObjectByType<VNDutchAngle>();
            if (heartbeat == null) heartbeat = FindFirstObjectByType<VNHeartbeat>();
            if (sakura == null) sakura = FindFirstObjectByType<VNSakuraBurst>();
            if (fakeDoF == null) fakeDoF = FindFirstObjectByType<VNFakeDoF>();
            if (cloudShadows == null) cloudShadows = FindFirstObjectByType<VNCloudShadows>();
            if (godRays == null) godRays = FindFirstObjectByType<VNGodRays>();
            if (heatHaze == null) heatHaze = FindFirstObjectByType<VNHeatHaze>();
            if (vignetteFocus == null) vignetteFocus = FindFirstObjectByType<VNVignetteFocus>();
            if (speakerHighlight == null) speakerHighlight = FindFirstObjectByType<VNSpeakerHighlight>();
            if (toneMatch == null) toneMatch = FindFirstObjectByType<VNToneMatch>();
            if (choicePanel == null) choicePanel = FindFirstObjectByType<VNChoicePanel>();
            if (vnAudio == null)
            {
                vnAudio = FindFirstObjectByType<VNAudio>();
                if (vnAudio == null) // 旧场景自愈：自动创建
                    vnAudio = new GameObject("VNAudio").AddComponent<VNAudio>();
            }

            if (characterLayer == null)
            {
                var go = GameObject.Find("LayerFront");
                if (go != null) characterLayer = (RectTransform)go.transform;
            }
            if (backgroundImage == null)
            {
                var go = GameObject.Find("Background");
                if (go != null) backgroundImage = go.GetComponent<Image>();
            }
            if (backgroundFx == null && backgroundImage != null)
                backgroundFx = backgroundImage.GetComponent<VNImageEffectController>();
        }

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

        /// <summary>该角色的实际显示高度（舞台统一高度 × 角色尺寸标定）</summary>
        float HeightFor(VNCharacterDef def) =>
            characterHeight * Mathf.Max(0.05f, def.sizeScale);

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
                // 标准站位 + 该角色的标定偏移（吸收素材构图差异）
                var pos = SlotPosition(at) + def.positionOffset;
                c.rect.anchoredPosition = pos;
                // 关键：同步各组件缓存的"基准位"，否则出场动画会把角色重置回旧位置
                c.animator.SetBasePosition(pos);
                c.emotes.SetBasePosition(pos);
                c.fx.SetFloatBaseY(pos.y);
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

            // 交叉溶解：角色完全可见时，旧表情以"残像"覆盖在上面淡出（新表情立即生效）
            var group = c.go.GetComponent<CanvasGroup>();
            bool visible = c.fx.GetDissolve() > 0.9f && (group == null || group.alpha > 0.5f);
            if (visible && expressionCrossfade > 0.01f && c.image.sprite != null)
                SpawnExpressionGhost(c);

            c.image.sprite = sprite;
            c.expression = expr;
            // 不同表情图宽高比可能不同 → 以该角色的标定高度重算宽度
            float h = HeightFor(c.def);
            float aspect = sprite.rect.width / sprite.rect.height;
            c.rect.sizeDelta = new Vector2(h * aspect, h);
        }

        /// <summary>复制一份旧表情立绘覆盖在角色上淡出（表情交叉溶解）</summary>
        void SpawnExpressionGhost(ActiveCharacter c)
        {
            var go = new GameObject("ExprGhost",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(c.rect.parent, false);
            rect.SetSiblingIndex(c.rect.GetSiblingIndex() + 1); // 盖在本体之上
            rect.anchorMin = c.rect.anchorMin;
            rect.anchorMax = c.rect.anchorMax;
            rect.pivot = c.rect.pivot;
            rect.sizeDelta = c.rect.sizeDelta;
            rect.anchoredPosition = c.rect.anchoredPosition;
            rect.localScale = c.rect.localScale;
            rect.localRotation = c.rect.localRotation;

            var img = go.GetComponent<Image>();
            img.sprite = c.image.sprite;
            img.preserveAspect = true;
            img.raycastTarget = false;

            img.DOFade(0f, expressionCrossfade).SetEase(Ease.InOutSine)
               .SetLink(go).OnComplete(() => Destroy(go));
        }

        /// <summary>角色滑步换位（move 命令）：悬浮暂停、基准位同步、到位后恢复</summary>
        public Tween Move(string id, string at, float duration, int line = 0)
        {
            var c = Get(id);
            if (c == null)
            {
                Debug.LogWarning($"[VNScript] 第 {line} 行：move 的角色「{id}」不在场上");
                return null;
            }

            var target = SlotPosition(at) + c.def.positionOffset;
            bool wasFloating = c.fx.IsFloating;
            c.fx.StopFloating(); // 会重置到旧基准位，从干净状态起步
            c.animator.SetBasePosition(target);
            c.emotes.SetBasePosition(target);
            c.fx.SetFloatBaseY(target.y);

            var t = c.rect.DOAnchorPos(target, Mathf.Max(0.05f, duration))
                          .SetEase(Ease.InOutSine).SetLink(c.go);
            if (wasFloating) t.OnComplete(() => c.fx.ResumeFloating());
            return t;
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
            rect.anchoredPosition = new Vector2(0f, -60f) + def.positionOffset;

            var img = go.GetComponent<Image>();
            img.preserveAspect = true;
            img.raycastTarget = false;
            var sprite = def.GetSprite(null);
            img.sprite = sprite;
            if (sprite != null)
            {
                float h = HeightFor(def);
                float aspect = sprite.rect.width / sprite.rect.height;
                rect.sizeDelta = new Vector2(h * aspect, h);
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

        /// <summary>角色显示名（未注册时原样返回，Backlog 用）</summary>
        public string GetDisplayName(string speaker)
        {
            var def = characters.Find(c => c.id == speaker);
            return def != null ? def.displayName : speaker;
        }

        // ------------------------------------------------------------------
        // 存档快照 / 读档恢复
        // ------------------------------------------------------------------

        /// <summary>把当前舞台状态写入存档数据</summary>
        public void CaptureSnapshot(VNSaveData data)
        {
            data.backgroundId = CurrentBackgroundId;
            data.weather = weather != null ? weather.Current.ToString() : null;
            data.mood = mood != null ? mood.Current.ToString() : null;
            data.bgm = vnAudio != null ? vnAudio.CurrentBgm : null;

            data.fxOn.Clear();
            foreach (var kv in _fxStates)
                if (kv.Value) data.fxOn.Add(kv.Key);

            data.characters.Clear();
            foreach (var kv in _active)
            {
                data.characters.Add(new VNSaveData.CharSave
                {
                    id = kv.Key,
                    x = kv.Value.rect.anchoredPosition.x,
                    expr = kv.Value.expression,
                });
            }
        }

        /// <summary>清空舞台并按存档数据瞬间摆台（读档用）</summary>
        public void RestoreSnapshot(VNSaveData data)
        {
            ClearStage();

            if (!string.IsNullOrEmpty(data.backgroundId))
                SetBackground(data.backgroundId, null);

            if (weather != null)
                weather.SetWeather(
                    VNScriptParser.ParseEnum(data.weather, VNWeather.None, 0), 0.1f);
            if (mood != null)
                mood.SetMood(
                    VNScriptParser.ParseEnum(data.mood, VNMood.Neutral, 0), 0.3f);

            // 先全部关掉可开关 fx，再打开存档里记录的
            foreach (var name in ToggleFxNames) Fx(name, "off");
            foreach (var name in data.fxOn) Fx(name, "on");

            if (vnAudio != null)
            {
                if (!string.IsNullOrEmpty(data.bgm)) vnAudio.PlayBgm(data.bgm, 0.6f);
                else vnAudio.StopBgm(0.6f);
            }

            foreach (var cs in data.characters)
                ShowInstant(cs.id, cs.x, cs.expr);
        }

        /// <summary>清空舞台：销毁全部在场角色、关闭残留的选项面板</summary>
        public void ClearStage()
        {
            foreach (var kv in _active)
                if (kv.Value.go != null) Destroy(kv.Value.go);
            _active.Clear();
            if (choicePanel != null) choicePanel.ForceClose();
            RefreshRegistries();
        }

        /// <summary>瞬间摆台一个角色（无出场动画，读档用）</summary>
        public void ShowInstant(string id, float x, string expr)
        {
            var def = characters.Find(c => c.id == id);
            if (def == null)
            {
                Debug.LogError($"[VNSave] 存档里的角色「{id}」未在 VNStage.characters 注册");
                return;
            }

            var c = Get(id) ?? CreateCharacter(def);
            // 存档里的 x 已含偏移，y 用标定偏移重建
            var pos = new Vector2(x, -60f + def.positionOffset.y);
            c.rect.anchoredPosition = pos;
            c.animator.SetBasePosition(pos);
            c.emotes.SetBasePosition(pos);
            c.fx.SetFloatBaseY(pos.y);
            ApplyExpression(c, expr);

            // 直接置为完全可见状态
            c.fx.SetDissolve(1f);
            c.fx.SetFlash(0f);
            var group = c.go.GetComponent<CanvasGroup>();
            if (group != null) group.alpha = 1f;
            c.animator.StartIdleEffects();
        }

        /// <summary>在场角色变化后，刷新色调匹配等注册表</summary>
        void RefreshRegistries()
        {
            if (speakerHighlight != null)
                speakerHighlight.characters.RemoveAll(f => f == null);

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

        /// <summary>当前背景 id（存档用）</summary>
        public string CurrentBackgroundId { get; private set; }

        /// <summary>切换背景（可选全屏转场），返回可等待的 Sequence（无转场时为 null）</summary>
        public Sequence SetBackground(string id, string transitionName, int line = 0)
        {
            var entry = backgrounds.Find(b => b.id == id);
            if (entry == null || entry.sprite == null)
            {
                Debug.LogError($"[VNScript] 第 {line} 行：未注册的背景「{id}」（检查 VNStage.backgrounds）");
                return null;
            }

            CurrentBackgroundId = id;
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

        /// <summary>可开关型 fx 的当前状态（存档用）</summary>
        readonly Dictionary<string, bool> _fxStates = new Dictionary<string, bool>();

        static readonly string[] ToggleFxNames =
            { "godrays", "dof", "clouds", "haze", "shimmer", "heartbeat", "dutch" };

        /// <summary>fx 命令：fx godrays on / fx dof off / fx focus 亚里沙 / fx heartbeat on …</summary>
        public void Fx(string name, string arg, int line = 0)
        {
            bool on = arg == "on" || arg == "true" || string.IsNullOrEmpty(arg);
            if (System.Array.IndexOf(ToggleFxNames, name) >= 0)
                _fxStates[name] = on && arg != "off"; // focus 等非开关型不记录
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
