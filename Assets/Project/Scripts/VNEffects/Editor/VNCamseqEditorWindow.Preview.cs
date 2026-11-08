using System.Collections.Generic;
using System.Globalization;
using System.Text;
using DG.Tweening;
using DG.Tweening.Core.Easing;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects.EditorTools
{
    public partial class VNCamseqEditorWindow
    {
        // ==================================================================
        // 场景内实时预览 / 捕获当前镜头
        // ==================================================================

        RectTransform FindZoomRoot()
        {
            var cam = Object.FindFirstObjectByType<VNCamera>();
            if (cam != null && cam.target != null) return cam.target;
            var go = GameObject.Find("ZoomRoot");
            return go != null ? go.transform as RectTransform : null;
        }

        void StartScenePreview()
        {
            _zoomRoot = FindZoomRoot();
            if (_zoomRoot == null)
            {
                ShowNotification(new GUIContent("场景里找不到 ZoomRoot（先生成剧本演示场景）"));
                return;
            }
            _origPos = _zoomRoot.anchoredPosition;
            _origScale = _zoomRoot.localScale;
            _scenePreviewing = true;
            ApplyStageToScene();
            ApplySceneState();
        }

        void StopScenePreview()
        {
            RestoreStage();
            if (!_scenePreviewing) return;
            _scenePreviewing = false;
            if (_zoomRoot != null)
            {
                _zoomRoot.anchoredPosition = _origPos;
                _zoomRoot.localScale = _origScale;
                EditorApplication.QueuePlayerLoopUpdate();
            }
        }

        // ==================================================================
        // 舞台预览：把绑定行的背景 / 立绘摆进场景，让 Game 视图也是那一行的画面
        // ==================================================================

        /// <summary>
        /// 按绑定行的推算结果摆舞台。背景只换 sprite（原值记下来可还原）；
        /// 立绘造临时 GameObject——**只挂最小的 RectTransform + Image**，
        /// 不带运行时那一堆 fx / blink / mouth 组件（它们的 Awake 在编辑期会乱来）。
        /// 全部标 HideFlags.DontSave：绝不写进场景文件，域重载也会被自动销毁。
        /// </summary>
        void ApplyStageToScene()
        {
            var stage = Object.FindFirstObjectByType<VNStage>();
            var info = RowStage;
            if (stage == null || info == null) return;
            _stagedRow = _linkedRow;

            if (info.backdrop != null && stage.backgroundImage != null)
            {
                if (!_bgTouched)
                {
                    _origBgSprite = stage.backgroundImage.sprite;
                    _bgTouched = true;
                }
                stage.backgroundImage.sprite = info.backdrop;
            }

            ClearPreviewCharacters();
            if (stage.characterLayer == null) return;
            foreach (var c in info.characters)
            {
                if (c.sprite == null) continue;

                float height = 880f;
                Vector2 offset = Vector2.zero;
                var def = stage.characters.Find(d => d != null && d.id == c.id);
                if (def != null)
                {
                    height = stage.characterHeight * Mathf.Max(0.05f, def.sizeScale);
                    offset = def.positionOffset;
                }

                var go = new GameObject($"[camseq预览] {c.id}",
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(Image))
                { hideFlags = HideFlags.DontSave };
                var rect = (RectTransform)go.transform;
                rect.SetParent(stage.characterLayer, false);
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition =
                    new Vector2(SlotX[Mathf.Clamp(c.slot, 0, 2)], -60f) + offset;

                float aspect = c.sprite.rect.width / Mathf.Max(1f, c.sprite.rect.height);
                rect.sizeDelta = new Vector2(height * aspect, height);

                var img = go.GetComponent<Image>();
                img.sprite = c.sprite;
                img.preserveAspect = true;
                img.raycastTarget = false;
                _previewChars.Add(go);
            }
            // 新建的临时立绘 localScale 是 1，补一次当前时刻的倍率，
            // 免得换绑定行重摆舞台的那一下立绘尺寸闪回原大小
            ApplyPreviewCharacterScale(PreviewAtTime(_scrub).state.CharScale);
            EditorApplication.QueuePlayerLoopUpdate();
        }

        void RestoreStage()
        {
            ClearPreviewCharacters();
            _stagedRow = -1;
            if (!_bgTouched) return;

            var stage = Object.FindFirstObjectByType<VNStage>();
            if (stage != null && stage.backgroundImage != null)
                stage.backgroundImage.sprite = _origBgSprite;
            _bgTouched = false;
            _origBgSprite = null;
            EditorApplication.QueuePlayerLoopUpdate();
        }

        void ClearPreviewCharacters()
        {
            foreach (var go in _previewChars)
                if (go != null) DestroyImmediate(go);
            _previewChars.Clear();
        }

        /// <summary>
        /// 把 mode 给的立绘额外倍率写进场景预览的临时立绘。
        /// 运行时这一层是 VNImageEffectController 的运镜通道（改 localScale），
        /// 预览的临时立绘只有裸 RectTransform，所以直接写 localScale——
        /// 不写的话 mode:char 下 ZoomRoot 恒为 1，整个 Game 视图会完全没反应。
        /// </summary>
        void ApplyPreviewCharacterScale(float mult)
        {
            var scale = Vector3.one * Mathf.Max(0.01f, mult);
            foreach (var go in _previewChars)
                if (go != null) go.transform.localScale = scale;
        }

        void ApplySceneState()
        {
            if (_zoomRoot == null)
            {
                StopScenePreview();
                return;
            }
            // 叠化段里 ZoomRoot 直接是目标状态（真实叠化由运行时的截屏覆盖层完成）
            var s = PreviewAtTime(_scrub).state;
            _zoomRoot.localScale = Vector3.one * s.zoom;
            _zoomRoot.anchoredPosition = _origPos + s.offset;
            ApplyPreviewCharacterScale(s.CharScale);
            EditorApplication.QueuePlayerLoopUpdate(); // 编辑态强制刷新 Game 视图
        }

        /// <summary>把 ZoomRoot 当前的实际状态反推成一个路径点（坐标类型）</summary>
        void CaptureCurrentCamera()
        {
            var root = _zoomRoot != null ? _zoomRoot : FindZoomRoot();
            if (root == null)
            {
                ShowNotification(new GUIContent("场景里找不到 ZoomRoot"));
                return;
            }
            // 预览中用记录的基准位；否则假定基准为当前值即无偏移的 (0,0)
            var basePos = _scenePreviewing ? _origPos : Vector2.zero;
            float zoom = Mathf.Max(0.1f, root.localScale.x);
            Vector2 offset = root.anchoredPosition - basePos;
            Vector2 point = -offset / zoom;

            _points.Add(new Waypoint
            {
                type = PointType.Coords,
                coords = Round(point),
                zoom = Mathf.Clamp(zoom, 0.5f, 3f),
                duration = 0.8f,
            });
            _list.index = _points.Count - 1;
            ShowNotification(new GUIContent($"已捕获：({point.x:0},{point.y:0}) ×{zoom:0.##}"));
        }

        // ==================================================================
        // 预设库
        // ==================================================================

        VNCamseqPresetLibrary EnsureLibrary()
        {
            if (_library != null) return _library;
            _library = LoadOrCreateLibrary();
            return _library;
        }

        static VNCamseqPresetLibrary LoadOrCreateLibrary()
        {
            var library = AssetDatabase.LoadAssetAtPath<VNCamseqPresetLibrary>(LibraryPath);
            if (library != null) return library;

            string folder = System.IO.Path.GetDirectoryName(LibraryPath).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder("Assets", "VNEffects");
            library = CreateInstance<VNCamseqPresetLibrary>();
            AssetDatabase.CreateAsset(library, LibraryPath);
            AssetDatabase.SaveAssets();
            return library;
        }

        /// <summary>存一条预设（剧本编辑器的「把本行存为预设」也走这里，同名覆盖）</summary>
        public static void SavePreset(string name, string camseqText)
        {
            if (string.IsNullOrEmpty(name)) return;
            var library = LoadOrCreateLibrary();
            var existing = library.presets.Find(p => p != null && p.name == name);
            if (existing != null) existing.camseqText = camseqText;
            else library.presets.Add(new VNCamseqPresetLibrary.Preset
                { name = name, camseqText = camseqText });
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
        }

        /// <summary>预设菜单：已存预设点一下就载入，下面两项管存/删（原来平铺 5 个控件占了半行）</summary>
        void ShowPresetMenu(Rect rect)
        {
            _library = AssetDatabase.LoadAssetAtPath<VNCamseqPresetLibrary>(LibraryPath);
            var menu = new GenericMenu();

            int count = _library != null ? _library.presets.Count : 0;
            if (count == 0)
                menu.AddDisabledItem(new GUIContent("（还没有预设）"));
            else
                for (int i = 0; i < count; i++)
                {
                    int index = i;
                    menu.AddItem(new GUIContent(MenuSafe(_library.presets[i].name)), false,
                        () => LoadPresetAt(index));
                }

            menu.AddSeparator("");
            menu.AddItem(new GUIContent("存为预设…"), false, () =>
            {
                if (_points.Count == 0)
                {
                    ShowNotification(new GUIContent("没有路径点可保存"));
                    return;
                }
                // 名字走独立小窗口：GenericMenu 的回调不在 OnGUI 里，
                // 这里调 PopupWindow.Show 会报 "GUI functions outside OnGUI"
                VNCamseqNamePopup.Open($"预设{System.DateTime.Now:HHmmss}", SavePresetNamed);
            });
            for (int i = 0; i < count; i++)
            {
                int index = i;
                menu.AddItem(new GUIContent("删除/" + MenuSafe(_library.presets[i].name)), false,
                    () => DeletePresetAt(index));
            }

            menu.DropDown(rect);
        }

        void SavePresetNamed(string name)
        {
            name = string.IsNullOrEmpty(name) ? "" : name.Trim();
            if (name.Length == 0 || _points.Count == 0) return;
            SavePreset(name, GenerateText());
            _library = EnsureLibrary();
            ShowNotification(new GUIContent($"已保存预设「{name}」"));
            Repaint();
        }

        /// <summary>内置运镜模板菜单：套用 = 整段替换当前编排（角色占位按场景里的第一个角色填）</summary>
        void ShowTemplateMenu(Rect rect)
        {
            var ids = CharacterIds();
            string character = ids.Length > 0 ? ids[0] : null;
            var menu = new GenericMenu();
            foreach (var entry in VNCamseqTemplates.All)
            {
                string text = VNCamseqTemplates.Resolve(entry.text, character);
                menu.AddItem(new GUIContent(entry.name), false, () =>
                {
                    CommitUndo();      // 整段替换，撤销时要能一步回到套用前
                    _pasteText = text;
                    ParseText();
                });
            }
            menu.DropDown(rect);
        }

        void LoadPresetAt(int index)
        {
            if (_library == null || index < 0 || index >= _library.presets.Count) return;
            CommitUndo();          // 整段替换，撤销时要能一步回到载入前
            _pasteText = _library.presets[index].camseqText;
            ParseText();
            Repaint();
        }

        void DeletePresetAt(int index)
        {
            if (_library == null || index < 0 || index >= _library.presets.Count) return;
            string name = _library.presets[index].name;
            if (!EditorUtility.DisplayDialog("删除预设", $"删除「{name}」？", "删除", "取消")) return;
            _library.presets.RemoveAt(index);
            EditorUtility.SetDirty(_library);
            AssetDatabase.SaveAssets();
            Repaint();
        }

        static Vector2 Round(Vector2 v) =>
            new Vector2(Mathf.Round(v.x), Mathf.Round(v.y));

        // 画布坐标（中心原点，y 向上）↔ GUI 像素
        static Vector2 CanvasToGui(Rect rect, Vector2 canvas)
        {
            return new Vector2(
                rect.x + (canvas.x + CanvasHalf.x) / (CanvasHalf.x * 2f) * rect.width,
                rect.y + (1f - (canvas.y + CanvasHalf.y) / (CanvasHalf.y * 2f)) * rect.height);
        }

        static Vector2 GuiToCanvas(Rect rect, Vector2 gui)
        {
            return new Vector2(
                (gui.x - rect.x) / rect.width * CanvasHalf.x * 2f - CanvasHalf.x,
                (1f - (gui.y - rect.y) / rect.height) * CanvasHalf.y * 2f - CanvasHalf.y);
        }

        void DrawCanvasFrame(Rect rect, Vector2 center, Vector2 half, Color color, float thickness)
            => DrawRectOutline(FrameGuiRect(rect, center, half), color, thickness);

        /// <summary>取景框（画布坐标的中心 + 半尺寸）在 GUI 里占的矩形</summary>
        static Rect FrameGuiRect(Rect canvasRect, Vector2 center, Vector2 half)
        {
            var tl = CanvasToGui(canvasRect, new Vector2(center.x - half.x, center.y + half.y));
            var br = CanvasToGui(canvasRect, new Vector2(center.x + half.x, center.y - half.y));
            return Rect.MinMaxRect(tl.x, tl.y, br.x, br.y);
        }

        static void DrawRectOutline(Rect r, Color c, float t)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, t), c);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - t, r.width, t), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y, t, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax - t, r.y, t, r.height), c);
        }

        void DrawDottedLine(Rect rect, Vector2 fromCanvas, Vector2 toCanvas, Color color)
        {
            const int dots = 24;
            for (int i = 0; i <= dots; i++)
            {
                var p = CanvasToGui(rect, Vector2.Lerp(fromCanvas, toCanvas, i / (float)dots));
                EditorGUI.DrawRect(new Rect(p.x - 1f, p.y - 1f, 2f, 2f), color);
            }
        }
    }
}
