using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace VNEffects.EditorTools
{
    /// <summary>
    /// VN 素材浏览器：把 VNGameConfig 里的素材库当图库来逛。
    ///
    /// 【为什么要有这个窗口】
    /// 本项目的素材文件名是 AI 生成时的原始 prompt 或纯数字
    /// （"masterpiece, very aesthetic, highly detailed, 1girl... s-1095962266.png"、"1.png"），
    /// **完全不表意** —— 光看名字不可能认出哪张是哪张。
    /// 所以这里以**大缩略图为主、id 为标签**，文件名退居次要信息。
    /// 音频同理：波形 + 一键试听，不靠名字猜。
    ///
    /// 【与 Inspector 的分工】
    /// Inspector（VNGameConfigEditor）负责"改配置"，一次看一行；
    /// 本窗口负责"找素材"，一次看几十个。两边共用 VNAssetUi 的绘制与预览。
    ///
    /// 【数据安全】
    /// 全程走 VNGameConfig 的 SerializedObject，改动自动进 Undo、自动标脏，
    /// 不直接写字段、不移动/重命名任何素材文件。
    /// </summary>
    public class VNAssetBrowserWindow : EditorWindow
    {
        [MenuItem("Tools/VN Effects/Asset Browser", priority = 203)]
        public static void Open()
        {
            var w = GetWindow<VNAssetBrowserWindow>("VN 素材");
            w.minSize = new Vector2(560f, 400f);
            w.Show();
        }

        // ==================================================================
        // 类别
        // ==================================================================

        enum Kind { Image, Audio, Object }

        class Cat
        {
            public readonly string title;
            public readonly string field;
            public readonly Kind kind;
            public Cat(string title, string field, Kind kind)
            { this.title = title; this.field = field; this.kind = kind; }
        }

        static readonly Cat[] Cats =
        {
            new Cat("背景",      "backgrounds",   Kind.Image),
            new Cat("CG",        "cgLibrary",     Kind.Image),
            new Cat("BGM",       "bgmLibrary",    Kind.Audio),
            new Cat("SE",        "seLibrary",     Kind.Audio),
            new Cat("语音",      "voiceLibrary",  Kind.Audio),
            new Cat("角色",      "characters",    Kind.Object),
            new Cat("对话框皮肤", "dialogueSkins", Kind.Object),
            new Cat("选项皮肤",   "choiceSkins",   Kind.Object),
            new Cat("天气",      "weatherDefs",   Kind.Object),
        };

        // ==================================================================
        // 状态
        // ==================================================================

        const string GridPrefKey = "VNEffects.AssetBrowser.GridSize";
        const string CatPrefKey = "VNEffects.AssetBrowser.Cat";

        const float LeftW = 132f;
        const float TopH = 21f;
        const float DetailH = 132f;
        const float Gap = 8f;
        const float LabelH = 15f;

        VNGameConfig _config;
        SerializedObject _so;

        int _cat;
        string _query = string.Empty;
        int _selected = -1;                 // 数组真实索引
        bool _onlyUnregistered;

        Vector2 _scrollGrid;
        readonly List<int> _visible = new List<int>();

        static float GridSize
        {
            get { return Mathf.Clamp(EditorPrefs.GetFloat(GridPrefKey, 108f), 48f, 240f); }
            set { EditorPrefs.SetFloat(GridPrefKey, Mathf.Clamp(value, 48f, 240f)); }
        }

        void OnEnable()
        {
            _cat = Mathf.Clamp(EditorPrefs.GetInt(CatPrefKey, 0), 0, Cats.Length - 1);
            Acquire();
        }

        void OnDisable()
        {
            VNAssetUi.StopPreview();        // 关窗口还在放歌就见鬼了
        }

        void Acquire()
        {
            if (_config == null)
                _config = AssetDatabase.LoadAssetAtPath<VNGameConfig>(VNGameConfig.AssetPath);
            if (_config == null)
                _config = Resources.Load<VNGameConfig>(VNGameConfig.ResourcesName);

            if (_config != null && (_so == null || _so.targetObject != _config))
                _so = new SerializedObject(_config);
        }

        // ==================================================================
        // 主绘制
        // ==================================================================

        void OnGUI()
        {
            Acquire();
            if (_config == null) { DrawNoConfig(); return; }
            _so.Update();

            var top = new Rect(0f, 0f, position.width, TopH);
            float bodyH = Mathf.Max(60f, position.height - TopH - DetailH);
            var left = new Rect(0f, TopH, LeftW, bodyH);
            var main = new Rect(LeftW, TopH, position.width - LeftW, bodyH);
            var detail = new Rect(0f, TopH + bodyH, position.width, DetailH);

            DrawTopBar(top);
            DrawCategories(left);

            var arr = CurrentArray();
            RebuildVisible(arr);

            if (Cats[_cat].kind == Kind.Audio) DrawAudioList(main, arr);
            else DrawGrid(main, arr);

            DrawDetail(detail, arr);

            _so.ApplyModifiedProperties();
        }

        void DrawNoConfig()
        {
            var r = new Rect(20f, 20f, position.width - 40f, 80f);
            EditorGUI.HelpBox(r, "还没有 VNGameConfig 资产。\n" +
                                 "素材库都存在它里面，先建一个。", MessageType.Info);
            if (GUI.Button(new Rect(20f, 108f, 200f, 24f), "创建 / 定位 VNGameConfig"))
            {
                VNGameConfigTools.CreateOrSelect();
                _config = null;
                Acquire();
            }
        }

        void DrawTopBar(Rect r)
        {
            GUI.Box(r, GUIContent.none, EditorStyles.toolbar);
            var row = VNAssetUi.Shrink(r, 1f);

            // 搜索
            var searchRect = VNAssetUi.CutLeft(ref row, Mathf.Min(240f, row.width * 0.4f));
            string q = EditorGUI.TextField(searchRect, _query, EditorStyles.toolbarSearchField);
            if (q != _query) { _query = q; _scrollGrid.y = 0f; }

            // 缩略图尺寸
            var sizeLabel = VNAssetUi.CutLeft(ref row, 34f, 2f);
            EditorGUI.LabelField(sizeLabel, "大小", EditorStyles.miniLabel);
            var slider = VNAssetUi.CutLeft(ref row, 90f);
            GridSize = GUI.HorizontalSlider(VNAssetUi.Line(slider, 12f), GridSize, 48f, 240f);

            // 只看未登记
            if (Cats[_cat].kind != Kind.Object)
            {
                var chk = VNAssetUi.CutLeft(ref row, 96f, 6f);
                _onlyUnregistered = GUI.Toggle(VNAssetUi.Line(chk), _onlyUnregistered,
                    new GUIContent("只看未登记",
                        "扫已登记素材所在的那些目录，列出还没登进库的文件。\n" +
                        "目录是从现有条目反推的，所以库全空时无从判断。"),
                    EditorStyles.toolbarButton);
            }

            // 右侧
            var ping = VNAssetUi.CutRight(ref row, 76f, 2f);
            if (GUI.Button(ping, new GUIContent("配置资产", "在 Inspector 里打开 VNGameConfig"),
                           EditorStyles.toolbarButton))
                VNAssetUi.Ping(_config);

            var rescan = VNAssetUi.CutRight(ref row, 84f, 2f);
            if (GUI.Button(rescan, new GUIContent("扫描目录", "按目录补登定义资产"),
                           EditorStyles.toolbarButton))
            {
                VNGameConfigTools.RescanAssetFolders();
                _so = null; Acquire();
                GUIUtility.ExitGUI();
            }
        }

        void DrawCategories(Rect r)
        {
            EditorGUI.DrawRect(r, new Color(0f, 0f, 0f, 0.10f));
            float y = r.y + 4f;
            for (int i = 0; i < Cats.Length; i++)
            {
                var arr = ArrayOf(Cats[i].field);
                int n = arr != null ? arr.arraySize : 0;

                var row = new Rect(r.x + 3f, y, r.width - 6f, 21f);
                bool on = i == _cat;
                if (on) EditorGUI.DrawRect(row, new Color(0.35f, 0.6f, 0.95f, 0.35f));

                if (GUI.Button(row, GUIContent.none, GUIStyle.none))
                {
                    _cat = i; _selected = -1; _scrollGrid = Vector2.zero;
                    EditorPrefs.SetInt(CatPrefKey, i);
                    GUI.FocusControl(null);
                }
                var label = new Rect(row.x + 6f, row.y, row.width - 40f, row.height);
                EditorGUI.LabelField(label, Cats[i].title,
                                     on ? EditorStyles.boldLabel : EditorStyles.label);
                EditorGUI.LabelField(new Rect(row.xMax - 36f, row.y, 32f, row.height),
                                     n.ToString(), VNAssetUi.RowLabel);
                y += 22f;
            }
        }

        // ==================================================================
        // 过滤
        // ==================================================================

        SerializedProperty CurrentArray() { return ArrayOf(Cats[_cat].field); }

        SerializedProperty ArrayOf(string field)
        {
            return _so == null ? null : _so.FindProperty(field);
        }

        void RebuildVisible(SerializedProperty arr)
        {
            _visible.Clear();
            if (arr == null) return;
            for (int i = 0; i < arr.arraySize; i++)
            {
                var el = arr.GetArrayElementAtIndex(i);
                if (!VNAssetUi.Matches(Haystack(el), _query)) continue;
                _visible.Add(i);
            }
        }

        static SerializedProperty IdProp(SerializedProperty el)
        {
            if (el == null || el.propertyType == SerializedPropertyType.ObjectReference) return null;
            var p = el.FindPropertyRelative("id");
            return (p != null && p.propertyType == SerializedPropertyType.String) ? p : null;
        }

        static SerializedProperty AssetProp(SerializedProperty el)
        {
            if (el == null) return null;
            if (el.propertyType == SerializedPropertyType.ObjectReference) return el;
            var it = el.Copy();
            var end = el.GetEndProperty();
            bool enter = true;
            while (it.NextVisible(enter) && !SerializedProperty.EqualContents(it, end))
            {
                enter = false;
                if (it.propertyType == SerializedPropertyType.ObjectReference) return it.Copy();
            }
            return null;
        }

        static Object AssetOf(SerializedProperty el)
        {
            var p = AssetProp(el);
            return p != null ? p.objectReferenceValue : null;
        }

        /// <summary>格子上显示的标签：优先 id，没有 id 就用资产名。</summary>
        static string LabelOf(SerializedProperty el)
        {
            var id = IdProp(el);
            if (id != null && !string.IsNullOrEmpty(id.stringValue)) return id.stringValue;
            var o = AssetOf(el);
            return o != null ? o.name : "(空)";
        }

        static string Haystack(SerializedProperty el)
        {
            var id = IdProp(el);
            var o = AssetOf(el);
            return VNAssetUi.Haystack(id != null ? id.stringValue : null,
                                      o != null ? o.name : null,
                                      VNAssetUi.AssetName(o));
        }

        // ==================================================================
        // 网格（图片 / 对象）
        // ==================================================================

        void DrawGrid(Rect r, SerializedProperty arr)
        {
            if (arr == null) { EditorGUI.LabelField(r, "（找不到这个库）"); return; }

            if (_onlyUnregistered && Cats[_cat].kind == Kind.Image) { DrawUnregistered(r, arr); return; }

            float cell = GridSize;
            float stepX = cell + Gap;
            float stepY = cell + LabelH + Gap;

            int cols = Mathf.Max(1, Mathf.FloorToInt((r.width - Gap - 14f) / stepX));
            int rows = Mathf.CeilToInt(_visible.Count / (float)cols);
            float contentH = rows * stepY + Gap + 40f;        // 末尾留出拖入提示的空间

            var content = new Rect(0f, 0f, r.width - 16f, Mathf.Max(contentH, r.height));
            _scrollGrid = GUI.BeginScrollView(r, _scrollGrid, content);

            // 虚拟化：只画滚动窗口内的那几行
            int firstRow = Mathf.Max(0, Mathf.FloorToInt((_scrollGrid.y - Gap) / stepY));
            int lastRow = Mathf.Min(rows - 1, Mathf.CeilToInt((_scrollGrid.y + r.height) / stepY));

            for (int row = firstRow; row <= lastRow; row++)
            {
                for (int c = 0; c < cols; c++)
                {
                    int v = row * cols + c;
                    if (v >= _visible.Count) break;
                    int i = _visible[v];
                    var cellRect = new Rect(Gap + c * stepX, Gap + row * stepY, cell, cell + LabelH);
                    DrawCell(cellRect, arr, i, cell);
                }
            }

            if (_visible.Count == 0)
                EditorGUI.LabelField(new Rect(Gap, Gap, content.width - Gap * 2f, 20f),
                    string.IsNullOrEmpty(_query) ? "这个库还是空的 —— 把素材拖进来即可。" : "没有匹配项",
                    VNAssetUi.RowLabel);

            // 末尾的批量拖入提示条
            var hint = new Rect(Gap, Mathf.Max(Gap + rows * stepY, 24f), content.width - Gap * 2f, 28f);
            DrawDropHint(hint, arr);

            GUI.EndScrollView();

            HandleDropOnto(r, arr);
        }

        void DrawCell(Rect cell, SerializedProperty arr, int index, float size)
        {
            var el = arr.GetArrayElementAtIndex(index);
            var thumb = new Rect(cell.x, cell.y, size, size);
            var label = new Rect(cell.x, cell.yMax - LabelH, size, LabelH);

            bool selected = _selected == index;
            if (selected)
                EditorGUI.DrawRect(VNAssetUi.Shrink(thumb, -3f), new Color(0.35f, 0.6f, 0.95f, 0.45f));

            var asset = AssetOf(el);
            var sprite = asset as Sprite;
            if (sprite != null) VNAssetUi.DrawSpriteThumb(thumb, sprite);
            else VNAssetUi.DrawObjectThumb(thumb, asset);

            if (asset == null)
                VNAssetUi.DrawOutline(thumb, new Color(1f, 0.6f, 0.2f, 0.8f), 1f);   // 空槽标橙

            var style = new GUIStyle(EditorStyles.miniLabel)
            { alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Clip };
            if (selected) style.fontStyle = FontStyle.Bold;
            GUI.Label(label, new GUIContent(LabelOf(el), Tooltip(el)), style);

            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && thumb.Contains(e.mousePosition))
            {
                _selected = index;
                GUI.FocusControl(null);
                if (e.clickCount >= 2 && asset != null) VNAssetUi.Ping(asset);
                e.Use();
                Repaint();
            }
            else if (e.type == EventType.ContextClick && thumb.Contains(e.mousePosition))
            {
                _selected = index;
                ShowRowMenu(arr, index);
                e.Use();
            }
        }

        static string Tooltip(SerializedProperty el)
        {
            var o = AssetOf(el);
            var sb = new System.Text.StringBuilder();
            var id = IdProp(el);
            if (id != null) sb.AppendLine("id：" + (string.IsNullOrEmpty(id.stringValue) ? "(未填)" : id.stringValue));
            if (o != null)
            {
                sb.AppendLine("文件：" + AssetDatabase.GetAssetPath(o));
                var sp = o as Sprite;
                if (sp != null) sb.AppendLine("尺寸：" + VNAssetUi.SpriteSizeText(sp));
            }
            else sb.AppendLine("（未指定素材）");
            return sb.ToString().TrimEnd();
        }

        // ==================================================================
        // 音频列表
        // ==================================================================

        void DrawAudioList(Rect r, SerializedProperty arr)
        {
            if (arr == null) { EditorGUI.LabelField(r, "（找不到这个库）"); return; }

            if (_onlyUnregistered) { DrawUnregistered(r, arr); return; }

            // 行高要够画两行（id + 文件名）：TwoLines 需要 2×18+2 = 38，
            // 而 work = rowH − 2（行距）− 4（Shrink），所以 rowH 至少 44。
            float rowH = Mathf.Max(46f, GridSize * 0.42f);
            float contentH = _visible.Count * rowH + 40f;
            var content = new Rect(0f, 0f, r.width - 16f, Mathf.Max(contentH, r.height));
            _scrollGrid = GUI.BeginScrollView(r, _scrollGrid, content);

            int first = Mathf.Max(0, Mathf.FloorToInt(_scrollGrid.y / rowH));
            int last = Mathf.Min(_visible.Count - 1, Mathf.CeilToInt((_scrollGrid.y + r.height) / rowH));

            for (int v = first; v <= last; v++)
            {
                int i = _visible[v];
                var row = new Rect(2f, v * rowH, content.width - 4f, rowH - 2f);
                DrawAudioRow(row, arr, i);
            }

            if (_visible.Count == 0)
                EditorGUI.LabelField(new Rect(6f, 6f, content.width - 12f, 20f),
                    string.IsNullOrEmpty(_query) ? "这个库还是空的 —— 把音频拖进来即可。" : "没有匹配项",
                    VNAssetUi.RowLabel);

            var hint = new Rect(4f, Mathf.Max(_visible.Count * rowH + 6f, 24f), content.width - 8f, 28f);
            DrawDropHint(hint, arr);

            GUI.EndScrollView();
            HandleDropOnto(r, arr);
        }

        void DrawAudioRow(Rect row, SerializedProperty arr, int index)
        {
            var el = arr.GetArrayElementAtIndex(index);
            var clip = AssetOf(el) as AudioClip;

            if (_selected == index) EditorGUI.DrawRect(row, new Color(0.35f, 0.6f, 0.95f, 0.30f));
            else if (index % 2 == 1) EditorGUI.DrawRect(row, new Color(1f, 1f, 1f, 0.025f));

            var work = VNAssetUi.Shrink(row, 2f);

            // ▶
            var btn = VNAssetUi.CutLeft(ref work, 22f, 3f);
            bool playing = VNAssetUi.IsPreviewing(clip);
            using (new EditorGUI.DisabledScope(clip == null || !VNAssetUi.CanPreviewAudio))
                if (GUI.Button(VNAssetUi.Line(btn, 18f), playing ? "■" : "▶", VNAssetUi.TinyButton))
                {
                    if (playing) VNAssetUi.StopPreview(); else VNAssetUi.PlayPreview(clip);
                    _selected = index;
                }

            // 波形
            var wave = VNAssetUi.CutLeft(ref work, Mathf.Min(150f, work.width * 0.3f));
            VNAssetUi.DrawWaveform(wave, clip);

            // 时长
            var len = VNAssetUi.CutRight(ref work, 46f);
            EditorGUI.LabelField(VNAssetUi.Line(len), VNAssetUi.ClipLengthText(clip), VNAssetUi.RowLabel);

            // 音量
            var volProp = el.propertyType == SerializedPropertyType.ObjectReference
                        ? null : el.FindPropertyRelative("volume");
            if (volProp != null)
            {
                var vr = VNAssetUi.CutRight(ref work, 104f);
                EditorGUI.PropertyField(VNAssetUi.Line(vr), volProp, GUIContent.none);
            }

            // id（可直接改）+ 文件名
            Rect top, bottom;
            VNAssetUi.TwoLines(work, out top, out bottom);
            var idProp = IdProp(el);
            if (idProp != null && work.height >= 38f)
            {
                EditorGUI.PropertyField(top, idProp, GUIContent.none);
                EditorGUI.LabelField(bottom, VNAssetUi.AssetName(clip), VNAssetUi.RowLabel);
            }
            else if (idProp != null)
            {
                EditorGUI.PropertyField(VNAssetUi.Line(work), idProp, GUIContent.none);
            }
            else EditorGUI.LabelField(VNAssetUi.Line(work), LabelOf(el));

            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && row.Contains(e.mousePosition))
            { _selected = index; Repaint(); }
            else if (e.type == EventType.ContextClick && row.Contains(e.mousePosition))
            { _selected = index; ShowRowMenu(arr, index); e.Use(); }
        }

        // ==================================================================
        // 详情栏
        // ==================================================================

        void DrawDetail(Rect r, SerializedProperty arr)
        {
            EditorGUI.DrawRect(r, new Color(0f, 0f, 0f, 0.14f));
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1f), new Color(0f, 0f, 0f, 0.35f));

            if (arr == null || _selected < 0 || _selected >= arr.arraySize)
            {
                EditorGUI.LabelField(VNAssetUi.Shrink(r, 8f),
                    "选中一项查看详情（双击 = 在 Project 中定位，右键 = 更多操作）", VNAssetUi.RowLabel);
                return;
            }

            var el = arr.GetArrayElementAtIndex(_selected);
            var asset = AssetOf(el);
            var work = VNAssetUi.Shrink(r, 8f);

            // 大预览
            float ph = work.height;
            var preview = VNAssetUi.CutLeft(ref work, ph * 1.4f, 10f);
            if (Cats[_cat].kind == Kind.Audio) VNAssetUi.DrawWaveform(preview, asset as AudioClip);
            else if (asset is Sprite) VNAssetUi.DrawSpriteThumb(preview, asset as Sprite);
            else VNAssetUi.DrawObjectThumb(preview, asset);

            float lh = EditorGUIUtility.singleLineHeight;
            float y = work.y;

            // id
            var idProp = IdProp(el);
            if (idProp != null)
            {
                var lab = new Rect(work.x, y, 56f, lh);
                EditorGUI.LabelField(lab, "id");
                EditorGUI.PropertyField(new Rect(work.x + 58f, y, work.width - 58f, lh),
                                        idProp, GUIContent.none);
                y += lh + 3f;
            }

            // 资产槽
            var assetProp = AssetProp(el);
            if (assetProp != null)
            {
                EditorGUI.LabelField(new Rect(work.x, y, 56f, lh), "素材");
                EditorGUI.PropertyField(new Rect(work.x + 58f, y, work.width - 58f, lh),
                                        assetProp, GUIContent.none);
                y += lh + 3f;
            }

            // 附加字段（CG 的差分组 / 音频的音量）
            var extra = el.propertyType == SerializedPropertyType.ObjectReference
                      ? null
                      : (el.FindPropertyRelative("group") ?? el.FindPropertyRelative("volume"));
            if (extra != null)
            {
                EditorGUI.LabelField(new Rect(work.x, y, 56f, lh),
                                     extra.name == "group" ? "差分组" : "音量");
                EditorGUI.PropertyField(new Rect(work.x + 58f, y, Mathf.Min(220f, work.width - 58f), lh),
                                        extra, GUIContent.none);
                y += lh + 3f;
            }

            // 路径
            if (asset != null)
            {
                var pathRect = new Rect(work.x, y, work.width, lh);
                EditorGUI.LabelField(pathRect, AssetDatabase.GetAssetPath(asset), VNAssetUi.RowLabel);
                y += lh + 2f;
            }

            // 操作按钮
            var bar = new Rect(work.x, work.yMax - 20f, work.width, 20f);
            var b1 = VNAssetUi.CutLeft(ref bar, 100f);
            using (new EditorGUI.DisabledScope(asset == null))
                if (GUI.Button(b1, "在 Project 中定位", EditorStyles.miniButton)) VNAssetUi.Ping(asset);

            if (Cats[_cat].kind == Kind.Audio)
            {
                var b2 = VNAssetUi.CutLeft(ref bar, 64f);
                var clip = asset as AudioClip;
                bool playing = VNAssetUi.IsPreviewing(clip);
                using (new EditorGUI.DisabledScope(clip == null || !VNAssetUi.CanPreviewAudio))
                    if (GUI.Button(b2, playing ? "停止" : "试听", EditorStyles.miniButton))
                    {
                        if (playing) VNAssetUi.StopPreview(); else VNAssetUi.PlayPreview(clip);
                    }
            }

            var bDel = VNAssetUi.CutRight(ref bar, 88f);
            if (GUI.Button(bDel, "从库中移除", EditorStyles.miniButton))
            {
                RemoveAt(arr, _selected);
                _selected = -1;
                _so.ApplyModifiedProperties();
                GUIUtility.ExitGUI();
            }
        }

        // ==================================================================
        // 右键菜单
        // ==================================================================

        void ShowRowMenu(SerializedProperty arr, int index)
        {
            var el = arr.GetArrayElementAtIndex(index);
            var asset = AssetOf(el);
            var m = new GenericMenu();

            if (asset != null)
            {
                m.AddItem(new GUIContent("在 Project 中定位"), false, () => VNAssetUi.Ping(asset));
                m.AddItem(new GUIContent("用文件名填 id"), false, () =>
                {
                    var id = IdProp(arr.GetArrayElementAtIndex(index));
                    if (id != null)
                    {
                        id.stringValue = VNAssetUi.AssetName(asset);
                        _so.ApplyModifiedProperties();
                    }
                });
            }
            else m.AddDisabledItem(new GUIContent("在 Project 中定位"));

            m.AddSeparator(string.Empty);
            m.AddItem(new GUIContent("上移"), false, () =>
            {
                if (index > 0) { arr.MoveArrayElement(index, index - 1); _so.ApplyModifiedProperties(); _selected = index - 1; }
            });
            m.AddItem(new GUIContent("下移"), false, () =>
            {
                if (index < arr.arraySize - 1) { arr.MoveArrayElement(index, index + 1); _so.ApplyModifiedProperties(); _selected = index + 1; }
            });
            m.AddSeparator(string.Empty);
            m.AddItem(new GUIContent("从库中移除"), false, () =>
            {
                RemoveAt(arr, index); _selected = -1; _so.ApplyModifiedProperties(); Repaint();
            });
            m.ShowAsContext();
        }

        static void RemoveAt(SerializedProperty arr, int index)
        {
            if (index < 0 || index >= arr.arraySize) return;
            int before = arr.arraySize;
            arr.DeleteArrayElementAtIndex(index);
            if (arr.arraySize == before) arr.DeleteArrayElementAtIndex(index);
        }

        // ==================================================================
        // 拖入登记
        // ==================================================================

        System.Type WantedType()
        {
            switch (Cats[_cat].kind)
            {
                case Kind.Image: return typeof(Sprite);
                case Kind.Audio: return typeof(AudioClip);
                default: return typeof(Object);
            }
        }

        void DrawDropHint(Rect r, SerializedProperty arr)
        {
            if (r.height <= 0f) return;
            EditorGUI.DrawRect(r, new Color(1f, 1f, 1f, 0.035f));
            var style = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter };
            var old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.5f);
            GUI.Label(r, "把素材从 Project 拖到这里批量登记（id 预填文件名）", style);
            GUI.color = old;
        }

        /// <summary>整个主区都能接投放，不用非得瞄准底部那条提示。</summary>
        void HandleDropOnto(Rect area, SerializedProperty arr)
        {
            if (arr == null) return;
            var e = Event.current;
            if (!area.Contains(e.mousePosition)) return;
            if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform) return;

            var wanted = WantedType();
            if (!AnyDraggable(wanted)) return;

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            VNAssetUi.DrawOutline(area, new Color(0.4f, 0.8f, 1f, 0.9f), 2f);
            if (e.type == EventType.DragUpdated) { e.Use(); return; }

            DragAndDrop.AcceptDrag();
            e.Use();
            BulkAdd(arr, wanted, DragAndDrop.objectReferences);
            GUIUtility.ExitGUI();
        }

        bool AnyDraggable(System.Type wanted)
        {
            var refs = DragAndDrop.objectReferences;
            if (refs == null) return false;
            foreach (var o in refs)
                if (Resolve(o, wanted) != null) return true;
            return false;
        }

        /// <summary>Object 类别接受任意资产；其余走 VNAssetUi 的贴图→Sprite 转换。</summary>
        Object Resolve(Object o, System.Type wanted)
        {
            if (wanted == typeof(Object)) return o != null && AssetDatabase.Contains(o) ? o : null;
            return VNAssetUi.Convert(o, wanted);
        }

        void BulkAdd(SerializedProperty arr, System.Type wanted, Object[] refs)
        {
            int added = 0;
            foreach (var raw in refs)
            {
                var obj = Resolve(raw, wanted);
                if (obj == null) continue;

                int i = arr.arraySize;
                arr.arraySize = i + 1;
                var el = arr.GetArrayElementAtIndex(i);

                if (el.propertyType == SerializedPropertyType.ObjectReference)
                {
                    el.objectReferenceValue = obj;
                }
                else
                {
                    ClearElement(el);
                    var slot = AssetProp(el);
                    if (slot != null) slot.objectReferenceValue = obj;
                    var id = IdProp(el);
                    if (id != null) id.stringValue = VNAssetUi.AssetName(obj);
                    // 音量这类有默认值的字段被 ClearElement 漏掉，补回 1
                    var vol = el.FindPropertyRelative("volume");
                    if (vol != null && vol.propertyType == SerializedPropertyType.Float &&
                        Mathf.Approximately(vol.floatValue, 0f)) vol.floatValue = 1f;
                }
                added++;
            }
            _so.ApplyModifiedProperties();
            if (added > 0)
            {
                _selected = arr.arraySize - 1;
                Debug.Log("[VN 素材] 登记 " + added + " 条到「" + Cats[_cat].title + "」");
            }
            Repaint();
        }

        static void ClearElement(SerializedProperty elem)
        {
            var it = elem.Copy();
            var end = elem.GetEndProperty();
            bool enter = true;
            while (it.NextVisible(enter) && !SerializedProperty.EqualContents(it, end))
            {
                enter = false;
                if (it.propertyType == SerializedPropertyType.String) it.stringValue = string.Empty;
                else if (it.propertyType == SerializedPropertyType.ObjectReference) it.objectReferenceValue = null;
            }
        }

        // ==================================================================
        // 「只看未登记」
        // ==================================================================

        /// <summary>
        /// 列出"素材目录里有、但库里没登记"的文件。
        /// 目录不写死，而是**从已登记条目的路径反推** —— 项目里素材目录改过好几次
        /// （Assets/CG 与 Assets/Art/Images/CG 并存），写死路径必然过时。
        /// </summary>
        void DrawUnregistered(Rect r, SerializedProperty arr)
        {
            var dirs = new HashSet<string>();
            var registered = new HashSet<string>();
            for (int i = 0; i < arr.arraySize; i++)
            {
                var o = AssetOf(arr.GetArrayElementAtIndex(i));
                if (o == null) continue;
                string p = AssetDatabase.GetAssetPath(o);
                if (string.IsNullOrEmpty(p)) continue;
                registered.Add(p);
                dirs.Add(Path.GetDirectoryName(p).Replace('\\', '/'));
            }

            if (dirs.Count == 0)
            {
                EditorGUI.LabelField(VNAssetUi.Shrink(r, 10f),
                    "库里一条都没有，无从推断素材放在哪个目录。\n先手动登记一条，之后这里就能列出同目录下漏掉的文件。",
                    EditorStyles.wordWrappedMiniLabel);
                return;
            }

            string filter = Cats[_cat].kind == Kind.Audio ? "t:AudioClip" : "t:Texture2D";
            var guids = AssetDatabase.FindAssets(filter, new List<string>(dirs).ToArray());

            var missing = new List<string>();
            foreach (var g in guids)
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                if (!registered.Contains(p)) missing.Add(p);
            }

            var head = new Rect(r.x + 6f, r.y + 4f, r.width - 12f, 32f);
            EditorGUI.LabelField(head,
                "扫描目录：" + string.Join("、", new List<string>(dirs).ToArray()) +
                "\n未登记 " + missing.Count + " 个文件（点一个即登记）",
                EditorStyles.wordWrappedMiniLabel);

            var listRect = new Rect(r.x, r.y + 40f, r.width, r.height - 40f);
            float rowH = 20f;
            var content = new Rect(0f, 0f, listRect.width - 16f,
                                   Mathf.Max(missing.Count * rowH + 4f, listRect.height));
            _scrollGrid = GUI.BeginScrollView(listRect, _scrollGrid, content);

            int first = Mathf.Max(0, Mathf.FloorToInt(_scrollGrid.y / rowH));
            int last = Mathf.Min(missing.Count - 1, Mathf.CeilToInt((_scrollGrid.y + listRect.height) / rowH));

            for (int i = first; i <= last; i++)
            {
                var row = new Rect(4f, i * rowH, content.width - 8f, rowH - 1f);
                if (i % 2 == 1) EditorGUI.DrawRect(row, new Color(1f, 1f, 1f, 0.03f));

                var btn = VNAssetUi.CutRight(ref row, 52f);
                EditorGUI.LabelField(row, Path.GetFileName(missing[i]), VNAssetUi.MiniLabel);
                if (GUI.Button(VNAssetUi.Line(btn, 17f), "登记", VNAssetUi.TinyButton))
                {
                    var o = AssetDatabase.LoadAssetAtPath<Object>(missing[i]);
                    BulkAdd(arr, WantedType(), new[] { o });
                    GUIUtility.ExitGUI();
                }
            }
            GUI.EndScrollView();
        }
    }
}
