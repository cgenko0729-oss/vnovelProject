using System;
using System.Collections.Generic;
using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// 私密相册：偷拍模式的照片，persistentDataPath/vn_secret_photos/，与大头贴相册互相独立，
    /// 画廊里是单独的「私密」标签页。条目记的是**拍摄时的舞台信息**，现在只当照片信息显示，
    /// 将来要做评分或图鉴时不用让玩家重拍。存储逻辑在 <see cref="VNAlbumStore{TEntry}"/>。
    /// </summary>
    public static class VNSecretAlbum
    {
        public const int Capacity = VNAlbumStore<Entry>.Capacity;

        [Serializable]
        public class Entry : VNAlbumEntry
        {
            public string character;   // 被拍角色 id（风景照为空）
            public string expression;  // 她当时的表情
            public string background;  // 背景 id
            public int zoomPercent;    // 缩放 ×100（120 = 1.2x）
            public int month;          // 月序（flag，没在养成模式时为 0）

            public float Zoom => zoomPercent > 0 ? zoomPercent / 100f : 1f;
        }

        const string NoCharacterTag = "scene";

        static readonly VNAlbumStore<Entry> Store = new VNAlbumStore<Entry>("vn_secret_photos", "VNSecretPhoto",
            file =>
            {
                string tag = VNAlbumStore<Entry>.CharacterFromFileName(file);
                return new Entry { character = tag == NoCharacterTag ? "" : tag };
            });

        public static IReadOnlyList<Entry> All => Store.All;
        public static int Count => Store.Count;
        public static bool IsFull => Store.IsFull;

        /// <summary>存一张。相册已满或写盘失败返回 null（调用方要提示，否则玩家白扣胶卷）；调用方负责销毁 Texture2D。</summary>
        public static Entry Add(Texture2D texture, string character, string expression,
            string background, float zoom, int month) =>
            Store.Add(texture, "secret", VNAlbumStore<Entry>.SanitizeFileName(character, NoCharacterTag),
                new Entry
                {
                    character = character ?? "",
                    expression = expression ?? "",
                    background = background ?? "",
                    zoomPercent = Mathf.RoundToInt(zoom * 100f),
                    month = month,
                });

        public static bool Delete(string file) => Store.Delete(file);
        public static Sprite LoadThumbnail(string file) => Store.LoadThumbnail(file);
        public static Sprite LoadSprite(string file) => Store.LoadSprite(file);
        public static void ClearCache() => Store.ClearCache();
    }
}
