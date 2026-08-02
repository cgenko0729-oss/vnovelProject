using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// SNS 手机聊天视图（剧本 sns 命令 + SNS 模式下的台词行落地层）。
    ///
    /// 定位：它**不是**事件模块，而是对话的另一种呈现层——
    /// sns open 之后，普通台词行 `亚里沙: 你好` 会被渲染成左侧气泡，
    /// `我: 好啊` 渲染成右侧气泡。这样存档、分支、翻译表全部沿用现成机制
    /// （event 模块是原子的，聊天中途就存不了档了）。
    ///
    /// 布局全部手工计算（不用 VerticalLayoutGroup + ContentSizeFitter）：
    /// TMP 的 preferredHeight 在同一帧内不可靠，手工测量反而稳定可控。
    ///
    /// 状态（会话 + 消息列表）进存档快照，读档时按列表重建气泡。
    /// </summary>
    public class VNSnsView : MonoBehaviour
    {
        /// <summary>玩家侧消息在数据里统一记这个发送者 id</summary>
        public const string PlayerSender = "me";

        /// <summary>台词行里被视为"玩家自己"的说话者写法（另加 sns open 的 me: 参数）</summary>
        static readonly string[] PlayerAliases = { "me", "我", "玩家", "主角" };

        // ---- 尺寸常量（1920×1080 画布下的手机外观）----
        const float PhoneW = 780f, PhoneH = 960f;
        const float HeaderH = 112f, FooterH = 104f;
        const float PadX = 26f, RowGap = 18f, EdgePad = 22f;
        const float AvatarSize = 72f, AvatarGap = 16f;
        const float BubblePadX = 26f, BubblePadY = 18f;
        const float MaxBubbleW = 500f, MinBubbleW = 96f;
        const float VoiceW = 300f, VoiceH = 84f;
        const float ImageMaxW = 340f, ImageMaxH = 260f;
        const int FontSize = 28;

        static readonly Color DimColor = new Color(0f, 0f, 0f, 0.72f);
        static readonly Color PhoneColor = new Color(0.09f, 0.10f, 0.13f, 0.99f);
        static readonly Color HeaderColor = new Color(0.15f, 0.17f, 0.23f, 1f);
        static readonly Color ChatColor = new Color(0.07f, 0.08f, 0.11f, 1f);
        static readonly Color LeftBubble = new Color(0.21f, 0.23f, 0.30f, 1f);
        static readonly Color RightBubble = new Color(0.24f, 0.55f, 0.42f, 1f);
        static readonly Color TextColor = new Color(0.96f, 0.97f, 1f, 1f);
        static readonly Color HintColor = new Color(1f, 1f, 1f, 0.42f);
        static readonly Color AccentColor = new Color(0.45f, 0.8f, 1f, 1f);
        static readonly Color UrgentColor = new Color(1f, 0.42f, 0.38f, 1f);

        // ---- 会话状态 ----
        readonly List<VNSnsMessage> _messages = new List<VNSnsMessage>();
        readonly List<RectTransform> _rows = new List<RectTransform>();
        int _nextId = 1;
        bool _open;
        string _sessionId, _title, _peerId, _playerAlias;

        VNStage _stage;
        VNCharacterDef _peer;

        // ---- UI ----
        RectTransform _root, _phone, _content, _viewport, _replyPanel, _imageViewer;
        TextMeshProUGUI _titleText, _statusText, _inputHint, _timerText;
        Image _avatarImage, _timerFill;
        RectTransform _typingRow;
        Tween _scrollTween;

        // ---- 回复交互 ----
        int _replyPick = -2;      // -2 = 未选，-1 = 超时，>=0 = 选项索引
        bool _replyActive;

        /// <summary>SNS 界面是否打开（Runner 据此把台词渲染成气泡）</summary>
        public bool IsOpen => _open;

        /// <summary>是否正在等待玩家回复（此时禁用剧本快捷键与存档，同 event 模块）</summary>
        public bool IsBlockingInput => _replyActive;

        /// <summary>当前会话 id（空 = 未打开）</summary>
        public string SessionId => _open ? _sessionId : null;

        /// <summary>该说话者是否算"玩家自己"（决定气泡在左还是在右）</summary>
        public bool IsPlayer(string sender) => IsPlayerSender(sender, _playerAlias);

        /// <summary>
        /// 静态版说话者判定：调试重建没有视图实例，也要按同一规则分左右，
        /// 因此判定逻辑放这里，实例方法只是补上当前会话的 me: 别名。
        /// </summary>
        public static bool IsPlayerSender(string sender, string playerAlias)
        {
            if (string.IsNullOrEmpty(sender)) return false;
            if (!string.IsNullOrEmpty(playerAlias) && sender == playerAlias) return true;
            foreach (var alias in PlayerAliases)
                if (sender == alias) return true;
            return false;
        }

        // ------------------------------------------------------------------
        // 开关
        // ------------------------------------------------------------------

        /// <summary>
        /// 打开聊天界面。peerId = 对方角色 id（用于头像与标题）；
        /// sessionId 留空则取 peerId；playerAlias = 剧本里代表玩家的说话者名。
        /// </summary>
        public void Open(VNStage stage, string peerId, string sessionId,
            string title, string playerAlias, bool instant = false)
        {
            _stage = stage != null ? stage : FindFirstObjectByType<VNStage>();
            _peerId = peerId;
            _peer = _stage != null && !string.IsNullOrEmpty(peerId)
                ? _stage.characters.Find(c => c != null && c.id == peerId) : null;
            _sessionId = string.IsNullOrEmpty(sessionId)
                ? (string.IsNullOrEmpty(peerId) ? "sns" : peerId) : sessionId;
            _playerAlias = playerAlias;
            _title = !string.IsNullOrEmpty(title) ? title
                : _peer != null ? _peer.LocalizedDisplayName
                : !string.IsNullOrEmpty(peerId) ? peerId : VNLocale.T("sns.title");

            Build();
            ClearRows();
            _messages.Clear();

            _titleText.text = _title;
            _statusText.text = VNLocale.T("sns.online");
            ApplyAvatar(_avatarImage, _peer);
            _inputHint.text = VNLocale.T("sns.inputHint");

            _root.gameObject.SetActive(true);
            _open = true;
            // 打开手机时收起对话框：台词从现在起走气泡
            if (_stage != null && _stage.dialogue != null)
                _stage.dialogue.SetInterfaceVisible(false);

            _phone.DOKill();
            if (instant)
            {
                _phone.anchoredPosition = Vector2.zero;
                _phone.localScale = Vector3.one;
            }
            else
            {
                _phone.anchoredPosition = new Vector2(0f, -80f);
                _phone.localScale = Vector3.one * 0.94f;
                _phone.DOAnchorPos(Vector2.zero, 0.34f).SetEase(Ease.OutCubic)
                      .SetLink(gameObject);
                _phone.DOScale(1f, 0.34f).SetEase(Ease.OutBack, 1.1f).SetLink(gameObject);
            }
            Layout();
        }

        /// <summary>关闭聊天界面并清空会话（消息列表随之作废）</summary>
        public void Close(bool instant = false)
        {
            if (!_open) return;
            _open = false;
            _replyActive = false;
            _replyPick = -2;
            HideTyping();
            CloseImageViewer();

            if (_stage != null && _stage.dialogue != null)
                _stage.dialogue.SetInterfaceVisible(true);

            if (_root == null) return;
            _phone.DOKill();
            if (instant)
            {
                FinishClose();
                return;
            }
            _phone.DOAnchorPos(new Vector2(0f, -110f), 0.26f).SetEase(Ease.InCubic)
                  .SetLink(gameObject).OnComplete(FinishClose);
        }

        void FinishClose()
        {
            ClearRows();
            _messages.Clear();
            if (_root != null) _root.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------
        // 追加消息
        // ------------------------------------------------------------------

        public VNSnsMessage AppendText(string sender, string text) =>
            Append(new VNSnsMessage
            {
                sender = Normalize(sender),
                kind = VNSnsMessage.KindText,
                text = text,
            });

        public VNSnsMessage AppendVoice(string sender, string voiceId, string caption) =>
            Append(new VNSnsMessage
            {
                sender = Normalize(sender),
                kind = VNSnsMessage.KindVoice,
                assetId = voiceId,
                text = caption,
            });

        public VNSnsMessage AppendImage(string sender, string cgId, bool unlock) =>
            Append(new VNSnsMessage
            {
                sender = Normalize(sender),
                kind = VNSnsMessage.KindImage,
                assetId = cgId,
                unlock = unlock,
            });

        /// <summary>系统提示（kind = system）或时间分割线（kind = time），居中显示</summary>
        public VNSnsMessage AppendNotice(string kind, string text) =>
            Append(new VNSnsMessage
            {
                sender = "",
                kind = kind == VNSnsMessage.KindTime
                    ? VNSnsMessage.KindTime : VNSnsMessage.KindSystem,
                text = text,
            });

        VNSnsMessage Append(VNSnsMessage msg)
        {
            if (!_open) return msg;
            msg.id = _nextId++;
            msg.sessionId = _sessionId;
            _messages.Add(msg);

            HideTyping();
            var row = BuildRow(msg);
            _rows.Add(row);
            Layout();
            ScrollToBottom(true);

            // 弹出：轻微放大 + 淡入（对方消息稍微带一点上浮）
            var group = row.GetComponent<CanvasGroup>();
            group.alpha = 0f;
            row.localScale = Vector3.one * 0.92f;
            row.DOScale(1f, 0.24f).SetEase(Ease.OutBack, 1.4f).SetLink(gameObject);
            group.DOFade(1f, 0.2f).SetLink(gameObject);
            return msg;
        }

        string Normalize(string sender) => IsPlayer(sender) ? PlayerSender : sender;

        /// <summary>把最后一条玩家消息标成"已读"</summary>
        public void MarkRead()
        {
            for (int i = _messages.Count - 1; i >= 0; i--)
            {
                if (_messages[i].sender != PlayerSender) continue;
                if (_messages[i].read) return;
                _messages[i].read = true;
                RebuildRow(i);
                Layout();
                ScrollToBottom(true);
                return;
            }
        }

        // ------------------------------------------------------------------
        // 正在输入…
        // ------------------------------------------------------------------

        /// <summary>显示「对方正在输入…」气泡，等待 seconds 后自动收起</summary>
        public IEnumerator TypingCo(float seconds)
        {
            if (!_open) yield break;
            ShowTyping();
            float t = 0f;
            while (t < seconds)
            {
                t += Time.deltaTime;
                yield return null;
            }
            HideTyping();
        }

        void ShowTyping()
        {
            if (!_open || _typingRow != null) return;

            var row = CreateRow(AvatarSize);
            var avatar = CreateImage("Avatar", row, VNProceduralTextures.RadialGlowSprite, Color.white);
            avatar.sizeDelta = new Vector2(AvatarSize, AvatarSize);
            AnchorTopLeft(avatar, PadX, 0f);
            ApplyAvatar(avatar.GetComponent<Image>(), _peer);

            var bubble = CreateImage("Typing", row, VNProceduralTextures.RoundedRectSprite, LeftBubble);
            bubble.GetComponent<Image>().type = Image.Type.Sliced;
            bubble.sizeDelta = new Vector2(120f, 62f);
            AnchorTopLeft(bubble, PadX + AvatarSize + AvatarGap, 6f);

            // 三点跳动
            for (int i = 0; i < 3; i++)
            {
                var dot = CreateImage("Dot", bubble, VNProceduralTextures.RadialGlowSprite,
                    new Color(1f, 1f, 1f, 0.85f));
                dot.sizeDelta = new Vector2(14f, 14f);
                dot.anchorMin = dot.anchorMax = new Vector2(0.5f, 0.5f);
                dot.anchoredPosition = new Vector2(-26f + i * 26f, 0f);
                dot.DOAnchorPosY(9f, 0.42f).SetEase(Ease.InOutSine)
                   .SetLoops(-1, LoopType.Yoyo).SetDelay(i * 0.14f).SetLink(gameObject);
            }

            _typingRow = row;
            _rows.Add(row);
            Layout();
            ScrollToBottom(true);
        }

        void HideTyping()
        {
            if (_typingRow == null) return;
            _rows.Remove(_typingRow);
            Destroy(_typingRow.gameObject);
            _typingRow = null;
            Layout();
        }

        // ------------------------------------------------------------------
        // 玩家回复
        // ------------------------------------------------------------------

        /// <summary>
        /// 弹出候选回复。timeout &gt; 0 时显示倒计时条，超时回调 -1（已读不回）。
        /// 选中的选项文本会自动变成一条右侧气泡。
        /// </summary>
        public IEnumerator ReplyCo(List<string> texts, float timeout, System.Action<int> onPick)
        {
            if (!_open || texts == null || texts.Count == 0)
            {
                onPick?.Invoke(-1);
                yield break;
            }

            _replyPick = -2;
            _replyActive = true;
            BuildReplyPanel(texts, timeout > 0.01f);

            float left = timeout;
            while (_replyPick == -2)
            {
                if (timeout > 0.01f)
                {
                    left -= Time.unscaledDeltaTime;
                    RefreshTimer(left, timeout);
                    if (left <= 0f) _replyPick = -1;
                }
                yield return null;
            }

            int pick = _replyPick;
            _replyActive = false;
            ClearReplyPanel();

            if (pick >= 0 && pick < texts.Count) AppendText(PlayerSender, texts[pick]);
            else AppendNotice(VNSnsMessage.KindSystem, VNLocale.T("sns.noReply"));

            yield return new WaitForSeconds(0.25f); // 让气泡落定再继续剧本
            onPick?.Invoke(pick);
        }

        void BuildReplyPanel(List<string> texts, bool timed)
        {
            ClearReplyPanel();

            float rowH = 74f, gap = 12f;
            float timerH = timed ? 34f : 0f;
            float panelH = EdgePad * 2f + timerH + texts.Count * rowH + (texts.Count - 1) * gap;

            _replyPanel = CreateImage("ReplyPanel", _phone,
                VNProceduralTextures.RoundedRectSprite, new Color(0.12f, 0.14f, 0.19f, 0.99f));
            _replyPanel.GetComponent<Image>().type = Image.Type.Sliced;
            _replyPanel.anchorMin = new Vector2(0f, 0f);
            _replyPanel.anchorMax = new Vector2(1f, 0f);
            _replyPanel.pivot = new Vector2(0.5f, 0f);
            _replyPanel.offsetMin = new Vector2(PadX * 0.6f, 0f);
            _replyPanel.offsetMax = new Vector2(-PadX * 0.6f, 0f);
            _replyPanel.sizeDelta = new Vector2(_replyPanel.sizeDelta.x, panelH);
            _replyPanel.anchoredPosition = new Vector2(0f, PadX * 0.5f);

            float y = -EdgePad;
            if (timed)
            {
                var barBg = CreateImage("TimerBg", _replyPanel,
                    VNProceduralTextures.RoundedRectSprite, new Color(1f, 1f, 1f, 0.12f));
                barBg.GetComponent<Image>().type = Image.Type.Sliced;
                barBg.anchorMin = new Vector2(0f, 1f);
                barBg.anchorMax = new Vector2(1f, 1f);
                barBg.pivot = new Vector2(0.5f, 1f);
                barBg.offsetMin = new Vector2(EdgePad, 0f);
                barBg.offsetMax = new Vector2(-EdgePad - 92f, 0f);
                barBg.sizeDelta = new Vector2(barBg.sizeDelta.x, 12f);
                barBg.anchoredPosition = new Vector2(-46f, y - 6f);

                var fill = CreateImage("TimerFill", barBg,
                    VNProceduralTextures.RoundedRectSprite, AccentColor);
                _timerFill = fill.GetComponent<Image>();
                _timerFill.type = Image.Type.Sliced;
                fill.anchorMin = Vector2.zero;
                fill.anchorMax = Vector2.one;
                fill.offsetMin = Vector2.zero;
                fill.offsetMax = Vector2.zero;

                _timerText = CreateText("Timer", _replyPanel, 26, HintColor, "");
                var tr = (RectTransform)_timerText.transform;
                tr.anchorMin = tr.anchorMax = new Vector2(1f, 1f);
                tr.pivot = new Vector2(1f, 1f);
                tr.sizeDelta = new Vector2(88f, 34f);
                tr.anchoredPosition = new Vector2(-EdgePad, y + 4f);
                _timerText.alignment = TextAlignmentOptions.Right;

                y -= timerH;
            }

            for (int i = 0; i < texts.Count; i++)
            {
                int captured = i;
                var go = new GameObject("Reply", typeof(RectTransform),
                    typeof(CanvasRenderer), typeof(Image), typeof(Button));
                var rect = (RectTransform)go.transform;
                rect.SetParent(_replyPanel, false);
                var image = go.GetComponent<Image>();
                image.sprite = VNProceduralTextures.RoundedRectSprite;
                image.type = Image.Type.Sliced;
                image.color = new Color(0.20f, 0.23f, 0.31f, 1f);
                var button = go.GetComponent<Button>();
                button.targetGraphic = image;
                button.onClick.AddListener(() => _replyPick = captured);

                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.offsetMin = new Vector2(EdgePad, 0f);
                rect.offsetMax = new Vector2(-EdgePad, 0f);
                rect.sizeDelta = new Vector2(rect.sizeDelta.x, rowH);
                rect.anchoredPosition = new Vector2(0f, y);

                var label = CreateText("Label", rect, 27, TextColor, texts[i]);
                label.alignment = TextAlignmentOptions.Left;
                label.textWrappingMode = TextWrappingModes.Normal;
                var lr = (RectTransform)label.transform;
                Stretch(lr);
                lr.offsetMin = new Vector2(24f, 0f);
                lr.offsetMax = new Vector2(-24f, 0f);

                y -= rowH + gap;
            }

            _replyPanel.localScale = new Vector3(1f, 0.9f, 1f);
            _replyPanel.DOScaleY(1f, 0.2f).SetEase(Ease.OutBack).SetLink(gameObject);
            if (_inputHint != null) _inputHint.gameObject.SetActive(false);
        }

        void RefreshTimer(float left, float total)
        {
            if (_timerFill == null || _timerText == null) return;
            float clamped = Mathf.Max(0f, left);
            _timerText.text = VNLocale.T("sns.timer", clamped.ToString("0"));

            var fillRect = (RectTransform)_timerFill.transform;
            fillRect.anchorMax = new Vector2(total > 0f ? Mathf.Clamp01(clamped / total) : 0f, 1f);

            bool urgent = clamped <= 3f;
            _timerFill.color = urgent ? UrgentColor : AccentColor;
            _timerText.color = urgent ? UrgentColor : HintColor;
            _timerText.transform.localScale = urgent
                ? Vector3.one * (1f + 0.1f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 8f)))
                : Vector3.one;
        }

        void ClearReplyPanel()
        {
            if (_replyPanel != null) Destroy(_replyPanel.gameObject);
            _replyPanel = null;
            _timerFill = null;
            _timerText = null;
            if (_inputHint != null) _inputHint.gameObject.SetActive(true);
        }

        // ------------------------------------------------------------------
        // 存档快照
        // ------------------------------------------------------------------

        public void CaptureSnapshot(VNSaveData data)
        {
            data.snsOpen = _open;
            data.snsPeerId = _open ? _peerId : null;
            data.snsSessionId = _open ? _sessionId : null;
            data.snsTitle = _open ? _title : null;
            data.snsPlayerAlias = _open ? _playerAlias : null;
            data.snsMessages.Clear();
            if (_open) data.snsMessages.AddRange(_messages);
        }

        /// <summary>按存档重建会话（读档 / 调试重建共用）</summary>
        public void RestoreSnapshot(VNSaveData data, VNStage stage)
        {
            if (data == null || !data.snsOpen)
            {
                if (_open) Close(true);
                return;
            }

            Open(stage, data.snsPeerId, data.snsSessionId, data.snsTitle,
                data.snsPlayerAlias, true);

            _messages.Clear();
            _nextId = 1;
            foreach (var msg in data.snsMessages)
            {
                if (msg == null) continue;
                _messages.Add(msg);
                _nextId = Mathf.Max(_nextId, msg.id + 1);
                var row = BuildRow(msg);
                _rows.Add(row);
            }
            Layout();
            ScrollToBottom(false);
        }

        // ------------------------------------------------------------------
        // 行渲染
        // ------------------------------------------------------------------

        void RebuildRow(int index)
        {
            if (index < 0 || index >= _messages.Count || index >= _rows.Count) return;
            var old = _rows[index];
            if (old != null) Destroy(old.gameObject);
            _rows[index] = BuildRow(_messages[index]);
        }

        RectTransform BuildRow(VNSnsMessage msg)
        {
            switch (msg.kind)
            {
                case VNSnsMessage.KindVoice: return BuildVoiceRow(msg);
                case VNSnsMessage.KindImage: return BuildImageRow(msg);
                case VNSnsMessage.KindSystem:
                case VNSnsMessage.KindTime: return BuildNoticeRow(msg);
                default: return BuildTextRow(msg);
            }
        }

        RectTransform BuildTextRow(VNSnsMessage msg)
        {
            bool mine = msg.sender == PlayerSender;
            string display = msg.text ?? "";

            // 先在行里量文本，再据此定气泡尺寸。
            // 手工测量绕开 ContentSizeFitter/LayoutGroup 的重建时序问题（TMP 的
            // preferredHeight 同帧内不可靠），代价是所有位置都要自己算。
            var row = CreateRow(AvatarSize);
            var text = CreateText("Text", row, FontSize, TextColor, display);
            text.textWrappingMode = TextWrappingModes.Normal;
            var size = text.GetPreferredValues(display, MaxBubbleW, 0f);

            float bubbleW = Mathf.Clamp(Mathf.Ceil(size.x) + BubblePadX * 2f + 2f,
                MinBubbleW, MaxBubbleW + BubblePadX * 2f);
            float bubbleH = Mathf.Ceil(size.y) + BubblePadY * 2f;
            row.sizeDelta = new Vector2(0f, Mathf.Max(bubbleH, AvatarSize));

            var bubble = CreateBubble(row, mine, bubbleW, bubbleH);
            text.rectTransform.SetParent(bubble, false);
            Stretch(text.rectTransform);
            text.rectTransform.offsetMin = new Vector2(BubblePadX, BubblePadY);
            text.rectTransform.offsetMax = new Vector2(-BubblePadX, -BubblePadY);
            text.alignment = TextAlignmentOptions.TopLeft;

            if (mine && msg.read) AddReadMark(row, bubbleW);
            return row;
        }

        RectTransform BuildVoiceRow(VNSnsMessage msg)
        {
            bool mine = msg.sender == PlayerSender;
            var row = CreateRow(Mathf.Max(VoiceH, AvatarSize));
            var bubble = CreateBubble(row, mine, VoiceW, VoiceH);

            var button = bubble.gameObject.AddComponent<Button>();
            button.targetGraphic = bubble.GetComponent<Image>();

            // 播放圆点 + 五根音波条（点击播放时逐根跳动）
            var dot = CreateImage("Play", bubble, VNProceduralTextures.RadialGlowSprite,
                new Color(1f, 1f, 1f, 0.92f));
            dot.sizeDelta = new Vector2(30f, 30f);
            dot.anchorMin = dot.anchorMax = new Vector2(0f, 0.5f);
            dot.pivot = new Vector2(0f, 0.5f);
            dot.anchoredPosition = new Vector2(24f, 0f);

            var bars = new List<RectTransform>();
            for (int i = 0; i < 5; i++)
            {
                var bar = CreateImage("Bar", bubble, VNProceduralTextures.RoundedRectSprite,
                    new Color(1f, 1f, 1f, 0.8f));
                bar.GetComponent<Image>().type = Image.Type.Sliced;
                bar.sizeDelta = new Vector2(8f, 20f + (i % 3) * 12f);
                bar.anchorMin = bar.anchorMax = new Vector2(0f, 0.5f);
                bar.pivot = new Vector2(0f, 0.5f);
                bar.anchoredPosition = new Vector2(74f + i * 18f, 0f);
                bars.Add(bar);
            }

            var caption = CreateText("Caption", bubble, 24, new Color(1f, 1f, 1f, 0.75f),
                string.IsNullOrEmpty(msg.text) ? VNLocale.T("sns.voice") : msg.text);
            caption.alignment = TextAlignmentOptions.Right;
            var cr = (RectTransform)caption.transform;
            cr.anchorMin = new Vector2(0f, 0f);
            cr.anchorMax = new Vector2(1f, 1f);
            cr.offsetMin = new Vector2(174f, 0f);
            cr.offsetMax = new Vector2(-20f, 0f);

            // 未听过的对方语音带一个红点，听过后消失
            RectTransform unread = null;
            if (!mine && !msg.played)
            {
                unread = CreateImage("Unread", bubble, VNProceduralTextures.RadialGlowSprite,
                    new Color(1f, 0.35f, 0.35f, 1f));
                unread.sizeDelta = new Vector2(16f, 16f);
                unread.anchorMin = unread.anchorMax = new Vector2(1f, 1f);
                unread.pivot = new Vector2(0.5f, 0.5f);
                unread.anchoredPosition = new Vector2(6f, 6f);
            }

            var captured = msg;
            var capturedUnread = unread;
            button.onClick.AddListener(() => PlayVoice(captured, bars, capturedUnread));

            if (mine && msg.read) AddReadMark(row, VoiceW);
            return row;
        }

        void PlayVoice(VNSnsMessage msg, List<RectTransform> bars, RectTransform unread)
        {
            if (_stage != null && _stage.vnAudio != null && !string.IsNullOrEmpty(msg.assetId))
                _stage.vnAudio.PlayVoice(msg.assetId);

            msg.played = true;
            if (unread != null) Destroy(unread.gameObject);

            for (int i = 0; i < bars.Count; i++)
            {
                var bar = bars[i];
                if (bar == null) continue;
                bar.DOKill();
                float baseH = 20f + (i % 3) * 12f;
                bar.sizeDelta = new Vector2(8f, baseH);
                bar.DOSizeDelta(new Vector2(8f, baseH * 1.9f), 0.22f)
                   .SetEase(Ease.InOutSine).SetLoops(6, LoopType.Yoyo)
                   .SetDelay(i * 0.06f).SetLink(gameObject);
            }
        }

        RectTransform BuildImageRow(VNSnsMessage msg)
        {
            bool mine = msg.sender == PlayerSender;
            var sprite = FindCgSprite(msg.assetId);

            float w = ImageMaxW, h = ImageMaxH;
            if (sprite != null)
            {
                float aspect = sprite.rect.width / Mathf.Max(1f, sprite.rect.height);
                if (aspect >= ImageMaxW / ImageMaxH) { w = ImageMaxW; h = w / aspect; }
                else { h = ImageMaxH; w = h * aspect; }
            }

            float bubbleW = w + 16f, bubbleH = h + 16f;
            var row = CreateRow(Mathf.Max(bubbleH, AvatarSize));
            var bubble = CreateBubble(row, mine, bubbleW, bubbleH);

            var thumb = CreateImage("Thumb", bubble, sprite, Color.white);
            thumb.sizeDelta = new Vector2(w, h);
            thumb.anchorMin = thumb.anchorMax = new Vector2(0.5f, 0.5f);
            var thumbImage = thumb.GetComponent<Image>();
            thumbImage.preserveAspect = true;
            if (sprite == null)
            {
                thumbImage.sprite = VNProceduralTextures.RoundedRectSprite;
                thumbImage.type = Image.Type.Sliced;
                thumbImage.color = new Color(1f, 1f, 1f, 0.12f);
                var missing = CreateText("Missing", thumb, 24, HintColor,
                    VNLocale.T("sns.imageMissing"));
                Stretch((RectTransform)missing.transform);
            }

            if (msg.unlock && !string.IsNullOrEmpty(msg.assetId) && sprite != null)
                VNCgUnlocks.Unlock(msg.assetId);

            var button = bubble.gameObject.AddComponent<Button>();
            button.targetGraphic = bubble.GetComponent<Image>();
            var capturedSprite = sprite;
            button.onClick.AddListener(() => OpenImageViewer(capturedSprite));

            if (mine && msg.read) AddReadMark(row, bubbleW);
            return row;
        }

        RectTransform BuildNoticeRow(VNSnsMessage msg)
        {
            string display = msg.text ?? "";
            var row = CreateRow(46f);
            var text = CreateText("Notice", row, 24, HintColor, display);
            text.textWrappingMode = TextWrappingModes.Normal;
            var rect = (RectTransform)text.transform;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.offsetMin = new Vector2(PadX, 0f);
            rect.offsetMax = new Vector2(-PadX, 0f);
            text.alignment = TextAlignmentOptions.Center;
            return row;
        }

        /// <summary>建一行容器：宽度撑满内容区，高度手工给定</summary>
        RectTransform CreateRow(float height)
        {
            var go = new GameObject("Row", typeof(RectTransform), typeof(CanvasGroup));
            var rect = (RectTransform)go.transform;
            rect.SetParent(_content, false);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(0f, 0f);
            rect.offsetMax = new Vector2(0f, 0f);
            rect.sizeDelta = new Vector2(0f, height);
            return rect;
        }

        /// <summary>建气泡（含左侧头像）：mine = 右对齐无头像，否则左对齐带头像</summary>
        RectTransform CreateBubble(RectTransform row, bool mine, float width, float height)
        {
            if (!mine)
            {
                var avatar = CreateImage("Avatar", row,
                    VNProceduralTextures.RadialGlowSprite, Color.white);
                avatar.sizeDelta = new Vector2(AvatarSize, AvatarSize);
                AnchorTopLeft(avatar, PadX, 0f);
                ApplyAvatar(avatar.GetComponent<Image>(), _peer);
            }

            var bubble = CreateImage("Bubble", row, VNProceduralTextures.RoundedRectSprite,
                mine ? RightBubble : LeftBubble);
            bubble.GetComponent<Image>().type = Image.Type.Sliced;
            bubble.sizeDelta = new Vector2(width, height);
            if (mine)
            {
                bubble.anchorMin = bubble.anchorMax = new Vector2(1f, 1f);
                bubble.pivot = new Vector2(1f, 1f);
                bubble.anchoredPosition = new Vector2(-PadX, 0f);
            }
            else
            {
                AnchorTopLeft(bubble, PadX + AvatarSize + AvatarGap, 0f);
            }
            return bubble;
        }

        /// <summary>玩家消息气泡左侧的"已读"小字</summary>
        void AddReadMark(RectTransform row, float bubbleWidth)
        {
            var text = CreateText("Read", row, 22, HintColor, VNLocale.T("sns.read"));
            var rect = (RectTransform)text.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(80f, 34f);
            rect.anchoredPosition = new Vector2(-PadX - bubbleWidth - 10f, -8f);
            text.alignment = TextAlignmentOptions.Right;
        }

        void AnchorTopLeft(RectTransform rect, float x, float y)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
        }

        void ApplyAvatar(Image image, VNCharacterDef def)
        {
            if (image == null) return;
            var sprite = def != null ? def.GetPortrait(null) : null;
            if (sprite == null && def != null) sprite = def.DefaultSprite;
            if (sprite != null)
            {
                image.sprite = sprite;
                image.type = Image.Type.Simple;
                image.preserveAspect = true;
                image.color = Color.white;
            }
            else
            {
                image.sprite = VNProceduralTextures.RadialGlowSprite;
                image.color = def != null ? def.nameColor : new Color(0.4f, 0.5f, 0.7f, 1f);
            }
        }

        Sprite FindCgSprite(string id)
        {
            if (_stage == null || string.IsNullOrEmpty(id)) return null;
            var entry = _stage.cgLibrary.Find(c => c != null && c.id == id);
            return entry != null ? entry.sprite : null;
        }

        // ------------------------------------------------------------------
        // 布局 / 滚动
        // ------------------------------------------------------------------

        /// <summary>
        /// 可视区高度。读档重建是在 Open 的同一帧就摆完全部气泡的，
        /// 那时 viewport 的 rect 还没被布局系统算出来（为 0），
        /// 直接用会把内容滚过头，所以退回按常量算的理论高度。
        /// </summary>
        float ViewportHeight =>
            _viewport != null && _viewport.rect.height > 1f
                ? _viewport.rect.height
                : PhoneH - HeaderH - 8f - FooterH;

        void Layout()
        {
            if (_content == null) return;
            float y = EdgePad;
            foreach (var row in _rows)
            {
                if (row == null) continue;
                row.anchoredPosition = new Vector2(0f, -y);
                y += row.sizeDelta.y + RowGap;
            }
            y += EdgePad - RowGap;
            _content.sizeDelta = new Vector2(0f, Mathf.Max(y, ViewportHeight));
        }

        void ScrollToBottom(bool animate)
        {
            if (_content == null || _viewport == null) return;
            float max = Mathf.Max(0f, _content.sizeDelta.y - ViewportHeight);
            _scrollTween?.Kill();
            if (!animate)
            {
                _content.anchoredPosition = new Vector2(0f, max);
                return;
            }
            _scrollTween = _content.DOAnchorPosY(max, 0.3f)
                                   .SetEase(Ease.OutCubic).SetLink(gameObject);
        }

        void ClearRows()
        {
            foreach (var row in _rows)
                if (row != null) Destroy(row.gameObject);
            _rows.Clear();
            _typingRow = null;
        }

        // ------------------------------------------------------------------
        // 大图查看
        // ------------------------------------------------------------------

        void OpenImageViewer(Sprite sprite)
        {
            if (sprite == null || _root == null) return;
            CloseImageViewer();

            _imageViewer = CreateImage("ImageViewer", _root, null, new Color(0f, 0f, 0f, 0.92f));
            Stretch(_imageViewer);
            var button = _imageViewer.gameObject.AddComponent<Button>();
            button.targetGraphic = _imageViewer.GetComponent<Image>();
            button.onClick.AddListener(CloseImageViewer);

            var big = CreateImage("Big", _imageViewer, sprite, Color.white);
            big.GetComponent<Image>().preserveAspect = true;
            big.anchorMin = new Vector2(0.5f, 0.5f);
            big.anchorMax = new Vector2(0.5f, 0.5f);
            big.sizeDelta = new Vector2(1440f, 810f);

            var hint = CreateText("Hint", _imageViewer, 26, HintColor, VNLocale.T("sns.closeImage"));
            var hr = (RectTransform)hint.transform;
            hr.anchorMin = new Vector2(0.5f, 0f);
            hr.anchorMax = new Vector2(0.5f, 0f);
            hr.pivot = new Vector2(0.5f, 0f);
            hr.sizeDelta = new Vector2(600f, 46f);
            hr.anchoredPosition = new Vector2(0f, 40f);

            _imageViewer.localScale = Vector3.one * 0.96f;
            _imageViewer.DOScale(1f, 0.2f).SetEase(Ease.OutCubic).SetLink(gameObject);
        }

        void CloseImageViewer()
        {
            if (_imageViewer == null) return;
            Destroy(_imageViewer.gameObject);
            _imageViewer = null;
        }

        // ------------------------------------------------------------------
        // 程序化 UI 骨架
        // ------------------------------------------------------------------

        void Build()
        {
            if (_root != null) return;

            var canvasGo = new GameObject("VNSnsCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // 盖住对话框（40）与事件层（60），但低于存读档/回想（600），
            // 这样聊天中途按 F5 仍能把存档面板叠在上面
            canvas.sortingOrder = 300;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            _root = CreateImage("SnsRoot", (RectTransform)canvasGo.transform, null, DimColor);
            Stretch(_root);

            _phone = CreateImage("Phone", _root, VNProceduralTextures.RoundedRectSprite, PhoneColor);
            _phone.GetComponent<Image>().type = Image.Type.Sliced;
            _phone.anchorMin = _phone.anchorMax = new Vector2(0.5f, 0.5f);
            _phone.sizeDelta = new Vector2(PhoneW, PhoneH);

            // 顶栏：头像 + 名字 + 状态
            var header = CreateImage("Header", _phone,
                VNProceduralTextures.RoundedRectSprite, HeaderColor);
            header.GetComponent<Image>().type = Image.Type.Sliced;
            header.anchorMin = new Vector2(0f, 1f);
            header.anchorMax = new Vector2(1f, 1f);
            header.pivot = new Vector2(0.5f, 1f);
            header.offsetMin = new Vector2(6f, 0f);
            header.offsetMax = new Vector2(-6f, 0f);
            header.sizeDelta = new Vector2(header.sizeDelta.x, HeaderH);
            header.anchoredPosition = new Vector2(0f, -6f);

            var avatar = CreateImage("Avatar", header,
                VNProceduralTextures.RadialGlowSprite, Color.white);
            avatar.sizeDelta = new Vector2(70f, 70f);
            avatar.anchorMin = avatar.anchorMax = new Vector2(0f, 0.5f);
            avatar.pivot = new Vector2(0f, 0.5f);
            avatar.anchoredPosition = new Vector2(24f, 0f);
            _avatarImage = avatar.GetComponent<Image>();

            _titleText = CreateText("Title", header, 32, TextColor, "");
            var titleRect = (RectTransform)_titleText.transform;
            titleRect.anchorMin = new Vector2(0f, 0.5f);
            titleRect.anchorMax = new Vector2(1f, 0.5f);
            titleRect.pivot = new Vector2(0f, 0f);
            titleRect.offsetMin = new Vector2(110f, 0f);
            titleRect.offsetMax = new Vector2(-24f, 0f);
            titleRect.sizeDelta = new Vector2(titleRect.sizeDelta.x, 40f);
            titleRect.anchoredPosition = new Vector2(110f, 2f);
            _titleText.alignment = TextAlignmentOptions.Left;

            _statusText = CreateText("Status", header, 22, HintColor, "");
            var statusRect = (RectTransform)_statusText.transform;
            statusRect.anchorMin = new Vector2(0f, 0.5f);
            statusRect.anchorMax = new Vector2(1f, 0.5f);
            statusRect.pivot = new Vector2(0f, 1f);
            statusRect.offsetMin = new Vector2(110f, 0f);
            statusRect.offsetMax = new Vector2(-24f, 0f);
            statusRect.sizeDelta = new Vector2(statusRect.sizeDelta.x, 32f);
            statusRect.anchoredPosition = new Vector2(110f, 0f);
            _statusText.alignment = TextAlignmentOptions.Left;

            // 聊天区（ScrollRect + 手工布局的 Content）
            var chat = CreateImage("Chat", _phone, null, ChatColor);
            chat.anchorMin = new Vector2(0f, 0f);
            chat.anchorMax = new Vector2(1f, 1f);
            chat.offsetMin = new Vector2(6f, FooterH);
            chat.offsetMax = new Vector2(-6f, -HeaderH - 8f);

            _viewport = CreateImage("Viewport", chat, null, new Color(0f, 0f, 0f, 0f));
            Stretch(_viewport);
            _viewport.gameObject.AddComponent<RectMask2D>();

            var contentGo = new GameObject("Content", typeof(RectTransform));
            _content = (RectTransform)contentGo.transform;
            _content.SetParent(_viewport, false);
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.offsetMin = new Vector2(0f, 0f);
            _content.offsetMax = new Vector2(0f, 0f);
            _content.sizeDelta = new Vector2(0f, 0f);

            var scroll = chat.gameObject.AddComponent<ScrollRect>();
            scroll.content = _content;
            scroll.viewport = _viewport;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 42f;

            // 底栏：输入框装饰（回复时被候选面板遮住）
            var footer = CreateImage("Footer", _phone,
                VNProceduralTextures.RoundedRectSprite, HeaderColor);
            footer.GetComponent<Image>().type = Image.Type.Sliced;
            footer.anchorMin = new Vector2(0f, 0f);
            footer.anchorMax = new Vector2(1f, 0f);
            footer.pivot = new Vector2(0.5f, 0f);
            footer.offsetMin = new Vector2(6f, 0f);
            footer.offsetMax = new Vector2(-6f, 0f);
            footer.sizeDelta = new Vector2(footer.sizeDelta.x, FooterH - 6f);
            footer.anchoredPosition = new Vector2(0f, 6f);

            var box = CreateImage("InputBox", footer,
                VNProceduralTextures.RoundedRectSprite, new Color(0.20f, 0.23f, 0.30f, 1f));
            box.GetComponent<Image>().type = Image.Type.Sliced;
            Stretch(box);
            box.offsetMin = new Vector2(22f, 18f);
            box.offsetMax = new Vector2(-22f, -18f);

            _inputHint = CreateText("InputHint", box, 25, HintColor, "");
            Stretch((RectTransform)_inputHint.transform);
            ((RectTransform)_inputHint.transform).offsetMin = new Vector2(24f, 0f);
            ((RectTransform)_inputHint.transform).offsetMax = new Vector2(-24f, 0f);
            _inputHint.alignment = TextAlignmentOptions.Left;

            _root.gameObject.SetActive(false);
        }

        static RectTransform CreateImage(string name, Transform parent, Sprite sprite, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            return rect;
        }

        static TextMeshProUGUI CreateText(string name, Transform parent,
            int size, Color color, string content)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.font = VNFont.Asset;
            text.fontSize = size;
            text.color = color;
            text.text = content;
            text.alignment = TextAlignmentOptions.Left;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            return text;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
