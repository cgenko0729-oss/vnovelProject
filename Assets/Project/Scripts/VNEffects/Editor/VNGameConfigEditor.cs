using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace VNEffects.EditorTools
{
    /// <summary>
    /// VNGameConfig 的分页 Inspector。
    ///
    /// 【解决什么】
    /// 默认 Inspector 把 30 多个字段、十几个列表**一路铺开**，找一项要滚很久。
    /// 这里按功能切成标签页，一次只画一组；每个列表再加搜索、分页、批量拖入。
    ///
    /// 【为什么不硬编码画每个字段】
    /// 页签只登记字段名，实际绘制仍走 EditorGUILayout.PropertyField ——
    /// 这样条目长什么样完全由 PropertyDrawer 决定（见 VNConfigEntryDrawers），
    /// 而且**以后往 VNGameConfig 加字段不会静默消失**：
    /// 没被任何页签认领的字段会自动落到「其他」页并给出提示，不会看不见。
    /// </summary>
    [CustomEditor(typeof(VNGameConfig))]
    public class VNGameConfigEditor : Editor
    {
        // ==================================================================
        // 页签定义
        // ==================================================================

        class Tab
        {
            public readonly string title;
            public readonly string[] fields;
            public Tab(string title, params string[] fields) { this.title = title; this.fields = fields; }
        }

        static readonly Tab[] Tabs =
        {
            new Tab("剧本",    "entryScript", "chapters"),
            new Tab("标题",    "gameTitle", "gameTitleEn", "gameTitleJa", "titleBackground", "titleBgm"),
            new Tab("UI 皮肤", "dialogueSkins", "choiceSkins", "systemUiSkin"),
            new Tab("舞台",    "characters", "backgrounds", "cgLibrary", "weatherDefs",
                               "interludes", "interludeImages", "tutorials"),
            new Tab("音频",    "bgmLibrary", "seLibrary", "voiceLibrary", "typingTick",
                               "overrideChannelVolumes", "bgmVolume", "seVolume", "voiceVolume"),
            new Tab("玩法",    "mapSprite", "mapLocations", "stats", "shops", "plans",
                               "quests", "trackers", "quizzes", "badmintons", "fogWipes"),
            new Tab("AI",      "aiPersonas", "aiProvider", "aiModel", "aiPricing"),
            new Tab("大头贴",  "photoFrames", "photoStickers", "photoBackdrops", "photoThemes",
                               "photoMeCharacterId"),
        };

        const string TabPrefKey = "VNEffects.GameConfig.Tab";
        const string PageSizePrefKey = "VNEffects.GameConfig.PageSize";

        int _tab;
        /// <summary>没有被任何页签认领的字段（加了新字段却忘了登记页签时兜底）</summary>
        readonly List<string> _orphans = new List<string>();

        // 每个列表各自的搜索串与当前页（跨域重载会丢，无所谓）
        readonly Dictionary<string, string> _queries = new Dictionary<string, string>();
        readonly Dictionary<string, int> _pages = new Dictionary<string, int>();

        void OnEnable()
        {
            _tab = Mathf.Clamp(EditorPrefs.GetInt(TabPrefKey, 0), 0, Tabs.Length);
            CollectOrphans();
        }

        /// <summary>扫一遍序列化字段，找出页签没登记的那些。</summary>
        void CollectOrphans()
        {
            _orphans.Clear();
            var claimed = new HashSet<string>();
            foreach (var t in Tabs)
                foreach (var f in t.fields) claimed.Add(f);

            var it = serializedObject.GetIterator();
            bool enter = true;
            while (it.NextVisible(enter))
            {
                enter = false;                                   // 只看顶层
                if (it.name == "m_Script") continue;
                if (!claimed.Contains(it.name)) _orphans.Add(it.name);
            }
        }

        // ==================================================================
        // 主绘制
        // ==================================================================

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawToolbar();
            EditorGUILayout.Space(2);

            int tabCount = Tabs.Length + (_orphans.Count > 0 ? 1 : 0) + 1;   // +其他 +全部
            _tab = Mathf.Clamp(_tab, 0, tabCount - 1);

            if (_tab < Tabs.Length) DrawFields(Tabs[_tab].fields);
            else if (_orphans.Count > 0 && _tab == Tabs.Length)
            {
                EditorGUILayout.HelpBox(
                    "这些字段还没登记到任何页签里（多半是新加的）。\n" +
                    "把字段名补进 VNGameConfigEditor.Tabs 就能归位。", MessageType.Info);
                DrawFields(_orphans.ToArray());
            }
            else DrawAll();

            ApplyAndNotify(serializedObject);
        }

        void DrawToolbar()
        {
            // ---- 第一行：页签 ----
            var titles = new List<string>();
            foreach (var t in Tabs) titles.Add(t.title);
            if (_orphans.Count > 0) titles.Add("其他 " + _orphans.Count);
            titles.Add("全部");

            // 页签多，窄 Inspector 下自动折成两行
            int perRow = Mathf.Max(4, Mathf.FloorToInt(EditorGUIUtility.currentViewWidth / 66f));
            int newTab = _tab;
            for (int start = 0; start < titles.Count; start += perRow)
            {
                int count = Mathf.Min(perRow, titles.Count - start);
                var slice = titles.GetRange(start, count).ToArray();
                int sel = (_tab >= start && _tab < start + count) ? _tab - start : -1;
                int picked = GUILayout.Toolbar(sel, slice, EditorStyles.toolbarButton);
                if (picked >= 0 && picked != sel) newTab = start + picked;
            }
            if (newTab != _tab) { _tab = newTab; EditorPrefs.SetInt(TabPrefKey, _tab); GUI.FocusControl(null); }

            // ---- 第二行：缩略图尺寸 + 快捷工具 ----
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("缩略图", EditorStyles.miniLabel, GUILayout.Width(42));
                float size = GUILayout.HorizontalSlider(VNAssetUi.ThumbSize,
                                VNAssetUi.MinThumb, VNAssetUi.MaxThumb, GUILayout.Width(80));
                VNAssetUi.ThumbSize = size;

                GUILayout.FlexibleSpace();

                if (GUILayout.Button(new GUIContent("扫描素材目录",
                        "Tools → VN Effects → 游戏配置 Game Config → 重扫素材目录 Rescan Asset Folders\n" +
                        "按目录补登角色/剧本/CG/属性等定义资产"),
                        EditorStyles.toolbarButton, GUILayout.Width(96)))
                {
                    VNGameConfigTools.RescanAssetFolders();
                    GUIUtility.ExitGUI();
                }
                if (GUILayout.Button(new GUIContent("从场景导入",
                        "把当前场景组件上的绑定搬进本资产"),
                        EditorStyles.toolbarButton, GUILayout.Width(84)))
                {
                    VNGameConfigTools.ImportFromScene();
                    GUIUtility.ExitGUI();
                }
            }
        }

        void DrawFields(string[] names)
        {
            foreach (var name in names)
            {
                var prop = serializedObject.FindProperty(name);
                if (prop == null) continue;
                DrawProperty(prop);
            }
        }

        void DrawAll()
        {
            var it = serializedObject.GetIterator();
            bool enter = true;
            while (it.NextVisible(enter))
            {
                enter = false;
                if (it.name == "m_Script") continue;
                DrawProperty(it.Copy());
            }
        }

        void DrawProperty(SerializedProperty prop)
        {
            if (IsDrawableArray(prop)) DrawSmartList(prop);
            else EditorGUILayout.PropertyField(prop, true);
        }

        /// <summary>
        /// 写回改动，并在**确实改了东西**时广播，让打开着的剧本编辑器重建下拉候选。
        /// 所有写入点统一走这里，否则在这儿新登记的素材在剧本编辑器里搜不到。
        /// </summary>
        static void ApplyAndNotify(SerializedObject so)
        {
            if (so != null && so.ApplyModifiedProperties())
                VNAssetLibraryEvents.RaiseChanged();
        }

        /// <summary>数组（但不含 string —— string 在序列化里也算 isArray）</summary>
        static bool IsDrawableArray(SerializedProperty p)
        {
            return p.isArray && p.propertyType != SerializedPropertyType.String;
        }

        // ==================================================================
        // 智能列表：折叠 + 计数 + 搜索 + 分页 + 行操作 + 批量拖入
        // ==================================================================

        static int PageSize
        {
            get { return Mathf.Max(10, EditorPrefs.GetInt(PageSizePrefKey, 50)); }
            set { EditorPrefs.SetInt(PageSizePrefKey, Mathf.Max(10, value)); }
        }

        void DrawSmartList(SerializedProperty arrayProp)
        {
            string key = arrayProp.propertyPath;
            string query;
            _queries.TryGetValue(key, out query);
            query = query ?? string.Empty;

            int total = arrayProp.arraySize;

            // ---- 标题行 ----
            using (new EditorGUILayout.HorizontalScope())
            {
                string title = DisplayName(arrayProp) + "  (" + total + ")";
                arrayProp.isExpanded = EditorGUILayout.Foldout(arrayProp.isExpanded, title, true,
                                                               EditorStyles.foldoutHeader);
            }

            if (!arrayProp.isExpanded) return;

            EditorGUI.indentLevel++;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                // ---- 工具行：搜索 + 添加 ----
                using (new EditorGUILayout.HorizontalScope())
                {
                    string newQuery = EditorGUILayout.TextField(query, EditorStyles.toolbarSearchField);
                    if (newQuery != query)
                    {
                        query = newQuery;
                        _queries[key] = query;
                        _pages[key] = 0;                       // 换关键字回到第一页
                    }
                    if (GUILayout.Button(new GUIContent("+", "在末尾添加一个空条目"),
                                         EditorStyles.miniButton, GUILayout.Width(24)))
                    {
                        AppendEmpty(arrayProp);
                        return;                                 // 数组变了，本帧不再往下画
                    }
                }

                // ---- 重复 / 空 id 提醒 ----
                DrawIdWarnings(arrayProp);

                // ---- 过滤 ----
                var visible = new List<int>(total);
                bool filtering = !string.IsNullOrWhiteSpace(query);
                for (int i = 0; i < total; i++)
                {
                    if (!filtering) { visible.Add(i); continue; }
                    if (VNAssetUi.Matches(ElementHaystack(arrayProp.GetArrayElementAtIndex(i)), query))
                        visible.Add(i);
                }

                if (filtering)
                    EditorGUILayout.LabelField("匹配 " + visible.Count + " / " + total,
                                               EditorStyles.miniLabel);

                // ---- 分页 ----
                int page = 0;
                _pages.TryGetValue(key, out page);
                int pageCount = Mathf.Max(1, Mathf.CeilToInt(visible.Count / (float)PageSize));
                page = Mathf.Clamp(page, 0, pageCount - 1);
                int from = page * PageSize;
                int to = Mathf.Min(visible.Count, from + PageSize);

                if (visible.Count == 0)
                    EditorGUILayout.LabelField(filtering ? "没有匹配项" : "（空列表）",
                                               VNAssetUi.RowLabel);

                // ---- 行 ----
                for (int v = from; v < to; v++)
                {
                    int i = visible[v];
                    if (DrawRow(arrayProp, i, filtering)) { GUIUtility.ExitGUI(); return; }
                }

                // ---- 翻页条 ----
                if (pageCount > 1)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.FlexibleSpace();
                        using (new EditorGUI.DisabledScope(page <= 0))
                            if (GUILayout.Button("◀", EditorStyles.miniButtonLeft, GUILayout.Width(26)))
                                _pages[key] = page - 1;
                        GUILayout.Label((page + 1) + " / " + pageCount, EditorStyles.miniLabel,
                                        GUILayout.Width(46));
                        using (new EditorGUI.DisabledScope(page >= pageCount - 1))
                            if (GUILayout.Button("▶", EditorStyles.miniButtonRight, GUILayout.Width(26)))
                                _pages[key] = page + 1;
                        GUILayout.Space(12);
                        GUILayout.Label("每页", EditorStyles.miniLabel, GUILayout.Width(26));
                        int ps = EditorGUILayout.IntField(PageSize, GUILayout.Width(40));
                        if (ps != PageSize) PageSize = ps;
                        GUILayout.FlexibleSpace();
                    }
                }

                // ---- 批量拖入区 ----
                DrawBulkDropZone(arrayProp);
            }
            EditorGUI.indentLevel--;
            EditorGUILayout.Space(2);
        }

        /// <summary>画一行；返回 true 表示数组结构变了（调用方需立即中断本帧绘制）。</summary>
        bool DrawRow(SerializedProperty arrayProp, int index, bool filtering)
        {
            var elem = arrayProp.GetArrayElementAtIndex(index);
            float h = ElementHeight(elem);

            var row = EditorGUILayout.GetControlRect(false, h);
            // 隔行底色，长列表里更好扫视
            if (index % 2 == 1) EditorGUI.DrawRect(row, new Color(1f, 1f, 1f, 0.025f));

            // 右侧操作按钮
            const float BtnW = 19f;
            var del = VNAssetUi.CutRight(ref row, BtnW, 1f);
            Rect up = default, down = default;
            if (!filtering)                       // 过滤时"相邻"没有意义，藏起来免得误操作
            {
                down = VNAssetUi.CutRight(ref row, BtnW, 1f);
                up = VNAssetUi.CutRight(ref row, BtnW, 3f);
            }

            EditorGUI.PropertyField(row, elem, GUIContent.none, true);

            if (!filtering)
            {
                using (new EditorGUI.DisabledScope(index <= 0))
                    if (GUI.Button(SquashV(up), new GUIContent("▲", "上移"), VNAssetUi.TinyButton))
                    {
                        arrayProp.MoveArrayElement(index, index - 1);
                        ApplyAndNotify(arrayProp.serializedObject);
                        return true;
                    }
                using (new EditorGUI.DisabledScope(index >= arrayProp.arraySize - 1))
                    if (GUI.Button(SquashV(down), new GUIContent("▼", "下移"), VNAssetUi.TinyButton))
                    {
                        arrayProp.MoveArrayElement(index, index + 1);
                        ApplyAndNotify(arrayProp.serializedObject);
                        return true;
                    }
            }

            if (GUI.Button(SquashV(del), new GUIContent("✕", "删除这一条"), VNAssetUi.TinyButton))
            {
                DeleteAt(arrayProp, index);
                ApplyAndNotify(arrayProp.serializedObject);
                return true;
            }
            return false;
        }

        /// <summary>把按钮压成一个居中的小方块，别跟着整行拉那么高。</summary>
        static Rect SquashV(Rect r)
        {
            float h = Mathf.Min(r.height, 20f);
            return new Rect(r.x, r.y + (r.height - h) * 0.5f, r.width, h);
        }

        static float ElementHeight(SerializedProperty elem)
        {
            return EditorGUI.GetPropertyHeight(elem, GUIContent.none, true);
        }

        // ==================================================================
        // id 校验
        // ==================================================================

        void DrawIdWarnings(SerializedProperty arrayProp)
        {
            int n = arrayProp.arraySize;
            if (n == 0) return;

            var seen = new HashSet<string>();
            var dup = new List<string>();
            int empty = 0;
            bool hasIdField = false;

            for (int i = 0; i < n; i++)
            {
                var el = arrayProp.GetArrayElementAtIndex(i);
                if (el.propertyType == SerializedPropertyType.ObjectReference) continue;
                var idProp = el.FindPropertyRelative("id");
                if (idProp == null || idProp.propertyType != SerializedPropertyType.String) continue;
                hasIdField = true;

                string id = idProp.stringValue;
                if (string.IsNullOrWhiteSpace(id)) { empty++; continue; }
                if (!seen.Add(id) && !dup.Contains(id)) dup.Add(id);
            }
            if (!hasIdField) return;

            if (dup.Count > 0)
                EditorGUILayout.HelpBox(
                    "id 重复：" + string.Join("、", dup) + "\n剧本按 id 查表时只会命中第一条。",
                    MessageType.Warning);
            if (empty > 0)
                EditorGUILayout.HelpBox("有 " + empty + " 条没填 id，剧本引用不到。", MessageType.Info);
        }

        // ==================================================================
        // 搜索用文本
        // ==================================================================

        /// <summary>
        /// 一个元素的可搜索文本 = 它所有字符串字段 + 所有引用资产的文件名。
        /// 通用做法（不认识具体类型），所以新加的条目类型不用改这里就能搜。
        /// </summary>
        static string ElementHaystack(SerializedProperty elem)
        {
            if (elem.propertyType == SerializedPropertyType.ObjectReference)
            {
                var o = elem.objectReferenceValue;
                return o == null ? string.Empty : o.name + " " + VNAssetUi.AssetName(o);
            }

            var sb = new System.Text.StringBuilder();
            var it = elem.Copy();
            var end = elem.GetEndProperty();
            bool enter = true;
            while (it.NextVisible(enter) && !SerializedProperty.EqualContents(it, end))
            {
                enter = false;
                if (it.propertyType == SerializedPropertyType.String) { sb.Append(it.stringValue); sb.Append(' '); }
                else if (it.propertyType == SerializedPropertyType.ObjectReference)
                {
                    var o = it.objectReferenceValue;
                    if (o != null) { sb.Append(o.name); sb.Append(' '); sb.Append(VNAssetUi.AssetName(o)); sb.Append(' '); }
                }
            }
            return sb.ToString();
        }

        // ==================================================================
        // 增删 / 批量拖入
        // ==================================================================

        static void AppendEmpty(SerializedProperty arrayProp)
        {
            int i = arrayProp.arraySize;
            arrayProp.arraySize = i + 1;
            ClearElement(arrayProp.GetArrayElementAtIndex(i));
            ApplyAndNotify(arrayProp.serializedObject);
        }

        /// <summary>Unity 复制上一条的值当新元素，这里清干净，免得出现两条一模一样的 id。</summary>
        static void ClearElement(SerializedProperty elem)
        {
            if (elem.propertyType == SerializedPropertyType.ObjectReference)
            {
                elem.objectReferenceValue = null;
                return;
            }
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

        static void DeleteAt(SerializedProperty arrayProp, int index)
        {
            int before = arrayProp.arraySize;
            arrayProp.DeleteArrayElementAtIndex(index);
            // 老版 Unity 对引用数组的第一次删除只是把元素置 null，需要再删一次
            if (arrayProp.arraySize == before) arrayProp.DeleteArrayElementAtIndex(index);
        }

        /// <summary>
        /// 列表底部的批量拖入区：从 Project 一次拖几十个素材进来自动建条目，
        /// id 预填文件名。200+ 素材手工敲 id 的体力活主要靠这个省掉。
        /// </summary>
        void DrawBulkDropZone(SerializedProperty arrayProp)
        {
            var elemType = GetElementType(arrayProp);
            if (elemType == null) return;

            var targetField = FindAssetField(arrayProp, elemType);
            if (targetField == null) return;          // 条目里没有资产槽（如纯数值条目），不提供拖入

            var zone = EditorGUILayout.GetControlRect(false, 30f);
            var e = Event.current;
            bool hovering = zone.Contains(e.mousePosition) &&
                            (e.type == EventType.DragUpdated || e.type == EventType.DragPerform) &&
                            VNAssetUi.FirstDraggedOfType(targetField) != null;

            EditorGUI.DrawRect(zone, new Color(1f, 1f, 1f, hovering ? 0.10f : 0.035f));
            if (hovering) VNAssetUi.DrawOutline(zone, new Color(0.4f, 0.8f, 1f, 0.9f), 2f);

            var style = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter };
            var oldColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, hovering ? 1f : 0.55f);
            GUI.Label(zone, "把 " + FriendlyTypeName(targetField) + " 拖到这里批量添加（id 预填文件名）", style);
            GUI.color = oldColor;

            if (!zone.Contains(e.mousePosition)) return;
            if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform) return;
            if (VNAssetUi.FirstDraggedOfType(targetField) == null) return;

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (e.type == EventType.DragUpdated) { e.Use(); return; }

            DragAndDrop.AcceptDrag();
            e.Use();
            BulkAdd(arrayProp, targetField);
            GUIUtility.ExitGUI();
        }

        void BulkAdd(SerializedProperty arrayProp, System.Type wanted)
        {
            var refs = DragAndDrop.objectReferences;
            int added = 0;
            foreach (var raw in refs)
            {
                var obj = VNAssetUi.Convert(raw, wanted);
                if (obj == null) continue;

                int i = arrayProp.arraySize;
                arrayProp.arraySize = i + 1;
                var elem = arrayProp.GetArrayElementAtIndex(i);
                ClearElement(elem);

                if (elem.propertyType == SerializedPropertyType.ObjectReference)
                {
                    elem.objectReferenceValue = obj;
                }
                else
                {
                    var slot = FirstObjectProperty(elem);
                    if (slot != null) slot.objectReferenceValue = obj;
                    var idProp = elem.FindPropertyRelative("id");
                    if (idProp != null && idProp.propertyType == SerializedPropertyType.String)
                        idProp.stringValue = VNAssetUi.AssetName(obj);
                }
                added++;
            }
            ApplyAndNotify(arrayProp.serializedObject);
            if (added > 0) Debug.Log("[VNGameConfig] 批量添加 " + added + " 条到 " + DisplayName(arrayProp));
        }

        /// <summary>元素里第一个对象引用属性（条目类的资产槽）。</summary>
        static SerializedProperty FirstObjectProperty(SerializedProperty elem)
        {
            var it = elem.Copy();
            var end = elem.GetEndProperty();
            bool enter = true;
            while (it.NextVisible(enter) && !SerializedProperty.EqualContents(it, end))
            {
                enter = false;
                if (it.propertyType == SerializedPropertyType.ObjectReference) return it.Copy();
            }
            return null;
        }

        // ==================================================================
        // 反射：拿 List<T> 的 T，以及条目里资产槽的类型
        // ==================================================================

        static System.Type GetElementType(SerializedProperty arrayProp)
        {
            var target = arrayProp.serializedObject.targetObject;
            if (target == null) return null;
            var field = target.GetType().GetField(arrayProp.name,
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null) return null;

            var t = field.FieldType;
            if (t.IsArray) return t.GetElementType();
            if (t.IsGenericType && t.GetGenericArguments().Length == 1) return t.GetGenericArguments()[0];
            return null;
        }

        /// <summary>条目该接收什么类型的资产：元素本身是引用就是它，否则取元素里第一个 Object 字段的类型。</summary>
        static System.Type FindAssetField(SerializedProperty arrayProp, System.Type elemType)
        {
            if (typeof(Object).IsAssignableFrom(elemType)) return elemType;

            var fields = elemType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (var f in fields)
                if (typeof(Object).IsAssignableFrom(f.FieldType)) return f.FieldType;
            return null;
        }

        static string FriendlyTypeName(System.Type t)
        {
            if (t == typeof(Sprite)) return "图片";
            if (t == typeof(AudioClip)) return "音频";
            if (t == typeof(GameObject)) return "prefab";
            if (t == typeof(TextAsset)) return "文本资产";
            return t.Name;
        }

        static string DisplayName(SerializedProperty p)
        {
            return string.IsNullOrEmpty(p.displayName) ? p.name : p.displayName;
        }
    }
}
