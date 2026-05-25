using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
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
            WingUL = new Piece { X = -2, Y = -8, Rotation = 314f, ZOrder = 5 },
            WingDL = new Piece { X = -1, Y = -10, Rotation = 0f, ZOrder = 4 },
            WingUR = new Piece { X = -2, Y = -9, Rotation = 318f, ZOrder = 5 },
            WingDR = new Piece { X = 0, Y = -12, Rotation = 0f, ZOrder = 4 },
        };

        /// <summary>
        /// 读取并解析 wings.json，文件缺失或失败时返回默认配置。
        /// </summary>
        public static Config Load(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return Defaults();
            try
            {
                string text = File.ReadAllText(path);
                var d = Defaults();
                d.WingUL = ParseSection(text, "wingUL", d.WingUL);
                d.WingDL = ParseSection(text, "wingDL", d.WingDL);
                d.WingUR = ParseSection(text, "wingUR", d.WingUR);
                d.WingDR = ParseSection(text, "wingDR", d.WingDR);
                return d;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[SkinSync] wings.json parse failed: " + ex.Message);
                return Defaults();
            }
        }

        private static Piece ParseSection(string text, string name, Piece fallback)
        {
            var section = Regex.Match(text, "\"" + name + "\"\\s*:\\s*\\{([^}]*)\\}");
            if (!section.Success) return fallback;
            string body = section.Groups[1].Value;
            return new Piece
            {
                X = IntFrom(body, "x", fallback.X),
                Y = IntFrom(body, "y", fallback.Y),
                Rotation = FloatFrom(body, "rotation", fallback.Rotation),
                ZOrder = IntFrom(body, "zOrder", fallback.ZOrder),
            };
        }

        private static int IntFrom(string body, string field, int fallback)
        {
            var m = Regex.Match(body, "\"" + field + "\"\\s*:\\s*(-?\\d+)");
            return m.Success && int.TryParse(m.Groups[1].Value, out int v) ? v : fallback;
        }

        private static float FloatFrom(string body, string field, float fallback)
        {
            var m = Regex.Match(body, "\"" + field + "\"\\s*:\\s*(-?\\d+(?:\\.\\d+)?)");
            return m.Success && float.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : fallback;
        }
    }
}
