using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>
    /// 把皮肤目录里的 PNG 按编辑器后端 defaultAssembly 表合成"站立第一帧"，
    /// 给配置面板的角色 tab 做预览。算法移植自 src-tauri/src/apiLayer/mod.rs.getCompositeImage。
    /// 渲染结果缓存为 Texture2D，只在切皮肤 / 配件 toggle 时重建。
    /// </summary>
    public static partial class SkinPreviewRenderer
    {
        public const int CompositeW = 100;
        public const int CompositeH = 100;

        internal enum Category { Body, Head, Wings, Accessories }

        internal struct Entry
        {
#pragma warning disable CS0649
            public string PartName;
            public Category Cat;
            public int X;
            public int Y;
            public int ZOrder;
            public bool FlipX;
            public float Rotation;
            // 头部 / 眼睛只渲默认变体——预览不引入 expression / variant 切换。
            public bool HeadDefaultOnly;
            public bool EyeOpenOnly;
#pragma warning restore CS0649
        }

        internal struct WingPiece
        {
            public int X;
            public int Y;
            public float Rotation;
            public int ZOrder;
        }

        // 默认装配表，与 src-tauri/src/assemblyTable/mod.rs.defaultAssembly() 一致。
        // F/B 共享 base 在数组中出现两次，按 zOrder 符号决定 sided 候选。
        internal static readonly Entry[] DefaultAssembly = new Entry[]
        {
            new Entry { PartName = "experimentUpArm",    Cat = Category.Body, X =  1, Y = -11, Rotation = 276f, ZOrder = -50 },
            new Entry { PartName = "experimentDownArm",  Cat = Category.Body, X = -2, Y =  -5, Rotation =   2f, ZOrder = -60 },
            new Entry { PartName = "experimentHandB",    Cat = Category.Body, X = -1, Y =   2, Rotation =  28f, ZOrder = -40 },
            new Entry { PartName = "experimentThigh",    Cat = Category.Body, X = -3, Y =   2, Rotation = 322f, ZOrder = -10 },
            new Entry { PartName = "experimentCrus",     Cat = Category.Body, X = -9, Y =   6, Rotation = 281f, ZOrder = -20 },
            new Entry { PartName = "experimentFoot",     Cat = Category.Body, X =-14, Y =  10, Rotation = 337f, ZOrder = -30 },
            new Entry { PartName = "experimentDownTorso",Cat = Category.Body, X =  1, Y =  -3, Rotation = 331f, ZOrder =  10 },
            new Entry { PartName = "experimentUpTorso",  Cat = Category.Body, X =  6, Y = -10, Rotation = 322f, ZOrder =  20 },
            new Entry { PartName = "experimentTail",     Cat = Category.Body, X =  0, Y =   3, Rotation =   1f, ZOrder =   0 },
            new Entry { PartName = "experimentThigh",    Cat = Category.Body, X =  2, Y =   1, Rotation =  55f, ZOrder = 100 },
            new Entry { PartName = "experimentCrus",     Cat = Category.Body, X =  3, Y =   5, Rotation = 316f, ZOrder =  90 },
            new Entry { PartName = "experimentFoot",     Cat = Category.Body, X =  1, Y =  12, Rotation =  16f, ZOrder =  80 },
            new Entry { PartName = "experimentUpArm",    Cat = Category.Body, X =  3, Y =  -8, Rotation = 324f, ZOrder = 150 },
            new Entry { PartName = "experimentDownArm",  Cat = Category.Body, X =  6, Y =  -1, Rotation =  58f, ZOrder = 160 },
            new Entry { PartName = "experimentHandF",    Cat = Category.Body, X = 12, Y =   2, Rotation =  70f, ZOrder = 170 },
            new Entry { PartName = "wingUL",             Cat = Category.Wings,X = -2, Y =  -8, Rotation = 314f, ZOrder = 5 },
            new Entry { PartName = "wingDL",             Cat = Category.Wings,X = -1, Y = -10, Rotation =   0f, ZOrder = 4 },
            new Entry { PartName = "wingUR",             Cat = Category.Wings,X = -2, Y =  -9, Rotation = 318f, ZOrder = 5 },
            new Entry { PartName = "wingDR",             Cat = Category.Wings,X =  0, Y = -12, Rotation =   0f, ZOrder = 4 },
            new Entry { PartName = "experimentHeadBack", Cat = Category.Head, X = 10, Y = -19, Rotation = 347f, ZOrder = 50, HeadDefaultOnly = true },
            new Entry { PartName = "experimentEyeOpen",  Cat = Category.Head, X = 10, Y = -19, Rotation = 347f, ZOrder = 53, EyeOpenOnly = true },
        };

        // 配件父锚映射：limb 名 → DefaultAssembly 中的索引（与编辑器 accessoryParentEntry 等价的最小子集）。
        // 预览不区分 F/B 的 limb（如 UpArm）默认走 F 侧（前侧）。
        internal static readonly Dictionary<string, int> AccessoryParentByLimb = new Dictionary<string, int>
        {
            { "Head",     20 },
            { "UpTorso",   7 },
            { "DownTorso", 6 },
            { "Tail",      8 },
            { "UpArmF",   12 }, { "DownArmF", 13 }, { "HandF", 14 },
            { "ThighF",    9 }, { "CrusF",    10 }, { "FootF", 11 },
            { "UpArmB",    0 }, { "DownArmB",  1 }, { "HandB",  2 },
            { "ThighB",    3 }, { "CrusB",     4 }, { "FootB",  5 },
            { "UpArm",    12 }, { "DownArm",  13 }, { "Hand",  14 },
            { "Thigh",     9 }, { "Crus",     10 }, { "Foot",  11 },
        };

        internal static string CategoryDir(Category c)
        {
            switch (c)
            {
                case Category.Body: return "Body";
                case Category.Head: return "Head";
                case Category.Wings: return "Wings";
                case Category.Accessories: return "Accessories";
            }
            return "Body";
        }

        /// <summary>F/B 共享 sprite 5 类有 sided 候选名，其它返回 null。</summary>
        internal static string SidedCandidateName(string basePart, char side)
        {
            if (side != 'F' && side != 'B') return null;
            switch (basePart)
            {
                case "experimentUpArm":
                case "experimentDownArm":
                case "experimentThigh":
                case "experimentCrus":
                case "experimentFoot":
                    return basePart + side;
            }
            return null;
        }

        /// <summary>读 wings.json 的 4 片配置，缺则用 DefaultAssembly 里 wing 默认。</summary>
        internal static Dictionary<string, WingPiece> LoadWings(string skinDir)
        {
            var res = new Dictionary<string, WingPiece>();
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
            catch (System.Exception ex)
            {
                SkinSyncMod.ModLog.Warning("preview LoadWings failed: " + ex.Message);
            }
            return res;
        }

        private static WingPiece ToWingPiece(WingsConfigLoader.Piece p)
        {
            return new WingPiece { X = p.X, Y = p.Y, Rotation = p.Rotation, ZOrder = p.ZOrder };
        }
    }
}

namespace SkinSyncMod
{
    public static partial class SkinPreviewRenderer
    {
        // —— 像素加载 —— //

        internal struct Pixels { public int W; public int H; public byte[] Rgba; }

        /// <summary>读 PNG → RGBA8 字节流；失败返回 W=0。Unity Texture2D.LoadImage 自动按 sRGB 解码。</summary>
        internal static Pixels LoadPng(string path)
        {
            var p = new Pixels();
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return p;
            try
            {
                byte[] data = File.ReadAllBytes(path);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.filterMode = FilterMode.Point;
                if (!ImageConversion.LoadImage(tex, data)) return p;
                p.W = tex.width;
                p.H = tex.height;
                // GetPixels32 是上下翻转的 Unity 坐标（左下原点），输出按 row 0=底端排列。
                // 我们的画布是左上原点，按 Rust 后端约定逐 (sy, sx) 索引时同样以左上为 (0,0)。
                // 转换：把 Unity 的像素重排为左上原点的 RGBA 字节流。
                Color32[] cols = tex.GetPixels32();
                p.Rgba = new byte[p.W * p.H * 4];
                for (int y = 0; y < p.H; y++)
                {
                    int srcRow = (p.H - 1 - y) * p.W;
                    int dstRow = y * p.W;
                    for (int x = 0; x < p.W; x++)
                    {
                        int s = (srcRow + x);
                        int d = (dstRow + x) * 4;
                        var c = cols[s];
                        p.Rgba[d] = c.r;
                        p.Rgba[d + 1] = c.g;
                        p.Rgba[d + 2] = c.b;
                        p.Rgba[d + 3] = c.a;
                    }
                }
                Object.Destroy(tex);
            }
            catch (System.Exception ex)
            {
                SkinSyncMod.ModLog.Warning("preview LoadPng failed: " + path + " : " + ex.Message);
            }
            return p;
        }

        // —— 旋转 + alpha-over blit —— //

        /// <summary>把 src 绕自身中心旋转 rotationDeg（Unity rotZ 一致），按 source-over 贴到 canvas，
        /// src 旋转后中心对齐到 (centerX, centerY)。flipX=true 时先水平镜像。最近邻采样保留像素风。
        /// 移植自 src-tauri/src/imageOps/mod.rs.blitWithRotation。</summary>
        internal static void BlitWithRotation(byte[] canvas, int canvasW, int canvasH,
            byte[] src, int srcW, int srcH, int centerX, int centerY, float rotationDeg, bool flipX)
        {
            if (srcW <= 0 || srcH <= 0 || src == null) return;

            float rad = -(rotationDeg * Mathf.Deg2Rad);
            float cosR = Mathf.Cos(rad);
            float sinR = Mathf.Sin(rad);
            float halfW = srcW * 0.5f;
            float halfH = srcH * 0.5f;

            // 旋转后 bbox（圈定要扫描的目标矩形）。
            float minDx = float.PositiveInfinity, maxDx = float.NegativeInfinity;
            float minDy = float.PositiveInfinity, maxDy = float.NegativeInfinity;
            float[,] corners = new float[4, 2]
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
    }
}

namespace SkinSyncMod
{
    public static partial class SkinPreviewRenderer
    {
        // —— 主合成 —— //

        // 配件虚拟 entry（按编辑器后端把 enabled 配件并入主排序的做法）。
        // partName 用 `__acc:<id>` 标记主件、`__acc:<id>#<idx>` 标记副件，便于主循环回查 sprite 名 + offX/offY。
        private struct AccVirtual
        {
            public string Tag;
            public int X;
            public int Y;
            public float Rotation;
            public int ZOrder;
            public bool FlipX;
        }

        /// <summary>合成皮肤站立第一帧到 100×100 RGBA8 字节数组。skinDir 不存在时返回全透明。
        /// settings 用于读配件 override（enabled / offX / offY / rot / z）；缺则用 entry 默认。</summary>
        internal static byte[] RenderRgba(string skinId, string skinDir, SkinSyncSettings settings)
        {
            var canvas = new byte[CompositeW * CompositeH * 4];
            if (string.IsNullOrEmpty(skinDir) || !Directory.Exists(skinDir)) return canvas;

            var wings = LoadWings(skinDir);

            // 收集渲染列表：基础 entry + 配件虚拟 entry。两者用统一 lambda 取像素 + 排序。
            var entries = new List<Entry>(DefaultAssembly);
            string accPath = Path.Combine(skinDir, "accessories.json");
            var accList = AccessoryConfigLoader.Load(accPath);
            var accById = new Dictionary<string, AccessoryConfigLoader.Entry>();
            var accVirtuals = new List<AccVirtual>();
            foreach (var a in accList)
            {
                if (a == null || string.IsNullOrEmpty(a.Id)) continue;
                bool? ovEnabled = settings != null ? settings.GetAccessoryOverride(skinId, a.Id)?.Enabled : null;
                bool effEnabled = ovEnabled ?? a.Enabled;
                if (!effEnabled) continue;
                accById[a.Id] = a;

                if (!AccessoryParentByLimb.TryGetValue(a.Limb ?? "", out int parentIdx)) continue;
                var parent = DefaultAssembly[parentIdx];

                var ov = settings != null ? settings.GetAccessoryOverride(skinId, a.Id) : null;
                int accZ = ov?.ZOrder ?? a.ZOrder;
                float accRot = ov?.Rotation ?? a.Rotation;

                accVirtuals.Add(new AccVirtual
                {
                    Tag = "__acc:" + a.Id,
                    X = parent.X, Y = parent.Y,
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
                            Tag = "__acc:" + a.Id + "#" + i,
                            X = secParent.X, Y = secParent.Y,
                            Rotation = secParent.Rotation + sec.Rotation,
                            ZOrder = secParent.ZOrder + sec.ZOrder,
                            FlipX = secParent.FlipX,
                        });
                    }
                }
            }

            // 把配件并入 entries（用伪 Entry 表示，partName 带 __acc 前缀让主循环识别）。
            foreach (var v in accVirtuals)
            {
                entries.Add(new Entry
                {
                    PartName = v.Tag,
                    Cat = Category.Accessories,
                    X = v.X, Y = v.Y,
                    Rotation = v.Rotation, ZOrder = v.ZOrder, FlipX = v.FlipX,
                });
            }
            // 翼用 wings.json 覆盖默认 X/Y/Rotation/ZOrder。
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e.Cat != Category.Wings) continue;
                if (!wings.TryGetValue(e.PartName, out var wp)) continue;
                e.X = wp.X; e.Y = wp.Y; e.Rotation = wp.Rotation; e.ZOrder = wp.ZOrder;
                entries[i] = e;
            }

            // 上翼姿态——给下翼父子链用：根 (X,Y) + Rotation + halfH（PNG 半高）。
            (int rootX, int rootY, float rootRot, float halfH)? upL = null;
            (int rootX, int rootY, float rootRot, float halfH)? upR = null;
            foreach (var e in entries)
            {
                if (e.Cat != Category.Wings) continue;
                if (e.PartName != "wingUL" && e.PartName != "wingUR") continue;
                var px = LoadPng(Path.Combine(skinDir, "Wings", e.PartName + ".png"));
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
                    DrawAccessory(canvas, entry, accById, skinDir);
                    continue;
                }

                int effX = entry.X;
                int effY = entry.Y;
                float effRot = entry.Rotation;

                // 下翼 chain：把局部偏移 (X, halfH+Y) 绕上翼根旋转，rotation 累加上翼旋转。
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

                // F/B sided：zOrder ≥ 0 → F，< 0 → B；缺 sided 则回退 base。
                char side = entry.ZOrder >= 0 ? 'F' : 'B';
                string actualBase = ResolveActualPartName(skinDir, entry.Cat, entry.PartName);
                string sided = SidedCandidateNameByLemma(actualBase, side);
                Pixels pix = default;
                if (!string.IsNullOrEmpty(sided))
                {
                    pix = LoadPng(Path.Combine(skinDir, CategoryDir(entry.Cat), sided + ".png"));
                }
                if (pix.W == 0)
                {
                    pix = LoadPng(Path.Combine(skinDir, CategoryDir(entry.Cat), actualBase + ".png"));
                }
                if (pix.W == 0) continue;

                int centerX = CompositeW / 2 + effX;
                int centerY = CompositeH / 2 + effY;
                BlitWithRotation(canvas, CompositeW, CompositeH, pix.Rgba, pix.W, pix.H,
                    centerX, centerY, effRot, entry.FlipX);

                // 区块光 / 粒子 / 动画——sided 名称优先，缺则回退 base 名（与后端 renderZoneLightsForPart 行为一致）。
                string renderedName = !string.IsNullOrEmpty(sided) && File.Exists(
                    Path.Combine(skinDir, CategoryDir(entry.Cat), sided + ".png"))
                    ? sided : actualBase;
                SkinPreviewZones.Render(canvas, CompositeW, CompositeH,
                    skinDir, CategoryDir(entry.Cat), renderedName,
                    pix.W, pix.H, centerX, centerY, effRot, entry.FlipX, 0f);
            }
            return canvas;
        }

        /// <summary>SidedCandidateName 接受 actual base 名，但内部白名单是 experiment* 硬编码——这里宽化为按尾缀匹配。</summary>
        private static string SidedCandidateNameByLemma(string actualBase, char side)
        {
            if (side != 'F' && side != 'B') return null;
            string[] lemmas = { "UpArm", "DownArm", "Thigh", "Crus", "Foot" };
            foreach (var l in lemmas)
                if (actualBase.EndsWith(l)) return actualBase + side;
            return null;
        }

        /// <summary>把 DefaultAssembly 里的 experiment* 硬编码 partName 换成皮肤目录里实际存在的同部位 PNG 名。</summary>
        private static string ResolveActualPartName(string skinDir, Category cat, string defaultName)
        {
            // 按 default 文件名末尾 lemma（"UpArm" / "DownArm" / ...）在分类目录里找第一个匹配 PNG。
            string lemma = StripExperimentLemma(defaultName);
            if (string.IsNullOrEmpty(lemma)) return defaultName;
            // 直接命中 default 名优先，避免反复扫盘。
            string defaultPath = Path.Combine(skinDir, CategoryDir(cat), defaultName + ".png");
            if (File.Exists(defaultPath)) return defaultName;
            string catDir = Path.Combine(skinDir, CategoryDir(cat));
            if (!Directory.Exists(catDir)) return defaultName;
            var hit = ResolvePartNameCache.Resolve(catDir, lemma);
            return hit ?? defaultName;
        }

        private static string StripExperimentLemma(string name)
        {
            const string p = "experiment";
            return name.StartsWith(p, System.StringComparison.Ordinal) ? name.Substring(p.Length) : name;
        }

        private static class ResolvePartNameCache
        {
            private static readonly Dictionary<string, Dictionary<string, string>> _byCatDir =
                new Dictionary<string, Dictionary<string, string>>();

            public static string Resolve(string catDir, string lemma)
            {
                if (!_byCatDir.TryGetValue(catDir, out var map))
                {
                    map = BuildMap(catDir);
                    _byCatDir[catDir] = map;
                }
                return map.TryGetValue(lemma, out var name) ? name : null;
            }

            private static Dictionary<string, string> BuildMap(string catDir)
            {
                var map = new Dictionary<string, string>();
                if (!Directory.Exists(catDir)) return map;
                string[] lemmas = { "UpArm", "DownArm", "HandF", "HandB", "Hand", "Thigh", "Crus", "Foot", "UpTorso", "DownTorso", "HeadBack", "Head", "Tail", "Nosebleed" };
                string[] eyeLemmas = { "EyeOpen", "EyeClosed", "EyeHalfClosed", "EyeSad", "EyeHappy", "EyeScared", "EyePanic", "EyeLookBack", "EyeGone", "EyeGoneHealed" };
                foreach (var f in Directory.GetFiles(catDir, "*.png"))
                {
                    string name = Path.GetFileNameWithoutExtension(f);
                    if (string.IsNullOrEmpty(name)) continue;
                    if (name.StartsWith("R_")) continue;
                    foreach (var l in lemmas)
                    {
                        if (!map.ContainsKey(l) && name.EndsWith(l, System.StringComparison.Ordinal)) { map[l] = name; break; }
                    }
                    foreach (var l in eyeLemmas)
                    {
                        if (!map.ContainsKey(l) && name.EndsWith(l, System.StringComparison.Ordinal)) { map[l] = name; break; }
                    }
                }
                return map;
            }
        }

        // 配件分支：partName 形如 `__acc:<id>` 或 `__acc:<id>#<idx>`，读 entry / sec 的 sprite 与 offX/offY。
        private static void DrawAccessory(byte[] canvas, Entry entry,
            Dictionary<string, AccessoryConfigLoader.Entry> accById, string skinDir)
        {
            string raw = entry.PartName;
            if (!raw.StartsWith("__acc:")) return;
            raw = raw.Substring(6);
            int hash = raw.IndexOf('#');
            string id = hash >= 0 ? raw.Substring(0, hash) : raw;
            int secIdx = hash >= 0 ? int.Parse(raw.Substring(hash + 1)) : -1;
            if (!accById.TryGetValue(id, out var acc) || acc == null) return;

            string sprite;
            int offX, offY;
            if (secIdx >= 0)
            {
                var sec = acc.SecondaryLimbs != null && secIdx < acc.SecondaryLimbs.Count
                    ? acc.SecondaryLimbs[secIdx] : null;
                if (sec == null) return;
                sprite = sec.Sprite; offX = sec.OffX; offY = sec.OffY;
            }
            else
            {
                sprite = acc.Sprite; offX = acc.OffX; offY = acc.OffY;
            }
            if (string.IsNullOrEmpty(sprite)) return;

            var pix = LoadPng(Path.Combine(skinDir, "Accessories", sprite + ".png"));
            if (pix.W == 0) return;

            float parentRad = -entry.Rotation * Mathf.Deg2Rad;
            float c = Mathf.Cos(parentRad), s = Mathf.Sin(parentRad);
            float dx = offX * c - offY * s;
            float dy = offX * s + offY * c;
            int centerX = CompositeW / 2 + entry.X + Mathf.RoundToInt(dx);
            int centerY = CompositeH / 2 + entry.Y + Mathf.RoundToInt(dy);
            BlitWithRotation(canvas, CompositeW, CompositeH, pix.Rgba, pix.W, pix.H,
                centerX, centerY, entry.Rotation, entry.FlipX);

            // 配件 sprite 也可能附带 zones；spriteName 即 PNG 基名。
            SkinPreviewZones.Render(canvas, CompositeW, CompositeH,
                skinDir, "Accessories", sprite,
                pix.W, pix.H, centerX, centerY, entry.Rotation, entry.FlipX, 0f);
        }
    }
}

namespace SkinSyncMod
{
    public static partial class SkinPreviewRenderer
    {
        // —— Texture2D 缓存 —— //

        private static readonly Dictionary<string, Texture2D> _texCache = new Dictionary<string, Texture2D>();

        /// <summary>按 skinId 取缓存或重建。skinDir 不存在时返回 null（UI 需自己处理空态）。</summary>
        internal static Texture2D GetOrBuild(string skinId, string skinDir, SkinSyncSettings settings)
        {
            if (string.IsNullOrEmpty(skinId)) return null;
            if (_texCache.TryGetValue(skinId, out var cached) && cached != null) return cached;

            byte[] rgba = RenderRgba(skinId, skinDir, settings);
            if (rgba == null) return null;

            // Unity Texture2D 的 SetPixels32 期望左下原点；本类内部用左上原点的 RGBA 字节流，需翻转一次回去。
            var tex = new Texture2D(CompositeW, CompositeH, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
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

        /// <summary>切皮肤 / 配件 toggle / 配件 transform 改动时调用，让下次 GetOrBuild 重新合成。</summary>
        internal static void Invalidate(string skinId)
        {
            if (string.IsNullOrEmpty(skinId)) return;
            if (_texCache.TryGetValue(skinId, out var t) && t != null)
            {
                Object.Destroy(t);
            }
            _texCache.Remove(skinId);
        }
    }
}
