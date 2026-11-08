using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace VNEffects
{
    public partial class VNScriptRunner
    {
        // ------------------------------------------------------------------
        // 存档 / 读档
        // ------------------------------------------------------------------

        public void SaveTo(int slot)
        {
            SaveTo(slot, null);
        }

        public void SaveTo(int slot, Texture2D thumbnail) => SaveTo(slot, thumbnail, false);

        public void SaveTo(int slot, Texture2D thumbnail, bool quick)
        {
            if (!_waitingAtSay)
            {
                VNToast.Show(VNLocale.T("runner.cannotSaveNow"));
                return;
            }
            var data = new VNSaveData
            {
                commandIndex = _currentSayIndex,
                chapter = script != null ? script.name : null,
                lastLine = _lastSayText,
            };
            CaptureCallStack(data);
            stage.CaptureSnapshot(data);
            // 只存锁定隐藏：普通隐藏玩家一碰就还原，存了反而让读档后界面莫名消失
            data.uiHidden = _uiHideLocked ? VNUiPartsUtil.ToToken(_uiHiddenParts) : "";
            VNSaveSystem.Save(slot, data, thumbnail);
            VNToast.Show(quick ? VNLocale.T("runner.quickSaved")
                               : VNLocale.T("runner.saved", slot));
        }

        public void LoadFrom(int slot) => LoadFrom(slot, false);

        public void LoadFrom(int slot, bool quick)
        {
            var data = VNSaveSystem.Load(slot);
            if (data == null)
            {
                VNToast.Show(quick ? VNLocale.T("runner.noQuickSave")
                                   : VNLocale.T("runner.slotEmpty", slot));
                return;
            }
            SetSkip(false);
            SetAuto(false);
            Stop();
            if (!string.IsNullOrEmpty(data.chapter) && !SwitchChapter(data.chapter, 0))
            {
                Debug.LogError($"[VNSave] 当前章节「{data.chapter}」无法恢复，已中止读档");
                return;
            }
            RestoreCallStack(data); // 旧存档字段为空，自动得到空栈
            stage.RestoreSnapshot(data);
            RestoreUiHidden(data);  // 旧存档字段为空 = 界面全开，语义正确
            VNToast.Show(quick ? VNLocale.T("runner.quickLoaded")
                               : VNLocale.T("runner.loaded", slot));
            ResumeAt(data.commandIndex);
        }

        // ------------------------------------------------------------------
        // 快速存读档（Q / L，专用槽 0，不在 20 槽面板里显示）
        // ------------------------------------------------------------------

        /// <summary>快速存档专用槽（面板网格只显示 1..SlotCount，0 不可见）</summary>
        public const int QuickSaveSlot = 0;

        Coroutine _quickSaveCo;

        /// <summary>Q 键 / 快存按钮：同 F5 的截图管线，但直接落盘不开面板不暂停。</summary>
        public void QuickSave()
        {
            if (!_waitingAtSay)
            {
                VNToast.Show(VNLocale.T("runner.cannotSaveNow"));
                return;
            }
            if (_quickSaveCo != null) return; // 截图进行中，忽略连按
            _quickSaveCo = StartCoroutine(QuickSaveCo());
        }

        /// <summary>L 键 / 快读按钮：读取快速存档；没有则提示。</summary>
        public void QuickLoad()
        {
            CancelSaveCapture();
            LoadFrom(QuickSaveSlot, true);
        }

        IEnumerator QuickSaveCo()
        {
            var capture = stage != null && stage.vnCamera != null
                ? stage.vnCamera.cameraFade : null;
            if (capture == null) capture = FindFirstObjectByType<VNCameraFade>();
            if (capture == null)
                capture = new GameObject("SaveThumbnailCapture").AddComponent<VNCameraFade>();

            Texture2D thumbnail = null;
            yield return capture.CaptureThumbnailCo(320, 180, texture => thumbnail = texture);
            _quickSaveCo = null;

            if (!_waitingAtSay) // 截图那一两帧里演出推进了：作废，避免存到不可恢复点
            {
                if (thumbnail != null) Destroy(thumbnail);
                yield break;
            }
            SaveTo(QuickSaveSlot, thumbnail, true);
            if (thumbnail != null) Destroy(thumbnail); // PNG 已落盘，纹理即可释放
        }

        void EnsureSaveLoadPanel()
        {
            if (_saveLoadPanel == null)
            {
                _saveLoadPanel = FindFirstObjectByType<VNSaveLoadPanel>();
                if (_saveLoadPanel == null)
                    _saveLoadPanel = new GameObject("VNSaveLoadPanel").AddComponent<VNSaveLoadPanel>();
            }
            _saveLoadPanel.Initialize(this);
        }

        void EnsureQuickToolbar()
        {
            if (stage == null || stage.dialogue == null) return;
            if (_quickToolbar == null)
            {
                _quickToolbar = stage.dialogue.GetComponent<VNQuickToolbar>();
                if (_quickToolbar == null)
                    _quickToolbar = stage.dialogue.gameObject.AddComponent<VNQuickToolbar>();
            }
            _quickToolbar.Initialize(this);
        }

        void EnsureConfigPanel()
        {
            if (_configPanel == null)
            {
                _configPanel = FindFirstObjectByType<VNConfigPanel>();
                if (_configPanel == null)
                    _configPanel = new GameObject("VNConfigPanel").AddComponent<VNConfigPanel>();
            }
            _configPanel.Initialize(this, stage);
        }

        /// <summary>F5 / 保存页签入口：先隐藏 UI 并截取游戏画面，再显示 20 槽网格。</summary>
        public void RequestSavePanel()
        {
            if (!_waitingAtSay)
            {
                VNToast.Show(VNLocale.T("runner.cannotSaveNow"));
                return;
            }
            EnsureSaveLoadPanel();
            PauseForSaveLoadMenu();
            _saveLoadPanel.PrepareForSaveCapture();

            if (_saveCaptureCo != null) StopCoroutine(_saveCaptureCo);
            int token = ++_saveCaptureToken;
            _saveCaptureCo = StartCoroutine(CaptureSaveThumbnailCo(token));
        }

        /// <summary>F9 / 读取页签入口。</summary>
        public void RequestLoadPanel()
        {
            EnsureSaveLoadPanel();
            CancelSaveCapture();
            PauseForSaveLoadMenu();
            _saveLoadPanel.OpenLoad();
        }

        public void RequestBacklog()
        {
            if (_backlog == null) return;
            _backlog.Open();
        }

        public void RequestQuestLog()
        {
            if (_questLog == null || _eventActive) return;
            _questLog.Toggle();
        }

        /// <summary>日记本（D 键）。事件进行中不开，同其他面板。</summary>
        public void RequestDiary()
        {
            if (_eventActive) return;
            if (_diaryPanel == null)
            {
                _diaryPanel = FindFirstObjectByType<VNAiDiaryPanel>();
                if (_diaryPanel == null)   // 没人手工摆也能用，同任务日志
                    _diaryPanel = new GameObject("VNAiDiaryPanel")
                        .AddComponent<VNAiDiaryPanel>();
            }
            _diaryPanel.Toggle();
        }

        public void RequestStatsPanel()
        {
            if (_statsHud == null || _eventActive) return;
            _statsHud.Toggle();
        }

        public void RequestInventory()
        {
            if (_inventory == null || _eventActive) return;
            _inventory.Toggle();
        }

        public void RequestCgGallery()
        {
            if (_cgGallery == null || _eventActive) return;
            _cgGallery.Toggle();
        }

        public void RequestConfigPanel()
        {
            EnsureConfigPanel();
            PauseForSaveLoadMenu();
            _configPanel.Open();
        }

        /// <summary>整块界面隐藏/恢复（右键、快捷条的「隐藏UI」按钮）。不锁定，玩家一碰就还原。</summary>
        public void SetInterfaceHidden(bool hidden) => SetUiHidden(VNUiParts.All, hidden, false);

        /// <summary>
        /// 按部件隐藏/恢复界面。
        /// locked = 剧本 hideHUD keep：隐藏后玩家点击只推进台词、不弹回界面，
        /// 要等剧本写 hideHUD off（或读到一个没锁定的存档）才恢复。
        /// 部件留空按全部处理，这样老剧本里光秃秃的一行 hideHUD 语义不变。
        /// </summary>
        public void SetUiHidden(VNUiParts parts, bool hidden, bool locked)
        {
            if (parts == VNUiParts.None) parts = VNUiParts.All;
            if (hidden)
            {
                _uiHiddenParts |= parts;
                // 锁定是整体状态而不是按部件的：混着写时以最后一条为准，
                // 不然「keep 藏属性栏 + 普通藏对话框」要不要拦输入根本说不清。
                _uiHideLocked = locked;
            }
            else
            {
                _uiHiddenParts &= ~parts;
                if (_uiHiddenParts == VNUiParts.None) _uiHideLocked = false;
            }
            ApplyUiHidden();
        }

        /// <summary>把当前的隐藏状态写到各 UI 上（事件进行中时养成 HUD 一律不显示）</summary>
        void ApplyUiHidden()
        {
            // 偷拍模式期间整套界面临时藏起来，但**不写进 _uiHiddenParts**：
            // 退出时按剧本/玩家原本的隐藏状态还原（hideHUD keep 的段落不能被它弹回 UI）
            if (stage != null && stage.dialogue != null)
                stage.dialogue.SetInterfaceVisible(
                    !_secretPhotoActive && (_uiHiddenParts & VNUiParts.Dialogue) == 0);
            ApplyGameplayHudVisible(!_eventActive && !_secretPhotoActive);
        }
    }
}
