using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace VNEffects.EditorTools
{
    /// <summary>
    /// 素材类 Inspector / 窗口的共用绘制与预览工具。
    ///
    /// 【为什么单独一层】
    /// 缩略图、音频试听、搜索匹配这三件事，PropertyDrawer（VNConfigEntryDrawers）、
    /// VNGameConfig 的分页 Inspector（VNGameConfigEditor）、素材浏览器窗口三边都要用。
    /// 抽这一层出来，三边画出来的行长得一模一样，改一处全都跟着变。
    ///
    /// 【Sprite 缩略图不用 AssetPreview 的理由】
    /// AssetPreview.GetAssetPreview 是**异步**的 —— 首帧返回 null，要靠反复 Repaint 才等得到，
    /// 列表里几十张图一起等会闪一片空白。Sprite 本身就知道自己在哪张 texture 的哪个矩形，
    /// 直接 GUI.DrawTextureWithTexCoords 画那块 UV 即可，**同步、精确、不需要 texture 可读**。
    /// AudioClip 没有这种捷径（波形只能靠 AssetPreview），所以音频那边仍走异步 + 占位兜底。
    /// </summary>
    public static class VNAssetUi
    {
        // ==================================================================
        // 缩略图尺寸（全项目共享一个值，存 EditorPrefs）
        // ==================================================================

        const string ThumbPrefKey = "VNEffects.AssetUi.ThumbSize";
        public const float MinThumb = 20f;
        public const float MaxThumb = 96f;

        static float _thumb = -1f;

        /// <summary>列表行里缩略图的边长（像素）。行高 = 这个值 + Pad。</summary>
        public static float ThumbSize
        {
            get
            {
                if (_thumb < 0f) _thumb = EditorPrefs.GetFloat(ThumbPrefKey, 36f);
                return Mathf.Clamp(_thumb, MinThumb, MaxThumb);
            }
            set
            {
                float v = Mathf.Clamp(value, MinThumb, MaxThumb);
                if (Mathf.Approximately(v, _thumb)) return;
                _thumb = v;
                EditorPrefs.SetFloat(ThumbPrefKey, v);
            }
        }

        /// <summary>行内元素间距</summary>
        public const float Pad = 4f;
        /// <summary>一行的总高度</summary>
        public static float RowHeight { get { return ThumbSize + Pad; } }

        // ==================================================================
        // Sprite 缩略图
        // ==================================================================

        /// <summary>
        /// 在 rect 内画 sprite 的缩略图，保持宽高比居中；sprite 为空时画空槽提示。
        /// </summary>
        public static void DrawSpriteThumb(Rect rect, Sprite sprite, bool drawFrame = true)
        {
            if (drawFrame) DrawSlotFrame(rect);

            if (sprite == null || sprite.texture == null)
            {
                DrawEmptySlot(rect, "d_Sprite Icon");
                return;
            }

            Rect tr;
            try { tr = sprite.textureRect; }
            catch { tr = sprite.rect; }          // 打包进图集且 tight 模式时可能取不到

            var tex = sprite.texture;
            if (tex.width <= 0 || tex.height <= 0 || tr.width <= 0f || tr.height <= 0f)
            {
                DrawEmptySlot(rect, "d_Sprite Icon");
                return;
            }

            var uv = new Rect(tr.x / tex.width, tr.y / tex.height,
                              tr.width / tex.width, tr.height / tex.height);

            GUI.DrawTextureWithTexCoords(FitAspect(rect, tr.width / tr.height), tex, uv, true);
        }

        /// <summary>在 rect 内画任意贴图，保持宽高比。</summary>
        public static void DrawTextureThumb(Rect rect, Texture tex, bool drawFrame = true)
        {
            if (drawFrame) DrawSlotFrame(rect);
            if (tex == null || tex.width <= 0 || tex.height <= 0) { DrawEmptySlot(rect, "d_Texture Icon"); return; }
            GUI.DrawTexture(FitAspect(rect, (float)tex.width / tex.height), tex,
                            ScaleMode.StretchToFill, true);
        }

        /// <summary>画一个 Object 的资产预览缩略图（prefab / SO / 贴图通用，走 AssetPreview 兜底图标）。</summary>
        public static void DrawObjectThumb(Rect rect, UnityEngine.Object obj, bool drawFrame = true)
        {
            if (drawFrame) DrawSlotFrame(rect);
            if (obj == null) { DrawEmptySlot(rect, "d_GameObject Icon"); return; }

            var sp = obj as Sprite;
            if (sp != null) { DrawSpriteThumb(rect, sp, false); return; }

            var tex = AssetPreview.GetAssetPreview(obj);
            if (tex != null) { GUI.DrawTexture(FitAspect(rect, (float)tex.width / tex.height), tex, ScaleMode.StretchToFill, true); return; }

            if (StillWorthWaiting(obj)) RequestDeferredRepaint();

            var icon = AssetPreview.GetMiniThumbnail(obj);
            var old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.8f);
            if (icon != null) GUI.DrawTexture(FitAspect(Shrink(rect, 3f), 1f), icon, ScaleMode.ScaleToFit, true);
            GUI.color = old;
        }

        /// <summary>按给定宽高比在 outer 内取最大居中矩形。</summary>
        public static Rect FitAspect(Rect outer, float aspect)
        {
            if (aspect <= 0f || float.IsNaN(aspect) || float.IsInfinity(aspect)) return outer;
            float w = outer.width, h = outer.height;
            if (w / h > aspect) w = h * aspect; else h = w / aspect;
            return new Rect(outer.x + (outer.width - w) * 0.5f,
                            outer.y + (outer.height - h) * 0.5f, w, h);
        }

        static void DrawSlotFrame(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.22f));
        }

        static void DrawEmptySlot(Rect rect, string iconName)
        {
            var old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.30f);
            var icon = EditorGUIUtility.IconContent(iconName);
            if (icon != null && icon.image != null)
                GUI.DrawTexture(FitAspect(Shrink(rect, 4f), 1f), icon.image, ScaleMode.ScaleToFit, true);
            GUI.color = old;
        }

        public static Rect Shrink(Rect r, float by)
        {
            return new Rect(r.x + by, r.y + by,
                            Mathf.Max(0f, r.width - by * 2f), Mathf.Max(0f, r.height - by * 2f));
        }

        // ==================================================================
        // 音频试听（UnityEditor.AudioUtil 是 internal，只能反射）
        // ==================================================================

        static Type _audioUtil;
        static MethodInfo _play, _stopAll, _isPlaying, _clipPos;
        static bool _audioProbed;
        static AudioClip _playingClip;

        static void ProbeAudioUtil()
        {
            if (_audioProbed) return;
            _audioProbed = true;

            _audioUtil = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
            if (_audioUtil == null) return;
            const BindingFlags F = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

            // Unity 各版本改过名：2020+ 是 PlayPreviewClip，更早叫 PlayClip。
            _play = _audioUtil.GetMethod("PlayPreviewClip", F, null,
                        new[] { typeof(AudioClip), typeof(int), typeof(bool) }, null);
            if (_play == null)
                _play = _audioUtil.GetMethod("PlayClip", F, null,
                            new[] { typeof(AudioClip), typeof(int), typeof(bool) }, null);
            if (_play == null)
                _play = _audioUtil.GetMethod("PlayClip", F, null, new[] { typeof(AudioClip) }, null);

            _stopAll = _audioUtil.GetMethod("StopAllPreviewClips", F, null, Type.EmptyTypes, null);
            if (_stopAll == null)
                _stopAll = _audioUtil.GetMethod("StopAllClips", F, null, Type.EmptyTypes, null);

            _isPlaying = _audioUtil.GetMethod("IsPreviewClipPlaying", F, null, Type.EmptyTypes, null);
            if (_isPlaying == null)
                _isPlaying = _audioUtil.GetMethod("IsClipPlaying", F, null, new[] { typeof(AudioClip) }, null);

            _clipPos = _audioUtil.GetMethod("GetPreviewClipPosition", F, null, Type.EmptyTypes, null);
            if (_clipPos == null)
                _clipPos = _audioUtil.GetMethod("GetClipPosition", F, null, new[] { typeof(AudioClip) }, null);
        }

        /// <summary>本编辑器版本能否试听（探测不到 AudioUtil 时按钮变灰而不是报错）。</summary>
        public static bool CanPreviewAudio { get { ProbeAudioUtil(); return _play != null; } }

        public static void PlayPreview(AudioClip clip)
        {
            ProbeAudioUtil();
            if (clip == null || _play == null) return;
            StopPreview();
            try
            {
                var ps = _play.GetParameters();
                _play.Invoke(null, ps.Length == 3 ? new object[] { clip, 0, false } : new object[] { clip });
                _playingClip = clip;
                StartRepaintPump();
            }
            catch (Exception e) { Debug.LogWarning("[VNAssetUi] 试听失败：" + e.Message); }
        }

        public static void StopPreview()
        {
            ProbeAudioUtil();
            _playingClip = null;
            if (_stopAll == null) return;
            try { _stopAll.Invoke(null, null); } catch { /* 版本差异，静默 */ }
        }

        /// <summary>指定 clip 是否正在试听。</summary>
        public static bool IsPreviewing(AudioClip clip)
        {
            if (clip == null || _playingClip != clip) return false;
            ProbeAudioUtil();
            if (_isPlaying == null) return true;      // 查不到就以本地记录为准
            try
            {
                var ps = _isPlaying.GetParameters();
                object r = _isPlaying.Invoke(null, ps.Length == 1 ? new object[] { clip } : null);
                return r is bool && (bool)r;
            }
            catch { return true; }
        }

        /// <summary>试听进度 0~1（拿不到返回 -1）。</summary>
        public static float PreviewProgress(AudioClip clip)
        {
            if (!IsPreviewing(clip) || clip == null || clip.length <= 0f) return -1f;
            if (_clipPos == null) return -1f;
            try
            {
                var ps = _clipPos.GetParameters();
                object r = _clipPos.Invoke(null, ps.Length == 1 ? new object[] { clip } : null);
                if (r is float) return Mathf.Clamp01((float)r / Mathf.Max(0.001f, clip.length));
            }
            catch { }
            return -1f;
        }

        // 试听期间需要持续重绘（进度条 + 播放按钮状态），节流到 ~15fps 免得空转。
        static bool _pumping;
        static double _nextPump;

        static void StartRepaintPump()
        {
            if (_pumping) return;
            _pumping = true;
            EditorApplication.update += RepaintPump;
        }

        static void RepaintPump()
        {
            if (_playingClip == null || !IsPreviewing(_playingClip))
            {
                _playingClip = null;
                _pumping = false;
                EditorApplication.update -= RepaintPump;
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
                return;
            }
            if (EditorApplication.timeSinceStartup < _nextPump) return;
            _nextPump = EditorApplication.timeSinceStartup + 1.0 / 15.0;
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        // ==================================================================
        // 音频波形（AssetPreview 是异步的，首帧拿不到就画占位并请求重绘）
        // ==================================================================

        public static void DrawWaveform(Rect rect, AudioClip clip)
        {
            DrawSlotFrame(rect);
            if (clip == null) { DrawEmptySlot(rect, "d_AudioClip Icon"); return; }

            var tex = AssetPreview.GetAssetPreview(clip);
            if (tex != null)
            {
                GUI.DrawTexture(rect, tex, ScaleMode.StretchToFill, false);
            }
            else
            {
                DrawEmptySlot(rect, "d_AudioClip Icon");
                if (StillWorthWaiting(clip)) RequestDeferredRepaint();
            }

            float p = PreviewProgress(clip);
            if (p >= 0f)
                EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 2f, rect.width * p, 2f),
                                   new Color(0.4f, 0.85f, 1f, 0.95f));
        }

        // AssetPreview 是异步的，没画出来就得重绘等它。但**不能无限等** ——
        // 有些资产（纯数据 SO、导入失败的音频）永远不会有预览图，那样会一直空转重绘。
        // Unity 6.5 起 IsLoadingAssetPreview(int) / GetInstanceID() 都是 error 级弃用，
        // 所以这里不问 Unity「还在加载吗」，改成自己给每个资产一个 3 秒的等待窗口，到点放弃。
        static readonly Dictionary<UnityEngine.Object, double> _previewDeadline =
            new Dictionary<UnityEngine.Object, double>();

        static bool StillWorthWaiting(UnityEngine.Object o)
        {
            if (o == null) return false;
            double now = EditorApplication.timeSinceStartup;
            double deadline;
            if (!_previewDeadline.TryGetValue(o, out deadline))
            {
                _previewDeadline[o] = now + 3.0;
                return true;
            }
            return now < deadline;
        }

        static double _nextDeferred;
        static void RequestDeferredRepaint()
        {
            if (EditorApplication.timeSinceStartup < _nextDeferred) return;
            _nextDeferred = EditorApplication.timeSinceStartup + 0.2;
            EditorApplication.delayCall += UnityEditorInternal.InternalEditorUtility.RepaintAllViews;
        }

        // ==================================================================
        // 搜索匹配
        // ==================================================================

        /// <summary>
        /// 空格分隔的多关键字，全部命中才算匹配（大小写不敏感、纯子串包含）。
        /// 与剧本编辑器的命令搜索口径一致 —— 不做模糊/拼音，避免"搜到一堆不相干的"。
        /// </summary>
        public static bool Matches(string haystack, string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return true;
            if (string.IsNullOrEmpty(haystack)) return false;
            var h = haystack.ToLowerInvariant();
            var tokens = query.ToLowerInvariant().Split(' ');
            for (int i = 0; i < tokens.Length; i++)
            {
                if (tokens[i].Length == 0) continue;
                if (h.IndexOf(tokens[i], StringComparison.Ordinal) < 0) return false;
            }
            return true;
        }

        /// <summary>把一堆可能为 null 的片段拼成搜索用的干草堆。</summary>
        public static string Haystack(params string[] parts)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < parts.Length; i++)
                if (!string.IsNullOrEmpty(parts[i])) { sb.Append(parts[i]); sb.Append(' '); }
            return sb.ToString();
        }

        /// <summary>Object 的资产文件名（不含扩展名）；非资产或 null 返回空串。</summary>
        public static string AssetName(UnityEngine.Object o)
        {
            if (o == null) return string.Empty;
            string path = AssetDatabase.GetAssetPath(o);
            return string.IsNullOrEmpty(path) ? o.name : System.IO.Path.GetFileNameWithoutExtension(path);
        }

        // ==================================================================
        // 小部件 / 布局辅助
        // ==================================================================

        static GUIStyle _miniLabel, _tinyBtn, _rowLabel;

        public static GUIStyle MiniLabel
        {
            get
            {
                if (_miniLabel == null)
                    _miniLabel = new GUIStyle(EditorStyles.miniLabel)
                    { alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
                return _miniLabel;
            }
        }

        /// <summary>行内次要信息（文件名、尺寸）的淡色小字</summary>
        public static GUIStyle RowLabel
        {
            get
            {
                if (_rowLabel == null)
                {
                    _rowLabel = new GUIStyle(EditorStyles.miniLabel)
                    { alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
                    var c = _rowLabel.normal.textColor; c.a = 0.6f;
                    _rowLabel.normal.textColor = c;
                }
                return _rowLabel;
            }
        }

        public static GUIStyle TinyButton
        {
            get
            {
                if (_tinyBtn == null)
                    _tinyBtn = new GUIStyle(EditorStyles.miniButton)
                    { padding = new RectOffset(1, 1, 1, 1), fontSize = 10 };
                return _tinyBtn;
            }
        }

        /// <summary>把 rect 从左边切掉 w 宽度，返回切下来的那块（rect 被就地缩短）。</summary>
        public static Rect CutLeft(ref Rect rect, float w, float gap = Pad)
        {
            var r = new Rect(rect.x, rect.y, w, rect.height);
            rect.x += w + gap;
            rect.width -= w + gap;
            return r;
        }

        /// <summary>把 rect 从右边切掉 w 宽度。</summary>
        public static Rect CutRight(ref Rect rect, float w, float gap = Pad)
        {
            var r = new Rect(rect.xMax - w, rect.y, w, rect.height);
            rect.width -= w + gap;
            return r;
        }

        /// <summary>在 row 内垂直居中一个标准行高的控件。</summary>
        public static Rect Line(Rect row, float h = 0f)
        {
            if (h <= 0f) h = EditorGUIUtility.singleLineHeight;
            return new Rect(row.x, row.y + (row.height - h) * 0.5f, row.width, h);
        }

        /// <summary>在 row 内取上下两条标准行高的控件（缩略图够高时用来放两行信息）。</summary>
        public static void TwoLines(Rect row, out Rect top, out Rect bottom)
        {
            float h = EditorGUIUtility.singleLineHeight;
            float total = h * 2f + 2f;
            float y = row.y + (row.height - total) * 0.5f;
            top = new Rect(row.x, y, row.width, h);
            bottom = new Rect(row.x, y + h + 2f, row.width, h);
        }

        // ==================================================================
        // 拖拽接收
        // ==================================================================

        /// <summary>
        /// 把一块 rect 变成可接收 Project 拖拽的投放区（缩略图格子直接拖图换素材）。
        /// 返回被投放的资产，没有投放返回 null。
        ///
        /// 拖进来的若是贴图，会自动取它的主 Sprite —— 因为 Project 里拖的往往是 .png 本体
        /// 而不是它下面那个 Sprite 子资产，不转一次的话拖了没反应，最难查。
        /// </summary>
        public static UnityEngine.Object DropTarget(Rect rect, Type wanted)
        {
            var e = Event.current;
            if (!rect.Contains(e.mousePosition)) return null;
            if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform) return null;

            var hit = FirstDraggedOfType(wanted);
            if (hit == null) return null;

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (e.type == EventType.DragUpdated) { e.Use(); return null; }

            DragAndDrop.AcceptDrag();
            e.Use();
            return hit;
        }

        /// <summary>拖拽中的对象里，第一个能当作 wanted 类型使用的（贴图会转成主 Sprite）。</summary>
        public static UnityEngine.Object FirstDraggedOfType(Type wanted)
        {
            var refs = DragAndDrop.objectReferences;
            if (refs == null) return null;
            for (int i = 0; i < refs.Length; i++)
            {
                var converted = Convert(refs[i], wanted);
                if (converted != null) return converted;
            }
            return null;
        }

        /// <summary>把一个资产转成想要的类型：本来就是就直接返回；Texture2D → 它的主 Sprite。</summary>
        public static UnityEngine.Object Convert(UnityEngine.Object o, Type wanted)
        {
            if (o == null) return null;
            if (wanted.IsInstanceOfType(o)) return o;

            if (wanted == typeof(Sprite))
            {
                string path = AssetDatabase.GetAssetPath(o);
                if (string.IsNullOrEmpty(path)) return null;
                var s = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (s != null) return s;
                // Multiple 模式切图：主资产不是 Sprite，取子资产里第一个
                var subs = AssetDatabase.LoadAllAssetsAtPath(path);
                for (int i = 0; i < subs.Length; i++)
                    if (subs[i] is Sprite) return subs[i];
            }
            return null;
        }

        /// <summary>画一圈高亮描边（拖拽悬停 / 选中态用）。</summary>
        public static void DrawOutline(Rect r, Color c, float w = 1f)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, w), c);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - w, r.width, w), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y, w, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax - w, r.y, w, r.height), c);
        }

        /// <summary>选中并高亮 Project 里的资产。</summary>
        public static void Ping(UnityEngine.Object o)
        {
            if (o == null) return;
            EditorGUIUtility.PingObject(o);
            Selection.activeObject = o;
        }

        /// <summary>Sprite 的像素尺寸文本，如 "1920×1080"。</summary>
        public static string SpriteSizeText(Sprite s)
        {
            if (s == null) return string.Empty;
            return Mathf.RoundToInt(s.rect.width) + "×" + Mathf.RoundToInt(s.rect.height);
        }

        /// <summary>AudioClip 的时长文本，如 "1:23"。</summary>
        public static string ClipLengthText(AudioClip c)
        {
            if (c == null) return string.Empty;
            int total = Mathf.RoundToInt(c.length);
            return (total / 60) + ":" + (total % 60).ToString("00");
        }
    }
}
