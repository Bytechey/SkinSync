using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;

namespace SkinSyncMod
{
    /// <summary>本地皮肤目录与 byte[] 之间的打包/解包；含路径/大小校验、内存缓存与可选落盘，给实验性"按需拉取皮肤包"用。</summary>
    internal static class SkinPackCodec
    {
        private const int MAGIC = 0x4B505331;
        internal const int MAX_PACK_SIZE = 512 * 1024;
        internal const int MAX_FILE_SIZE = 64 * 1024;
        internal const int MAX_FILE_COUNT = 256;

        private static readonly Dictionary<string, Dictionary<string, byte[]>> _cache
            = new Dictionary<string, Dictionary<string, byte[]>>();

        internal static bool HasInMemory(string skinID)
            => !string.IsNullOrEmpty(skinID) && _cache.ContainsKey(skinID);

        internal static Dictionary<string, byte[]> GetInMemory(string skinID)
            => _cache.TryGetValue(skinID, out var d) ? d : null;

        /// <summary>打包 plugins/CustomSprites/&lt;skinID&gt;/ 下所有 .png 为 byte[]；缺目录、超限、含不合法路径返回 null。</summary>
        internal static byte[] PackLocalSkin(string skinID)
        {
            if (!IsSafeSkinId(skinID)) return null;
            string baseDir = SkinPathResolver.GetSkinDir(skinID);
            if (!Directory.Exists(baseDir)) return null;

            var files = new List<KeyValuePair<string, byte[]>>();
            foreach (string fp in Directory.GetFiles(baseDir, "*.png", SearchOption.AllDirectories))
            {
                string rel = MakeRelative(baseDir, fp);
                if (!IsSafeRelPath(rel)) continue;
                byte[] bytes;
                try { bytes = File.ReadAllBytes(fp); }
                catch { continue; }
                if (bytes.Length == 0 || bytes.Length > MAX_FILE_SIZE) continue;
                files.Add(new KeyValuePair<string, byte[]>(rel, bytes));
                if (files.Count > MAX_FILE_COUNT) return null;
            }
            foreach (string fp in Directory.GetFiles(baseDir, "*.zones.json", SearchOption.AllDirectories))
            {
                string rel = MakeRelative(baseDir, fp);
                if (!IsSafeRelPath(rel)) continue;
                byte[] bytes;
                try { bytes = File.ReadAllBytes(fp); }
                catch { continue; }
                if (bytes.Length == 0 || bytes.Length > MAX_FILE_SIZE) continue;
                files.Add(new KeyValuePair<string, byte[]>(rel, bytes));
                if (files.Count > MAX_FILE_COUNT) return null;
            }
            string baseSizesPath = Path.Combine(baseDir, "baseSizes.json");
            if (File.Exists(baseSizesPath))
            {
                try
                {
                    byte[] bs = File.ReadAllBytes(baseSizesPath);
                    if (bs.Length > 0 && bs.Length <= MAX_FILE_SIZE)
                        files.Add(new KeyValuePair<string, byte[]>("baseSizes.json", bs));
                }
                catch { }
            }
            string bloodJsonPath = Path.Combine(baseDir, "blood.json");
            if (File.Exists(bloodJsonPath))
            {
                try
                {
                    byte[] bj = File.ReadAllBytes(bloodJsonPath);
                    if (bj.Length > 0 && bj.Length <= MAX_FILE_SIZE)
                        files.Add(new KeyValuePair<string, byte[]>("blood.json", bj));
                }
                catch { }
            }
            string accPath = Path.Combine(baseDir, "accessories.json");
            if (File.Exists(accPath))
            {
                try
                {
                    byte[] aj = File.ReadAllBytes(accPath);
                    if (aj.Length > 0 && aj.Length <= MAX_FILE_SIZE)
                        files.Add(new KeyValuePair<string, byte[]>("accessories.json", aj));
                }
                catch { }
            }
            string wingsPath = Path.Combine(baseDir, "wings.json");
            if (File.Exists(wingsPath))
            {
                try
                {
                    byte[] wj = File.ReadAllBytes(wingsPath);
                    if (wj.Length > 0 && wj.Length <= MAX_FILE_SIZE)
                        files.Add(new KeyValuePair<string, byte[]>("wings.json", wj));
                }
                catch { }
            }
            if (files.Count == 0) return null;

            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write(MAGIC);
                byte[] idBytes = Encoding.UTF8.GetBytes(skinID);
                bw.Write(idBytes.Length);
                bw.Write(idBytes);
                bw.Write(files.Count);
                foreach (var kv in files)
                {
                    byte[] pathBytes = Encoding.UTF8.GetBytes(kv.Key);
                    bw.Write(pathBytes.Length);
                    bw.Write(pathBytes);
                    bw.Write(kv.Value.Length);
                    bw.Write(kv.Value);
                }
                bw.Flush();
                if (ms.Length > MAX_PACK_SIZE) return null;
                return ms.ToArray();
            }
        }

        /// <summary>解包 + 全字段校验；成功后写入内存缓存，可选落盘到 plugins/CustomSprites/&lt;skinID&gt;/。</summary>
        internal static bool UnpackAndCache(byte[] payload, bool persistToDisk, out string skinID)
        {
            skinID = null;
            if (payload == null || payload.Length < 16 || payload.Length > MAX_PACK_SIZE) return false;

            try
            {
                using (var ms = new MemoryStream(payload))
                using (var br = new BinaryReader(ms))
                {
                    if (br.ReadInt32() != MAGIC) return false;

                    int idLen = br.ReadInt32();
                    if (idLen <= 0 || idLen > 128) return false;
                    skinID = Encoding.UTF8.GetString(br.ReadBytes(idLen));
                    if (!IsSafeSkinId(skinID)) return false;

                    int fileCount = br.ReadInt32();
                    if (fileCount <= 0 || fileCount > MAX_FILE_COUNT) return false;

                    var dict = new Dictionary<string, byte[]>(fileCount);
                    for (int i = 0; i < fileCount; i++)
                    {
                        int pl = br.ReadInt32();
                        if (pl <= 0 || pl > 256) return false;
                        string path = Encoding.UTF8.GetString(br.ReadBytes(pl));
                        if (!IsSafeRelPath(path)) return false;

                        int dl = br.ReadInt32();
                        if (dl <= 0 || dl > MAX_FILE_SIZE) return false;
                        byte[] data = br.ReadBytes(dl);
                        if (data.Length != dl) return false;
                        if (path.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase))
                        {
                            if (data[0] != 0x89 || data[1] != 0x50 || data[2] != 0x4E || data[3] != 0x47) return false;
                        }
                        dict[path] = data;
                    }
                    _cache[skinID] = dict;
                    if (persistToDisk) PersistToDisk(skinID, dict, persistent: true);
                    else PersistToDisk(skinID, dict, persistent: false);
                    return true;
                }
            }
            catch (Exception ex)
            {
                SkinSyncMod.ModLog.Warning("SkinPackCodec.Unpack failed: " + ex.Message);
                return false;
            }
        }

        private static void PersistToDisk(string skinID, Dictionary<string, byte[]> dict, bool persistent)
        {
            try
            {
                string baseDir = persistent
                    ? Path.Combine(Paths.PluginPath, "CustomSprites", skinID)
                    : Path.Combine(SkinPathResolver.SyncCacheRoot, skinID);
                Directory.CreateDirectory(baseDir);
                foreach (var kv in dict)
                {
                    string fullPath = Path.Combine(baseDir, kv.Key);
                    string dir = Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    File.WriteAllBytes(fullPath, kv.Value);
                }
                SkinPathResolver.Invalidate(skinID);
                SkinSyncMod.ModLog.Info($"SkinPackCodec: 已落盘 {skinID}（{dict.Count} 个文件，persistent={persistent}）");
            }
            catch (Exception ex) { SkinSyncMod.ModLog.Warning("SkinPackCodec.PersistToDisk failed: " + ex.Message); }
        }

        private static string MakeRelative(string baseDir, string fullPath)
        {
            string rel = fullPath.Substring(baseDir.Length).TrimStart('/', '\\');
            return rel.Replace('\\', '/');
        }

        private static bool IsSafeSkinId(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length > 64) return false;
            foreach (char c in s)
            {
                if (char.IsLetterOrDigit(c)) continue;
                if (c == '_' || c == '-' || c == ' ' || c == '(' || c == ')') continue;
                return false;
            }
            return true;
        }

        private static bool IsSafeRelPath(string p)
        {
            if (string.IsNullOrEmpty(p) || p.Length > 256) return false;
            if (p.IndexOf("..", StringComparison.Ordinal) >= 0) return false;
            if (p[0] == '/' || p[0] == '\\') return false;
            if (p.Length >= 2 && p[1] == ':') return false;
            foreach (char c in p)
            {
                if (char.IsLetterOrDigit(c)) continue;
                if (c == '_' || c == '-' || c == '.' || c == '/' || c == ' ' || c == '(' || c == ')') continue;
                return false;
            }
            if (!p.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                && !p.EndsWith(".zones.json", StringComparison.OrdinalIgnoreCase)
                && !p.Equals("baseSizes.json", StringComparison.OrdinalIgnoreCase)
                && !p.Equals("blood.json", StringComparison.OrdinalIgnoreCase)
                && !p.Equals("accessories.json", StringComparison.OrdinalIgnoreCase)
                && !p.Equals("wings.json", StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }
    }
}
