using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 教程播放器：压暗全屏 → 挖洞高亮 → 图文卡片 → 点一下下一步。
    /// 讲解期间整个玩法冻结（<see cref="VNPause"/>），ESC 一键跳过整篇。
    ///
    /// 【三个触发入口，一份实现】
    ///   ① 剧本 `tutorial &lt;id&gt; [force:on]` —— VNScriptRunner 协程等它播完
    ///   ② 模块首次启动自动播 —— VNEventModule 在 OnLaunch **之后**调
    ///      （必须之后：要高亮记分板，记分板得先存在）
    ///   ③ 模块内任意时机 `yield return VNTutorialPlayer.PlayIdCo(id)`
    ///
    /// 【排序为什么是 92】
    /// 在对话框(40) / 事件层(60) / 过场层(90) 之上、全屏转场(100) 之下。
    /// 压在转场之下是项目一贯的语义：任何时候 transition 都能盖住一切。
    ///
    /// 【为什么挂在主 Canvas 下而不是自建 Overlay Canvas】
    /// 同 VNInterludeScreen：Screen Space - Overlay 的画布永远压在
    /// Screen Space - Camera 的主画布之上，转场层就再也盖不住它了；
    /// 而且挂主 Canvas 才吃得到 URP 后处理 —— 洞口的 HDR 描边靠 Bloom 发光。
    ///
    /// 【光标】
    /// 亲密互动模块会把系统光标藏起来（VNTouchCursor 里 Cursor.visible = false）。
    /// 教程弹在它上面时玩家看不见指针也就点不了「下一步」，所以进教程强制显示、
    /// 退出时还原原值。
    ///
    /// 【不进存档】
    /// 教程是一段一次性演出，播完什么都不留；「看过没有」走全局
    /// <see cref="VNTutorialSeen"/>。所以既不进 VNSaveData，也不参与调试重建。
    /// </summary>
    public class VNTutorialPlayer : MonoBehaviour
    {
        const float FadeIn = 0.22f;
        const float FadeOut = 0.18f;
        /// <summary>每步最短停留：挡掉从上一屏带过来的那一下点击</summary>
        const float MinStepTime = 0.22f;

        [Header("渲染排序（务必保持 < 全屏转场的 100）")]
        public int sortingOrder = 92;

        [Header("卡片宽度（像素，1920×1080 基准；程序化卡片用）")]
        public float cardWidth = 780f;

        // 出正式包前建议指一份材质资产：只被 Shader.Find 引用的 shader 会被打包剥掉，
        // 同 VNScreenShockwave.sourceMaterial 的处理。留空时运行时走 Shader.Find。
        [Header("暗幕材质来源（留空 = 运行时 Shader.Find(\"VN/TutorialMask\")）")]
        public Material maskMaterial;

        // 属性 HUD(580) / 日历(578) / 背包·任务面板(600) 都是 Screen Space - Overlay 画布，
        // 永远压在主 Canvas 之上——教程层挂主 Canvas 下就永远盖不住它们。
        // 一步的目标落在 Overlay 画布上时，整层临时搬到这块更高的 Overlay 画布；
        // 代价是那一步吃不到 Bloom（描边不发光），比挖不到强。低于 Toast(999)。
        [Header("目标在 Overlay 画布上时临时使用的画布排序（须 > 各面板的 600）")]
        public int overlaySortingOrder = 950;

        static VNTutorialPlayer _instance;

        Transform _mainHost;       // 正常宿主：本物体（主 Canvas 下的嵌套画布）
        Transform _overlayHost;    // 临时宿主：独立 Overlay 画布，按需创建
        bool _onOverlay;

        RectTransform _root;
        CanvasGroup _group;
        VNTutorialMask _mask;

        // 卡片：皮肤优先，缺失退回程序化
        VNTutorialSkin _skin;
        RectTransform _card;
        CanvasGroup _cardGroup;
        TMP_Text _title, _body, _progress, _hint, _skipHint;
        GameObject _imageRoot;
        Image _image;

        VNPause.Handle _pause;
        VNScriptRunner _runner;
        bool _cursorWasVisible;
        bool _buttonPressed;
        bool _cancelled;

        readonly List<VNTutorialMask.Hole> _holes = new List<VNTutorialMask.Hole>();

        public bool IsPlaying { get; private set; }

        /// <summary>场景里的播放器；没有就返回 null（VNStage 负责自愈创建）</summary>
        public static VNTutorialPlayer Instance
        {
            get
            {
                if (_instance == null)
                    _instance = FindFirstObjectByType<VNTutorialPlayer>(FindObjectsInactive.Include);
                return _instance;
            }
        }

        void Awake()
        {
            if (_instance == null) _instance = this;
        }

        // ==================================================================
        // 查表与静态入口
        // ==================================================================

        /// <summary>按 id 找教程资产。库只在 VNGameConfig 里（同 interlude，没有场景侧配置）。</summary>
        public static VNTutorialDef Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var config = VNGameConfig.Active;
            if (config == null || config.tutorials == null) return null;
            foreach (var def in config.tutorials)
            {
                if (def == null) continue;
                // id 留空时按资产文件名认，与 interlude / chapter 的容错一致
                string key = string.IsNullOrEmpty(def.id) ? def.name : def.id;
                if (key == id) return def;
            }
            return null;
        }

        /// <summary>
        /// 播一篇教程并等它结束（模块内用：<c>yield return VNTutorialPlayer.PlayIdCo("羽毛球基础")</c>）。
        /// 找不到播放器 / 找不到资产 / 已经看过，都会立刻结束，不会卡住调用方。
        /// </summary>
        public static IEnumerator PlayIdCo(string id, bool force = false)
        {
            var player = Instance;
            var def = Find(id);
            if (player == null || def == null)
            {
                if (def == null && !string.IsNullOrEmpty(id))
                    Debug.LogWarning($"[VNTutorial] 找不到教程「{id}」" +
                                     "（在 VNGameConfig 的「教程库」里登记 VNTutorialDef 资产）");
                yield break;
            }
            yield return player.PlayCo(def, force);
        }

        /// <summary>发后不管地播一篇（模块首次自动播用）。</summary>
        public static void PlayAuto(string id, MonoBehaviour host)
        {
            var player = Instance;
            var def = Find(id);
            if (player == null || def == null)
            {
                if (def == null && !string.IsNullOrEmpty(id))
                    Debug.LogWarning($"[VNTutorial] 找不到教程「{id}」" +
                                     "（在 VNGameConfig 的「教程库」里登记 VNTutorialDef 资产）");
                return;
            }
            if (!player.ShouldPlay(def, false)) return;
            // 宿主可能中途被销毁（模块被 Destroy），所以协程挂在播放器自己身上
            player.StartCoroutine(player.PlayCo(def, false));
        }

        // ==================================================================
        // 播放
        // ==================================================================

        /// <summary>看过了 / 总开关关着 / 没有步骤 → 不该播</summary>
        public bool ShouldPlay(VNTutorialDef def, bool force)
        {
            if (def == null || def.StepCount == 0) return false;
            if (force) return true;
            if (!VNTutorialSeen.Enabled) return false;
            string key = string.IsNullOrEmpty(def.id) ? def.name : def.id;
            return !(def.once && VNTutorialSeen.Has(key));
        }

        /// <summary>播一篇教程。协程结束时教程层已经完全收起、暂停已经释放。</summary>
        public IEnumerator PlayCo(VNTutorialDef def, bool force = false)
        {
            if (IsPlaying) yield break;          // 同时只有一篇，后来的直接放弃
            if (!ShouldPlay(def, force)) yield break;

            Build();
            IsPlaying = true;
            _cancelled = false;

            // ---- 冻住世界 ----
            _pause = VNPause.Acquire(gameObject, "tutorial");
            _cursorWasVisible = Cursor.visible;
            Cursor.visible = true;              // 互动模块藏了系统光标，这里要抢回来
            if (_runner == null) _runner = FindFirstObjectByType<VNScriptRunner>();
            _runner?.SetSkip(false);            // 到教学必停，同 choice / event
            _runner?.SetAuto(false);

            _root.gameObject.SetActive(true);
            _group.alpha = 0f;
            _group.blocksRaycasts = true;
            yield return _group.DOFade(1f, FadeIn).SetUpdate(true).SetLink(gameObject)
                               .WaitForCompletion();

            // ---- 逐步讲解 ----
            int total = def.steps.Count;
            for (int i = 0; i < total; i++)
            {
                var step = def.steps[i];
                if (step == null) continue;
                ApplyStep(def, step, i, total);
                PlayStepSe(step);
                yield return WaitStepCo(def);
                if (_cancelled) break;
            }

            // ---- 收起 ----
            // 淡出至少跨两帧，所以推进用的那一下点击/按键在解除暂停时
            // wasPressedThisFrame 早已复位，不会被下面的模块或 Runner 再吃一次
            // （ESC 尤其要紧：羽毛球把它当认输键）
            yield return _group.DOFade(0f, FadeOut).SetUpdate(true).SetLink(gameObject)
                               .WaitForCompletion();
            // ESC 跳过也算看过：玩家明确表示不想看，下次不该再拦他一次。
            // 想重看走设置面板的「重置教程记录」或剧本 force:on。
            VNTutorialSeen.Mark(string.IsNullOrEmpty(def.id) ? def.name : def.id);
            Close();
        }

        /// <summary>
        /// 立刻收起。剧本 Stop / 调试中断 / 场景切换都要走它，
        /// 否则暗幕留在屏幕上，而且暂停不解除 = 游戏永久卡死。
        /// </summary>
        public void CancelImmediate()
        {
            if (!IsPlaying) return;
            StopAllCoroutines();
            _cancelled = true;
            if (_group != null)
            {
                _group.DOKill();
                _group.alpha = 0f;
            }
            Close();
        }

        void Close()
        {
            IsPlaying = false;
            IsEditorPreview = false;
            if (_group != null) _group.blocksRaycasts = false;
            if (_root != null) _root.gameObject.SetActive(false);
            Cursor.visible = _cursorWasVisible;
            VNPause.Release(ref _pause);
        }

        // 三条兜底路径：宿主被禁用 / 被销毁时也必须把暂停还回去
        void OnDisable()
        {
            if (IsPlaying) CancelImmediate();
            else VNPause.Release(ref _pause);
        }

        void OnDestroy()
        {
            VNPause.Release(ref _pause);
            // 临时 Overlay 画布挂在场景根（嵌在任何 Canvas 下都会变成嵌套画布），
            // 不随本物体一起销毁，得手动收
            if (_overlayHost != null) Destroy(_overlayHost.gameObject);
            if (_instance == this) _instance = null;
        }

        // ==================================================================
        // 宿主切换：主 Canvas 下的嵌套画布 ⇄ 独立 Overlay 画布
        // ==================================================================

        /// <summary>目标控件是否在 Screen Space - Overlay 画布上（主 Canvas 下的教程层盖不住）</summary>
        static bool NeedsOverlay(RectTransform target)
        {
            if (target == null) return false;
            var canvas = target.GetComponentInParent<Canvas>();
            return canvas != null && canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay;
        }

        void EnsureHost(bool overlay)
        {
            // 裸场景自愈路径本来就是 Overlay 画布（_mainHost 不是本物体），无需切换
            if (_root == null || _mainHost == null || _mainHost != transform) return;
            if (overlay == _onOverlay) return;

            Transform host;
            if (overlay)
            {
                if (_overlayHost == null)
                {
                    var go = new GameObject("VNTutorialOverlayCanvas",
                        typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                    go.hideFlags = HideFlags.DontSave;
                    var canvas = go.GetComponent<Canvas>();
                    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                    canvas.sortingOrder = overlaySortingOrder;
                    // 缩放规则照抄各面板（1920×1080 参考），洞的像素参数才对得上
                    var scaler = go.GetComponent<CanvasScaler>();
                    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                    scaler.referenceResolution = new Vector2(1920f, 1080f);
                    var mainScaler = GetComponentInParent<CanvasScaler>();
                    if (mainScaler != null) scaler.matchWidthOrHeight = mainScaler.matchWidthOrHeight;
                    _overlayHost = go.transform;
                }
                host = _overlayHost;
            }
            else host = _mainHost;

            _root.SetParent(host, false);
            Stretch(_root);
            _onOverlay = overlay;
        }

        /// <summary>正处于编辑器实时预览（此时 IsPlaying 也为 true，PlayCo 会被拒绝）</summary>
        public bool IsEditorPreview { get; private set; }

#if UNITY_EDITOR
        // ==================================================================
        // 编辑器实时预览（教程编辑器专用；正式流程不走这里）
        //   停在某一步、改文字立刻刷新；不标「看过」、不吃点击推进、不响应 ESC。
        //   同样占用 VNPause（否则羽毛球的球照飞），退出时走同一个 Close()。
        // ==================================================================

        int _previewIndex = -1;

        /// <summary>显示 def 的第 index 步（越界自动钳制）。首次调用会自动开启预览态。</summary>
        public void EditorPreviewApply(VNTutorialDef def, int index)
        {
            if (def == null || def.steps.Count == 0) { EditorPreviewEnd(); return; }
            if (IsPlaying && !IsEditorPreview) return;   // 正式教程在播，别插队

            if (!IsEditorPreview)
            {
                Build();
                StopAllCoroutines();
                IsPlaying = true;
                IsEditorPreview = true;
                _cancelled = false;
                _previewIndex = -1;
                if (_pause == null) _pause = VNPause.Acquire(gameObject, "tutorial-preview");
                _cursorWasVisible = Cursor.visible;
                Cursor.visible = true;
                _root.gameObject.SetActive(true);
                _group.DOKill();
                _group.alpha = 1f;
                _group.blocksRaycasts = true;
            }

            index = Mathf.Clamp(index, 0, def.steps.Count - 1);
            var step = def.steps[index];
            if (step == null) return;
            // 换步才弹入动画；同一步反复刷新（改字）就原地重画
            ApplyStep(def, step, index, def.steps.Count, animate: index != _previewIndex);
            _previewIndex = index;
        }

        /// <summary>预览态下临时隐藏/显示整层（截 Game 视图当底图时用）</summary>
        public void EditorPreviewSetVisible(bool visible)
        {
            if (!IsEditorPreview || _group == null) return;
            _group.DOKill();
            _group.alpha = visible ? 1f : 0f;
        }

        /// <summary>结束预览：收层、放开暂停，不写「看过」记录</summary>
        public void EditorPreviewEnd()
        {
            if (!IsEditorPreview) return;
            if (_group != null)
            {
                _group.DOKill();
                _group.alpha = 0f;
            }
            _previewIndex = -1;
            Close();
        }
#endif

        // ==================================================================
        // 一步的内容与等待
        // ==================================================================

        void ApplyStep(VNTutorialDef def, VNTutorialStep step, int index, int total,
            bool animate = true)
        {
            // ---- 洞 ----
            _holes.Clear();
            var target = VNTutorialAnchors.Get(step.anchor);
            if (target != null)
            {
                _holes.Add(new VNTutorialMask.Hole { target = target, padding = step.padding });
            }
            else
            {
                if (!string.IsNullOrEmpty(step.anchor) && !step.HasArea)
                    Debug.LogWarning($"[VNTutorial]「{def.id}」第 {index + 1} 步的锚点" +
                                     $"「{step.anchor}」没有登记，这一步只有整屏压暗");
                if (step.HasArea)
                    _holes.Add(new VNTutorialMask.Hole { area = step.area, padding = step.padding });
            }
            // 目标在 Overlay 画布上（HUD / 背包 / 任务面板）→ 整层搬到高排序的 Overlay 画布
            EnsureHost(NeedsOverlay(target));
            _mask.Apply(def, step, _holes);

            // ---- 文字 / 图 ----
            string title = step.ResolveTitle();
            if (_title != null)
            {
                _title.text = title ?? "";
                _title.gameObject.SetActive(!string.IsNullOrEmpty(title));
            }
            if (_body != null) _body.text = step.ResolveBody() ?? "";
            if (_imageRoot != null)
            {
                bool hasImage = step.image != null;
                _imageRoot.SetActive(hasImage);
                if (hasImage && _image != null)
                {
                    _image.sprite = step.image;
                    var le = _imageRoot.GetComponent<LayoutElement>();
                    if (le != null) le.preferredHeight = Mathf.Max(40f, step.imageHeight);
                }
            }
            if (_progress != null)
                _progress.text = VNLocale.T("tutorial.progress", index + 1, total);
            if (_hint != null)
                _hint.text = VNLocale.T(index + 1 >= total ? "tutorial.done" : "tutorial.next");
            if (_skipHint != null)
            {
                _skipHint.text = VNLocale.T("tutorial.skip");
                _skipHint.gameObject.SetActive(def.allowSkip);
            }

            PlaceCard(step);

            // 换页的小弹入：卡片整体轻微下沉后回位，让「翻了一页」看得出来
            // （编辑器实时预览改一个字就重放一次会很晃，所以可以关掉）
            if (_card != null && !animate)
            {
                _card.DOKill();
                _cardGroup.DOKill();
                _cardGroup.alpha = 1f;
            }
            else if (_card != null)
            {
                _card.DOKill();
                _cardGroup.DOKill();
                _cardGroup.alpha = 0f;
                _cardGroup.DOFade(1f, 0.18f).SetUpdate(true).SetLink(gameObject);
                Vector2 to = _card.anchoredPosition;
                _card.anchoredPosition = to + new Vector2(0f, -18f);
                _card.DOAnchorPos(to, 0.22f).SetEase(Ease.OutCubic)
                     .SetUpdate(true).SetLink(gameObject);
            }
        }

        /// <summary>卡片落位：Auto 时躲开洞（洞在上半屏就把卡片放下半屏）</summary>
        void PlaceCard(VNTutorialStep step)
        {
            if (_card == null) return;
            var spot = step.card;
            if (spot == VNTutorialCardSpot.Auto)
            {
                spot = _mask != null && _mask.TryGetFirstHoleUv(out Rect uv)
                    ? (uv.center.y > 0.5f ? VNTutorialCardSpot.Bottom : VNTutorialCardSpot.Top)
                    : VNTutorialCardSpot.Center;
            }

            float height = Mathf.Max(1f, _root.rect.height);
            float y;
            switch (spot)
            {
                case VNTutorialCardSpot.Top: y = height * 0.28f; break;
                case VNTutorialCardSpot.Bottom: y = -height * 0.28f; break;
                default: y = 0f; break;
            }
            _card.anchoredPosition = new Vector2(0f, y);
        }

        void PlayStepSe(VNTutorialStep step)
        {
            if (string.IsNullOrEmpty(step.se)) return;
            var audio = FindFirstObjectByType<VNAudio>();
            audio?.PlaySe(step.se);
        }

        /// <summary>等玩家点一下（或按 Enter/Space）；ESC = 跳过整篇</summary>
        IEnumerator WaitStepCo(VNTutorialDef def)
        {
            _buttonPressed = false;
            float shownAt = Time.unscaledTime;   // 教程期间全局暂停，只能用真实时间
            while (true)
            {
                yield return null;
                if (Time.unscaledTime - shownAt < MinStepTime) continue;

                var kb = Keyboard.current;
                if (def.allowSkip && kb != null && kb.escapeKey.wasPressedThisFrame)
                {
                    _cancelled = true;
                    yield break;
                }

                var mouse = Mouse.current;
                bool advance = _buttonPressed
                    || (mouse != null && mouse.leftButton.wasPressedThisFrame)
                    || (kb != null && (kb.enterKey.wasPressedThisFrame ||
                                       kb.spaceKey.wasPressedThisFrame));
                if (advance) yield break;
            }
        }

        // ==================================================================
        // UI 骨架
        // ==================================================================

        void Build()
        {
            if (_root != null) return;

            Transform parent = transform;
            if (GetComponentInParent<Canvas>() != null)
            {
                // 主 Canvas 下：嵌套画布 + overrideSorting，才能夹在事件层与全屏转场之间
                var canvas = gameObject.GetComponent<Canvas>();
                if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
                canvas.overrideSorting = true;
                canvas.sortingOrder = sortingOrder;
                if (gameObject.GetComponent<GraphicRaycaster>() == null)
                    gameObject.AddComponent<GraphicRaycaster>();
                Stretch(EnsureRect(gameObject));
            }
            else
            {
                // 裸场景自愈：自建 Overlay 画布
                var canvasGo = new GameObject("VNTutorialCanvas",
                    typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                canvasGo.transform.SetParent(transform, false);
                var canvas = canvasGo.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = sortingOrder;
                var scaler = canvasGo.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                parent = canvasGo.transform;
            }

            _root = CreateRect("TutorialRoot", parent);
            Stretch(_root);
            _mainHost = parent;
            _group = _root.gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;

            var maskRect = CreateRect("Mask", _root);
            Stretch(maskRect);
            maskRect.gameObject.AddComponent<RawImage>();
            // 先禁用再挂：VNTutorialMask 的 Awake 要读 sourceMaterial
            // （项目惯例：运行时创建带 Awake 配置的组件先 SetActive(false)）
            maskRect.gameObject.SetActive(false);
            _mask = maskRect.gameObject.AddComponent<VNTutorialMask>();
            _mask.sourceMaterial = maskMaterial;
            maskRect.gameObject.SetActive(true);

            BuildCard();
            _root.gameObject.SetActive(false);
        }

        void BuildCard()
        {
            var skinPrefab = VNSystemUiSkinUtility.Prefab(s => s.tutorialPrefab);
            _skin = VNSystemUiSkinUtility.Instantiate<VNTutorialSkin>(
                skinPrefab, _root, "VNTutorial");
            if (_skin != null)
            {
                BindSkin(_skin);
                return;
            }
            BuildProceduralCard();
        }

        void BindSkin(VNTutorialSkin skin)
        {
            _card = skin.panelRoot;
            // 定位由播放器统一驱动（步骤的 card 字段），所以锚点强制居中
            _card.anchorMin = _card.anchorMax = new Vector2(0.5f, 0.5f);
            _card.pivot = new Vector2(0.5f, 0.5f);

            _cardGroup = _card.GetComponent<CanvasGroup>();
            if (_cardGroup == null) _cardGroup = _card.gameObject.AddComponent<CanvasGroup>();

            _title = skin.titleText;
            _body = skin.bodyText;
            _progress = skin.progressText;
            _hint = skin.hintText;
            _skipHint = skin.skipHintText;
            _imageRoot = skin.imageRoot;
            _image = skin.image;
            if (skin.nextButton != null)
            {
                skin.nextButton.onClick.RemoveAllListeners();
                skin.nextButton.onClick.AddListener(() => _buttonPressed = true);
            }
        }

        void BuildProceduralCard()
        {
            var go = new GameObject("Card", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image), typeof(CanvasGroup), typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter));
            _card = (RectTransform)go.transform;
            _card.SetParent(_root, false);
            _card.anchorMin = _card.anchorMax = new Vector2(0.5f, 0.5f);
            _card.pivot = new Vector2(0.5f, 0.5f);
            _card.sizeDelta = new Vector2(cardWidth, 0f);

            var bg = go.GetComponent<Image>();
            bg.sprite = VNProceduralTextures.RoundedRectSprite;
            bg.type = Image.Type.Sliced;
            bg.color = new Color(0.07f, 0.08f, 0.13f, 0.96f);
            bg.raycastTarget = false;

            var layout = go.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(38, 38, 30, 26);
            layout.spacing = 14f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var fitter = go.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _cardGroup = go.GetComponent<CanvasGroup>();
            _cardGroup.blocksRaycasts = false;   // 点哪儿都算「下一步」，别让卡片吃掉点击

            _title = CreateText("Title", _card, 36, new Color(1f, 0.93f, 0.72f, 1f),
                TextAlignmentOptions.Left);
            _title.font = VNFont.DisplayAsset;
            _title.fontStyle = FontStyles.Bold;
            AddPreferredHeight(_title.gameObject, 48f);

            // 配图容器：没图时整块收起，不留空白
            _imageRoot = CreateRect("Image", _card).gameObject;
            _image = _imageRoot.AddComponent<Image>();
            _image.preserveAspect = true;
            _image.raycastTarget = false;
            AddPreferredHeight(_imageRoot, 220f);
            _imageRoot.SetActive(false);

            _body = CreateText("Body", _card, 27, new Color(0.92f, 0.93f, 0.97f, 1f),
                TextAlignmentOptions.TopLeft);
            var bodyElement = _body.gameObject.AddComponent<LayoutElement>();
            bodyElement.flexibleHeight = 1f;

            // 底行：左边进度、右边继续提示
            var footer = CreateRect("Footer", _card);
            var footerLayout = footer.gameObject.AddComponent<HorizontalLayoutGroup>();
            footerLayout.childControlWidth = true;
            footerLayout.childControlHeight = true;
            footerLayout.childForceExpandWidth = true;
            AddPreferredHeight(footer.gameObject, 34f);

            _skipHint = CreateText("Skip", footer, 21, new Color(1f, 1f, 1f, 0.45f),
                TextAlignmentOptions.MidlineLeft);
            _progress = CreateText("Progress", footer, 21, new Color(1f, 1f, 1f, 0.55f),
                TextAlignmentOptions.Center);
            _hint = CreateText("Hint", footer, 22, new Color(1f, 0.88f, 0.55f, 0.9f),
                TextAlignmentOptions.MidlineRight);
        }

        // ------------------------------------------------------------------

        static void AddPreferredHeight(GameObject go, float height)
        {
            var element = go.GetComponent<LayoutElement>();
            if (element == null) element = go.AddComponent<LayoutElement>();
            element.preferredHeight = height;
        }

        static RectTransform EnsureRect(GameObject go)
        {
            var rect = go.GetComponent<RectTransform>();
            if (rect == null) rect = go.AddComponent<RectTransform>();
            return rect;
        }

        static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        static TextMeshProUGUI CreateText(string name, Transform parent, int size, Color color,
            TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.font = VNFont.Asset;
            text.fontSize = size;
            text.color = color;
            text.alignment = align;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            return text;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
