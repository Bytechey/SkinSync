using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;

namespace SkinSyncMod
{
    /// <summary>将皮肤目录文件打包为单包二进制负载，或解包校验并写入内存与缓存目录。</summary>
    internal static class SkinPackCodec
    {
        private const int MAGIC = 0x4B505331;
        internal const int SINGLE_PACKET_LIMIT = 48 * 1024;
        internal const int CHUNK_PAYLOAD_SIZE = 32 * 1024;
        internal const int MAX_PACK_SIZE = 2 * 1024 * 1024;
        internal const int MAX_FILE_SIZE = 512 * 1024;
        internal const int MAX_FILE_COUNT = 256;

        private static readonly Dictionary<string, Dictionary<string, byte[]>> _cache
            = new Dictionary<string, Dictionary<string, byte[]>>(StringComparer.Ordinal);
        private static readonly Dictionary<string, bool> _remoteAllowPersist
            = new Dictionary<string, bool>(StringComparer.Ordinal);

        internal static bool HasInMemory(string skinID)
            => !string.IsNullOrEmpty(skinID) && _cache.ContainsKey(skinID);

        internal static Dictionary<string, byte[]> GetInMemory(string skinID)
            => !string.IsNullOrEmpty(skinID) && _cache.TryGetValue(skinID, out var d) ? d : null;

        /// <summary>从首片缓冲区头部预读并校验皮肤名称，供分片组装器在超时或淘汰时释放对应请求去重锁。</summary>
        internal static bool TryPeekSkinId(byte[] buffer, int validBytes, out string skinID)
        {
            skinID = null;
            if (buffer == null || validBytes < 12 || buffer.Length < validBytes) return false;

            int magic = buffer[0] | (buffer[1] << 8) | (buffer[2] << 16) | (buffer[3] << 24);
            if (magic != MAGIC) return false;

            int idLen = buffer[4] | (buffer[5] << 8) | (buffer[6] << 16) | (buffer[7] << 24);
            if (idLen <= 0 || idLen > 128 || 8 + idLen > validBytes) return false;

            try
            {
                string candidate = Encoding.UTF8.GetString(buffer, 8, idLen);
                if (!SkinPathResolver.IsSafeSkinName(candidate)) return false;
                skinID = candidate;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>按来源归属打包指定皮肤并计算有效的持久化授权，防止中继节点篡改原作者隐私设置。</summary>
        internal static bool TryPackForShare(string skinID, bool localAllowPersist, out byte[] payload, out bool effectiveAllowPersist)
        {
            payload = null;
            effectiveAllowPersist = false;
            if (!SkinPathResolver.IsSafeSkinName(skinID)) return false;

            if (SkinPathResolver.HasLocalCustomSkin(skinID))
            {
                string localDir = SkinPathResolver.GetSkinDir(skinID);
                payload = TryPackDirectory(skinID, localDir);
                if (payload == null) return false;
                effectiveAllowPersist = localAllowPersist;
                return true;
            }

            if (_cache.TryGetValue(skinID, out var memDict))
            {
                payload = PackFromMemoryDict(skinID, memDict);
            }
            if (payload == null)
            {
                string syncDir = Path.Combine(SkinPathResolver.SyncCacheRoot, skinID);
                if (Directory.Exists(syncDir))
                {
                    payload = TryPackDirectory(skinID, syncDir);
                }
            }
            if (payload == null) return false;

            effectiveAllowPersist = _remoteAllowPersist.TryGetValue(skinID, out bool origAllow) && origAllow;
            return true;
        }

        /// <summary>读取指定皮肤的贴图与配置文件并编码为字节数组，超限或缺失时返回 null。</summary>
        internal static byte[] PackLocalSkin(string skinID)
        {
            return TryPackForShare(skinID, false, out byte[] payload, out _) ? payload : null;
        }

        private static byte[] TryPackDirectory(string skinID, string baseDir)
        {
            if (string.IsNullOrEmpty(baseDir) || !Directory.Exists(baseDir)) return null;

            var files = new List<KeyValuePair<string, byte[]>>();
            if (!TryCollectFiles(baseDir, "*.png", files)) return null;
            if (!TryCollectFiles(baseDir, "*.zones.json", files)) return null;
            if (!TryAppendRootJson(baseDir, "baseSizes.json", files)) return null;
            if (!TryAppendRootJson(baseDir, "blood.json", files)) return null;
            if (!TryAppendRootJson(baseDir, "accessories.json", files)) return null;
            if (!TryAppendRootJson(baseDir, "wings.json", files)) return null;
            return EncodeFiles(skinID, files);
        }

        private static byte[] PackFromMemoryDict(string skinID, Dictionary<string, byte[]> memDict)
        {
            if (memDict == null || memDict.Count == 0 || memDict.Count > MAX_FILE_COUNT) return null;
            var files = new List<KeyValuePair<string, byte[]>>(memDict.Count);
            foreach (var kv in memDict)
            {
                if (!IsSafeRelPath(kv.Key) || kv.Value == null || kv.Value.Length == 0 || kv.Value.Length > MAX_FILE_SIZE)
                    continue;
                files.Add(new KeyValuePair<string, byte[]>(kv.Key, kv.Value));
            }
            return EncodeFiles(skinID, files);
        }

        private static byte[] EncodeFiles(string skinID, List<KeyValuePair<string, byte[]>> files)
        {
            if (files == null || files.Count == 0 || files.Count > MAX_FILE_COUNT) return null;
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
                if (ms.Length > MAX_PACK_SIZE)
                {
                    SkinSyncMod.ModLog.Warning($"SkinPackCodec: 皮肤包 {skinID} 打包后 {ms.Length} bytes 超过传输上限 {MAX_PACK_SIZE}，无法同步。");
                    return null;
                }
                return ms.ToArray();
            }
        }

        private static bool TryCollectFiles(string baseDir, string pattern, List<KeyValuePair<string, byte[]>> files)
        {
            foreach (string fp in Directory.GetFiles(baseDir, pattern, SearchOption.AllDirectories))
            {
                string rel = MakeRelative(baseDir, fp);
                if (!IsSafeRelPath(rel)) continue;
                byte[] bytes;
                try { bytes = File.ReadAllBytes(fp); }
                catch { continue; }
                if (bytes.Length == 0 || bytes.Length > MAX_FILE_SIZE) continue;
                if (files.Count >= MAX_FILE_COUNT) return false;
                files.Add(new KeyValuePair<string, byte[]>(rel, bytes));
            }
            return true;
        }

        private static bool TryAppendRootJson(string baseDir, string fileName, List<KeyValuePair<string, byte[]>> files)
        {
            string path = Path.Combine(baseDir, fileName);
            if (!File.Exists(path)) return true;
            try
            {
                byte[] data = File.ReadAllBytes(path);
                if (data.Length > 0 && data.Length <= MAX_FILE_SIZE)
                {
                    if (files.Count >= MAX_FILE_COUNT) return false;
                    files.Add(new KeyValuePair<string, byte[]>(fileName, data));
                }
            }
            catch { }
            return true;
        }

        /// <summary>纯校验二进制皮肤包结构与文件头合法性而不写入内存或磁盘，供服务端保护本地同名皮肤时转发校验。</summary>
        internal static bool ValidatePack(byte[] payload, out string skinID)
        {
            return TryDecodePayload(payload, skipIfLocalOwned: false, out skinID, out _);
        }

        /// <summary>校验并解包皮肤数据字节流，保护本机同名自有皮肤不被覆盖，通过后写入内存缓存与磁盘目录。</summary>
        internal static bool UnpackAndCache(byte[] payload, bool persistToDisk, out string skinID, bool remoteAllowPersist = false)
        {
            if (!TryDecodePayload(payload, skipIfLocalOwned: true, out skinID, out Dictionary<string, byte[]> dict))
            {
                return false;
            }

            _remoteAllowPersist[skinID] = remoteAllowPersist;
            _cache[skinID] = dict;
            bool persisted = PersistToDisk(skinID, dict, persistent: persistToDisk);
            SkinPathResolver.Invalidate(skinID);
            if (persistToDisk && persisted)
            {
                SkinSync.NotifyDiskSkinsChanged(skinID);
            }
            return true;
        }

        private static bool TryDecodePayload(
            byte[] payload,
            bool skipIfLocalOwned,
            out string skinID,
            out Dictionary<string, byte[]> dict)
        {
            skinID = null;
            dict = null;
            if (payload == null || payload.Length < 16 || payload.Length > MAX_PACK_SIZE) return false;

            try
            {
                using (var ms = new MemoryStream(payload))
                using (var br = new BinaryReader(ms))
                {
                    if (br.ReadInt32() != MAGIC) return false;

                    int idLen = br.ReadInt32();
                    if (idLen <= 0 || idLen > 128) return false;
                    byte[] idBytes = br.ReadBytes(idLen);
                    if (idBytes.Length != idLen) return false;
                    skinID = Encoding.UTF8.GetString(idBytes);
                    if (!SkinPathResolver.IsSafeSkinName(skinID)) return false;

                    if (skipIfLocalOwned && SkinPathResolver.HasLocalCustomSkin(skinID))
                    {
                        SkinSyncMod.ModLog.Debug($"SkinPackCodec: 本机已存在自有皮肤 {skinID}，跳过远端皮肤包解包与缓存写入。");
                        return false;
                    }

                    int fileCount = br.ReadInt32();
                    if (fileCount <= 0 || fileCount > MAX_FILE_COUNT) return false;

                    var parsed = new Dictionary<string, byte[]>(fileCount, StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < fileCount; i++)
                    {
                        int pl = br.ReadInt32();
                        if (pl <= 0 || pl > 256) return false;
                        byte[] pathBytes = br.ReadBytes(pl);
                        if (pathBytes.Length != pl) return false;
                        string path = Encoding.UTF8.GetString(pathBytes);
                        if (!IsSafeRelPath(path)) return false;

                        int dl = br.ReadInt32();
                        if (dl <= 0 || dl > MAX_FILE_SIZE) return false;
                        byte[] data = br.ReadBytes(dl);
                        if (data.Length != dl) return false;
                        if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                        {
                            if (data.Length < 8
                                || data[0] != 0x89 || data[1] != 0x50 || data[2] != 0x4E || data[3] != 0x47
                                || data[4] != 0x0D || data[5] != 0x0A || data[6] != 0x1A || data[7] != 0x0A)
                                return false;
                        }
                        parsed[path] = data;
                    }
                    dict = parsed;
                    return true;
                }
            }
            catch (Exception ex)
            {
                SkinSyncMod.ModLog.Warning("SkinPackCodec.Unpack failed: " + ex.Message);
                return false;
            }
        }

        private static bool PersistToDisk(string skinID, Dictionary<string, byte[]> dict, bool persistent)
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
                return true;
            }
            catch (Exception ex)
            {
                SkinSyncMod.ModLog.Warning("SkinPackCodec.PersistToDisk failed: " + ex.Message);
                return false;
            }
        }

        private static string MakeRelative(string baseDir, string fullPath)
        {
            string rel = fullPath.Substring(baseDir.Length).TrimStart('/', '\\');
            return rel.Replace('\\', '/');
        }

        private static bool IsSafeRelPath(string p)
        {
            if (string.IsNullOrEmpty(p) || p.Length > 256) return false;
            if (p.IndexOf("..", StringComparison.Ordinal) >= 0) return false;
            if (p[0] == '/' || p[0] == '\\' || char.IsWhiteSpace(p[0]) || char.IsWhiteSpace(p[p.Length - 1])) return false;
            if (p.Length >= 2 && p[1] == ':') return false;

            string[] segs = p.Split('/');
            for (int i = 0; i < segs.Length; i++)
            {
                string s = segs[i];
                if (string.IsNullOrEmpty(s) || s == "." || s[s.Length - 1] == '.' || char.IsWhiteSpace(s[s.Length - 1]))
                    return false;
                if (SkinPathResolver.IsReservedFileStem(s)) return false;
            }

            foreach (char c in p)
            {
                if (char.IsControl(c)) return false;
                if (c == '\\' || c == ':' || c == '*' || c == '?' || c == '"' || c == '<' || c == '>' || c == '|')
                    return false;
            }

            return p.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                || p.EndsWith(".zones.json", StringComparison.OrdinalIgnoreCase)
                || p.Equals("baseSizes.json", StringComparison.OrdinalIgnoreCase)
                || p.Equals("blood.json", StringComparison.OrdinalIgnoreCase)
                || p.Equals("accessories.json", StringComparison.OrdinalIgnoreCase)
                || p.Equals("wings.json", StringComparison.OrdinalIgnoreCase);
        }
    }
}
