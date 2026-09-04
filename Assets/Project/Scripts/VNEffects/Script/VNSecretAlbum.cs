using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// 私密相册 —— 偷拍模式拍下的照片，全局永久存储，与 20 槽存档系统完全分离，
    /// 也与大头贴相册（<see cref="VNPhotoAlbum"/>）分离：目录、索引、容量各自独立，
    /// 画廊里是单独一个「私密」标签页（解锁后才出现）。
    ///
    /// 结构照抄 VNPhotoAlbum（写盘 / 索引对账 / LRU 纹理缓存 / 独立缩略图缓存），
    /// 只有条目字段不同：这里记的是**拍摄时的舞台信息**（角色 / 表情 / 背景 / 缩放 / 月序），
    /// 现在只当照片信息显示，将来要做评分或图鉴时不用让玩家重拍。
    ///
    /// 文件布局：
    ///   persistentDataPath/vn_secret_photos/secret_20260904_183045_123_星野结衣.png
    ///   persistentDataPath/vn_secret_photos/index.json
    /// </summary>
    public static class VNSecretAlbum
    {
        public const int Capacity = 200;
        const int TextureCacheSize = 12;
        const int ThumbnailWidth = 192;
        const string DirName = "vn_secret_photos";
        const string FilePrefix = "secret";

        [Serializable]
        public class Entry
        {
            public string file;        // 文件名（不含目录）
            public long ticks;         // 拍摄时间
            public string character;   // 被拍角色 id（风景照为空）
            public string expression;  // 她当时的表情
            public string background;  // 背景 id
            public int zoomPercent;    // 缩放 ×100（120 = 1.2x）
            public int month;          // 月序（flag，没在养成模式时为 0）

            public DateTime Time => new DateTime(ticks);
            public float Zoom => zoomPercent > 0 ? zoomPercent / 100f : 1f;
        }

        [Serializable]
        class SaveShape
        {
            public List<Entry> photos = new List<Entry>();
        }

        static List<Entry> _entries;
        static readonly Dictionary<string, Texture2D> _textureCache = new Dictionary<string, Texture2D>();
        static readonly List<string> _cacheOrder = new List<string>();
        static readonly Dictionary<string, Sprite> _thumbnails = new Dictionary<string, Sprite>();

        public static string Dir => Path.Combine(Application.persistentDataPath, DirName);
        static string IndexPath => Path.Combine(Dir, "index.json");

        /// <summary>全部照片，新→旧</summary>
        public static IReadOnlyList<Entry> All
        {
            get { EnsureLoaded(); return _entries; }
        }

        public static int Count
        {
            get { EnsureLoaded(); return _entries.Count; }
        }

        public static bool IsFull => Count >= Capacity;

        // ------------------------------------------------------------------
        // 读写
        // ------------------------------------------------------------------

        static void EnsureLoaded()
        {
            if (_entries != null) return;
            _entries = new List<Entry>();

            try
            {
                if (File.Exists(IndexPath))
                {
                    var data = JsonUtility.FromJson<SaveShape>(File.ReadAllText(IndexPath, Encoding.UTF8));
                    if (data?.photos != null)
                        foreach (var e in data.photos)
                            if (e != null && !string.IsNullOrEmpty(e.file)) _entries.Add(e);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[VNSecretPhoto] 相册索引读取失败（按空相册处理）：{e.Message}");
            }

            // 索引与磁盘对账：丢文件的条目删掉，没登记的 png 补回来
            try
            {
                if (Directory.Exists(Dir))
                {
                    _entries.RemoveAll(e => !File.Exists(Path.Combine(Dir, e.file)));

                    var known = new HashSet<string>();
                    foreach (var e in _entries) known.Add(e.file);

                    foreach (var path in Directory.GetFiles(Dir, "*.png"))
                    {
                        string name = Path.GetFileName(path);
                        if (known.Contains(name)) continue;
                        _entries.Add(new Entry
                        {
                            file = name,
                            ticks = File.GetLastWriteTime(path).Ticks,
                            character = GuessCharacterFromFileName(name),
                        });
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[VNSecretPhoto] 相册目录对账失败：{e.Message}");
            }

            Sort();
        }

        static void Sort() => _entries.Sort((a, b) => b.ticks.CompareTo(a.ticks));

        static void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var shape = new SaveShape { photos = _entries };
                File.WriteAllText(IndexPath, JsonUtility.ToJson(shape, true), Encoding.UTF8);
            }
            catch (Exception e)
            {
                Debug.LogError($"[VNSecretPhoto] 相册索引写入失败：{e.Message}");
            }
        }

        /// <summary>
        /// 存一张。返回新条目；相册已满或写盘失败返回 null（调用方要提示，否则玩家白扣胶卷）。
        /// 调用方负责销毁传进来的 Texture2D。
        /// </summary>
        public static Entry Add(Texture2D texture, string character, string expression,
            string background, float zoom, int month)
        {
            if (texture == null) return null;
            EnsureLoaded();
            if (_entries.Count >= Capacity)
            {
                Debug.LogWarning($"[VNSecretPhoto] 私密相册已满（{Capacity} 张），本张未保存");
                return null;
            }

            var now = DateTime.Now;
            string file = $"{FilePrefix}_{now:yyyyMMdd_HHmmss_fff}_{SanitizeFileName(character)}.png";

            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllBytes(Path.Combine(Dir, file), texture.EncodeToPNG());
            }
            catch (Exception e)
            {
                Debug.LogError($"[VNSecretPhoto] 照片写盘失败：{e.Message}");
                return null;
            }

            var entry = new Entry
            {
                file = file,
                ticks = now.Ticks,
                character = character ?? "",
                expression = expression ?? "",
                background = background ?? "",
                zoomPercent = Mathf.RoundToInt(zoom * 100f),
                month = month,
            };
            _entries.Add(entry);
            Sort();
            Save();
            return entry;
        }

        /// <summary>删除一张（同时删磁盘 png）。文件不存在也算删成功。</summary>
        public static bool Delete(string file)
        {
            if (string.IsNullOrEmpty(file)) return false;
            EnsureLoaded();

            try
            {
                string path = Path.Combine(Dir, file);
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception e)
            {
                Debug.LogError($"[VNSecretPhoto] 照片删除失败：{e.Message}");
                return false;
            }

            DropFromCache(file);
            if (_thumbnails.TryGetValue(file, out var thumb))
            {
                if (thumb != null)
                {
                    var tex = thumb.texture;
                    UnityEngine.Object.Destroy(thumb);
                    if (tex != null) UnityEngine.Object.Destroy(tex);
                }
                _thumbnails.Remove(file);
            }
            int removed = _entries.RemoveAll(e => e.file == file);
            if (removed > 0) Save();
            return true;
        }

        // ------------------------------------------------------------------
        // 纹理读取（带 LRU 缓存）
        // ------------------------------------------------------------------

        /// <summary>读一张照片的纹理；失败返回 null。纹理由相册持有，调用方不要销毁。</summary>
        public static Texture2D LoadTexture(string file)
        {
            if (string.IsNullOrEmpty(file)) return null;
            if (_textureCache.TryGetValue(file, out var cached) && cached != null)
            {
                Touch(file);
                return cached;
            }

            try
            {
                string path = Path.Combine(Dir, file);
                if (!File.Exists(path)) return null;

                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                {
                    name = file,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.DontSave,
                };
                if (!tex.LoadImage(File.ReadAllBytes(path)))
                {
                    UnityEngine.Object.Destroy(tex);
                    return null;
                }

                _textureCache[file] = tex;
                Touch(file);
                TrimCache();
                return tex;
            }
            catch (Exception e)
            {
                Debug.LogError($"[VNSecretPhoto] 照片读取失败（{file}）：{e.Message}");
                return null;
            }
        }

        /// <summary>
        /// 缩略图（网格用）。单独一份不驱逐的缓存——网格一屏几十张，
        /// 走 12 张的 LRU 会把先加载的驱逐掉、Sprite 变白块（同 VNPhotoAlbum 的教训）。
        /// </summary>
        public static Sprite LoadThumbnail(string file)
        {
            if (string.IsNullOrEmpty(file)) return null;
            if (_thumbnails.TryGetValue(file, out var cached) && cached != null) return cached;

            Texture2D full = null;
            try
            {
                string path = Path.Combine(Dir, file);
                if (!File.Exists(path)) return null;

                full = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave };
                if (!full.LoadImage(File.ReadAllBytes(path)))
                {
                    UnityEngine.Object.Destroy(full);
                    return null;
                }

                int height = Mathf.Max(1, Mathf.RoundToInt(ThumbnailWidth * full.height / (float)full.width));
                var small = new Texture2D(ThumbnailWidth, height, TextureFormat.RGBA32, false)
                {
                    name = "thumb_" + file,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.DontSave,
                };
                var pixels = new Color[ThumbnailWidth * height];
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < ThumbnailWidth; x++)
                        pixels[y * ThumbnailWidth + x] = full.GetPixelBilinear(
                            (x + 0.5f) / ThumbnailWidth, (y + 0.5f) / height);
                small.SetPixels(pixels);
                small.Apply(false);

                var sprite = Sprite.Create(small, new Rect(0, 0, ThumbnailWidth, height),
                    new Vector2(0.5f, 0.5f), 100f);
                sprite.name = "thumb_" + file;
                sprite.hideFlags = HideFlags.DontSave;
                _thumbnails[file] = sprite;
                return sprite;
            }
            catch (Exception e)
            {
                Debug.LogError($"[VNSecretPhoto] 缩略图生成失败（{file}）：{e.Message}");
                return null;
            }
            finally
            {
                if (full != null) UnityEngine.Object.Destroy(full);
            }
        }

        /// <summary>把一张照片包成 Sprite（每次调用都新建 Sprite，纹理仍走缓存）</summary>
        public static Sprite LoadSprite(string file)
        {
            var tex = LoadTexture(file);
            if (tex == null) return null;
            var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f), 100f);
            sprite.name = file;
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }

        /// <summary>关掉相册界面时调用，把解码出来的纹理与缩略图都放掉</summary>
        public static void ClearCache()
        {
            foreach (var kv in _textureCache)
                if (kv.Value != null) UnityEngine.Object.Destroy(kv.Value);
            _textureCache.Clear();
            _cacheOrder.Clear();

            foreach (var kv in _thumbnails)
            {
                if (kv.Value == null) continue;
                var tex = kv.Value.texture;
                UnityEngine.Object.Destroy(kv.Value);
                if (tex != null) UnityEngine.Object.Destroy(tex);
            }
            _thumbnails.Clear();
        }

        static void Touch(string file)
        {
            _cacheOrder.Remove(file);
            _cacheOrder.Add(file);
        }

        static void TrimCache()
        {
            while (_cacheOrder.Count > TextureCacheSize)
                DropFromCache(_cacheOrder[0]);
        }

        static void DropFromCache(string file)
        {
            _cacheOrder.Remove(file);
            if (_textureCache.TryGetValue(file, out var tex))
            {
                if (tex != null) UnityEngine.Object.Destroy(tex);
                _textureCache.Remove(file);
            }
        }

        // ------------------------------------------------------------------
        // 工具
        // ------------------------------------------------------------------

        static string SanitizeFileName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "scene";
            var sb = new StringBuilder(raw.Length);
            foreach (char c in raw)
                sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ||
                          c == '_' || c == ' ' ? '-' : c);
            return sb.ToString();
        }

        /// <summary>从 secret_日期_时间_毫秒_角色.png 里把角色名抠出来（索引丢失时的兜底）</summary>
        static string GuessCharacterFromFileName(string file)
        {
            string name = Path.GetFileNameWithoutExtension(file);
            var parts = name.Split('_');
            if (parts.Length < 5) return "";
            return parts[4] == "scene" ? "" : parts[4];
        }
    }
}
