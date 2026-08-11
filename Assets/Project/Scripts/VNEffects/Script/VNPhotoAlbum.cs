using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// 相册 —— 玩家拍过的大头贴，全局永久存储，与 20 槽存档系统完全分离
    /// （和 VNCgUnlocks 同一个道理：读旧档、开新周目都不该把拍过的照片弄丢）。
    ///
    /// 文件布局：
    ///   persistentDataPath/vn_photos/photo_20260812_183045_123_亚里沙.png
    ///   persistentDataPath/vn_photos/index.json   ← 拍摄信息（时间/角色/主题/得分）
    ///
    /// index.json 损坏或丢失也不会丢照片：EnsureLoaded 会把目录里没登记的 png
    /// 按文件名补进索引（文件名里就带时间与角色，这是刻意的冗余设计）。
    /// </summary>
    public static class VNPhotoAlbum
    {
        /// <summary>相册上限。到顶后拒绝新增，由 UI 提示玩家去删</summary>
        public const int Capacity = 200;

        /// <summary>纹理缓存上限（翻相册时不要把 200 张全解码进内存）</summary>
        const int TextureCacheSize = 12;

        /// <summary>缩略图宽度（网格用）。192×144 约 110KB，200 张也才 22MB</summary>
        const int ThumbnailWidth = 192;

        [Serializable]
        public class Entry
        {
            public string file;      // 文件名（不含目录）
            public long ticks;       // 拍摄时间（DateTime.Ticks）
            public string her;       // 合影对象角色 id
            public string me;        // 主角角色 id
            public string theme;     // 主题 id（自由拍照为空）
            public int score;
            public int grade = -1;   // 2 完美 / 1 普通 / 0 失败；-1 = 自由拍照不评分

            public DateTime Time => new DateTime(ticks);
        }

        [Serializable]
        class SaveShape
        {
            public List<Entry> photos = new List<Entry>();
        }

        static List<Entry> _entries;
        static readonly Dictionary<string, Texture2D> _textureCache =
            new Dictionary<string, Texture2D>();
        static readonly List<string> _cacheOrder = new List<string>();
        static readonly Dictionary<string, Sprite> _thumbnails = new Dictionary<string, Sprite>();

        public static string Dir => Path.Combine(Application.persistentDataPath, "vn_photos");
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
                    var data = JsonUtility.FromJson<SaveShape>(
                        File.ReadAllText(IndexPath, Encoding.UTF8));
                    if (data?.photos != null)
                        foreach (var e in data.photos)
                            if (e != null && !string.IsNullOrEmpty(e.file)) _entries.Add(e);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[VNPhoto] 相册索引读取失败（按空相册处理）：{e.Message}");
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
                            her = GuessCharacterFromFileName(name),
                        });
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[VNPhoto] 相册目录对账失败：{e.Message}");
            }

            Sort();
        }

        static void Sort() => _entries.Sort((a, b) => b.ticks.CompareTo(a.ticks));

        static void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(IndexPath,
                    JsonUtility.ToJson(new SaveShape { photos = _entries }, true),
                    Encoding.UTF8);
            }
            catch (Exception e)
            {
                Debug.LogError($"[VNPhoto] 相册索引写入失败：{e.Message}");
            }
        }

        // ------------------------------------------------------------------
        // 增删
        // ------------------------------------------------------------------

        /// <summary>
        /// 把一张拍好的照片存进相册。返回新条目；相册已满或写盘失败返回 null。
        /// 注意：调用方负责销毁传进来的 Texture2D。
        /// </summary>
        public static Entry Add(Texture2D texture, string her, string me, string theme,
            int score, int grade)
        {
            if (texture == null) return null;
            EnsureLoaded();
            if (_entries.Count >= Capacity)
            {
                Debug.LogWarning($"[VNPhoto] 相册已满（{Capacity} 张），本张未保存");
                return null;
            }

            var now = DateTime.Now;
            string file = $"photo_{now:yyyyMMdd_HHmmss_fff}_{SanitizeFileName(her)}.png";

            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllBytes(Path.Combine(Dir, file), texture.EncodeToPNG());
            }
            catch (Exception e)
            {
                Debug.LogError($"[VNPhoto] 照片写盘失败：{e.Message}");
                return null;
            }

            var entry = new Entry
            {
                file = file,
                ticks = now.Ticks,
                her = her,
                me = me,
                theme = theme,
                score = score,
                grade = grade,
            };
            _entries.Add(entry);
            Sort();
            Save();
            return entry;
        }

        /// <summary>删除一张照片（同时删磁盘 png）。文件不存在也算删成功。</summary>
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
                Debug.LogError($"[VNPhoto] 照片删除失败：{e.Message}");
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

        /// <summary>读一张照片的纹理；失败返回 null。返回的纹理由相册持有，调用方不要销毁。</summary>
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
                Debug.LogError($"[VNPhoto] 照片读取失败（{file}）：{e.Message}");
                return null;
            }
        }

        /// <summary>
        /// 缩略图（网格用）。**不能让网格走 LoadTexture**：那是 12 张的 LRU，
        /// 一屏显示几十张时先加载的纹理会被驱逐，而 Sprite 还引用着它 —— 直接变成白块。
        /// 所以缩略图单独一份缓存、降到 192×144（约 110KB/张）、不驱逐。
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

                full = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.DontSave,
                };
                if (!full.LoadImage(File.ReadAllBytes(path)))
                {
                    UnityEngine.Object.Destroy(full);
                    return null;
                }

                int height = Mathf.Max(1, Mathf.RoundToInt(
                    ThumbnailWidth * full.height / (float)full.width));
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
                Debug.LogError($"[VNPhoto] 缩略图生成失败（{file}）：{e.Message}");
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
            if (string.IsNullOrEmpty(raw)) return "photo";
            var sb = new StringBuilder(raw.Length);
            foreach (char c in raw)
                sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ||
                          c == '_' || c == ' ' ? '-' : c);
            return sb.ToString();
        }

        /// <summary>从 photo_日期_时间_毫秒_角色.png 里把角色名抠出来（索引丢失时的兜底）</summary>
        static string GuessCharacterFromFileName(string file)
        {
            string name = Path.GetFileNameWithoutExtension(file);
            var parts = name.Split('_');
            return parts.Length >= 5 ? parts[4] : "";
        }
    }
}
