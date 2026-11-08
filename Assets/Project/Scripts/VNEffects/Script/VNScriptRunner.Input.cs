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
        // 秘密偷拍模式（VNSecretPhotoMode 回调用）
        // ------------------------------------------------------------------

        public VNStatsHud StatsHud => _statsHud;

        /// <summary>右上角相机图标此刻该不该显示：没有别的面板/事件/标题盖着</summary>
        public bool IsSecretPhotoIconAllowed()
        {
            if (!_running || _eventActive) return false;
            if (_titleMenu != null && _titleMenu.IsOpen) return false;
            if (stage != null && stage.IsSnsOpen) return false;
            if (_uiHidden && !_uiHideLocked) return false; // 右键藏 UI 期间图标也一起藏
            if (_configPanel != null && _configPanel.IsOpen) return false;
            if (_saveLoadPanel != null && _saveLoadPanel.IsOpen) return false;
            if (_backlog != null && _backlog.IsOpen) return false;
            if (_questLog != null && _questLog.IsOpen) return false;
            if (_diaryPanel != null && _diaryPanel.IsOpen) return false;
            if (_statsHud != null && _statsHud.IsOpen) return false;
            if (_inventory != null && _inventory.IsOpen) return false;
            if (_cgGallery != null && _cgGallery.IsOpen) return false;
            return true;
        }

        /// <summary>能不能进偷拍模式：图标条件 + 停在台词上（与存档同一条规则）+ 没被教程冻住</summary>
        public bool CanOpenSecretPhoto() =>
            IsSecretPhotoIconAllowed() && _waitingAtSay && !VNPause.IsPaused;

        /// <summary>偷拍模式进/出：临时隐藏对话框与 HUD，退出按原状态还原</summary>
        public void SetSecretPhotoActive(bool active)
        {
            if (_secretPhotoActive == active) return;
            _secretPhotoActive = active;
            ApplyUiHidden();
        }

        /// <summary>
        /// 剧本之外插一句话（偷拍被发现时她的反应）：直接写对话框 + 进回想，
        /// 不走 RunInlineCo——那会嵌套一层 SayCo 把 _waitingAtSay 清掉。
        /// 玩家点一下就推进原剧本的下一句。
        /// </summary>
        public void SayOutOfScript(string speakerId, string text)
        {
            if (stage == null || string.IsNullOrEmpty(text)) return;
            stage.Say(speakerId, null, text);
            _backlog?.Record(stage.GetDisplayName(speakerId), text);
        }

        /// <summary>
        /// 常驻养成 HUD（属性条 / 日历）的显隐。事件模块期间一律藏掉（allowed = false）——
        /// 小游戏自己会画满屏 UI，顶上再压一条属性条只是噪音。
        /// 事件结束时按 <see cref="_uiHiddenParts"/> 恢复，不会把玩家/剧本藏起来的又翻出来。
        /// </summary>
        void ApplyGameplayHudVisible(bool allowed)
        {
            _statsHud?.SetHudVisible(allowed && (_uiHiddenParts & VNUiParts.Stats) == 0);
            _calendarHud?.SetVisible(allowed && (_uiHiddenParts & VNUiParts.Calendar) == 0);
        }

        /// <summary>hideHUD [off] [keep] [dialogue|stats|calendar|all]… 的参数解析（运行与调试重建共用）</summary>
        static void ParseHideHudArgs(VNScriptCommand cmd, out VNUiParts parts,
                                     out bool hide, out bool locked)
        {
            parts = VNUiParts.None;
            hide = true;
            locked = false;
            foreach (var token in cmd.args)
            {
                if (string.IsNullOrEmpty(token)) continue;
                string lower = token.Trim().ToLowerInvariant();
                if (lower == "off" || token == "显示" || token == "恢复") { hide = false; continue; }
                if (lower == "keep" || lower == "lock" || token == "保持" || token == "锁定")
                {
                    locked = true;
                    continue;
                }
                var part = VNUiPartsUtil.Parse(token);
                if (part != VNUiParts.None) { parts |= part; continue; }
                Debug.LogWarning($"[VNScript] 第 {cmd.line} 行：hideHUD 不认识的参数「{token}」" +
                                 "（可用 off / keep / dialogue / stats / calendar / all）");
            }
        }

        /// <summary>读档 / 调试重建：把存档里的锁定隐藏状态放回去</summary>
        void RestoreUiHidden(VNSaveData data)
        {
            // 存档里只会有锁定的隐藏（非锁定的一碰就还原，是瞬态不存），
            // 所以取回来非空就一定是锁定态。
            _uiHiddenParts = VNUiPartsUtil.FromToken(data.uiHidden);
            _uiHideLocked = _uiHiddenParts != VNUiParts.None;
            ApplyUiHidden();
        }

        IEnumerator CaptureSaveThumbnailCo(int token)
        {
            VNCameraFade capture = stage != null && stage.vnCamera != null
                ? stage.vnCamera.cameraFade : null;
            if (capture == null) capture = FindFirstObjectByType<VNCameraFade>();
            if (capture == null)
                capture = new GameObject("SaveThumbnailCapture").AddComponent<VNCameraFade>();

            Texture2D thumbnail = null;
            yield return capture.CaptureThumbnailCo(320, 180, texture => thumbnail = texture);
            _saveCaptureCo = null;
            if (token != _saveCaptureToken || _saveLoadPanel == null || !_menuPaused)
            {
                if (thumbnail != null) Destroy(thumbnail);
                yield break;
            }
            _saveLoadPanel.OpenSave(thumbnail);
        }

        void PauseForSaveLoadMenu()
        {
            if (_menuPaused) return;
            _menuPaused = true;
            _timeScaleBeforeMenu = Time.timeScale;
            Time.timeScale = 0f;
            if (_auto) SetAuto(false);
            if (_skip) SetSkip(false);
        }

        public void OnSaveLoadPanelClosed()
        {
            CancelSaveCapture();
            if (!_menuPaused) return;
            Time.timeScale = _timeScaleBeforeMenu;
            _menuPaused = false;
        }

        public void OnConfigPanelClosed() => OnSaveLoadPanelClosed();

        void CancelSaveCapture()
        {
            _saveCaptureToken++;
            if (_saveCaptureCo == null) return;
            StopCoroutine(_saveCaptureCo);
            _saveCaptureCo = null;
        }

        public void LoadFromPanel(int slot)
        {
            _saveLoadPanel?.Close();
            LoadFrom(slot);
        }

        // ------------------------------------------------------------------
        // 模式
        // ------------------------------------------------------------------

        public void SetAuto(bool on)
        {
            _auto = on;
            if (on) SetSkip(false);
            UpdateModeLabel();
            VNToast.Show(VNLocale.T(on ? "runner.autoOn" : "runner.autoOff"));
        }

        public void SetSkip(bool on)
        {
            if (_skip == on) return;
            _skip = on;
            if (on) _auto = false;
            DOTween.timeScale = on ? skipTimeScale : 1f;
            UpdateModeLabel();
            VNToast.Show(VNLocale.T(on ? "runner.skipOn" : "runner.skipOff"));
        }

        void UpdateModeLabel() =>
            VNToast.SetMode(_skip ? "SKIP ▶▶" : _auto ? "AUTO ▶" : null);

        void OnDestroy()
        {
            VNLocale.LanguageChanged -= OnLocaleChanged;
            if (_skip) DOTween.timeScale = 1f; // 别把加速留给别的场景
            if (_menuPaused) Time.timeScale = _timeScaleBeforeMenu;
        }

        // ------------------------------------------------------------------
        // 输入
        // ------------------------------------------------------------------

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null) return;

            if (_eventActive) return; // 事件模块进行中：输入全部交给模块

            // 教程讲解中（VNPause）：全局快捷键一律屏蔽。
            // 不加这一条的话，剧情层弹出的教程盖着屏幕，F5 存档 / H 回想 /
            // A / S / I / C / G / J 还全都能按 —— 存出来的档还会卡在教程半截。
            if (VNPause.IsPaused) return;

            // 偷拍模式打开期间：输入全部归它（ESC / 空格 / 滚轮 / 拖动），
            // 这里直接 return 也就顺带挡掉了 F5/F9/Q/L 与推进
            if (_secretPhoto != null && _secretPhoto.IsOpen) return;

            // SNS 手机聊天：等玩家挑回复时输入全部交给面板（同 event，顺带挡掉存档）
            bool snsOpen = stage != null && stage.IsSnsOpen;
            if (snsOpen && stage.sns.IsBlockingInput) return;

            // 隐藏 UI 后，第一次操作只恢复界面，不会顺便推进台词。
            // 但 hideHUD keep 的锁定隐藏不吃这一条——那正是「点了也一直藏着」的意思，
            // 输入照常往下走（台词继续推进，只是看不见对话框）。
            if (_uiHidden && !_uiHideLocked)
            {
                bool restore = kb.uKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame ||
                               kb.spaceKey.wasPressedThisFrame ||
                               (mouse != null && (mouse.leftButton.wasPressedThisFrame ||
                                                  mouse.rightButton.wasPressedThisFrame));
                if (restore) SetInterfaceHidden(false);
                return;
            }

            if (_configPanel != null && _configPanel.IsOpen)
            {
                if (kb.escapeKey.wasPressedThisFrame) _configPanel.Close();
                return;
            }

            // 存读档界面打开期间只响应界面快捷键，不推进剧情。
            if (_saveLoadPanel != null && _saveLoadPanel.IsOpen)
            {
                if (kb.escapeKey.wasPressedThisFrame) _saveLoadPanel.Close();
                else if (kb.f5Key.wasPressedThisFrame) RequestSavePanel();
                else if (kb.f9Key.wasPressedThisFrame) RequestLoadPanel();
                return;
            }

            // 回想面板打开期间：只处理关闭，不推进剧情
            if (_backlog != null && _backlog.IsOpen)
            {
                if (kb.hKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame)
                    _backlog.Close();
                return;
            }

            // 任务日志打开期间：只处理关闭，不推进剧情
            if (_questLog != null && _questLog.IsOpen)
            {
                if (kb.jKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame)
                    _questLog.Close();
                return;
            }

            // 日记本打开期间：同上
            if (_diaryPanel != null && _diaryPanel.IsOpen)
            {
                if (kb.dKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame)
                    _diaryPanel.Close();
                return;
            }

            // 属性面板打开期间：只处理关闭，不推进剧情
            if (_statsHud != null && _statsHud.IsOpen)
            {
                if (kb.cKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame)
                    _statsHud.Close();
                return;
            }

            // 物品栏打开期间：只处理关闭，不推进剧情
            if (_inventory != null && _inventory.IsOpen)
            {
                if (kb.iKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame)
                    _inventory.Close();
                return;
            }

            // CG 鉴赏打开期间：只处理翻页与关闭，不推进剧情。
            // 比其他面板多一层——全屏浏览时 ←→ 翻差分、Esc/G 先退回网格。
            if (_cgGallery != null && _cgGallery.IsOpen)
            {
                if (_cgGallery.IsViewerOpen)
                {
                    if (kb.escapeKey.wasPressedThisFrame || kb.gKey.wasPressedThisFrame)
                        _cgGallery.CloseViewer();
                    else if (kb.rightArrowKey.wasPressedThisFrame ||
                             kb.downArrowKey.wasPressedThisFrame)
                        _cgGallery.ViewerNext();
                    else if (kb.leftArrowKey.wasPressedThisFrame ||
                             kb.upArrowKey.wasPressedThisFrame)
                        _cgGallery.ViewerPrev();
                }
                else if (kb.gKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame)
                {
                    _cgGallery.Close();
                }
                return;
            }

            // 标题菜单打开期间：按钮走 EventSystem，游戏快捷键与推进全部屏蔽。
            // （读档/设置/画廊面板叠在标题之上时，它们的关闭键由上面各自的分支处理。）
            if (_titleMenu != null && _titleMenu.IsOpen) return;

            // SNS 打开期间屏蔽会打架的快捷键：
            // 滚轮要留给聊天记录滚动、聊天消息不进回想、隐藏 UI 也没有意义。
            // 存读档（F5/F9/Q/L）照常可用——气泡停顿处就是合法存档点。
            if (!snsOpen)
            {
                // 滚轮上滑开回想是可关的（设置面板「滚轮打开回想」）——有人嫌误触；
                // 关掉后 H 键照常。
                if (kb.hKey.wasPressedThisFrame ||
                    (VNConfigPanel.WheelOpensBacklog &&
                     mouse != null && mouse.scroll.ReadValue().y > 0.1f))
                {
                    _backlog?.Open();
                    return;
                }

                if (kb.jKey.wasPressedThisFrame)
                {
                    _questLog?.Open();
                    return;
                }

                if (kb.dKey.wasPressedThisFrame)
                {
                    RequestDiary();   // 面板按需创建，所以走 Request 而不是直接 Open
                    return;
                }

                if (kb.cKey.wasPressedThisFrame)
                {
                    _statsHud?.Open();
                    return;
                }

                if (kb.iKey.wasPressedThisFrame)
                {
                    _inventory?.Open();
                    return;
                }

                if (kb.gKey.wasPressedThisFrame)
                {
                    _cgGallery?.Open();
                    return;
                }

                if (mouse != null && mouse.rightButton.wasPressedThisFrame)
                {
                    SetInterfaceHidden(true);
                    return;
                }
            }

            if (kb.f5Key.wasPressedThisFrame) { RequestSavePanel(); return; }
            if (kb.f9Key.wasPressedThisFrame) { RequestLoadPanel(); return; }
            if (kb.qKey.wasPressedThisFrame) { QuickSave(); return; }
            if (kb.lKey.wasPressedThisFrame) { QuickLoad(); return; }
            if (!snsOpen && kb.aKey.wasPressedThisFrame) { SetAuto(!_auto); return; }
            if (!snsOpen && kb.sKey.wasPressedThisFrame) { SetSkip(!_skip); return; }

            if (!_running) return;

            // 左键推进：整个画面都是 uGUI（背景/立绘/对话框都是 Canvas 里的 Image），
            // IsPointerOverGameObject() 恒为 true 会把点击全部拦掉；
            // 只有点在可交互控件（按钮/滑条等 Selectable）上才不推进。
            // 点击喷水模式（liquid click on）期间左键归喷水，不推进台词。
            // Enter/Space 一定要留着：玩家没有别的出路时会被卡死在这一句里。
            bool liquidClick = stage != null && stage.liquidSplash != null &&
                               stage.liquidSplash.clickMode;
            bool pressed = kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame ||
                           (!liquidClick && mouse != null && mouse.leftButton.wasPressedThisFrame &&
                            !IsPointerOverInteractiveUi(mouse));
            if (!pressed) return;

            // 手动推进会顺手退出快进（惯例）
            if (_skip) SetSkip(false);

            // SNS 气泡没有打字机（对话框此时是隐藏的），点击一律等于推进
            if (!snsOpen && stage != null && stage.dialogue != null && stage.dialogue.IsTyping)
                stage.dialogue.CompleteTyping();
            else
                _advance = true;
        }

        static readonly List<RaycastResult> _pointerRaycastResults = new List<RaycastResult>();

        /// <summary>
        /// 指针是否落在可交互控件上（Selectable：按钮/滑条/输入框等）。
        /// 用射线命中链向上找 Selectable，而不是 IsPointerOverGameObject ——
        /// 后者对任何 raycastTarget 都为 true，本项目全屏皆 UI，会拦掉一切点击。
        /// </summary>
        /// <summary>
        /// 外部（事件模块）请求推进当前正在等待的台词。
        ///
        /// **为什么必须有这个入口**：Update 第一行就是 `if (_eventActive) return;`
        /// —— 事件模块进行中，输入全部交给模块。于是模块内部用 RunInlineCo 播的
        /// 阻塞台词会死等 `_advance`，玩家点破屏幕也过不去。模块在阻塞期间
        /// 自己检测推进输入，转发到这里。
        ///
        /// 语义与 Update 里的手动推进一致：还在打字就先补完，打完了才真推进。
        /// </summary>
        public void RequestAdvance()
        {
            if (stage != null && stage.dialogue != null && stage.dialogue.IsTyping)
            {
                stage.dialogue.CompleteTyping();
                return;
            }
            if (_waitingAtSay) _advance = true;
        }

        /// <summary>当前是否正卡在某句台词上等玩家推进（模块判断要不要转发输入）</summary>
        public bool IsWaitingAtSay => _waitingAtSay;

        static bool IsPointerOverInteractiveUi(Mouse mouse)
        {
            if (EventSystem.current == null) return false;
            var data = new PointerEventData(EventSystem.current)
            {
                position = mouse.position.ReadValue(),
            };
            _pointerRaycastResults.Clear();
            EventSystem.current.RaycastAll(data, _pointerRaycastResults);
            foreach (var hit in _pointerRaycastResults)
                if (hit.gameObject.GetComponentInParent<Selectable>() != null)
                    return true;
            return false;
        }
    }
}
