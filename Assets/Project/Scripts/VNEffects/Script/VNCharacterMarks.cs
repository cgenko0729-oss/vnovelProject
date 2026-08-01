using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>漫符种类（剧本里用英文正名或中文别名引用，见 VNCharacterMarks.Aliases）</summary>
    public enum VNMarkKind
    {
        Sweat,      // 汗滴：尴尬 / 无奈
        Anger,      // 井字怒气
        Exclaim,    // 感叹号：惊
        Question,   // 问号：疑惑
        Heart,      // 爱心：心动
        Note,       // 音符：心情好
        Blush,      // 红晕：害羞（贴脸，需要另配 pos:）
        Bulb,       // 灯泡：想到了
        Ellipsis,   // 省略号：无语 / 沉默
        Dizzy,      // 眩晕星星：受击 / 懵了
        Steam,      // 怒气蒸汽
    }

    /// <summary>
    /// 立绘漫符叠加层：在角色立绘上方的指定位置弹出汗滴 / 井字怒气 / 感叹号等漫画符号。
    ///
    /// 硬约定：
    /// - 符号是角色 GameObject 的子物体 → uGUI 保证画在立绘之上，且跟着立绘一起震动/移动/缩放。
    /// - 与嘴部叠加层一样共用角色的材质实例 → 出场溶解、退场淡出、色调匹配自动带上漫符。
    /// - 位置 = 角色资产的 markAnchor（相对立绘尺寸的归一化偏移，(0,0) 是立绘中心），
    ///   剧本可用 pos:x,y 临时覆盖；不同构图的立绘各自标定，不做任何猜测。
    /// - 一次性符号播完自毁；keep 符号常驻直到 clear / 角色退场，常驻状态进存档。
    /// </summary>
    public class VNCharacterMarks : MonoBehaviour
    {
        // ------------------------------------------------------------------
        // 名称解析（剧本 / 编辑器 / 校验器共用的单一真相）
        // ------------------------------------------------------------------

        /// <summary>英文正名（编辑器下拉与校验器用这一份）</summary>
        public static readonly string[] CanonicalNames =
        {
            "sweat", "anger", "exclaim", "question", "heart",
            "note", "blush", "bulb", "ellipsis", "dizzy", "steam",
        };

        static readonly Dictionary<string, VNMarkKind> Aliases =
            new Dictionary<string, VNMarkKind>(System.StringComparer.OrdinalIgnoreCase)
            {
                { "sweat", VNMarkKind.Sweat }, { "汗", VNMarkKind.Sweat }, { "汗滴", VNMarkKind.Sweat },
                { "anger", VNMarkKind.Anger }, { "怒", VNMarkKind.Anger }, { "怒气", VNMarkKind.Anger },
                { "exclaim", VNMarkKind.Exclaim }, { "惊", VNMarkKind.Exclaim }, { "叹号", VNMarkKind.Exclaim },
                { "question", VNMarkKind.Question }, { "问", VNMarkKind.Question }, { "问号", VNMarkKind.Question },
                { "heart", VNMarkKind.Heart }, { "心", VNMarkKind.Heart }, { "爱心", VNMarkKind.Heart },
                { "note", VNMarkKind.Note }, { "音符", VNMarkKind.Note },
                { "blush", VNMarkKind.Blush }, { "红晕", VNMarkKind.Blush }, { "脸红", VNMarkKind.Blush },
                { "bulb", VNMarkKind.Bulb }, { "灯泡", VNMarkKind.Bulb }, { "灵光", VNMarkKind.Bulb },
                { "ellipsis", VNMarkKind.Ellipsis }, { "省略", VNMarkKind.Ellipsis }, { "省略号", VNMarkKind.Ellipsis },
                { "dizzy", VNMarkKind.Dizzy }, { "眩晕", VNMarkKind.Dizzy }, { "星星", VNMarkKind.Dizzy },
                { "steam", VNMarkKind.Steam }, { "蒸汽", VNMarkKind.Steam },
            };

        /// <summary>剧本写法 → 种类；英文正名与中文别名都接受</summary>
        public static bool TryParse(string name, out VNMarkKind kind)
        {
            kind = VNMarkKind.Sweat;
            return !string.IsNullOrEmpty(name) && Aliases.TryGetValue(name.Trim(), out kind);
        }

        /// <summary>种类 → 英文正名（存档与日志用，永不本地化）</summary>
        public static string NameOf(VNMarkKind kind) => CanonicalNames[(int)kind];

        // ------------------------------------------------------------------

        class Live
        {
            public GameObject go;
            public Image image;
            public RectTransform rect;
            public Sequence seq;
            public Tween idle;
            public bool keep;
        }

        readonly Dictionary<VNMarkKind, Live> _live = new Dictionary<VNMarkKind, Live>();

        RectTransform _host;
        VNCharacterDef _def;
        Material _shared;

        /// <summary>立绘高度的多少倍作为漫符基准边长（再乘角色资产的 markScale 与剧本 size:）</summary>
        const float BaseSizeRatio = 0.15f;

        public void Initialize(RectTransform host, VNCharacterDef definition, Material sharedMaterial)
        {
            _host = host;
            _def = definition;
            _shared = sharedMaterial;
        }

        /// <summary>当前常驻中的漫符（存档用，按枚举顺序稳定输出）</summary>
        public IEnumerable<VNMarkKind> KeepKinds
        {
            get
            {
                foreach (VNMarkKind kind in System.Enum.GetValues(typeof(VNMarkKind)))
                    if (_live.TryGetValue(kind, out var live) && live != null && live.keep)
                        yield return kind;
            }
        }

        /// <summary>常驻漫符序列化成存档字符串（英文正名，逗号分隔；无常驻 = null）</summary>
        public string SerializeKeep()
        {
            var names = new List<string>();
            foreach (var kind in KeepKinds) names.Add(NameOf(kind));
            return names.Count > 0 ? string.Join(",", names) : null;
        }

        /// <summary>
        /// 弹出一个漫符。返回的 Sequence 只包含"弹出"这一段（约 0.28 秒），
        /// 停留与消失不在返回值里 —— 这样剧本默认同步也只等一下弹出，不会卡住对白节奏。
        /// </summary>
        public Sequence Show(VNMarkKind kind, bool keep, Vector2? posOverride,
            float sizeMultiplier, float hold)
        {
            var live = Create(kind, posOverride, sizeMultiplier);
            if (live == null) return null;
            live.keep = keep;

            Vector2 basePos = live.rect.anchoredPosition;
            live.rect.anchoredPosition = basePos + new Vector2(0f, -14f);
            live.rect.localScale = Vector3.one * 0.35f;
            SetAlpha(live, 0f);

            var seq = DOTween.Sequence()
                .Append(live.rect.DOScale(1.15f, 0.18f).SetEase(Ease.OutBack))
                .Join(live.rect.DOAnchorPos(basePos, 0.22f).SetEase(Ease.OutBack))
                .Join(live.image.DOFade(1f, 0.12f))
                .Append(live.rect.DOScale(1f, 0.1f).SetEase(Ease.OutQuad));
            seq.SetLink(live.go);
            live.seq = seq;

            if (keep)
            {
                // 常驻符号轻轻上下浮动，避免"贴纸钉死在立绘上"的呆板感
                seq.OnComplete(() =>
                {
                    if (live.rect == null) return;
                    live.idle = live.rect
                        .DOAnchorPosY(basePos.y + 4f, 1.1f)
                        .SetEase(Ease.InOutSine)
                        .SetLoops(-1, LoopType.Yoyo)
                        .SetLink(live.go);
                });
            }
            else
            {
                float wait = Mathf.Max(0f, hold);
                DOVirtual.DelayedCall(0.28f + wait, () => FadeOut(kind, 0.25f))
                    .SetLink(live.go);
            }

            return seq;
        }

        /// <summary>读档 / 调试重建：直接把常驻漫符置为终态，不播弹出动画</summary>
        public void ShowInstant(VNMarkKind kind)
        {
            var live = Create(kind, null, 1f);
            if (live == null) return;
            live.keep = true;
            SetAlpha(live, 1f);
            live.rect.localScale = Vector3.one;
            Vector2 basePos = live.rect.anchoredPosition;
            live.idle = live.rect.DOAnchorPosY(basePos.y + 4f, 1.1f)
                .SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo).SetLink(live.go);
        }

        /// <summary>淡出并销毁指定漫符</summary>
        public void FadeOut(VNMarkKind kind, float duration = 0.25f)
        {
            if (!_live.TryGetValue(kind, out var live) || live == null) return;
            _live.Remove(kind);
            KillTweens(live);
            if (live.go == null) return;

            var seq = DOTween.Sequence()
                .Append(live.image.DOFade(0f, duration))
                .Join(live.rect.DOScale(0.8f, duration).SetEase(Ease.InQuad))
                .Join(live.rect.DOAnchorPosY(live.rect.anchoredPosition.y + 10f, duration));
            seq.OnComplete(() => { if (live.go != null) Destroy(live.go); });
            seq.SetLink(live.go);
        }

        /// <summary>清空全部漫符（clear 命令 / 角色退场用）</summary>
        public void ClearAll(float duration = 0.2f)
        {
            var kinds = new List<VNMarkKind>(_live.Keys);
            foreach (var kind in kinds) FadeOut(kind, duration);
        }

        /// <summary>立即销毁全部漫符，不播淡出（读档清台用）</summary>
        public void ClearImmediate()
        {
            foreach (var live in _live.Values)
            {
                KillTweens(live);
                if (live.go != null) Destroy(live.go);
            }
            _live.Clear();
        }

        // ------------------------------------------------------------------

        Live Create(VNMarkKind kind, Vector2? posOverride, float sizeMultiplier)
        {
            if (_host == null)
            {
                Debug.LogWarning("[VNScript] 漫符组件未初始化（VNCharacterMarks.Initialize 未调用）");
                return null;
            }

            // 同种符号重复触发 = 重播：先干净地拆掉旧的，避免两份叠在一起闪烁
            if (_live.TryGetValue(kind, out var old) && old != null)
            {
                KillTweens(old);
                if (old.go != null) Destroy(old.go);
                _live.Remove(kind);
            }

            var sprite = ResolveSprite(kind);
            if (sprite == null) return null;

            var go = new GameObject($"Mark_{NameOf(kind)}",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(_host, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);

            Vector2 hostSize = _host.rect.size;
            Vector2 anchor = posOverride ?? (_def != null ? _def.markAnchor : new Vector2(0.2f, 0.36f));
            rect.anchoredPosition = new Vector2(anchor.x * hostSize.x, anchor.y * hostSize.y);

            float scale = _def != null ? Mathf.Max(0.05f, _def.markScale) : 1f;
            float size = Mathf.Max(24f, hostSize.y * BaseSizeRatio * scale *
                                        Mathf.Max(0.05f, sizeMultiplier));
            rect.sizeDelta = new Vector2(size, size);

            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            if (_shared != null) image.material = _shared;

            var live = new Live { go = go, image = image, rect = rect };
            _live[kind] = live;
            return live;
        }

        /// <summary>角色资产里配了自定义图就用自定义图，否则回退程序化生成</summary>
        Sprite ResolveSprite(VNMarkKind kind)
        {
            if (_def != null && _def.markSprites != null)
            {
                foreach (var entry in _def.markSprites)
                {
                    if (entry == null || entry.sprite == null) continue;
                    if (TryParse(entry.name, out var entryKind) && entryKind == kind)
                        return entry.sprite;
                }
            }
            return VNProceduralTextures.MarkSprite(kind);
        }

        static void SetAlpha(Live live, float alpha)
        {
            if (live.image == null) return;
            var c = live.image.color;
            c.a = alpha;
            live.image.color = c;
        }

        static void KillTweens(Live live)
        {
            if (live.seq != null && live.seq.IsActive()) live.seq.Kill();
            if (live.idle != null && live.idle.IsActive()) live.idle.Kill();
            live.seq = null;
            live.idle = null;
        }

        void OnDestroy()
        {
            foreach (var live in _live.Values) KillTweens(live);
            _live.Clear();
        }
    }
}
