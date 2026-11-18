using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>将皮肤目录中的各部位贴图、翅膀、配件与区块特效合成为静态角色预览图。</summary>
    public static class SkinPreviewRenderer
    {
        public const int CompositeW = 100;
        public const int CompositeH = 100;

        internal enum Category { Body, Head, Wings, Accessories }

        internal struct Entry
        {
            public string PartName;
            public Category Cat;
            public int X;
            public int Y;
            public int ZOrder;
            public bool FlipX;
            public float Rotation;
            public bool HeadDefaultOnly;
            public bool EyeOpenOnly;
        }

        internal struct WingPiece
        {
            public int X;
            public int Y;
            public float Rotation;
            public int ZOrder;
        }

        internal struct Pixels
        {
            public int W;
            public int H;
            public byte[] Rgba;
        }

        private struct AccVirtual
        {
            public string Tag;
            public int X;
            public int Y;
            public float Rotation;
            public int ZOrder;
            public bool FlipX;
        }

        internal static readonly Entry[] DefaultAssembly =
        {
            new Entry { PartName = "experimentUpArm",     Cat = Category.Body,  X =   1, Y = -11, Rotation = 276f, ZOrder = -50 },
            new Entry { PartName = "experimentDownArm",   Cat = Category.Body,  X =  -2, Y =  -5, Rotation =   2f, ZOrder = -60 },
            new Entry { PartName = "experimentHandB",     Cat = Category.Body,  X =  -1, Y =   2, Rotation =  28f, ZOrder = -40 },
            new Entry { PartName = "experimentThigh",     Cat = Category.Body,  X =  -3, Y =   2, Rotation = 322f, ZOrder = -10 },
            new Entry { PartName = "experimentCrus",      Cat = Category.Body,  X =  -9, Y =   6, Rotation = 281f, ZOrder = -20 },
            new Entry { PartName = "experimentFoot",      Cat = Category.Body,  X = -14, Y =  10, Rotation = 337f, ZOrder = -30 },
            new Entry { PartName = "experimentDownTorso", Cat = Category.Body,  X =   1, Y =  -3, Rotation = 331f, ZOrder =  10 },
            new Entry { PartName = "experimentUpTorso",   Cat = Category.Body,  X =   6, Y = -10, Rotation = 322f, ZOrder =  20 },
            new Entry { PartName = "experimentTail",      Cat = Category.Body,  X =   0, Y =   3, Rotation =   1f, ZOrder =   0 },
            new Entry { PartName = "experimentThigh",     Cat = Category.Body,  X =   2, Y =   1, Rotation =  55f, ZOrder = 100 },
            new Entry { PartName = "experimentCrus",      Cat = Category.Body,  X =   3, Y =   5, Rotation = 316f, ZOrder =  90 },
            new Entry { PartName = "experimentFoot",      Cat = Category.Body,  X =   1, Y =  12, Rotation =  16f, ZOrder =  80 },
            new Entry { PartName = "experimentUpArm",     Cat = Category.Body,  X =   3, Y =  -8, Rotation = 324f, ZOrder = 150 },
            new Entry { PartName = "experimentDownArm",   Cat = Category.Body,  X =   6, Y =  -1, Rotation =  58f, ZOrder = 160 },
            new Entry { PartName = "experimentHandF",     Cat = Category.Body,  X =  12, Y =   2, Rotation =  70f, ZOrder = 170 },
            new Entry { PartName = "wingUL",              Cat = Category.Wings, X =  -2, Y =  -8, Rotation = 314f, ZOrder =   5 },
            new Entry { PartName = "wingDL",              Cat = Category.Wings, X =  -1, Y = -10, Rotation =   0f, ZOrder =   4 },
            new Entry { PartName = "wingUR",              Cat = Category.Wings, X =  -2, Y =  -9, Rotation = 318f, ZOrder =   5 },
            new Entry { PartName = "wingDR",              Cat = Category.Wings, X =   0, Y = -12, Rotation =   0f, ZOrder =   4 },
            new Entry { PartName = "experimentHeadBack",  Cat = Category.Head,  X =  10, Y = -19, Rotation = 347f, ZOrder =  50, HeadDefaultOnly = true },
            new Entry { PartName = "experimentEyeOpen",   Cat = Category.Head,  X =  10, Y = -19, Rotation = 347f, ZOrder =  53, EyeOpenOnly = true },
        };

        internal static readonly Dictionary<string, int> AccessoryParentByLimb = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            { "Head",      20 },
            { "UpTorso",    7 },
            { "DownTorso",  6 },
            { "Tail",       8 },
            { "UpArmF",    12 }, { "DownArmF", 13 }, { "HandF", 14 },
            { "ThighF",     9 }, { "CrusF",    10 }, { "FootF", 11 },
            { "UpArmB",     0 }, { "DownArmB",  1 }, { "HandB",  2 },
            { "ThighB",     3 }, { "CrusB",     4 }, { "FootB",  5 },
            { "UpArm",     12 }, { "DownArm",  13 }, { "Hand",  14 },
            { "Thigh",      9 }, { "Crus",     10 }, { "Foot",  11 },
        };

        private static readonly Dictionary<string, Texture2D> _texCache
            = new Dictionary<string, Texture2D>(StringComparer.Ordinal);

        /// <summary>获取或生成指定皮肤的预览贴图，皮肤目录不存在时返回 null。</summary>
        internal static Texture2D GetOrBuild(string skinId, string skinDir, SkinSyncSettings settings)
        {
            if (string.IsNullOrEmpty(skinId)) return null;
            if (_texCache.TryGetValue(skinId, out var cached) && cached != null) return cached;

            byte[] rgba = RenderRgba(skinId, skinDir, settings);
            if (rgba == null) return null;

            var tex = new Texture2D(CompositeW, CompositeH, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var pixels = new Color32[CompositeW * CompositeH];
            for (int y = 0; y < CompositeH; y++)
            {
                int srcRow = y * CompositeW;
                int dstRow = (CompositeH - 1 - y) * CompositeW;
                for (int x = 0; x < CompositeW; x++)
                {
                    int s = (srcRow + x) * 4;
                    pixels[dstRow + x] = new Color32(rgba[s], rgba[s + 1], rgba[s + 2], rgba[s + 3]);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            _texCache[skinId] = tex;
            return tex;
        }

        /// <summary>释放单个皮肤的预览贴图与部件解析缓存。</summary>
        internal static void Invalidate(string skinId)
        {
            if (string.IsNullOrEmpty(skinId)) return;
            if (_texCache.TryGetValue(skinId, out var t) && t != null)
            {
                UnityEngine.Object.Destroy(t);
            }
            _texCache.Remove(skinId);
            string skinDir = SkinPathResolver.GetSkinDir(skinId);
            if (!string.IsNullOrEmpty(skinDir))
            {
                ResolvePartNameCache.InvalidateSkin(skinDir);
            }
        }

        /// <summary>清空并销毁所有皮肤预览贴图与部件解析缓存。</summary>
        internal static void InvalidateAll()
        {
            foreach (var kv in _texCache)
            {
                if (kv.Value != null) UnityEngine.Object.Destroy(kv.Value);
            }
            _texCache.Clear();
            ResolvePartNameCache.Clear();
        }

        internal static string CategoryDir(Category c)
        {
            switch (c)
            {
                case Category.Body: return "Body";
                case Category.Head: return "Head";
                case Category.Wings: return "Wings";
                case Category.Accessories: return "Accessories";
                default: return "Body";
            }
        }

        internal static Dictionary<string, WingPiece> LoadWings(string skinDir)
        {
            var res = new Dictionary<string, WingPiece>(StringComparer.Ordinal);
            foreach (var e in DefaultAssembly)
            {
                if (e.Cat != Category.Wings) continue;
                res[e.PartName] = new WingPiece { X = e.X, Y = e.Y, Rotation = e.Rotation, ZOrder = e.ZOrder };
            }
            try
            {
                string path = Path.Combine(skinDir, "wings.json");
                if (!File.Exists(path)) return res;
                var cfg = WingsConfigLoader.Load(path);
                if (cfg == null) return res;
                res["wingUL"] = ToWingPiece(cfg.WingUL);
                res["wingUR"] = ToWingPiece(cfg.WingUR);
                res["wingDL"] = ToWingPiece(cfg.WingDL);
                res["wingDR"] = ToWingPiece(cfg.WingDR);
            }
            catch (Exception ex)
            {
                ModLog.Warning("preview LoadWings failed: " + ex.Message);
            }
            return res;
        }

        private static WingPiece ToWingPiece(WingsConfigLoader.Piece p)
        {
            return new WingPiece { X = p.X, Y = p.Y, Rotation = p.Rotation, ZOrder = p.ZOrder };
        }

        /// <summary>读取 PNG 文件并解码为左上原点的 RGBA8 字节数组。</summary>
        internal static Pixels LoadPng(string path)
        {
            var p = new Pixels();
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return p;
            try
            {
                byte[] data = File.ReadAllBytes(path);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    hideFlags = HideFlags.HideAndDontSave,
                };
                try
                {
                    if (!ImageConversion.LoadImage(tex, data)) return p;
                    p.W = tex.width;
                    p.H = tex.height;
                    Color32[] cols = tex.GetPixels32();
                    p.Rgba = new byte[p.W * p.H * 4];
                    for (int y = 0; y < p.H; y++)
                    {
                        int srcRow = (p.H - 1 - y) * p.W;
                        int dstRow = y * p.W;
                        for (int x = 0; x < p.W; x++)
                        {
                            int s = srcRow + x;
                            int d = (dstRow + x) * 4;
                            var c = cols[s];
                            p.Rgba[d] = c.r;
                            p.Rgba[d + 1] = c.g;
                            p.Rgba[d + 2] = c.b;
                            p.Rgba[d + 3] = c.a;
                        }
                    }
                }
                finally
                {
                    UnityEngine.Object.Destroy(tex);
                }
            }
            catch (Exception ex)
            {
                ModLog.Warning("preview LoadPng failed: " + path + " : " + ex.Message);
            }
            return p;
        }

        /// <summary>将源像素按给定角度与镜像参数最近邻旋转混合到目标画布。</summary>
        internal static void BlitWithRotation(byte[] canvas, int canvasW, int canvasH,
            byte[] src, int srcW, int srcH, int centerX, int centerY, float rotationDeg, bool flipX)
        {
            if (srcW <= 0 || srcH <= 0 || src == null) return;

            float rad = -(rotationDeg * Mathf.Deg2Rad);
            float cosR = Mathf.Cos(rad);
            float sinR = Mathf.Sin(rad);
            float halfW = srcW * 0.5f;
            float halfH = srcH * 0.5f;

            float minDx = float.PositiveInfinity, maxDx = float.NegativeInfinity;
            float minDy = float.PositiveInfinity, maxDy = float.NegativeInfinity;
            float[,] corners =
            {
                { -halfW, -halfH }, { halfW, -halfH },
                { -halfW,  halfH }, { halfW,  halfH },
            };
            for (int i = 0; i < 4; i++)
            {
                float cx = corners[i, 0], cy = corners[i, 1];
                float dx = cx * cosR - cy * sinR;
                float dy = cx * sinR + cy * cosR;
                if (dx < minDx) minDx = dx;
                if (dx > maxDx) maxDx = dx;
                if (dy < minDy) minDy = dy;
                if (dy > maxDy) maxDy = dy;
            }
            int dxStart = Mathf.FloorToInt(centerX + minDx);
            int dyStart = Mathf.FloorToInt(centerY + minDy);
            int dxEnd = Mathf.CeilToInt(centerX + maxDx);
            int dyEnd = Mathf.CeilToInt(centerY + maxDy);

            float invCos = cosR;
            float invSin = -sinR;

            for (int dy = dyStart; dy <= dyEnd; dy++)
            {
                if (dy < 0 || dy >= canvasH) continue;
                float ry = dy - centerY;
                for (int dx = dxStart; dx <= dxEnd; dx++)
                {
                    if (dx < 0 || dx >= canvasW) continue;
                    float rx = dx - centerX;
                    float srcXF = rx * invCos - ry * invSin + halfW;
                    float srcYF = rx * invSin + ry * invCos + halfH;
                    int sx = Mathf.FloorToInt(srcXF);
                    int sy = Mathf.FloorToInt(srcYF);
                    if (sx < 0 || sy < 0 || sx >= srcW || sy >= srcH) continue;
                    if (flipX) sx = srcW - 1 - sx;
                    int sBase = (sy * srcW + sx) * 4;
                    int sa = src[sBase + 3];
                    if (sa == 0) continue;
                    int sr = src[sBase];
                    int sg = src[sBase + 1];
                    int sb = src[sBase + 2];
                    int dBase = (dy * canvasW + dx) * 4;
                    int dr = canvas[dBase];
                    int dg = canvas[dBase + 1];
                    int db = canvas[dBase + 2];
                    int da = canvas[dBase + 3];
                    int dstFactor = (da * (255 - sa) + 127) / 255;
                    int outA = sa + dstFactor;
                    if (outA == 0) continue;
                    int half = outA / 2;
                    canvas[dBase]     = (byte)Mathf.Min(255, (sr * sa + dr * dstFactor + half) / outA);
                    canvas[dBase + 1] = (byte)Mathf.Min(255, (sg * sa + dg * dstFactor + half) / outA);
                    canvas[dBase + 2] = (byte)Mathf.Min(255, (sb * sa + db * dstFactor + half) / outA);
                    canvas[dBase + 3] = (byte)Mathf.Min(255, outA);
                }
            }
        }

        /// <summary>合成皮肤站立姿态到 100x100 RGBA8 缓冲区。</summary>
        internal static byte[] RenderRgba(string skinId, string skinDir, SkinSyncSettings settings)
        {
            if (string.IsNullOrEmpty(skinDir) || !Directory.Exists(skinDir)) return null;
            var canvas = new byte[CompositeW * CompositeH * 4];

            var wings = LoadWings(skinDir);
            var entries = new List<Entry>(DefaultAssembly);
            string accPath = Path.Combine(skinDir, "accessories.json");
            var accList = AccessoryConfigLoader.Load(accPath);
            var accById = new Dictionary<string, AccessoryConfigLoader.Entry>(StringComparer.Ordinal);
            var accVirtuals = new List<AccVirtual>();

            foreach (var a in accList)
            {
                if (a == null || string.IsNullOrEmpty(a.Id)) continue;
                var ov = settings != null ? settings.GetAccessoryOverride(skinId, a.Id) : null;
                bool effEnabled = ov?.Enabled ?? a.Enabled;
                if (!effEnabled) continue;
                accById[a.Id] = a;

                if (!AccessoryParentByLimb.TryGetValue(a.Limb ?? "", out int parentIdx)) continue;
                var parent = DefaultAssembly[parentIdx];
                int accZ = ov?.ZOrder ?? a.ZOrder;
                float accRot = ov?.Rotation ?? a.Rotation;

                accVirtuals.Add(new AccVirtual
                {
                    Tag = "__acc:" + a.Id,
                    X = parent.X,
                    Y = parent.Y,
                    Rotation = parent.Rotation + accRot,
                    ZOrder = parent.ZOrder + accZ,
                    FlipX = parent.FlipX,
                });

                if (a.SecondaryLimbs != null)
                {
                    for (int i = 0; i < a.SecondaryLimbs.Count; i++)
                    {
                        var sec = a.SecondaryLimbs[i];
                        if (sec == null) continue;
                        if (!AccessoryParentByLimb.TryGetValue(sec.Limb ?? "", out int secIdx)) continue;
                        var secParent = DefaultAssembly[secIdx];
                        accVirtuals.Add(new AccVirtual
                        {
                            Tag = "__acc:" + a.Id + "#" + i.ToString(CultureInfo.InvariantCulture),
                            X = secParent.X,
                            Y = secParent.Y,
                            Rotation = secParent.Rotation + sec.Rotation,
                            ZOrder = secParent.ZOrder + sec.ZOrder,
                            FlipX = secParent.FlipX,
                        });
                    }
                }
            }

            foreach (var v in accVirtuals)
            {
                entries.Add(new Entry
                {
                    PartName = v.Tag,
                    Cat = Category.Accessories,
                    X = v.X,
                    Y = v.Y,
                    Rotation = v.Rotation,
                    ZOrder = v.ZOrder,
                    FlipX = v.FlipX,
                });
            }

            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e.Cat != Category.Wings) continue;
                if (!wings.TryGetValue(e.PartName, out var wp)) continue;
                e.X = wp.X;
                e.Y = wp.Y;
                e.Rotation = wp.Rotation;
                e.ZOrder = wp.ZOrder;
                entries[i] = e;
            }

            (int rootX, int rootY, float rootRot, float halfH)? upL = null;
            (int rootX, int rootY, float rootRot, float halfH)? upR = null;
            var preloadedWings = new Dictionary<string, Pixels>(StringComparer.Ordinal);
            foreach (var e in entries)
            {
                if (e.Cat != Category.Wings) continue;
                if (e.PartName != "wingUL" && e.PartName != "wingUR") continue;
                var px = LoadPng(Path.Combine(skinDir, "Wings", e.PartName + ".png"));
                preloadedWings[e.PartName] = px;
                if (px.W == 0) continue;
                var pose = (e.X, e.Y, e.Rotation, px.H * 0.5f);
                if (e.PartName == "wingUL") upL = pose;
                else upR = pose;
            }

            entries.Sort((a, b) => a.ZOrder.CompareTo(b.ZOrder));

            foreach (var entry in entries)
            {
                if (entry.Cat == Category.Accessories)
                {
                    DrawAccessory(canvas, entry, accById, skinId, skinDir, settings);
                    continue;
                }

                int effX = entry.X;
                int effY = entry.Y;
                float effRot = entry.Rotation;

                if (entry.Cat == Category.Wings && (entry.PartName == "wingDL" || entry.PartName == "wingDR"))
                {
                    var anchor = entry.PartName == "wingDL" ? upL : upR;
                    if (anchor.HasValue)
                    {
                        var a = anchor.Value;
                        float localX = entry.X;
                        float localY = a.halfH + entry.Y;
                        float rad = -a.rootRot * Mathf.Deg2Rad;
                        float c = Mathf.Cos(rad), s = Mathf.Sin(rad);
                        float dx = localX * c - localY * s;
                        float dy = localX * s + localY * c;
                        effX = a.rootX + Mathf.RoundToInt(dx);
                        effY = a.rootY + Mathf.RoundToInt(dy);
                        effRot = a.rootRot + entry.Rotation;
                    }
                }

                char side = entry.ZOrder >= 0 ? 'F' : 'B';
                string actualBase = ResolveActualPartName(skinDir, entry.Cat, entry.PartName);
                string sided = SidedCandidateNameByLemma(actualBase, side);
                Pixels pix = default;
                bool usedSided = false;

                if (entry.Cat == Category.Wings && preloadedWings.TryGetValue(entry.PartName, out var cachedWing))
                {
                    pix = cachedWing;
                }
                else
                {
                    if (!string.IsNullOrEmpty(sided))
                    {
                        pix = LoadPng(Path.Combine(skinDir, CategoryDir(entry.Cat), sided + ".png"));
                        usedSided = pix.W > 0;
                    }
                    if (pix.W == 0)
                    {
                        pix = LoadPng(Path.Combine(skinDir, CategoryDir(entry.Cat), actualBase + ".png"));
                    }
                }
                if (pix.W == 0) continue;

                int centerX = CompositeW / 2 + effX;
                int centerY = CompositeH / 2 + effY;
                BlitWithRotation(canvas, CompositeW, CompositeH, pix.Rgba, pix.W, pix.H,
                    centerX, centerY, effRot, entry.FlipX);

                string renderedName = usedSided ? sided : actualBase;
                SkinPreviewZones.Render(canvas, CompositeW, CompositeH,
                    skinDir, CategoryDir(entry.Cat), renderedName,
                    pix.W, pix.H, centerX, centerY, effRot, entry.FlipX, 0f);
            }
            return canvas;
        }

        private static string SidedCandidateNameByLemma(string actualBase, char side)
        {
            if (side != 'F' && side != 'B') return null;
            string[] lemmas = { "UpArm", "DownArm", "Thigh", "Crus", "Foot" };
            foreach (var l in lemmas)
            {
                if (actualBase.EndsWith(l, StringComparison.OrdinalIgnoreCase)) return actualBase + side;
            }
            if (actualBase.Equals("mildownarm", StringComparison.OrdinalIgnoreCase))
            {
                return actualBase + side;
            }
            return null;
        }

        private static string ResolveActualPartName(string skinDir, Category cat, string defaultName)
        {
            string lemma = StripExperimentLemma(defaultName);
            if (string.IsNullOrEmpty(lemma)) return defaultName;
            string defaultPath = Path.Combine(skinDir, CategoryDir(cat), defaultName + ".png");
            if (File.Exists(defaultPath)) return defaultName;
            string catDir = Path.Combine(skinDir, CategoryDir(cat));
            if (!Directory.Exists(catDir)) return defaultName;
            return ResolvePartNameCache.Resolve(catDir, lemma) ?? defaultName;
        }

        private static string StripExperimentLemma(string name)
        {
            const string p = "experiment";
            return name.StartsWith(p, StringComparison.Ordinal) ? name.Substring(p.Length) : name;
        }

        private static class ResolvePartNameCache
        {
            private static readonly Dictionary<string, Dictionary<string, string>> _byCatDir
                = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

            public static string Resolve(string catDir, string lemma)
            {
                if (!_byCatDir.TryGetValue(catDir, out var map))
                {
                    map = BuildMap(catDir);
                    _byCatDir[catDir] = map;
                }
                return map.TryGetValue(lemma, out var name) ? name : null;
            }

            public static void InvalidateSkin(string skinDir)
            {
                if (string.IsNullOrEmpty(skinDir)) return;
                var removeKeys = new List<string>();
                foreach (var k in _byCatDir.Keys)
                {
                    if (k.StartsWith(skinDir, StringComparison.OrdinalIgnoreCase))
                        removeKeys.Add(k);
                }
                foreach (var k in removeKeys) _byCatDir.Remove(k);
            }

            public static void Clear()
            {
                _byCatDir.Clear();
            }

            private static Dictionary<string, string> BuildMap(string catDir)
            {
                var map = new Dictionary<string, string>(StringComparer.Ordinal);
                if (!Directory.Exists(catDir)) return map;
                string[] lemmas = { "UpArm", "DownArm", "HandF", "HandB", "Hand", "Thigh", "Crus", "Foot", "UpTorso", "DownTorso", "HeadBack", "Head", "Tail", "Nosebleed" };
                string[] eyeLemmas = { "EyeOpen", "EyeClosed", "EyeHalfClosed", "EyeSad", "EyeHappy", "EyeScared", "EyePanic", "EyeLookBack", "EyeGone", "EyeGoneHealed" };
                foreach (var f in Directory.GetFiles(catDir, "*.png"))
                {
                    if (f.EndsWith(".zones.png", StringComparison.OrdinalIgnoreCase)) continue;
                    string name = Path.GetFileNameWithoutExtension(f);
                    if (string.IsNullOrEmpty(name) || name.StartsWith("R_", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!map.ContainsKey("UpTorso") && name.Equals("milkytorso", StringComparison.OrdinalIgnoreCase))
                    {
                        map["UpTorso"] = name;
                        continue;
                    }
                    if (!map.ContainsKey("DownArm") && name.Equals("mildownarm", StringComparison.OrdinalIgnoreCase))
                    {
                        map["DownArm"] = name;
                        continue;
                    }
                    foreach (var l in lemmas)
                    {
                        if (!map.ContainsKey(l) && name.EndsWith(l, StringComparison.OrdinalIgnoreCase)) { map[l] = name; break; }
                    }
                    foreach (var l in eyeLemmas)
                    {
                        if (!map.ContainsKey(l) && name.EndsWith(l, StringComparison.OrdinalIgnoreCase)) { map[l] = name; break; }
                    }
                }
                return map;
            }
        }

        private static void DrawAccessory(byte[] canvas, Entry entry,
            Dictionary<string, AccessoryConfigLoader.Entry> accById, string skinId, string skinDir, SkinSyncSettings settings)
        {
            string raw = entry.PartName;
            if (string.IsNullOrEmpty(raw) || !raw.StartsWith("__acc:", StringComparison.Ordinal)) return;
            raw = raw.Substring(6);

            AccessoryConfigLoader.Entry acc = null;
            int secIdx = -1;
            int hash = raw.LastIndexOf('#');
            if (hash > 0 && int.TryParse(raw.Substring(hash + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedIdx))
            {
                string candidateId = raw.Substring(0, hash);
                if (accById.TryGetValue(candidateId, out var candidateAcc)
                    && candidateAcc?.SecondaryLimbs != null
                    && parsedIdx >= 0
                    && parsedIdx < candidateAcc.SecondaryLimbs.Count)
                {
                    acc = candidateAcc;
                    secIdx = parsedIdx;
                }
            }
            if (acc == null && !accById.TryGetValue(raw, out acc)) return;
            if (acc == null) return;

            string sprite;
            int offX, offY;
            float parentRot = entry.Rotation;
            if (secIdx >= 0)
            {
                var sec = acc.SecondaryLimbs[secIdx];
                if (sec == null) return;
                sprite = sec.Sprite;
                offX = sec.OffX;
                offY = sec.OffY;
                if (AccessoryParentByLimb.TryGetValue(sec.Limb ?? "", out int secPIdx))
                    parentRot = DefaultAssembly[secPIdx].Rotation;
            }
            else
            {
                var ov = settings != null ? settings.GetAccessoryOverride(skinId, acc.Id) : null;
                sprite = acc.Sprite;
                offX = ov?.OffX ?? acc.OffX;
                offY = ov?.OffY ?? acc.OffY;
                if (AccessoryParentByLimb.TryGetValue(acc.Limb ?? "", out int pIdx))
                    parentRot = DefaultAssembly[pIdx].Rotation;
            }
            if (string.IsNullOrEmpty(sprite)) return;

            var pix = LoadPng(Path.Combine(skinDir, "Accessories", sprite + ".png"));
            if (pix.W == 0) return;

            float parentRad = -parentRot * Mathf.Deg2Rad;
            float c = Mathf.Cos(parentRad), s = Mathf.Sin(parentRad);
            float dx = offX * c - offY * s;
            float dy = offX * s + offY * c;
            int centerX = CompositeW / 2 + entry.X + Mathf.RoundToInt(dx);
            int centerY = CompositeH / 2 + entry.Y + Mathf.RoundToInt(dy);
            BlitWithRotation(canvas, CompositeW, CompositeH, pix.Rgba, pix.W, pix.H,
                centerX, centerY, entry.Rotation, entry.FlipX);

            SkinPreviewZones.Render(canvas, CompositeW, CompositeH,
                skinDir, "Accessories", sprite,
                pix.W, pix.H, centerX, centerY, entry.Rotation, entry.FlipX, 0f);
        }
    }
}
