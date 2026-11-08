using System;
using System.Collections.Generic;
using UnityEngine;

namespace VNEffects
{
    /// <summary>大头贴相册：persistentDataPath/vn_photos/，存储逻辑在 <see cref="VNAlbumStore{TEntry}"/>。</summary>
    public static class VNPhotoAlbum
    {
        public const int Capacity = VNAlbumStore<Entry>.Capacity;

        [Serializable]
        public class Entry : VNAlbumEntry
        {
            public string her;       // 合影对象角色 id
            public string me;        // 主角角色 id
            public string theme;     // 主题 id（自由拍照为空）
            public int score;
            public int grade = -1;   // 2 完美 / 1 普通 / 0 失败；-1 = 自由拍照不评分
        }

        static readonly VNAlbumStore<Entry> Store = new VNAlbumStore<Entry>("vn_photos", "VNPhoto",
            file => new Entry { her = VNAlbumStore<Entry>.CharacterFromFileName(file) });

        public static IReadOnlyList<Entry> All => Store.All;

        /// <summary>存一张。相册已满或写盘失败返回 null；调用方负责销毁 Texture2D。</summary>
        public static Entry Add(Texture2D texture, string her, string me, string theme, int score, int grade) =>
            Store.Add(texture, "photo", VNAlbumStore<Entry>.SanitizeFileName(her, "photo"),
                new Entry { her = her, me = me, theme = theme, score = score, grade = grade });

        public static bool Delete(string file) => Store.Delete(file);
        public static Sprite LoadThumbnail(string file) => Store.LoadThumbnail(file);
        public static Sprite LoadSprite(string file) => Store.LoadSprite(file);
        public static void ClearCache() => Store.ClearCache();
    }
}
