using System.Collections.Generic;
using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>
    /// 按 accessories.json 配置在玩家 limb 上挂载额外 sprite GameObject，
    /// 维护"id → 主 GameObject"映射供调试面板按 id 切显隐；副件随主件一起显隐。
    /// </summary>
    public static class AccessoryAttacher
    {
        private const float PIXELS_PER_UNIT = 8f;
        private const string ContainerName = "HwAccessories";

        /// <summary>id → 主挂载 GameObject；调试面板枚举它来显示 toggle。</summary>
        public static readonly Dictionary<string, GameObject> Mounted = new Dictionary<string, GameObject>();

        /// <summary>id → 装备依赖元数据：requireSlot / 用户期望的 enabled / 所属 Body。
        /// AccessoryEnforcer 每帧扫这个表决定 GO 实际 active。</summary>
        internal sealed class MountMeta
        {
            public string RequireSlot;
            public bool DesiredEnabled;
            public Body Body;
            public string MountLimb;
            public string SpriteBaseName;
            public List<SecondaryRef> Secondaries;
        }

        internal sealed class SecondaryRef
        {
            public string MountLimb;
            public string SpriteBaseName;
            public GameObject Go;
        }
        internal static readonly Dictionary<string, MountMeta> Meta = new Dictionary<string, MountMeta>();

        /// <summary>清掉旧挂载，按 entries 重新创建。skinId 用于读取 ConfigFile 里的配件覆盖。</summary>
        public static void Apply(GameObject playerObj, IList<AccessoryConfigLoader.Entry> entries, Dictionary<string, Sprite> sprites, string skinId = null)
        {
            if (playerObj == null) return;
            Body body = playerObj.GetComponentInChildren<Body>(true);
            if (body == null) return;
            bool isRight = body.isRight;

            ClearExisting(body);
            Mounted.Clear();
            Meta.Clear();

            if (entries == null || entries.Count == 0) return;
            var bodyRenderer = body.GetComponentInChildren<SpriteRenderer>();
            Material litMat = bodyRenderer != null ? bodyRenderer.sharedMaterial : null;

            foreach (var entry in entries)
            {
                Limb limb = body.LimbByName(entry.Limb);
                if (limb == null) { SkinSyncMod.ModLog.Warning($"accessory {entry.Id} target limb {entry.Limb} not found"); continue; }
                Sprite sprite = ResolveAccessorySprite(sprites, entry.Sprite, entry.Limb, isRight);
                if (sprite == null)
                {
                    SkinSyncMod.ModLog.Warning($"accessory {entry.Id} sprite {entry.Sprite} not loaded");
                    continue;
                }

                int effOffX = entry.OffX;
                int effOffY = entry.OffY;
                float effRot = entry.Rotation;
                int effZ = entry.ZOrder;
                bool effEnabled = entry.Enabled;
                if (!string.IsNullOrEmpty(skinId) && SkinSync.Settings != null)
                {
                    var ov = SkinSync.Settings.GetAccessoryOverride(skinId, entry.Id);
                    if (ov != null)
                    {
                        if (ov.Enabled.HasValue) effEnabled = ov.Enabled.Value;
                        if (ov.OffX.HasValue) effOffX = ov.OffX.Value;
                        if (ov.OffY.HasValue) effOffY = ov.OffY.Value;
                        if (ov.Rotation.HasValue) effRot = ov.Rotation.Value;
                        if (ov.ZOrder.HasValue) effZ = ov.ZOrder.Value;
                    }
                }

                var go = AttachOne(
                    parent: limb.transform,
                    name: ContainerName + "_" + entry.Id,
                    sprite: sprite,
                    offX: effOffX, offY: effOffY, rot: effRot,
                    limbSr: limb.GetComponent<SpriteRenderer>(),
                    zOffset: effZ,
                    litMat: litMat);
                go.SetActive(effEnabled);
                Mounted[entry.Id] = go;
                var meta = new MountMeta
                {
                    RequireSlot = entry.RequireWornSlot,
                    DesiredEnabled = effEnabled,
                    Body = body,
                    MountLimb = entry.Limb,
                    SpriteBaseName = entry.Sprite,
                };
                Meta[entry.Id] = meta;

                if (entry.SecondaryLimbs != null)
                {
                    foreach (var sec in entry.SecondaryLimbs)
                    {
                        Limb secLimb = body.LimbByName(sec.Limb);
                        if (secLimb == null) { SkinSyncMod.ModLog.Warning($"accessory {entry.Id} secondary limb {sec.Limb} not found"); continue; }
                        Sprite secSprite = ResolveAccessorySprite(sprites, sec.Sprite, sec.Limb, isRight);
                        if (secSprite == null)
                        {
                            SkinSyncMod.ModLog.Warning($"accessory {entry.Id} secondary sprite {sec.Sprite} not loaded");
                            continue;
                        }
                        var secGo = AttachOne(
                            parent: secLimb.transform,
                            name: ContainerName + "_" + entry.Id + "__" + sec.Limb,
                            sprite: secSprite,
                            offX: sec.OffX, offY: sec.OffY, rot: sec.Rotation,
                            limbSr: secLimb.GetComponent<SpriteRenderer>(),
                            zOffset: sec.ZOrder,
                            litMat: litMat);
                        secGo.transform.SetParent(go.transform, worldPositionStays: true);
                        if (meta.Secondaries == null) meta.Secondaries = new List<SecondaryRef>();
                        meta.Secondaries.Add(new SecondaryRef
                        {
                            MountLimb = sec.Limb,
                            SpriteBaseName = sec.Sprite,
                            Go = secGo,
                        });
                    }
                }
            }
        }

        private static GameObject AttachOne(Transform parent, string name, Sprite sprite,
            int offX, int offY, float rot, SpriteRenderer limbSr, int zOffset,
            Material litMat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = new Vector3(offX / PIXELS_PER_UNIT, -offY / PIXELS_PER_UNIT, 0f);
            go.transform.localRotation = Quaternion.Euler(0f, 0f, rot);
            go.transform.localScale = Vector3.one;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            if (limbSr != null)
            {
                sr.sortingLayerID = limbSr.sortingLayerID;
                sr.sortingOrder = limbSr.sortingOrder + zOffset;
                sr.color = limbSr.color;
            }
            if (litMat != null) sr.sharedMaterial = litMat;
            ShadowAttacher.Ensure(go);
            return go;
        }

        /// <summary>朝向变化时调：只重选所有 Mounted 的 SpriteRenderer.sprite。</summary>
        public static void RefreshSidedSprites(GameObject playerObj, Dictionary<string, Sprite> sprites, bool isRight)
        {
            if (playerObj == null || sprites == null) return;
            foreach (var kv in Mounted)
            {
                if (!Meta.TryGetValue(kv.Key, out var meta) || meta == null) continue;
                if (kv.Value == null) continue;
                var sr = kv.Value.GetComponent<SpriteRenderer>();
                if (sr != null)
                {
                    var s = ResolveAccessorySprite(sprites, meta.SpriteBaseName, meta.MountLimb, isRight);
                    if (s != null) sr.sprite = s;
                }
                if (meta.Secondaries == null) continue;
                foreach (var sec in meta.Secondaries)
                {
                    if (sec == null || sec.Go == null) continue;
                    var secSr = sec.Go.GetComponent<SpriteRenderer>();
                    if (secSr == null) continue;
                    var s = ResolveAccessorySprite(sprites, sec.SpriteBaseName, sec.MountLimb, isRight);
                    if (s != null) secSr.sprite = s;
                }
            }
        }

        private static Sprite ResolveAccessorySprite(Dictionary<string, Sprite> sprites, string baseName, string limbName, bool isRight)
        {
            if (sprites == null || string.IsNullOrEmpty(baseName)) return null;
            char side = '\0';
            if (!string.IsNullOrEmpty(limbName))
            {
                char last = limbName[limbName.Length - 1];
                if (last == 'F' || last == 'B') side = last;
            }
            return SkinApplier.ResolveSidedSprite(sprites, baseName, side, isRight);
        }

        /// <summary>调试面板调它切单件显隐；不存在 id 静默忽略。</summary>
        public static void SetEnabled(string id, bool enabled)
        {
            if (Mounted.TryGetValue(id, out var go) && go != null) go.SetActive(enabled);
            if (Meta.TryGetValue(id, out var m)) m.DesiredEnabled = enabled;
        }

        /// <summary>面板实时改配件 transform / sortingOrder。不存在 id 静默忽略。</summary>
        public static void SetTransform(string id, int offX, int offY, float rot, int z)
        {
            if (!Mounted.TryGetValue(id, out var go) || go == null) return;
            go.transform.localPosition = new Vector3(offX / PIXELS_PER_UNIT, -offY / PIXELS_PER_UNIT, 0f);
            go.transform.localRotation = Quaternion.Euler(0f, 0f, rot);
            var sr = go.GetComponent<SpriteRenderer>();
            if (sr == null) return;
            // 还原相对 limb 的 z 偏移：mounted GO 的 sortingOrder 是 limbSr.sortingOrder + z
            // 但运行时不缓存 limb，无法精确还原；折中——直接把 sortingOrder 设为父 SpriteRenderer + z
            var limbSr = go.transform.parent != null ? go.transform.parent.GetComponent<SpriteRenderer>() : null;
            int baseOrder = limbSr != null ? limbSr.sortingOrder : 0;
            sr.sortingOrder = baseOrder + z;
        }

        private static void ClearExisting(Body body)
        {
            foreach (var tf in body.GetComponentsInChildren<Transform>(true))
            {
                if (tf == null || tf.gameObject == null) continue;
                if (tf.name.StartsWith(ContainerName + "_"))
                    Object.Destroy(tf.gameObject);
            }
        }
    }
}
