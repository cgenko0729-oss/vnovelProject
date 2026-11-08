using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEditorInternal;
using UnityEngine;

namespace VNEffects.EditorTools
{
    public partial class VNScenarioEditorWindow
    {
        // ------------------------------------------------------------------
        // 快捷键（走 ShortcutManager：窗口作用域，用户可在 Edit → Shortcuts 里改键位）
        // ------------------------------------------------------------------

        [Shortcut("VN/Scenario Editor/Save", typeof(VNScenarioEditorWindow),
            KeyCode.S, ShortcutModifiers.Action)]
        static void ShortcutSave(ShortcutArguments args)
        {
            if (args.context is VNScenarioEditorWindow w) w.SaveFile(false);
        }

        // 注意：ShortcutManager 不接受 Return/Enter 当绑定键（会被忽略并刷警告），
        // 所以主键位用 F5/F6，Ctrl+Enter 那套在 HandleShortcutKeys 里走 IMGUI 自己收。
        [Shortcut("VN/Scenario Editor/Play From Selected Row", typeof(VNScenarioEditorWindow),
            KeyCode.F5)]
        static void ShortcutPlaySelected(ShortcutArguments args)
        {
            if (args.context is VNScenarioEditorWindow w) w.PlayFromSelectedRow();
        }

        [Shortcut("VN/Scenario Editor/Replay Last Line", typeof(VNScenarioEditorWindow),
            KeyCode.F6)]
        static void ShortcutReplayLast(ShortcutArguments args)
        {
            if (args.context is VNScenarioEditorWindow w) w.ReplayLastLine();
        }

        /// <summary>ShortcutManager 收不了的 Ctrl+Enter / Ctrl+Shift+Enter，在 IMGUI 里补</summary>
        void HandleShortcutKeys()
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown) return;
            if (e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter) return;
            if (!(e.control || e.command) || e.alt) return;

            if (e.shift) ReplayLastLine();
            else PlayFromSelectedRow();
            e.Use();
        }

        /// <summary>
        /// Ctrl+E 命令面板。PopupWindow.Show 只能在 OnGUI 里调（要 GUI 坐标换算），
        /// 所以这里只置旗标，真正弹出在 DrawEditTab 末尾。
        /// 键位没选 Ctrl+K：那是 Unity Search 的默认绑定，Shortcuts 面板会标冲突。
        /// </summary>
        [Shortcut("VN/Scenario Editor/Command Palette", typeof(VNScenarioEditorWindow),
            KeyCode.E, ShortcutModifiers.Action)]
        static void ShortcutCommandPalette(ShortcutArguments args)
        {
            if (!(args.context is VNScenarioEditorWindow w)) return;
            w._tab = Tab.Edit;
            w._pendingPalette = true;
            w.Repaint();
        }

        /// <summary>
        /// Ctrl+/ = 把选中行临时禁用/恢复（VS 的注释键位）。
        /// 正在文本框里打字时不抢——那时的 "/" 应该老老实实变成一个斜杠。
        /// </summary>
        [Shortcut("VN/Scenario Editor/Toggle Disable Rows", typeof(VNScenarioEditorWindow),
            KeyCode.Slash, ShortcutModifiers.Action)]
        static void ShortcutToggleDisable(ShortcutArguments args)
        {
            if (!(args.context is VNScenarioEditorWindow w)) return;
            if (w._tab != Tab.Edit || EditorGUIUtility.editingTextField) return;
            w.ToggleDisableSelection();
        }

        /// <summary>Ctrl+F = 焦点丢给工具栏的搜索框（真正的聚焦要在 OnGUI 里做，这里只置旗标）</summary>
        [Shortcut("VN/Scenario Editor/Focus Search", typeof(VNScenarioEditorWindow),
            KeyCode.F, ShortcutModifiers.Action)]
        static void ShortcutFocusSearch(ShortcutArguments args)
        {
            if (!(args.context is VNScenarioEditorWindow w)) return;
            w._tab = Tab.Edit;
            w._focusSearchPending = true;
            w.Repaint();
        }

        [Shortcut("VN/Scenario Editor/Toggle Debug Pause", typeof(VNScenarioEditorWindow),
            KeyCode.F8)]
        static void ShortcutTogglePause(ShortcutArguments args)
        {
            if (!(args.context is VNScenarioEditorWindow w)) return;
            VNScriptRunner runner = w.ResolveRunner();
            if (runner == null) return;
            runner.SetDebugPaused(!runner.IsDebugPaused);
            w.Repaint();
        }

        [Shortcut("VN/Scenario Editor/Debug Step", typeof(VNScenarioEditorWindow),
            KeyCode.F10)]
        static void ShortcutStep(ShortcutArguments args)
        {
            if (!(args.context is VNScenarioEditorWindow w)) return;
            VNScriptRunner runner = w.ResolveRunner();
            if (runner == null) return;
            runner.RequestDebugStep();
            w.Repaint();
        }

        /// <summary>Enter = 在选区下方插一条台词行，Shift+Enter = 插在上方（文本框内不抢键）</summary>
        void HandleInsertKeys()
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown || _tab != Tab.Edit) return;
            if (e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter) return;
            if (e.control || e.command || e.alt) return;
            // 正在文本框里打字：这次 Enter 交给文本框结束编辑，再按一次才插入
            if (EditorGUIUtility.editingTextField) return;

            var selected = SelectedRowIndices();
            if (selected.Count == 0)
                _pendingInsertAt = _doc.rows.Count;
            else
                _pendingInsertAt = e.shift
                    ? selected[0]
                    : selected[selected.Count - 1] + 1;
            e.Use();
            Repaint();
        }

        /// <summary>
        /// 搜索弹窗 / 命令面板攒好的行，在下一个 Layout 事件里插进文档。
        /// 位置语义与 Enter 一致：选区下方（Shift = 上方），没有选区就追加到末尾。
        /// </summary>
        void ApplyPendingNewRow()
        {
            if (_pendingNewRow == null) return;
            VNRow row = _pendingNewRow;
            bool above = _pendingNewRowAbove;
            _pendingNewRow = null;

            var selected = SelectedRowIndices();
            int at;
            if (selected.Count == 0) at = _doc.rows.Count;
            else at = above ? selected[0] : selected[selected.Count - 1] + 1;
            at = Mathf.Clamp(at, 0, _doc.rows.Count);

            MarkStructural();
            _doc.rows.Insert(at, row);
            _list.ClearSelection();
            _list.index = at;
            _list.Select(at, true);
            if (row.kind == VNRowKind.Say) _pendingFocusRow = at;   // 台词行直接进输入状态
            ScrollRowIntoView(at);
            Bump();
        }

        /// <summary>在下一个 Layout 事件里真正插行（改列表长度必须避开其它 IMGUI 事件）</summary>
        void ApplyPendingInsert()
        {
            if (_pendingInsertAt < 0) return;
            int at = Mathf.Clamp(_pendingInsertAt, 0, _doc.rows.Count);
            _pendingInsertAt = -1;

            MarkStructural();
            _doc.rows.Insert(at, NewSayRow());
            _list.ClearSelection();
            _list.index = at;
            _list.Select(at, true);
            _pendingFocusRow = at;
            ScrollRowIntoView(at);
            Bump();
        }

        /// <summary>目标行已经在可视区内就不动滚动条，避免插一行画面就跳一下</summary>
        void ScrollRowIntoView(int index)
        {
            if (index < 0 || index >= _doc.rows.Count) return;
            float top = 0f;
            for (int i = 0; i < index; i++) top += RowHeight(_doc.rows[i]);
            float bottom = top + RowHeight(_doc.rows[index]);
            float viewH = Mathf.Max(120f, position.height - 130f);
            if (top < _scroll.y)
                _pendingScrollY = Mathf.Max(0f, top - 40f);
            else if (bottom > _scroll.y + viewH)
                _pendingScrollY = Mathf.Max(0f, bottom - viewH + 40f);
        }
    }
}
