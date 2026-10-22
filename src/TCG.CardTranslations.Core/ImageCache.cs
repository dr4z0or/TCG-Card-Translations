using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace TCG.CardTranslations.Core
{
    // Card images already decoded and compressed, stored in BepInEx\cache so that the next
    // games load them in a few milliseconds instead of decoding the PNG again.
    // A cache file is named after the image path, size, date and settings: editing a PNG
    // simply produces a new cache file, and Prune removes the ones no image uses any more.
    public static class ImageCache
    {
        const string Magic = "TCT1";
        public const string Extension = ".tex";

        public sealed class Entry
        {
            public int Width, Height, Format, MipCount;
            public byte[] Data = Array.Empty<byte>();
        }

        public static string FileFor(string cacheDir, string imagePath, bool compressed)
        {
            var info = new FileInfo(imagePath);
            var identity = string.Join("|", Path.GetFullPath(imagePath).ToLowerInvariant(), info.Length, info.LastWriteTimeUtc.Ticks, compressed ? "dxt" : "raw", Magic);
            using var sha = SHA1.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(identity));
            return Path.Combine(cacheDir, string.Concat(hash.Select(b => b.ToString("x2"))) + Extension);
        }

        public static byte[] Encode(Entry entry)
        {
            using var stream = new MemoryStream();
            using (var w = new BinaryWriter(stream))
            {
                w.Write(Encoding.ASCII.GetBytes(Magic));
                w.Write(entry.Width); w.Write(entry.Height); w.Write(entry.Format); w.Write(entry.MipCount);
                w.Write(entry.Data.Length);
                w.Write(entry.Data);
            }
            return stream.ToArray();
        }

        public static Entry? Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 24 || Encoding.ASCII.GetString(bytes, 0, 4) != Magic) return null;
            using var r = new BinaryReader(new MemoryStream(bytes, 4, bytes.Length - 4));
            var entry = new Entry { Width = r.ReadInt32(), Height = r.ReadInt32(), Format = r.ReadInt32(), MipCount = r.ReadInt32() };
            var length = r.ReadInt32();
            if (entry.Width <= 0 || entry.Height <= 0 || entry.MipCount <= 0 || length <= 0 || length != bytes.Length - 24) return null;
            entry.Data = r.ReadBytes(length);
            return entry;
        }

        public static Entry? Read(string file)
        {
            try { return File.Exists(file) ? Decode(File.ReadAllBytes(file)) : null; }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        // Written to a temporary file first, so a game closed mid-write never leaves a broken entry.
        public static void Write(string file, Entry entry)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var temp = file + ".tmp";
            File.WriteAllBytes(temp, Encode(entry));
            if (File.Exists(file)) File.Delete(file);
            File.Move(temp, file);
        }

        // Deletes cache files that belong to no current image (edited or removed PNGs).
        public static int Prune(string cacheDir, IEnumerable<string> keep)
        {
            if (!Directory.Exists(cacheDir)) return 0;
            var wanted = new HashSet<string>(keep.Select(Path.GetFileName), StringComparer.OrdinalIgnoreCase);
            var removed = 0;
            foreach (var file in Directory.GetFiles(cacheDir))
            {
                var name = Path.GetFileName(file);
                if (wanted.Contains(name)) continue;
                if (!name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) && !name.EndsWith(Extension + ".tmp", StringComparison.OrdinalIgnoreCase)) continue;
                try { File.Delete(file); removed++; } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            return removed;
        }
    }
}
