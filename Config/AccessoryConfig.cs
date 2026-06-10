using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>
    /// 解析皮肤目录下的 accessories.json 配件配置（JSON 数组）。
    /// </summary>
    public static class AccessoryConfigLoader
    {
        public class Entry
        {
            public string Id;
            public string Limb;
            public string Sprite;
            public int OffX;
            public int OffY;
            public float Rotation;
            public int ZOrder;
            public bool Enabled;
            /// <summary>非空时只在玩家穿戴该 wearSlotId 装备时才显示。</summary>
            public string RequireWornSlot;
            public List<SecondaryLimb> SecondaryLimbs;
        }

        public class SecondaryLimb
        {
            public string Limb;
            public string Sprite;
            public int OffX;
            public int OffY;
            public float Rotation;
            public int ZOrder;
        }

        /// <summary>读取 accessories.json，文件缺失或解析失败返回空列表。</summary>
        public static List<Entry> Load(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return new List<Entry>();
            try { return Parse(File.ReadAllText(path)); }
            catch (System.Exception ex)
            {
                SkinSyncMod.ModLog.Warning("accessories.json parse failed: " + ex.Message);
                return new List<Entry>();
            }
        }

        /// <summary>从 JSON 文本解析 accessories.json；解析失败返回空列表。给内存包 fallback 用。</summary>
        public static List<Entry> Parse(string text)
        {
            var list = new List<Entry>();
            if (string.IsNullOrEmpty(text)) return list;
            try
            {
                var raw = JsonConvert.DeserializeObject<List<RawEntry>>(text);
                if (raw == null) return list;
                foreach (var r in raw)
                {
                    if (r == null) continue;
                    if (string.IsNullOrEmpty(r.id) || string.IsNullOrEmpty(r.limb) || string.IsNullOrEmpty(r.sprite)) continue;
                    list.Add(ToEntry(r));
                }
            }
            catch { }
            return list;
        }

        private static Entry ToEntry(RawEntry r)
        {
            var entry = new Entry
            {
                Id = r.id,
                Limb = r.limb,
                Sprite = r.sprite,
                OffX = r.offX,
                OffY = r.offY,
                Rotation = r.rot,
                ZOrder = r.z,
                Enabled = r.enabled,
                RequireWornSlot = string.IsNullOrEmpty(r.requireWornSlot) ? null : r.requireWornSlot,
            };
            if (r.secondaryLimbs != null && r.secondaryLimbs.Count > 0)
            {
                entry.SecondaryLimbs = new List<SecondaryLimb>();
                foreach (var s in r.secondaryLimbs)
                {
                    if (s == null || string.IsNullOrEmpty(s.limb) || string.IsNullOrEmpty(s.sprite)) continue;
                    entry.SecondaryLimbs.Add(new SecondaryLimb
                    {
                        Limb = s.limb,
                        Sprite = s.sprite,
                        OffX = s.offX,
                        OffY = s.offY,
                        Rotation = s.rot,
                        ZOrder = s.z,
                    });
                }
            }
            return entry;
        }

        private class RawEntry
        {
#pragma warning disable CS0649
            public string id;
            public string limb;
            public string sprite;
            public int offX;
            public int offY;
            public float rot;
            public string requireWornSlot;
            public List<RawSecondary> secondaryLimbs;
#pragma warning restore CS0649
            public int z = 5;
            public bool enabled = true;
        }

        private class RawSecondary
        {
#pragma warning disable CS0649
            public string limb;
            public string sprite;
            public int offX;
            public int offY;
            public float rot;
#pragma warning restore CS0649
            public int z = 5;
        }
    }
}
