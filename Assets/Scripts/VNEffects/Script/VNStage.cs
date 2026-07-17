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
        public VNSpeedLines speedLines;
        public VNScreenShockwave shockwave;
        public VNRetroFilter retroFilter;
        public VNKenBurns kenBurns;
        public VNLetterbox letterbox;
        public VNShootingStars shootingStars;
        public VNDriftingClouds driftingClouds;
        public VNHeatHaze heatHaze;
        public VNVignetteFocus vignetteFocus;
        public VNSpeakerHighlight speakerHighlight;
        public VNToneMatch toneMatch;
        public VNChoicePanel choicePanel;
        public VNAudio vnAudio;
        public VNEventRegistry eventRegistry;

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
            public VNCharacterBlink blink;
            public VNCharacterMouth mouth;
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
            if (speedLines == null) speedLines = FindFirstObjectByType<VNSpeedLines>();
            if (shockwave == null) shockwave = FindFirstObjectByType<VNScreenShockwave>();
            if (retroFilter == null) retroFilter = FindFirstObjectByType<VNRetroFilter>();
            if (letterbox == null) letterbox = FindFirstObjectByType<VNLetterbox>();
            if (shootingStars == null) shootingStars = FindFirstObjectByType<VNShootingStars>();
            if (driftingClouds == null) driftingClouds = FindFirstObjectByType<VNDriftingClouds>();
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
            if (kenBurns == null) kenBurns = FindFirstObjectByType<VNKenBurns>();
            if (kenBurns == null && backgroundImage != null) // 旧场景自愈：自动补挂
                kenBurns = backgroundImage.gameObject.AddComponent<VNKenBurns>();
            // Ken Burns 默认开启：种入 fx 状态表，存档才能正确记录"仍开着"
            _fxStates["kenburns"] = kenBurns != null && kenBurns.playOnAwake;
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
            c.mouth?.ForceClosed();
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
            if (sprite == null) return;

            bool isDefault = c.def.IsDefaultExpression(expr);
            c.blink?.PrepareForExpressionChange();
            c.mouth?.PrepareForExpressionChange();

            if (sprite == c.image.sprite)
            {
                c.expression = expr;
                c.blink?.SetExpression(sprite, isDefault);
                c.mouth?.SetExpression(expr);
                return;
            }

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
            c.blink?.SetExpression(sprite, isDefault);
            c.mouth?.SetExpression(expr);
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
            c.blink = go.AddComponent<VNCharacterBlink>();
            c.blink.Initialize(img, def);
            c.mouth = go.AddComponent<VNCharacterMouth>();
            c.mouth.Initialize(img, def, c.fx.Mat);
            go.AddComponent<VNFootShadow>();

            _active[def.id] = c;
            if (speakerHighlight != null) speakerHighlight.Register(c.fx);
            RefreshRegistries();
            return c;
        }

        // ------------------------------------------------------------------
        // 镜头目标点解析（camseq / camto / camcut 用）
        // ------------------------------------------------------------------

        /// <summary>
        /// 解析镜头目标点 token → 画布坐标（中心为原点，1920×1080 坐标系）。
        /// 支持：
        ///   九宫格锚点：topleft top topright left middle(center) right
        ///               bottomleft bottom bottomright，origin/reset = 中心
        ///   角色[:部位]：亚里沙 / 亚里沙:head|chest|waist|feet|up|mid|down
        ///   裸坐标：300,200
        /// 解析失败返回 null 并告警。
        /// </summary>
        public Vector2? ResolveCamPoint(string token, int line = 0)
        {
            if (string.IsNullOrEmpty(token))
            {
                Debug.LogWarning($"[VNScript] 第 {line} 行：镜头目标点为空");
                return null;
            }

            // 九宫格锚点
            switch (token.ToLower())
            {
                case "topleft": return new Vector2(-620f, 340f);
                case "top": return new Vector2(0f, 340f);
                case "topright": return new Vector2(620f, 340f);
                case "left": return new Vector2(-620f, 0f);
                case "middle":
                case "center":
                case "origin":
                case "reset": return Vector2.zero;
                case "right": return new Vector2(620f, 0f);
                case "bottomleft": return new Vector2(-620f, -340f);
                case "bottom": return new Vector2(0f, -340f);
                case "bottomright": return new Vector2(620f, -340f);
            }

            // 裸坐标 x,y
            int comma = token.IndexOf(',');
            if (comma > 0)
            {
                if (float.TryParse(token.Substring(0, comma), out float px) &&
                    float.TryParse(token.Substring(comma + 1), out float py))
                    return new Vector2(px, py);
                Debug.LogWarning($"[VNScript] 第 {line} 行：坐标「{token}」格式应为 x,y");
                return null;
            }

            // 角色[:部位]
            string id = token;
            string part = null;
            int colon = token.IndexOf(':');
            if (colon > 0)
            {
                id = token.Substring(0, colon);
                part = token.Substring(colon + 1).ToLower();
            }

            var c = Get(id);
            if (c == null)
            {
                Debug.LogWarning($"[VNScript] 第 {line} 行：镜头目标「{token}」不是锚点/坐标，角色「{id}」也不在场上");
                return null;
            }

            float frac = 0f; // 相对立绘高度的纵向偏移比例
            switch (part)
            {
                case null: case "": frac = 0f; break;
                case "head": frac = 0.36f; break;
                case "chest": frac = 0.15f; break;
                case "waist": frac = -0.08f; break;
                case "feet": frac = -0.42f; break;
                case "up": frac = 0.3f; break;
                case "mid": frac = 0f; break;
                case "down": frac = -0.3f; break;
                default:
                    Debug.LogWarning($"[VNScript] 第 {line} 行：未知身体部位「{part}」" +
                                     "（head/chest/waist/feet/up/mid/down），使用角色中心");
                    break;
            }
            return c.rect.anchoredPosition + new Vector2(0f, c.rect.sizeDelta.y * frac);
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
            data.bgmVol = vnAudio != null ? vnAudio.CurrentBgmVol : 1f;
            data.portraitOff = _portraitOff;

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
        public void RestoreSnapshot(VNSaveData data) => RestoreSnapshot(data, false);

        /// <summary>恢复舞台快照；instant 用于“从选中行播放”的静默状态重建。</summary>
        public void RestoreSnapshot(VNSaveData data, bool instant)
        {
            ClearStage();

            if (!string.IsNullOrEmpty(data.backgroundId))
                SetBackground(data.backgroundId, null);
            else
            {
                CurrentBackgroundId = null;
                if (backgroundImage != null) backgroundImage.sprite = null;
            }

            if (weather != null)
                weather.SetWeather(
                    VNScriptParser.ParseEnum(data.weather, VNWeather.None, 0),
                    instant ? 0.01f : 0.1f);
            var restoredMood = VNScriptParser.ParseEnum(data.mood, VNMood.Neutral, 0);
            if (mood != null)
                mood.SetMood(restoredMood, instant ? 0.01f : 0.3f);

            // 先全部关掉可开关 fx，再打开存档里记录的
            foreach (var name in ToggleFxNames) Fx(name, "off");
            foreach (var name in data.fxOn) Fx(name, "on");
            // 黑边若与回忆色调同时恢复，视为自动黑边（之后离开回忆会自动撤掉）
            _letterboxAuto = restoredMood == VNMood.Memory && data.fxOn.Contains("letterbox");
            // 复古滤镜同理：与对应 mood 同时恢复视为自动滤镜
            _retroAuto = (restoredMood == VNMood.Memory && data.fxOn.Contains("filmgrain")) ||
                         (restoredMood == VNMood.Dream && data.fxOn.Contains("crt"));

            if (vnAudio != null)
            {
                if (instant) vnAudio.ResetForDebug();
                float fade = instant ? 0.01f : 0.6f;
                if (!string.IsNullOrEmpty(data.bgm))
                    vnAudio.PlayBgm(data.bgm, fade, data.bgmVol > 0f ? data.bgmVol : 1f);
                else vnAudio.StopBgm(fade);
            }

            SetPortraitEnabled(!data.portraitOff);

            foreach (var cs in data.characters)
                ShowInstant(cs.id, cs.x, cs.expr);
        }

        /// <summary>清空舞台：销毁全部在场角色、关闭残留的选项面板</summary>
        public void ClearStage()
        {
            StopSpeaking();
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
                dialogue.Say(c.def.displayName, text);
                c.mouth?.BeginSpeaking(followVoice, dialogue, vnAudio);
            }
            else
            {
                if (speakerHighlight != null && string.IsNullOrEmpty(speaker) == false)
                    speakerHighlight.ClearSpeaker();
                dialogue.SetPortrait(null); // 旁白/未注册角色不显示头像
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
        /// </summary>
        public Sequence SetBackground(string id, string transitionName, int line = 0,
            System.Action onCovered = null)
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
                if (VNScreenTransition.SupportsDirectBackground(type) &&
                    backgroundImage != null && backgroundImage.sprite != null)
                {
                    var directSequence = transition.PlayBackground(type, backgroundImage, entry.sprite, () =>
                    {
                        if (toneMatch != null) toneMatch.MatchTo(entry.sprite);
                        onCovered?.Invoke();
                    });
                    if (directSequence != null) return directSequence;
                    // 直接转场 Shader 不可用时安全退回原全屏转场，确保背景仍会切换。
                }
                return transition.Play(type, () =>
                {
                    ApplyBackground(entry.sprite);
                    onCovered?.Invoke();
                });
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
            { "godrays", "dof", "clouds", "haze", "shimmer", "heartbeat", "dutch",
              "speedlines", "letterbox", "meteor", "skycloud", "filmgrain", "crt",
              "kenburns" };

        [Tooltip("mood Memory（回忆）自动上电影黑边、离开回忆自动撤掉")]
        public bool autoMemoryLetterbox = true;

        [Tooltip("mood Memory（回忆）自动上胶片滤镜、mood Dream（梦境）自动上 CRT 滤镜")]
        public bool autoMoodRetroFilter = true;

        bool _letterboxAuto; // 当前黑边是否由回忆色调自动打开（离开回忆时才自动撤）
        bool _retroAuto;     // 当前复古滤镜是否由 mood 自动打开（离开对应 mood 时才自动撤）

        /// <summary>mood 命令入口：切换情绪色调 + 回忆黑边/复古滤镜自动联动</summary>
        public void SetMood(VNMood m, float duration = -1f)
        {
            if (mood != null)
            {
                if (duration > 0f) mood.SetMood(m, duration);
                else mood.SetMood(m);
            }
            if (autoMemoryLetterbox && letterbox != null)
            {
                if (m == VNMood.Memory && !letterbox.IsShown)
                {
                    letterbox.Show();
                    _fxStates["letterbox"] = true;
                    _letterboxAuto = true;
                }
                else if (m != VNMood.Memory && _letterboxAuto)
                {
                    letterbox.Hide();
                    _fxStates["letterbox"] = false;
                    _letterboxAuto = false;
                }
            }
            if (autoMoodRetroFilter && retroFilter != null)
            {
                if (m == VNMood.Memory && !retroFilter.IsShown)
                {
                    retroFilter.ShowFilm();
                    _fxStates["filmgrain"] = true;
                    _retroAuto = true;
                }
                else if (m == VNMood.Dream && !retroFilter.IsShown)
                {
                    retroFilter.ShowCrt();
                    _fxStates["crt"] = true;
                    _retroAuto = true;
                }
                else if (m != VNMood.Memory && m != VNMood.Dream && _retroAuto)
                {
                    retroFilter.Hide();
                    _fxStates["filmgrain"] = false;
                    _fxStates["crt"] = false;
                    _retroAuto = false;
                }
            }
        }

        /// <summary>letterbox 命令入口：手动控制会接管自动黑边</summary>
        public void SetLetterbox(bool on, float height = -1f, float duration = -1f)
        {
            _letterboxAuto = false;
            _fxStates["letterbox"] = on;
            if (letterbox == null) return;
            if (on) letterbox.Show(height, duration);
            else letterbox.Hide(duration);
        }

        /// <summary>章节转场用：关闭天气、情绪色调和全部持续型画面特效。</summary>
        public void ResetEffects()
        {
            weather?.SetWeather(VNWeather.None, 0.8f);
            mood?.SetMood(VNMood.Neutral, 0.8f);
            foreach (var name in ToggleFxNames) Fx(name, "off");
            Fx("focus", "off");
            // Ken Burns 是默认开启的常驻氛围（"永不静止"），重置回默认开而非关
            if (kenBurns != null && kenBurns.playOnAwake) Fx("kenburns", "on");
        }

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
                case "meteor":
                    if (shootingStars == null) break;
                    if (on) shootingStars.Show(); else shootingStars.Hide();
                    break;
                case "skycloud":
                    if (driftingClouds == null) break;
                    if (on) driftingClouds.Show(); else driftingClouds.Hide();
                    break;
                case "letterbox":
                    _letterboxAuto = false;
                    if (letterbox == null) break;
                    if (on) letterbox.Show(); else letterbox.Hide();
                    break;
                case "speedlines":
                    if (speedLines == null) break;
                    if (arg == "burst") speedLines.Burst(); // 一次性冲击，不记录开关状态
                    else if (on) speedLines.Show();
                    else speedLines.Hide();
                    break;
                case "shockwave": // 一次性演出，不记录开关状态
                    if (shockwave == null) break;
                    shockwave.Play(arg == "heavy" ? 1.4f : arg == "light" ? 0.6f : 1f);
                    break;
                case "filmgrain": // 与 crt 互斥：手动控制会接管 mood 自动滤镜
                    _retroAuto = false;
                    if (on) _fxStates["crt"] = false;
                    if (retroFilter == null) break;
                    if (on) retroFilter.ShowFilm(); else retroFilter.Hide();
                    break;
                case "crt":
                    _retroAuto = false;
                    if (on) _fxStates["filmgrain"] = false;
                    if (retroFilter == null) break;
                    if (on) retroFilter.ShowCrt(); else retroFilter.Hide();
                    break;
                case "kenburns":
                    if (kenBurns != null) kenBurns.SetPlaying(on);
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
