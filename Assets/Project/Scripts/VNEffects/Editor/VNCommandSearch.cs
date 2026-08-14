using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace VNEffects.EditorTools
{
    /// <summary>
    /// 一条搜索候选。<see cref="value"/> 是真正写进剧本的值，
    /// title / subtitle / accent 只影响显示，searchExtra 是额外可搜文本（分类名、别称）。
    /// </summary>
    sealed class VNSearchItem
    {
        public string value = "";
        public string title = "";
        public string subtitle = "";
        public string searchExtra = "";
        public Color accent = new Color(0f, 0f, 0f, 0f);   // alpha 0 = 不画左侧色块

        public bool Matches(string query)
        {
            if (string.IsNullOrEmpty(query)) return true;
            return Contains(title, query) || Contains(value, query) ||
                   Contains(subtitle, query) || Contains(searchExtra, query);
        }

        static bool Contains(string text, string query) =>
            !string.IsNullOrEmpty(text) &&
            text.IndexOf(query, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// 搜索框 + 候选列表的共用绘制件：子串匹配（大小写无关）、↑↓/PageUp/PageDown 移动、
    /// Enter 确认、Esc 取消、Tab 跳过，命中片段高亮。
    ///
    /// 【硬约定】键盘处理必须排在 <see cref="EditorGUI.TextField"/> 之前——
    /// IMGUI 里文本框会把 ↑↓ 拿去移光标、把 Enter 当成"结束编辑"吃掉，
    /// 先 Use() 掉才轮得到我们。
    ///
    /// 【为什么不用 Unity 原生 AdvancedDropdown】它只匹配 item 名字、候选行只有一行文字，
    /// 放不下命令的 hint 副标题；而 Ctrl+E 命令面板无论如何要自写带输入框的窗口，
    /// 与其两套控件两套键位，不如共用这一套。
    /// 【匹配规则】刻意只做子串包含，不做模糊/拼音——够用，且行为可预期。
    /// </summary>
    sealed class VNSearchListView
    {
        public enum Act { None, Choose, Cancel, Skip }

        public struct Result
        {
            public Act act;
            public VNSearchItem item;
            public bool shift;      // Enter 时是否按着 Shift（命令面板用来决定插上方还是下方）
        }

        const string FieldName = "VNSearchListView.Query";
        const float SearchH = 18f;
        const float FooterH = 15f;

        /// <summary>候选行画两行（标题 + 灰字副标题）；纯 id 列表设 false 更紧凑</summary>
        public bool twoLine = true;
        public string query = "";

        int _index;
        Vector2 _scroll;
        bool _focusPending = true;
        float _rowH = 20f;
        float _viewH = 100f;

        readonly List<VNSearchItem> _filtered = new List<VNSearchItem>();

        static GUIStyle _titleStyle;
        static GUIStyle _subStyle;
        static GUIStyle _emptyStyle;

        /// <summary>
        /// 换阶段时清空搜索框（命令面板逐步问参数时每步都要清）。
        ///
        /// 【必须放掉键盘焦点】IMGUI 的文本框只要还持有 keyboardControl，就用它内部
        /// TextEditor 的缓冲，程序里把 query 改成 "" 不生效——下一帧那个控件会把旧文本
        /// 原样 return 回来，等于又把 query 写回去。先 keyboardControl = 0 让它重新
        /// 从源字符串同步，再靠 _focusPending 把焦点抢回来。
        /// </summary>
        public void Reset()
        {
            query = "";
            _index = 0;
            _scroll = Vector2.zero;
            _focusPending = true;
            GUIUtility.keyboardControl = 0;
            EditorGUIUtility.editingTextField = false;
        }

        /// <summary>
        /// 画一次。head 是不参与过滤、永远排在最前的一条（"使用自定义值…" / "↵ 插入这一行"），
        /// 不需要就传 null。
        /// </summary>
        public Result Draw(Rect rect, IList<VNSearchItem> source, VNSearchItem head, string footer)
        {
            EnsureStyles();
            var result = new Result();
            var e = Event.current;

            _rowH = twoLine ? 32f : 19f;
            var searchRect = new Rect(rect.x + 4f, rect.y + 3f, rect.width - 8f, SearchH);
            float footerH = string.IsNullOrEmpty(footer) ? 0f : FooterH;
            var listRect = new Rect(rect.x + 4f, searchRect.yMax + 3f, rect.width - 8f,
                rect.yMax - searchRect.yMax - footerH - 6f);
            _viewH = listRect.height;

            Filter(source, head);
            _index = Mathf.Clamp(_index, 0, Mathf.Max(0, _filtered.Count - 1));

            // ---- 键盘：必须抢在 TextField 之前 ----
            if (e.type == EventType.KeyDown)
            {
                switch (e.keyCode)
                {
                    case KeyCode.DownArrow: Move(1); e.Use(); break;
                    case KeyCode.UpArrow: Move(-1); e.Use(); break;
                    case KeyCode.PageDown: Move(6); e.Use(); break;
                    case KeyCode.PageUp: Move(-6); e.Use(); break;
                    case KeyCode.Return:
                    case KeyCode.KeypadEnter:
                        result.act = Act.Choose;
                        result.item = _index >= 0 && _index < _filtered.Count
                            ? _filtered[_index] : null;
                        result.shift = e.shift;
                        e.Use();
                        break;
                    case KeyCode.Escape:
                        result.act = Act.Cancel;
                        e.Use();
                        break;
                    case KeyCode.Tab:
                        result.act = Act.Skip;
                        e.Use();
                        break;
                }
            }

            // ---- 搜索框 ----
            GUI.SetNextControlName(FieldName);
            string typed = EditorGUI.TextField(searchRect, query, EditorStyles.toolbarSearchField);
            if (typed != query)
            {
                query = typed;
                _index = 0;
                _scroll.y = 0f;
            }
            // 控件画完后再抢焦点（IMGUI 只认已经存在的控件名）
            if (_focusPending && e.type == EventType.Repaint)
            {
                _focusPending = false;
                EditorGUI.FocusTextInControl(FieldName);
            }

            DrawList(listRect, ref result);

            if (footerH > 0f)
                GUI.Label(new Rect(rect.x + 6f, listRect.yMax + 1f, rect.width - 12f, footerH),
                    footer, _subStyle);

            return result;
        }

        void Filter(IList<VNSearchItem> source, VNSearchItem head)
        {
            _filtered.Clear();
            if (head != null) _filtered.Add(head);
            if (source == null) return;
            for (int i = 0; i < source.Count; i++)
                if (source[i].Matches(query)) _filtered.Add(source[i]);
        }

        void Move(int delta)
        {
            if (_filtered.Count == 0) return;
            _index = Mathf.Clamp(_index + delta, 0, _filtered.Count - 1);
            float top = _index * _rowH;
            float bottom = top + _rowH;
            if (top < _scroll.y) _scroll.y = top;
            else if (bottom > _scroll.y + _viewH) _scroll.y = bottom - _viewH;
        }

        void DrawList(Rect rect, ref Result result)
        {
            EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.12f));
            if (_filtered.Count == 0)
            {
                GUI.Label(rect, "没有匹配项", _emptyStyle);
                return;
            }

            var e = Event.current;
            var view = new Rect(0f, 0f, rect.width - 16f, _filtered.Count * _rowH);
            _scroll = GUI.BeginScrollView(rect, _scroll, view);
            for (int i = 0; i < _filtered.Count; i++)
            {
                var row = new Rect(0f, i * _rowH, view.width, _rowH);
                if (row.yMax < _scroll.y || row.y > _scroll.y + rect.height) continue;
                DrawItem(row, _filtered[i], i == _index);
                if (e.type == EventType.MouseDown && e.button == 0 &&
                    row.Contains(e.mousePosition))
                {
                    _index = i;
                    result.act = Act.Choose;
                    result.item = _filtered[i];
                    result.shift = e.shift;
                    e.Use();
                }
            }
            GUI.EndScrollView();
        }

        void DrawItem(Rect rect, VNSearchItem item, bool selected)
        {
            if (selected)
                EditorGUI.DrawRect(rect, new Color(0.25f, 0.5f, 0.85f, 0.35f));

            float x = rect.x + 4f;
            if (item.accent.a > 0f)
            {
                EditorGUI.DrawRect(new Rect(x, rect.y + 3f, 4f, rect.height - 6f), item.accent);
                x += 9f;
            }

            bool two = twoLine && !string.IsNullOrEmpty(item.subtitle);
            var titleRect = new Rect(x, rect.y + (two ? 1f : 0f), rect.xMax - x - 4f,
                two ? 16f : rect.height);
            GUI.Label(titleRect, Highlight(item.title, query), _titleStyle);
            if (two)
                GUI.Label(new Rect(x, titleRect.yMax - 1f, rect.xMax - x - 4f, 14f),
                    item.subtitle, _subStyle);
        }

        /// <summary>把命中的片段染色加粗（富文本；id 里不会出现尖括号，不做转义）</summary>
        static string Highlight(string text, string query)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(query)) return text;
            int at = text.IndexOf(query, System.StringComparison.OrdinalIgnoreCase);
            if (at < 0) return text;
            return text.Substring(0, at) + "<b><color=#5FC8FF>" +
                   text.Substring(at, query.Length) + "</color></b>" +
                   text.Substring(at + query.Length);
        }

        static void EnsureStyles()
        {
            if (_titleStyle != null) return;
            _titleStyle = new GUIStyle(EditorStyles.label)
            {
                richText = true,
                alignment = TextAnchor.MiddleLeft,
            };
            _subStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false,
            };
            _subStyle.normal.textColor = new Color(0.62f, 0.62f, 0.62f);
            _emptyStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
            };
        }
    }

    /// <summary>
    /// 通用搜索选择弹窗：行类型切换（右键命令按钮）、底部 [+] 加行、参数格下拉共用。
    /// allowFreeValue = true 时，输入的内容不在候选里就在顶部给一条「使用自定义值」。
    /// </summary>
    sealed class VNSearchPopup : PopupWindowContent
    {
        readonly string _title;
        readonly List<VNSearchItem> _items;
        readonly System.Action<VNSearchItem> _onSelect;
        readonly System.Action _onCustom;      // 可空：保留老的 "custom…" 常驻文本框入口
        readonly bool _allowFreeValue;
        readonly Vector2 _size;
        readonly VNSearchListView _view = new VNSearchListView();

        public VNSearchPopup(string title, List<VNSearchItem> items,
            System.Action<VNSearchItem> onSelect, bool twoLine = true,
            bool allowFreeValue = false, System.Action onCustom = null,
            float width = 430f, float height = 330f)
        {
            _title = title;
            _items = items ?? new List<VNSearchItem>();
            _onSelect = onSelect;
            _onCustom = onCustom;
            _allowFreeValue = allowFreeValue;
            _size = new Vector2(width, height);
            _view.twoLine = twoLine;
        }

        public override Vector2 GetWindowSize() => _size;

        public override void OnGUI(Rect rect)
        {
            var head = new Rect(rect.x, rect.y, rect.width, 18f);
            EditorGUI.DrawRect(head, new Color(0f, 0f, 0f, 0.22f));
            float titleW = _onCustom != null ? head.width - 76f : head.width - 8f;
            GUI.Label(new Rect(head.x + 6f, head.y, titleW, head.height), _title,
                EditorStyles.miniLabel);
            if (_onCustom != null &&
                GUI.Button(new Rect(head.xMax - 70f, head.y + 1f, 66f, 16f), "custom…",
                    EditorStyles.miniButton))
            {
                _onCustom();
                editorWindow.Close();
                return;
            }

            VNSearchItem free = null;
            if (_allowFreeValue && !string.IsNullOrEmpty(_view.query) &&
                !HasExactValue(_view.query))
                free = new VNSearchItem
                {
                    value = _view.query,
                    title = $"使用自定义值 “{_view.query}”",
                    subtitle = "直接写进剧本，不受候选限制",
                };

            var body = new Rect(rect.x, head.yMax, rect.width, rect.height - head.height);
            var result = _view.Draw(body, _items, free, "↑↓ 选择　Enter 确认　Esc 关闭");

            if (result.act == VNSearchListView.Act.Cancel)
            {
                editorWindow.Close();
                return;
            }
            if (result.act == VNSearchListView.Act.Choose && result.item != null)
            {
                _onSelect?.Invoke(result.item);
                editorWindow.Close();
            }
        }

        bool HasExactValue(string value)
        {
            foreach (var item in _items)
                if (string.Equals(item.value, value, System.StringComparison.Ordinal))
                    return true;
            return false;
        }
    }

    /// <summary>
    /// Ctrl+E 命令面板（向导式）：选命令 → 逐个问位置参数 → 可选参数菜单循环 → 插行。
    /// 面板只负责组装一个 <see cref="VNRow"/>，插哪、怎么撤销由 commit 回调（窗口）决定。
    ///
    /// say 行的说话者走 <see cref="VNRow.speaker"/> 专用字段而不是 values——
    /// 这是编辑器铁律，两条路径混用会生成出野参数。
    /// </summary>
    sealed class VNCommandPalette : PopupWindowContent
    {
        public delegate List<VNSearchItem> CandidateResolver(VNRow row, VNParamDef param);

        enum Stage { Command, Param, KwargMenu }

        const string DoneValue = "done";

        /// <summary>say 行的合成参数：说话者（写 VNRow.speaker，不进 values）</summary>
        static readonly VNParamDef SaySpeaker = new VNParamDef
        {
            id = "say.speaker", label = "说话者", source = VNParamSource.Character,
        };

        readonly List<VNSearchItem> _commands;
        readonly CandidateResolver _candidates;
        readonly System.Func<string, VNRow> _makeRow;
        readonly System.Action<VNRow, bool> _commit;   // (行, 是否插在选区上方)
        readonly Vector2 _size = new Vector2(540f, 350f);
        readonly VNSearchListView _view = new VNSearchListView();

        Stage _stage = Stage.Command;
        VNRow _row;
        VNCommandDef _def;
        bool _isSay;

        readonly List<VNParamDef> _queue = new List<VNParamDef>();   // 待问的位置参数
        int _queueIndex;
        VNParamDef _current;
        bool _currentIsKwarg;

        public VNCommandPalette(List<VNSearchItem> commands, CandidateResolver candidates,
            System.Func<string, VNRow> makeRow, System.Action<VNRow, bool> commit)
        {
            _commands = commands;
            _candidates = candidates;
            _makeRow = makeRow;
            _commit = commit;
        }

        public override Vector2 GetWindowSize() => _size;

        public override void OnGUI(Rect rect)
        {
            var head = new Rect(rect.x, rect.y, rect.width, 20f);
            EditorGUI.DrawRect(head, new Color(0f, 0f, 0f, 0.28f));
            GUI.Label(new Rect(head.x + 6f, head.y + 1f, head.width - 12f, head.height),
                Breadcrumb(), EditorStyles.boldLabel);

            var body = new Rect(rect.x, head.yMax, rect.width, rect.height - head.height);
            List<VNSearchItem> source;
            VNSearchItem headItem = null;
            string footer;

            switch (_stage)
            {
                case Stage.Param:
                    source = _candidates(_row, _current);
                    _view.twoLine = false;
                    if (!string.IsNullOrEmpty(_view.query) && !HasExactValue(source, _view.query))
                        headItem = new VNSearchItem
                        {
                            value = _view.query,
                            title = $"使用输入值 “{_view.query}”",
                        };
                    // source == null 表示这个参数是自由文本/数字，没有候选可筛
                    footer = source == null
                        ? "直接输入内容　Enter 确认　Tab 跳过这个参数　Esc 取消"
                        : "Enter 确认　Tab 跳过这个参数　Esc 取消";
                    break;

                case Stage.KwargMenu:
                    source = RemainingKwargs();
                    _view.twoLine = false;
                    if (string.IsNullOrEmpty(_view.query))
                        headItem = new VNSearchItem
                        {
                            value = DoneValue,
                            title = "↵ 插入这一行：" + Preview(),
                        };
                    footer = "Enter 选参数／空查询 Enter 完成　Shift+Enter 插到选区上方　Esc 取消";
                    break;

                default:
                    source = _commands;
                    _view.twoLine = true;
                    footer = "↑↓ 选择　Enter 下一步　Esc 取消";
                    break;
            }

            var result = _view.Draw(body, source, headItem, footer);

            switch (result.act)
            {
                case VNSearchListView.Act.Cancel:
                    editorWindow.Close();
                    return;

                case VNSearchListView.Act.Skip:
                    if (_stage == Stage.Param) AfterParamValue();
                    return;

                case VNSearchListView.Act.Choose:
                    if (result.item == null) return;
                    Choose(result.item, result.shift);
                    return;
            }
        }

        void Choose(VNSearchItem item, bool shift)
        {
            switch (_stage)
            {
                case Stage.Command:
                    StartCommand(item.value);
                    return;

                case Stage.Param:
                    SetValue(_current, item.value);
                    AfterParamValue();
                    return;

                case Stage.KwargMenu:
                    if (item.value == DoneValue)
                    {
                        Commit(shift);
                        return;
                    }
                    _current = _def != null ? _def.FindKwarg(item.value) : null;
                    if (_current == null) return;
                    _currentIsKwarg = true;
                    _stage = Stage.Param;
                    _view.Reset();
                    return;
            }
        }

        void StartCommand(string keyword)
        {
            _row = _makeRow(keyword);
            if (_row == null) return;
            _isSay = _row.kind == VNRowKind.Say;
            _def = _isSay ? null : VNScenarioSchema.Find(keyword);

            _queue.Clear();
            if (_isSay) _queue.Add(SaySpeaker);
            else if (_def != null) _queue.AddRange(_def.Positional());
            _queueIndex = -1;
            _currentIsKwarg = false;
            AfterParamValue();
        }

        /// <summary>问完（或跳过）一个参数：kwarg 回菜单，位置参数往下走，走完就收尾</summary>
        void AfterParamValue()
        {
            if (_currentIsKwarg)
            {
                _currentIsKwarg = false;
                _current = null;
                _stage = Stage.KwargMenu;
                _view.Reset();
                return;
            }

            _queueIndex++;
            if (_queueIndex < _queue.Count)
            {
                _current = _queue[_queueIndex];
                _stage = Stage.Param;
                _view.Reset();
                return;
            }

            if (_isSay || _def == null || RemainingKwargs().Count == 0)
            {
                Commit(false);
                return;
            }
            _stage = Stage.KwargMenu;
            _view.Reset();
        }

        void SetValue(VNParamDef param, string value)
        {
            if (param == null || _row == null) return;
            if (param.id == SaySpeaker.id) _row.speaker = value;   // 铁律：不进 values
            else _row.Set(param.id, value);
        }

        void Commit(bool insertAbove)
        {
            if (_row != null) _commit?.Invoke(_row, insertAbove);
            editorWindow.Close();
        }

        List<VNSearchItem> RemainingKwargs()
        {
            var items = new List<VNSearchItem>();
            if (_def == null) return items;
            foreach (var p in _def.parameters)
            {
                if (!p.kwarg) continue;
                string filled = _row.Get(p.id);
                items.Add(new VNSearchItem
                {
                    value = p.id,
                    title = string.IsNullOrEmpty(filled)
                        ? $"{p.id}:　{p.label}"
                        : $"{p.id}:{filled}　（已填，可改）",
                    searchExtra = p.label,
                });
            }
            return items;
        }

        static bool HasExactValue(List<VNSearchItem> items, string value)
        {
            if (items == null) return false;
            foreach (var item in items)
                if (string.Equals(item.value, value, System.StringComparison.Ordinal))
                    return true;
            return false;
        }

        string Breadcrumb()
        {
            if (_row == null) return "插入新行　▸　选命令";
            string s = Preview();
            if (_stage == Stage.Param && _current != null)
                s += _currentIsKwarg ? $"　▸　{_current.id}:" : $"　▸　{_current.label}:";
            else if (_stage == Stage.KwargMenu)
                s += "　▸　可选参数";
            return s;
        }

        /// <summary>当前组装到一半的行长什么样（面包屑与「插入这一行」共用）</summary>
        string Preview()
        {
            if (_row == null) return "";
            if (_isSay)
                return string.IsNullOrEmpty(_row.speaker)
                    ? "say（旁白）" : $"{_row.speaker}: …";

            var sb = new System.Text.StringBuilder(_row.keyword);
            if (_def != null)
            {
                foreach (var p in _def.Positional())
                {
                    string v = _row.Get(p.id);
                    if (!string.IsNullOrEmpty(v)) sb.Append(' ').Append(v);
                }
                foreach (var p in _def.parameters)
                {
                    if (!p.kwarg) continue;
                    string v = _row.Get(p.id);
                    if (!string.IsNullOrEmpty(v)) sb.Append(' ').Append(p.id).Append(':').Append(v);
                }
            }
            return sb.ToString();
        }
    }
}
