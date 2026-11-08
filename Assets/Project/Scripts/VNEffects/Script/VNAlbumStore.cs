using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VNEffects
{
    /// <summary>相册条目的公共字段；各相册在子类里加自己的拍摄信息。</summary>
    [Serializable]
    public class VNAlbumEntry
    {
        public string file;   // 文件名（不含目录）
        public long ticks;    // 拍摄时间（DateTime.Ticks）

        public DateTime Time => new DateTime(ticks);
    }

    /// <summary>
    /// 相册存储：全局永久保存，与 20 槽存档完全分离（读旧档、开新周目都不该把照片弄丢）。
    /// 负责写盘、索引对账、纹理缓存；<see cref="VNPhotoAlbum"/> 与 <see cref="VNSecretAlbum"/>
    /// 各持有一份，目录、索引、容量互相独立。
    ///
    /// 文件布局：persistentDataPath/&lt;目录&gt;/&lt;前缀&gt;_日期_时间_毫秒_角色.png + index.json。
    /// index.json 损坏或丢失也不会丢照片：加载时把目录里没登记的 png 按文件名补进索引
    /// （文件名里就带时间与角色，这是刻意的冗余设计）。
    /// </summary>
    public sealed class VNAlbumStore<TEntry> where TEntry : VNAlbumEntry
    {
        public const int Capacity = 200;
        const int TextureCacheSize = 12;   // 翻相册时不要把 200 张全解码进内存
        const int ThumbnailWidth = 192;    // 192×144 约 110KB，200 张也才 22MB

        [Serializable]
        class SaveShape
        {
            public List<TEntry> photos = new List<TEntry>();
        }

        readonly string _dirName;
        readonly string _logTag;
        readonly Func<string, TEntry> _recover;   // 索引丢失时，按文件名补一条
        List<TEntry> _entries;
        readonly Dictionary<string, Texture2D> _textureCache = new Dictionary<string, Texture2D>();
        readonly List<string> _cacheOrder = new List<string>();
        readonly Dictionary<string, Sprite> _thumbnails = new Dictionary<string, Sprite>();

        public VNAlbumStore(string dirName, string logTag, Func<string, TEntry> recover)
        {
            _dirName = dirName;
            _logTag = logTag;
            _recover = recover;
        }

        public string Dir => Path.Combine(Application.persistentDataPath, _dirName);
        string IndexPath => Path.Combine(Dir, "index.json");

        /// <summary>全部照片，新→旧</summary>
        public IReadOnlyList<TEntry> All { get { EnsureLoaded(); return _entries; } }
        public int Count { get { EnsureLoaded(); return _entries.Count; } }
        public bool IsFull => Count >= Capacity;

        void EnsureLoaded()
        {
            if (_entries != null) return;
            _entries = new List<TEntry>();

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
                Debug.LogError($"[{_logTag}] 相册索引读取失败（按空相册处理）：{e.Message}");
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
                        var entry = _recover(name);
                        entry.file = name;
                        entry.ticks = File.GetLastWriteTime(path).Ticks;
                        _entries.Add(entry);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[{_logTag}] 相册目录对账失败：{e.Message}");
            }

            Sort();
        }

        void Sort() => _entries.Sort((a, b) => b.ticks.CompareTo(a.ticks));

        void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(IndexPath,
                    JsonUtility.ToJson(new SaveShape { photos = _entries }, true), Encoding.UTF8);
            }
            catch (Exception e)
            {
                Debug.LogError($"[{_logTag}] 相册索引写入失败：{e.Message}");
            }
        }

        /// <summary>
        /// 存一张。相册已满或写盘失败返回 null（调用方要提示）。
        /// 调用方负责销毁传进来的 Texture2D。fileTag 是已清洗过的文件名角色段。
        /// </summary>
        public TEntry Add(Texture2D texture, string prefix, string fileTag, TEntry entry)
        {
            if (texture == null) return null;
            EnsureLoaded();
            if (_entries.Count >= Capacity)
            {
                Debug.LogWarning($"[{_logTag}] 相册已满（{Capacity} 张），本张未保存");
                return null;
            }

            var now = DateTime.Now;
            string file = $"{prefix}_{now:yyyyMMdd_HHmmss_fff}_{fileTag}.png";

            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllBytes(Path.Combine(Dir, file), texture.EncodeToPNG());
            }
            catch (Exception e)
            {
                Debug.LogError($"[{_logTag}] 照片写盘失败：{e.Message}");
                return null;
            }

            entry.file = file;
            entry.ticks = now.Ticks;
            _entries.Add(entry);
            Sort();
            Save();
            return entry;
        }

        /// <summary>删除一张（同时删磁盘 png）。文件不存在也算删成功。</summary>
        public bool Delete(string file)
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
                Debug.LogError($"[{_logTag}] 照片删除失败：{e.Message}");
                return false;
            }

            DropFromCache(file);
            if (_thumbnails.TryGetValue(file, out var thumb))
            {
                DestroySprite(thumb);
                _thumbnails.Remove(file);
            }
            int removed = _entries.RemoveAll(e => e.file == file);
            if (removed > 0) Save();
            return true;
        }

        /// <summary>读一张照片的纹理；失败返回 null。纹理由相册持有，调用方不要销毁。</summary>
        public Texture2D LoadTexture(string file)
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
                    Object.Destroy(tex);
                    return null;
                }

                _textureCache[file] = tex;
                Touch(file);
                while (_cacheOrder.Count > TextureCacheSize) DropFromCache(_cacheOrder[0]);
                return tex;
            }
            catch (Exception e)
            {
                Debug.LogError($"[{_logTag}] 照片读取失败（{file}）：{e.Message}");
                return null;
            }
        }

        /// <summary>
        /// 缩略图（网格用）。**不能让网格走 LoadTexture**：那是 12 张的 LRU，
        /// 一屏几十张时先加载的纹理会被驱逐，而 Sprite 还引用着它，直接变成白块。
        /// 所以缩略图单独一份缓存、降到 192 宽、不驱逐。
        /// </summary>
        public Sprite LoadThumbnail(string file)
        {
            if (string.IsNullOrEmpty(file)) return null;
            if (_thumbnails.TryGetValue(file, out var cached) && cached != null) return cached;

            Texture2D full = null;
            try
            {
                string path = Path.Combine(Dir, file);
                if (!File.Exists(path)) return null;

                full = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave };
                if (!full.LoadImage(File.ReadAllBytes(path))) return null;

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
                Debug.LogError($"[{_logTag}] 缩略图生成失败（{file}）：{e.Message}");
                return null;
            }
            finally
            {
                if (full != null) Object.Destroy(full);
            }
        }

        /// <summary>把一张照片包成 Sprite（每次调用都新建 Sprite，纹理仍走缓存）</summary>
        public Sprite LoadSprite(string file)
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
        public void ClearCache()
        {
            foreach (var kv in _textureCache)
                if (kv.Value != null) Object.Destroy(kv.Value);
            _textureCache.Clear();
            _cacheOrder.Clear();

            foreach (var kv in _thumbnails) DestroySprite(kv.Value);
            _thumbnails.Clear();
        }

        static void DestroySprite(Sprite sprite)
        {
            if (sprite == null) return;
            var tex = sprite.texture;
            Object.Destroy(sprite);
            if (tex != null) Object.Destroy(tex);
        }

        void Touch(string file)
        {
            _cacheOrder.Remove(file);
            _cacheOrder.Add(file);
        }

        void DropFromCache(string file)
        {
            _cacheOrder.Remove(file);
            if (_textureCache.TryGetValue(file, out var tex))
            {
                if (tex != null) Object.Destroy(tex);
                _textureCache.Remove(file);
            }
        }

        /// <summary>把角色名清洗成文件名的一段（'_' 是分隔符，必须换掉）。</summary>
        public static string SanitizeFileName(string raw, string fallback)
        {
            if (string.IsNullOrEmpty(raw)) return fallback;
            var sb = new StringBuilder(raw.Length);
            foreach (char c in raw)
                sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ||
                          c == '_' || c == ' ' ? '-' : c);
            return sb.ToString();
        }

        /// <summary>从 前缀_日期_时间_毫秒_角色.png 里把角色名抠出来（索引丢失时的兜底）</summary>
        public static string CharacterFromFileName(string file)
        {
            var parts = Path.GetFileNameWithoutExtension(file).Split('_');
            return parts.Length >= 5 ? parts[4] : "";
        }
    }
}
