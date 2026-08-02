namespace VNEffects
{
    /// <summary>
    /// SNS（手机通讯软件）聊天里的一条消息。
    ///
    /// 设计约定：
    ///   - text 存的是**当时的显示文本**（台词行已经过 VNScriptLocale.TextOf 解析）。
    ///     翻译表按"出现序号"匹配 key，脱离命令流无法单句反查，因此这里存结果而非原文；
    ///     语言在会话中途切换时，已发出的气泡保持原语言，重开会话即为新语言。
    ///   - 带 sessionId 与全局自增 id，是为将来的"跨章节永久聊天记录"预留；
    ///     当前版本只保留 sns open ~ sns close 之间的一次会话。
    ///   - 整个类进存档（JsonUtility 序列化），字段只增不改名。
    /// </summary>
    [System.Serializable]
    public class VNSnsMessage
    {
        /// <summary>全局自增序号（跨会话唯一，永久历史用）</summary>
        public int id;

        /// <summary>所属会话 id（默认取 sns open 的对方角色 id）</summary>
        public string sessionId;

        /// <summary>发送者：角色 id；玩家侧统一存 VNSnsView.PlayerSender</summary>
        public string sender;

        /// <summary>消息种类：text / voice / image / system / time</summary>
        public string kind;

        /// <summary>显示文本（text 消息的正文；system/time 的提示语；voice 的可选文字稿）</summary>
        public string text;

        /// <summary>voice = 语音 id；image = CG id</summary>
        public string assetId;

        /// <summary>图片消息是否解锁进 CG 鉴赏画廊</summary>
        public bool unlock;

        /// <summary>玩家发出的消息是否已被对方"已读"</summary>
        public bool read;

        /// <summary>语音消息玩家是否点开听过（未听过显示未读红点）</summary>
        public bool played;

        public const string KindText = "text";
        public const string KindVoice = "voice";
        public const string KindImage = "image";
        public const string KindSystem = "system";
        public const string KindTime = "time";

        /// <summary>系统提示与时间分割线不属于任何一侧，居中显示</summary>
        public bool IsCentered => kind == KindSystem || kind == KindTime;
    }
}
