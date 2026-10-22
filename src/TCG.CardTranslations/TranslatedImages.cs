using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using TCG.CardTranslations.Core;
using UnityEngine;

using static TCG.Shared.Lang;
namespace TCG.CardTranslations
{
    // Loads translated card images on demand, a few per frame, and keeps the most
    // recently used ones in memory. Images on screen are never unloaded.
    internal sealed class TranslatedImages
    {
        sealed class Entry
        {
            public Texture2D Texture = null!;
            public Sprite Sprite = null!;
            public LinkedListNode<string> Node = null!;
        }

        // What an Image component is waiting for: our file, and EPL's original asset as a fallback.
        sealed class Want
        {
            public Component Image = null!;
            public string Path = "";
            public object Asset = null!;
        }

        readonly Dictionary<string, Entry> cache = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        readonly LinkedList<string> recent = new LinkedList<string>();
        readonly Dictionary<int, Want> wants = new Dictionary<int, Want>();
        readonly Queue<string> queue = new Queue<string>();
        readonly HashSet<string> queued = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> failed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly Action<Component, object> fallback;
        readonly Action<string> log;
        readonly Func<int> capacity;
        readonly Func<bool> compress;
        readonly string? diskCache;
        int pngLoads, cacheLoads;
        double pngMs, cacheMs;
        bool cacheWriteWarned;

        public TranslatedImages(Action<Component, object> fallback, Action<string> log, Func<int> capacity, Func<bool> compress, string? diskCache)
        {
            this.fallback = fallback; this.log = log; this.capacity = capacity; this.compress = compress; this.diskCache = diskCache;
        }

        public int Cached => cache.Count;
        public string Stats => pngLoads + cacheLoads == 0 ? T("aucune image chargée")
            : F("{0} image(s) préparée(s) depuis le PNG ({1:0} ms en moyenne), {2} relue(s) depuis le cache ({3:0.0} ms en moyenne), {4} en mémoire",
                pngLoads, pngLoads == 0 ? 0 : pngMs / pngLoads, cacheLoads, cacheLoads == 0 ? 0 : cacheMs / cacheLoads, cache.Count);

        public bool HasFailed(string path) => failed.Contains(path);

        // Shows the translated image on this Image: now if it is already loaded, otherwise
        // within a few frames. The caller has cancelled EPL's own pending request.
        public void Show(Component image, string path, object asset)
        {
            wants[image.GetInstanceID()] = new Want { Image = image, Path = path, Asset = asset };
            if (cache.TryGetValue(path, out var entry))
            {
                Touch(entry);
                ImageSprite.Set(image, entry.Sprite);
                return;
            }
            if (queued.Add(path)) queue.Enqueue(path);
        }

        // The Image now shows something else (another card, a base game card): stop waiting for our file.
        public void Forget(Component? image)
        {
            if (image != null) wants.Remove(image.GetInstanceID());
        }

        public void Update()
        {
            if (queue.Count == 0) return;
            var budget = Stopwatch.StartNew();
            // At least one image per frame, then more while the frame budget allows.
            do
            {
                var path = queue.Dequeue();
                queued.Remove(path);
                if (cache.ContainsKey(path)) { Deliver(path); continue; }
                var watch = Stopwatch.StartNew();
                var entry = LoadCached(path, out var cacheFile);
                var fromCache = entry != null;
                entry ??= Load(path, cacheFile);
                watch.Stop();
                if (entry == null) { Fail(path); continue; }
                if (fromCache) { cacheLoads++; cacheMs += watch.Elapsed.TotalMilliseconds; }
                else { pngLoads++; pngMs += watch.Elapsed.TotalMilliseconds; }
                cache[path] = entry;
                entry.Node = recent.AddFirst(path);
                Deliver(path);
            }
            while (queue.Count > 0 && budget.ElapsedMilliseconds < 6);
            Trim();
        }

        Entry? Load(string path, string? cacheFile)
        {
            Texture2D? texture = null;
            try
            {
                var bytes = File.ReadAllBytes(path);
                var name = System.IO.Path.GetFileNameWithoutExtension(path);
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = name };
                if (!texture.LoadImage(bytes, false)) throw new InvalidDataException(T("format d'image non reconnu"));
                // Fully opaque images are stored without alpha: half the memory once compressed.
                if (texture.format != TextureFormat.RGB24)
                {
                    var pixels = texture.GetPixels32();
                    var opaque = true;
                    for (var i = 0; i < pixels.Length; i++) if (pixels[i].a != 255) { opaque = false; break; }
                    if (opaque)
                    {
                        var rgb = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, true) { name = name };
                        rgb.SetPixels32(pixels);
                        UnityEngine.Object.Destroy(texture);
                        texture = rgb;
                    }
                }
                texture.Apply(true, false);
                // Block compression (DXT) divides memory by 4 to 6; it needs dimensions that are multiples of 4.
                if (compress() && texture.width % 4 == 0 && texture.height % 4 == 0) texture.Compress(true);
                if (cacheFile != null) Store(cacheFile, texture);
                return Finish(texture);
            }
            catch (Exception e)
            {
                if (texture != null) UnityEngine.Object.Destroy(texture);
                log(F("Image ignorée ({0}) : {1}", System.IO.Path.GetFileName(path), e.Message));
                return null;
            }
        }

        // The image as it was prepared last time, if its PNG has not changed since.
        Entry? LoadCached(string path, out string? cacheFile)
        {
            cacheFile = null;
            if (diskCache == null) return null;
            Texture2D? texture = null;
            try
            {
                cacheFile = ImageCache.FileFor(diskCache, path, compress());
                var stored = ImageCache.Read(cacheFile);
                if (stored == null) return null;
                texture = new Texture2D(stored.Width, stored.Height, (TextureFormat)stored.Format, stored.MipCount, false) { name = System.IO.Path.GetFileNameWithoutExtension(path) };
                texture.LoadRawTextureData(stored.Data);
                return Finish(texture);
            }
            catch (Exception e)
            {
                // Unreadable or from another Unity version: prepared again from the PNG and rewritten.
                if (texture != null) UnityEngine.Object.Destroy(texture);
                if (cacheFile != null) try { File.Delete(cacheFile); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                log(F("Cache ignoré pour {0} : {1}", System.IO.Path.GetFileName(path), e.Message));
                return null;
            }
        }

        void Store(string cacheFile, Texture2D texture)
        {
            try
            {
                ImageCache.Write(cacheFile, new ImageCache.Entry
                {
                    Width = texture.width, Height = texture.height, Format = (int)texture.format,
                    MipCount = texture.mipmapCount, Data = texture.GetRawTextureData(),
                });
            }
            catch (Exception e)
            {
                if (!cacheWriteWarned) { cacheWriteWarned = true; log(F("Cache des images non écrit : {0}", e.Message)); }
            }
        }

        static Entry Finish(Texture2D texture)
        {
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Trilinear;
            texture.anisoLevel = 4;
            texture.Apply(false, true);
            var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            sprite.name = texture.name;
            return new Entry { Texture = texture, Sprite = sprite };
        }

        void Deliver(string path)
        {
            var entry = cache[path];
            Touch(entry);
            foreach (var want in wants.Values.Where(w => w.Path == path).ToList())
                if (want.Image != null) ImageSprite.Set(want.Image, entry.Sprite);
        }

        // A file that cannot be read: show the original English card instead, and never try it again.
        void Fail(string path)
        {
            failed.Add(path);
            foreach (var pair in wants.Where(w => w.Value.Path == path).ToList())
            {
                wants.Remove(pair.Key);
                if (pair.Value.Image != null) fallback(pair.Value.Image, pair.Value.Asset);
            }
        }

        void Touch(Entry entry)
        {
            if (entry.Node.List != null && entry.Node != recent.First) { recent.Remove(entry.Node); recent.AddFirst(entry.Node); }
        }

        // Unloads the least recently used images that no Image currently shows.
        void Trim()
        {
            var max = Math.Max(16, capacity());
            if (cache.Count <= max) return;
            foreach (var dead in wants.Where(w => w.Value.Image == null).Select(w => w.Key).ToList()) wants.Remove(dead);
            var shown = new HashSet<string>(wants.Values.Where(w => cache.TryGetValue(w.Path, out var e) && ImageSprite.Get(w.Image) == e.Sprite).Select(w => w.Path), StringComparer.OrdinalIgnoreCase);
            var node = recent.Last;
            while (cache.Count > max && node != null)
            {
                var previous = node.Previous;
                if (!shown.Contains(node.Value))
                {
                    var entry = cache[node.Value];
                    cache.Remove(node.Value);
                    recent.Remove(node);
                    UnityEngine.Object.Destroy(entry.Sprite);
                    UnityEngine.Object.Destroy(entry.Texture);
                }
                node = previous;
            }
        }
    }

    // UnityEngine.UI.Image.sprite, by reflection: the game's UnityEngine.UI.dll targets
    // .NET Standard 2.1 and cannot be referenced from a .NET Standard 2.0 plugin.
    internal static class ImageSprite
    {
        static readonly System.Reflection.PropertyInfo? property = HarmonyLib.AccessTools.Property(HarmonyLib.AccessTools.TypeByName("UnityEngine.UI.Image"), "sprite");
        public static bool Available => property != null;
        public static void Set(Component image, Sprite sprite) => property!.SetValue(image, sprite, null);
        public static Sprite? Get(Component image) => property!.GetValue(image, null) as Sprite;
    }
}
