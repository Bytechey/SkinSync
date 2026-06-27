using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>
    /// 解析皮肤目录下的 wings.json 配置。
    /// </summary>
    public static class WingsConfigLoader
    {
        public struct Piece
        {
            public int X;
            public int Y;
            public float Rotation;
            public int ZOrder;
        }

        public class Config
        {
            public Piece WingUL;
            public Piece WingDL;
            public Piece WingUR;
            public Piece WingDR;
        }

        public static Config Defaults() => new Config
        {
            WingUL = new Piece { X = 1, Y = -8, Rotation = 314f, ZOrder = 5 },
            WingDL = new Piece { X = 2, Y = -10, Rotation = 0f, ZOrder = 5 },
            WingUR = new Piece { X = 1, Y = -9, Rotation = 318f, ZOrder = 5 },
            WingDR = new Piece { X = 3, Y = -12, Rotation = 0f, ZOrder = 5 },
        };

        /// <summary>读取并解析 wings.json，文件缺失或失败时返回默认配置。</summary>
        public static Config Load(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return Defaults();
            try { return Parse(File.ReadAllText(path)); }
            catch (System.Exception ex)
            {
                SkinSyncMod.ModLog.Warning("wings.json parse failed: " + ex.Message);
                return Defaults();
            }
        }

        /// <summary>从 JSON 文本解析 wings.json；解析失败返回 Defaults()。给内存包 fallback 用。</summary>
        public static Config Parse(string text)
        {
            var def = Defaults();
            if (string.IsNullOrEmpty(text)) return def;
            try
            {
                var raw = JsonConvert.DeserializeObject<RawConfig>(text);
                if (raw == null) return def;
                return new Config
                {
                    WingUL = ToPiece(raw.wingUL, def.WingUL),
                    WingDL = ToPiece(raw.wingDL, def.WingDL),
                    WingUR = ToPiece(raw.wingUR, def.WingUR),
                    WingDR = ToPiece(raw.wingDR, def.WingDR),
                };
            }
            catch { return def; }
        }

        private static Piece ToPiece(RawPiece r, Piece fallback)
        {
            if (r == null) return fallback;
            return new Piece
            {
                X = r.x ?? fallback.X,
                Y = r.y ?? fallback.Y,
                Rotation = r.rotation ?? fallback.Rotation,
                ZOrder = r.zOrder ?? fallback.ZOrder,
            };
        }

        private class RawConfig
        {
#pragma warning disable CS0649
            public RawPiece wingUL;
            public RawPiece wingDL;
            public RawPiece wingUR;
            public RawPiece wingDR;
#pragma warning restore CS0649
        }

        private class RawPiece
        {
#pragma warning disable CS0649
            public int? x;
            public int? y;
            public float? rotation;
            public int? zOrder;
#pragma warning restore CS0649
        }
    }
}
