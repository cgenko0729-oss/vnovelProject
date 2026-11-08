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
        // 数据源
        // ------------------------------------------------------------------

        void RefreshSources()
        {
            var ids = new List<string>();
            _ctx.expressions.Clear();
            _characterPreviews.Clear();
            _assetIdCache.Clear();   // event 各模块的资产 id 候选，跟着一起重扫
            foreach (var guid in AssetDatabase.FindAssets("t:VNCharacterDef"))
            {
                var def = AssetDatabase.LoadAssetAtPath<VNCharacterDef>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (def == null || string.IsNullOrEmpty(def.id)) continue;
                ids.Add(def.id);
                var exprs = new List<string>();
                var previews = new List<SpritePreviewItem>();
                foreach (var e in def.expressions)
                {
                    if (string.IsNullOrEmpty(e.name)) continue;
                    exprs.Add(e.name);
                    previews.Add(new SpritePreviewItem(e.name, e.sprite));
                }
                _ctx.expressions[def.id] = exprs.ToArray();
                _characterPreviews.Add(new CharacterPreviewItem(def.id, previews));
            }
            _ctx.characterIds = ids.ToArray();

            // ★ 素材库按**与运行时同一套覆盖语义**取数：VNGameConfig 里填了就用它的，
            //   留空才回退场景组件。以前这里只读场景上的 VNStage / VNAudio，于是
            //   在 VNGameConfig（或素材浏览器）里新登记的素材**在下拉里根本搜不到** ——
            //   而项目的铁律恰恰是「配置进资产，不进场景」，两边对不上。
            var stage = FindFirstObjectByType<VNStage>();
            var audio = FindFirstObjectByType<VNAudio>();
            var config = LoadGameConfig();

            _backgroundPreviews.Clear();
            _cgPreviews.Clear();

            var bgSrc = PickLibrary(config != null ? config.backgrounds : null,
                                    stage != null ? stage.backgrounds : null);
            if (bgSrc != null)
            {
                var bgs = new List<string>();
                foreach (var b in bgSrc)
                {
                    if (b == null || string.IsNullOrEmpty(b.id)) continue;
                    bgs.Add(b.id);
                    _backgroundPreviews.Add(new SpritePreviewItem(b.id, b.sprite));
                }
                _ctx.backgroundIds = bgs.ToArray();
            }
            else _ctx.backgroundIds = System.Array.Empty<string>();

            var cgSrc = PickLibrary(config != null ? config.cgLibrary : null,
                                    stage != null ? stage.cgLibrary : null);
            if (cgSrc != null)
            {
                var cgs = new List<string>();
                foreach (var c in cgSrc)
                {
                    if (c == null || string.IsNullOrEmpty(c.id)) continue;
                    cgs.Add(c.id);
                    _cgPreviews.Add(new SpritePreviewItem(c.id, c.sprite));
                }
                _ctx.cgIds = cgs.ToArray();
            }
            else _ctx.cgIds = System.Array.Empty<string>();

            var bgmSrc = PickLibrary(config != null ? config.bgmLibrary : null,
                                     audio != null ? audio.bgmLibrary : null);
            var seSrc = PickLibrary(config != null ? config.seLibrary : null,
                                    audio != null ? audio.seLibrary : null);
            var voiceSrc = PickLibrary(config != null ? config.voiceLibrary : null,
                                       audio != null ? audio.voiceLibrary : null);
            if (bgmSrc != null || seSrc != null || voiceSrc != null)
            {
                // 旧混合库的条目三个通道都能用，因此并入每个候选列表
                var legacy = audio != null ? audio.library : null;
                _ctx.bgmIds = CollectAudioIds(bgmSrc, legacy, _bgmClips);
                _ctx.seIds = CollectAudioIds(seSrc, legacy, _seClips);
                _ctx.voiceIds = CollectAudioIds(voiceSrc, legacy, _voiceClips);
            }
            else
            {
                _ctx.bgmIds = System.Array.Empty<string>();
                _ctx.seIds = System.Array.Empty<string>();
                _ctx.voiceIds = System.Array.Empty<string>();
                _bgmClips.Clear();
                _seClips.Clear();
                _voiceClips.Clear();
            }

            var eventRegistry = FindFirstObjectByType<VNEventRegistry>();
            _ctx.eventIds = eventRegistry != null
                ? new List<string>(eventRegistry.Ids).ToArray()
                : System.Array.Empty<string>();

            var questIds = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:VNQuestDef"))
            {
                var quest = AssetDatabase.LoadAssetAtPath<VNQuestDef>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (quest != null && !string.IsNullOrEmpty(quest.id))
                    questIds.Add(quest.id);
            }
            _ctx.questIds = questIds.ToArray();

            // 天气候选：内置叶型正名 + 雨雪萤火虫 + None，再补上项目里的 VNWeatherDef 资产 id。
            // （中文别名 落樱/枫叶/… 剧本里照样能写，只是不塞进下拉免得列表太长）
            var weatherIds = new List<string> { "None" };
            foreach (VNLeafShape shape in System.Enum.GetValues(typeof(VNLeafShape)))
                weatherIds.Add(VNWeatherDef.DefaultId(shape));
            weatherIds.Add(VNWeather.Rain.ToString());
            weatherIds.Add(VNWeather.Snow.ToString());
            weatherIds.Add(VNWeather.Fireflies.ToString());
            foreach (var guid in AssetDatabase.FindAssets("t:VNWeatherDef"))
            {
                var wd = AssetDatabase.LoadAssetAtPath<VNWeatherDef>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (wd != null && !string.IsNullOrEmpty(wd.id) && !weatherIds.Contains(wd.id))
                    weatherIds.Add(wd.id);
            }
            _ctx.weatherIds = weatherIds.ToArray();

            // 过场候选：VNGameConfig 过场库里登记的资产（id 留空按文件名，与运行时 FindInterlude 一致）
            var interludeIds = new List<string>();
            var gameConfig = LoadGameConfig();
            if (gameConfig != null && gameConfig.interludes != null)
            {
                foreach (var def in gameConfig.interludes)
                {
                    if (def == null) continue;
                    string id = string.IsNullOrEmpty(def.id) ? def.name : def.id;
                    if (!string.IsNullOrEmpty(id) && !interludeIds.Contains(id)) interludeIds.Add(id);
                }
            }
            _ctx.interludeIds = interludeIds.ToArray();

            // 教程候选：VNGameConfig 教程库（id 留空按文件名，与运行时 VNTutorialPlayer.Find 一致）
            var tutorialIds = new List<string>();
            if (gameConfig != null && gameConfig.tutorials != null)
            {
                foreach (var def in gameConfig.tutorials)
                {
                    if (def == null) continue;
                    string id = string.IsNullOrEmpty(def.id) ? def.name : def.id;
                    if (!string.IsNullOrEmpty(id) && !tutorialIds.Contains(id)) tutorialIds.Add(id);
                }
            }
            _ctx.tutorialIds = tutorialIds.ToArray();

            // UI 皮肤候选：VNGameConfig 登记的 id（default 由 OptionsFor 统一补在最前）
            var dialogueSkins = new List<string>();
            var choiceSkins = new List<string>();
            var uiCfg = VNGameConfig.Active;
            if (uiCfg != null)
            {
                foreach (var e in uiCfg.dialogueSkins)
                    if (e != null && !string.IsNullOrEmpty(e.id)) dialogueSkins.Add(e.id);
                foreach (var e in uiCfg.choiceSkins)
                    if (e != null && !string.IsNullOrEmpty(e.id)) choiceSkins.Add(e.id);
            }
            _ctx.dialogueSkinIds = dialogueSkins.ToArray();
            _ctx.choiceSkinIds = choiceSkins.ToArray();

            _ctx.scenarioLabels.Clear();
            _ctx.scenarioPaths.Clear();
            var qualifiedLabels = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets(
                         "t:TextAsset", new[] { "Assets/Scenarios" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".vn.txt", System.StringComparison.OrdinalIgnoreCase))
                    continue;
                var scenario = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                if (scenario == null) continue;
                string file = Path.GetFileName(path);
                string normalized = VNStoryAddress.NormalizeFile(file);
                var labels = VNScenarioDoc.Parse(scenario.text).CollectLabels().ToArray();
                _ctx.scenarioLabels[normalized] = labels;
                _ctx.scenarioPaths[normalized] = path;
                string displayFile = VNStoryAddress.NormalizeFile(file);
                foreach (string label in labels)
                    qualifiedLabels.Add(displayFile + "::" + label);
            }
            qualifiedLabels.Sort();
            _ctx.qualifiedLabelIds = qualifiedLabels.ToArray();

            _validatedVersion = -1; // 数据源变了要重新校验
        }

        /// <summary>通道专属库 + 旧混合库合并去重后的 id 列表（保持登记顺序）。
        /// clips 非空时同步填充 id → AudioClip 映射（行内试听用）。</summary>
        /// <summary>
        /// 编辑期取 VNGameConfig 资产（不走 VNGameConfig.Active 的运行时缓存，
        /// 免得受 Play Mode 进出清缓存的时机影响）。
        /// </summary>
        static VNGameConfig LoadGameConfig()
        {
            var cfg = AssetDatabase.LoadAssetAtPath<VNGameConfig>(VNGameConfig.AssetPath);
            return cfg != null ? cfg : Resources.Load<VNGameConfig>(VNGameConfig.ResourcesName);
        }

        /// <summary>
        /// 覆盖语义的取数：config 侧非空就用它，否则回退场景组件侧。
        /// 与 VNGameConfig.ApplyList 的运行时规则保持一致（填了才覆盖，留空不动）。
        /// </summary>
        static List<T> PickLibrary<T>(List<T> fromConfig, List<T> fromScene)
        {
            if (fromConfig != null && fromConfig.Count > 0) return fromConfig;
            return fromScene;
        }

        static string[] CollectAudioIds(List<VNAudio.AudioEntry> channelLib,
            List<VNAudio.AudioEntry> legacyLib, Dictionary<string, AudioClip> clips = null)
        {
            clips?.Clear();
            var ids = new List<string>();
            var seen = new HashSet<string>();
            foreach (var lib in new[] { channelLib, legacyLib })
            {
                if (lib == null) continue;
                foreach (var e in lib)
                    if (e != null && !string.IsNullOrEmpty(e.id) && seen.Add(e.id))
                    {
                        ids.Add(e.id);
                        if (clips != null && e.clip != null) clips[e.id] = e.clip;
                    }
            }
            return ids.ToArray();
        }

        void ValidateIfNeeded()
        {
            if (_validatedVersion == _version) return;
            _validatedVersion = _version;
            _issues = _doc.Validate(_ctx);
            _rowHasError.Clear();
            foreach (var issue in _issues)
            {
                if (issue.isError) _rowHasError[issue.rowIndex] = true;
                else if (!_rowHasError.ContainsKey(issue.rowIndex))
                    _rowHasError[issue.rowIndex] = false;
            }
            _labels = _doc.CollectLabels();
            _flags = _doc.CollectFlags();
            var ops = new List<string>();
            foreach (var f in _flags) { ops.Add(f + "+1"); ops.Add(f + "-1"); ops.Add(f); }
            _flagOps = ops.ToArray();
        }

        // ------------------------------------------------------------------
        // 文件
        // ------------------------------------------------------------------

        void OpenFile()
        {
            if (_dirty && !EditorUtility.DisplayDialog("Unsaved changes",
                    "Discard unsaved changes?", "Discard", "Cancel")) return;
            string dir = Path.Combine(Application.dataPath, "Scenarios");
            if (!Directory.Exists(dir)) dir = Application.dataPath;
            string p = EditorUtility.OpenFilePanel("Open scenario (.vn.txt)", dir, "txt");
            if (string.IsNullOrEmpty(p)) return;
            LoadFile(p);
        }

        void LoadFile(string absolutePath)
        {
            _path = absolutePath;
            _doc = VNScenarioDoc.Parse(File.ReadAllText(absolutePath));
            _fileTime = File.GetLastWriteTimeUtc(absolutePath);
            _dirty = false;
            _externalChanged = false;
            _undoStack.Clear();
            _redoStack.Clear();
            RebindList();
            Bump();
        }

        void SaveFile(bool saveAs)
        {
            string p = _path;
            if (saveAs || string.IsNullOrEmpty(p))
            {
                string dir = Path.Combine(Application.dataPath, "Scenarios");
                if (!Directory.Exists(dir)) dir = Application.dataPath;
                p = EditorUtility.SaveFilePanel("Save scenario", dir, "NewScenario.vn", "txt");
                if (string.IsNullOrEmpty(p)) return;
            }
            File.WriteAllText(p, _doc.GenerateText(), new UTF8Encoding(false));
            _path = p;
            _fileTime = File.GetLastWriteTimeUtc(p);
            _dirty = false;
            _externalChanged = false;

            // 项目内文件刷新导入
            string assets = Application.dataPath.Replace('\\', '/');
            string norm = p.Replace('\\', '/');
            if (norm.StartsWith(assets))
                AssetDatabase.ImportAsset("Assets" + norm.Substring(assets.Length));
            ShowNotification(new GUIContent("Saved"));
        }

        void CheckExternalChange()
        {
            if (string.IsNullOrEmpty(_path) || !File.Exists(_path)) return;
            if (File.GetLastWriteTimeUtc(_path) == _fileTime) return;
            if (!_dirty)
            {
                LoadFile(_path); // 没有本地改动 → 静默重载
                ShowNotification(new GUIContent("Reloaded (changed on disk)"));
            }
            else _externalChanged = true;
        }

        // ------------------------------------------------------------------
        // 撤销
        // ------------------------------------------------------------------

        void Bump() { _version++; _dirty = true; }

        void PushUndo(string snapshot)
        {
            if (string.IsNullOrEmpty(snapshot)) return;
            if (_undoStack.Count > 0 && _undoStack[_undoStack.Count - 1] == snapshot) return;
            _undoStack.Add(snapshot);
            if (_undoStack.Count > 100) _undoStack.RemoveAt(0);
            _redoStack.Clear();
        }

        void MarkStructural() => PushUndo(_frameSnapshot);

        void LoadFromText(string text)
        {
            _doc = VNScenarioDoc.Parse(text);
            // 行对象整批换掉了，按旧行引用记的编辑状态全部作废
            _popupResults.Clear();
            _customEdit.Clear();
            _pendingNewRow = null;
            RebindList();
            Bump();
        }

        void HandleUndoKeys()
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown || !(e.control || e.command)) return;
            if (EditorGUIUtility.editingTextField) return; // 文本框内用系统自带撤销

            if (e.keyCode == KeyCode.Z && _undoStack.Count > 0)
            {
                _redoStack.Add(_doc.GenerateText());
                string s = _undoStack[_undoStack.Count - 1];
                _undoStack.RemoveAt(_undoStack.Count - 1);
                LoadFromText(s);
                e.Use();
            }
            else if (e.keyCode == KeyCode.Y && _redoStack.Count > 0)
            {
                _undoStack.Add(_doc.GenerateText());
                string s = _redoStack[_redoStack.Count - 1];
                _redoStack.RemoveAt(_redoStack.Count - 1);
                LoadFromText(s);
                e.Use();
            }
        }
    }
}
