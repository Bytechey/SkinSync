using BepInEx;
using SkinSyncMod.Interop.Consumers;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>
    /// 将本地 PNG 与 wings.json 配置应用到玩家 GameObject 的所有 limb / 表情 / 尾巴 / 翅膀。
    /// </summary>
    public static class SkinApplier
    {
        private static Dictionary<string, Dictionary<string, Sprite>> _skinCache = new Dictionary<string, Dictionary<string, Sprite>>();
        private static Dictionary<string, WingsConfigLoader.Config> _wingsCache = new Dictionary<string, WingsConfigLoader.Config>();
        private static string _currentCharacter = null;
        // 多人模式下每个 chara 当前应用的皮肤名——给 BloodPatches 等按 chara 反查 character。
        private static readonly Dictionary<GameObject, string> _byChara = new Dictionary<GameObject, string>();
        private static readonly HashSet<string> _requestedPacks = new HashSet<string>();
        private static readonly Dictionary<int, Sprite> _originalLimbSprites = new Dictionary<int, Sprite>();
        private static readonly Dictionary<int, Sprite> _originalTailSprites = new Dictionary<int, Sprite>();
        private static readonly Dictionary<int, FacialOriginal> _originalFaces = new Dictionary<int, FacialOriginal>();

        /// <summary>已被「使用原版皮肤」恢复成原版的角色；开关关闭后据此重新套皮肤。</summary>
        private static readonly HashSet<GameObject> _vanillaApplied = new HashSet<GameObject>();

        private struct FacialOriginal
        {
            public Sprite DefaultHead;
            public Sprite DefaultHeadMouth;
            public Sprite DefaultHeadMouthHalf;
            public Sprite EyesGone;
            public Sprite EyesGoneHealed;
            public Sprite[] DisfiguredHead;
            public Sprite[] DisfiguredHeadHeal;
            public Eye[] Eyes;
        }

        /// <summary>查指定 chara 当前已应用的皮肤名；未应用或当前是原版外观时返回 null。</summary>
        public static string GetCharacterByChara(GameObject chara)
        {
            if (chara == null) return null;
            if (_vanillaApplied.Contains(chara)) return null;
            return _byChara.TryGetValue(chara, out var name) ? name : null;
        }

        /// <summary>按 world 坐标找半径内最近 Body 对应的 character；用于落地血迹 / 爆血粒子按 owner 染色。</summary>
        public static bool TryGetCharacterByPosition(Vector2 pos, float radius, out string character)
        {
            character = null;
            float bestDist = radius;
            foreach (var body in UnityEngine.Object.FindObjectsOfType<Body>())
            {
                if (body == null) continue;
                float d = Vector2.Distance(pos, body.transform.position);
                if (d > bestDist) continue;
                GameObject host = body.transform.parent != null ? body.transform.parent.gameObject : body.gameObject;
                if (_byChara.TryGetValue(host, out var name))
                {
                    bestDist = d;
                    character = name;
                }
            }
            return character != null;
        }

        /// <summary>已应用 character 数=1 时直接返回该 character；用作 world 血迹 / 粒子无 owner 链路时的兜底。</summary>
        public static bool TryGetAnyAppliedCharacter(out string character)
        {
            character = null;
            string only = null;
            foreach (var kv in _byChara)
            {
                if (string.IsNullOrEmpty(kv.Value)) continue;
                if (only == null) { only = kv.Value; continue; }
                if (only != kv.Value) return false;
            }
            if (only == null) return false;
            character = only;
            return true;
        }

        /// <summary>当前已应用的 character 对应 sprite 字典，供 ZoneFrameAnimator 等运行时组件解析帧名用。</summary>
        public static Dictionary<string, Sprite> GetSpriteDict()
        {
            if (_currentCharacter != null && _skinCache.TryGetValue(_currentCharacter, out var d)) return d;
            return null;
        }

        private static System.Action<GameObject, bool> _skinApplied;

        /// <summary>注册皮肤应用回调；每次某玩家应用皮肤后回调 (playerObj, 是否含翅膀)。供 ExtraLimbs 等外部 mod 反射订阅。</summary>
        public static void RegisterSkinAppliedListener(System.Action<GameObject, bool> cb)
        {
            if (cb == null) return;
            _skinApplied -= cb;
            _skinApplied += cb;
        }

        public static void UnregisterSkinAppliedListener(System.Action<GameObject, bool> cb)
        {
            if (cb == null) return;
            _skinApplied -= cb;
        }

        /// <summary>判定玩家当前是否挂有翅膀（SkinSync 的 HwWings 容器存在）。</summary>
        public static bool HasWings(GameObject playerObj)
        {
            if (playerObj == null) return false;
            Body body = playerObj.GetComponentInChildren<Body>(true);
            if (body == null) return false;
            foreach (var tf in body.GetComponentsInChildren<Transform>(true))
                if (tf != null && tf.name == "HwWings") return true;
            return false;
        }

        private const float PIXELS_PER_UNIT = 8f;
        private const float UPTORSO_OFFSET_X = 6f;
        private const float UPTORSO_OFFSET_Y = -10f;

        /// <summary>尺寸归一后的 Sprite 缓存：(纹理实例ID, 目标宽, 目标高) → Sprite。</summary>
        private static readonly Dictionary<long, Sprite> _fittedSprites = new Dictionary<long, Sprite>();

        private static int _lastLimbTotal;
        private static int _lastLimbApplied;

        /// <summary>配置开关；插件未初始化时（如预览路径）按开启处理，与默认值一致。</summary>
        private static bool NormalizeSizeEnabled
            => SkinSync.Settings == null || SkinSync.Settings.NormalizeSpriteSize.Value;

        /// <summary>
        /// 入口：刷新 sprite、表情、翅膀挂载，并确保 TailFlowDeform 已附着。
        /// </summary>
        /// <param name="characterName">皮肤标识（磁盘目录名或内存包名）。</param>
        /// <param name="remoteCharType">
        /// 远端发送者上报的真实角色类型，0 = 未知（本地应用或旧版客户端）。
        /// 仅在取不到 body.charType 时作为翅膀缩放的依据：Together 不同步 characterSelected，
        /// 远端 Body.charType 只是本机 prefab 的类型，不能当对方的真实角色。
        /// </param>
        public static void ApplySkinToPlayer(GameObject playerObj, string characterName, int remoteCharType = 0)
        {
            if (playerObj == null) return;

            // 「使用原版皮肤」命中时直接恢复原版外观，不再走后面的皮肤装配。
            if (ShouldUseVanillaSkin(playerObj))
            {
                RestoreVanillaSkin(playerObj);
                return;
            }
            _vanillaApplied.Remove(playerObj);

            if (string.IsNullOrEmpty(characterName)) return;

            bool sameSkin = _byChara.TryGetValue(playerObj, out var current) && current == characterName;

            if (!_skinCache.TryGetValue(characterName, out var spriteDict))
            {
                spriteDict = LoadCharacterSprites(characterName);
                if (spriteDict == null || spriteDict.Count == 0)
                {
                    _byChara[playerObj] = characterName;
                    return;
                }
                _skinCache[characterName] = spriteDict;
            }

            if (!_wingsCache.TryGetValue(characterName, out var wingsCfg))
            {
                string wingsJsonPath = Path.Combine(SkinPathResolver.GetSkinDir(characterName), "wings.json");
                if (File.Exists(wingsJsonPath))
                    wingsCfg = WingsConfigLoader.Load(wingsJsonPath);
                else
                    wingsCfg = LoadJsonFromMemoryPack(characterName, "wings.json", WingsConfigLoader.Parse) ?? WingsConfigLoader.Defaults();
                _wingsCache[characterName] = wingsCfg;
            }

            if (!sameSkin)
            {
                Body bodyForDir = playerObj.GetComponentInChildren<Body>(true);
                bool isRight = bodyForDir != null ? bodyForDir.isRight : true;
                int charType = bodyForDir != null ? bodyForDir.charType : 0;

                // 全局倍率本角色只算一次：所有部件（含头、眼睛、嘴）用同一个值。
                float fitScale = GlobalFitScale(spriteDict);

                int limbTotal = 0;
                int limbApplied = 0;
                foreach (var limb in playerObj.GetComponentsInChildren<Limb>(true))
                {
                    limbTotal++;
                    if (ReplaceLimbSprite(limb, spriteDict, isRight, charType, fitScale)) limbApplied++;
                }
                _lastLimbTotal = limbTotal;
                _lastLimbApplied = limbApplied;

                foreach (var tail in playerObj.GetComponentsInChildren<TailScript>(true))
                    ReplaceTailSprite(tail, spriteDict, isRight, charType, fitScale);

                foreach (var face in playerObj.GetComponentsInChildren<FacialExpression>(true))
                    ReplaceFacialExpressionSprites(face, spriteDict, isRight, charType, fitScale);

                EnsureWingsAttached(playerObj, spriteDict, wingsCfg, remoteCharType);

                foreach (var tail in playerObj.GetComponentsInChildren<TailScript>(true))
                {
                    if (tail.GetComponent<TailFlowDeform>() != null)
                        continue;

                    // A disabled renderer can be an animation proxy owned by
                    // another component/mod. Deforming it would create a new,
                    // independently rendered tail and reveal hidden visuals.
                    var renderer = tail.GetComponent<SpriteRenderer>();
                    if (tail.enabled && renderer != null && renderer.enabled)
                        tail.gameObject.AddComponent<TailFlowDeform>();
                }
            }

            string accessoriesPath = Path.Combine(SkinPathResolver.GetSkinDir(characterName), "accessories.json");
            var accEntries = File.Exists(accessoriesPath)
                ? AccessoryConfigLoader.Load(accessoriesPath)
                : (LoadJsonFromMemoryPack(characterName, "accessories.json", AccessoryConfigLoader.Parse) ?? new List<AccessoryConfigLoader.Entry>());
            AccessoryAttacher.Apply(playerObj, accEntries, spriteDict, characterName);
            SkinSyncMod.ModLog.Debug($"应用皮肤 {characterName}：肢体 {playerObj.GetComponentsInChildren<Limb>(true).Length} 个，配件 {accEntries.Count} 项{(sameSkin ? "（皮肤未变，仅刷新配件）" : string.Empty)}");
            if (!sameSkin)
            {
                ReportCoverage(characterName, playerObj);
            }

            _currentCharacter = characterName;
            _byChara[playerObj] = characterName;
            if (!sameSkin)
            {
                ZonesAttacher.Apply(playerObj, characterName);
            }
            BloodAttacher.Apply(playerObj, characterName);
            EnsureLimbDirWatcher(playerObj);
            _skinApplied?.Invoke(playerObj, HasWings(playerObj));
        }

        /// <summary>
        /// 当前角色是否应该显示游戏原版皮肤。
        /// 本地玩家看 VanillaSkinForSelf，远端玩家看 VanillaSkinForOthers，两个开关互不干扰。
        /// </summary>
        private static bool ShouldUseVanillaSkin(GameObject playerObj)
        {
            var settings = SkinSync.Settings;
            if (settings == null) return false;
            bool isLocal = IsLocalPlayer(playerObj);
            return isLocal ? settings.VanillaSkinForSelf.Value : settings.VanillaSkinForOthers.Value;
        }

        /// <summary>判定这个角色对象是不是本机操控的玩家。</summary>
        private static bool IsLocalPlayer(GameObject playerObj)
        {
            var cam = PlayerCamera.main;
            if (cam == null || cam.body == null) return false;
            var body = playerObj.GetComponentInChildren<Body>(true);
            return body != null && body == cam.body;
        }

        /// <summary>
        /// 把角色恢复成游戏原版外观：还原缓存里的原版 sprite，删掉模组挂上去的容器与组件。
        /// 缓存里的原版 sprite 是首次套皮肤时记下的，所以只能恢复曾经套过皮肤的角色。
        /// </summary>
        public static void RestoreVanillaSkin(GameObject playerObj)
        {
            if (playerObj == null) return;

            foreach (var limb in playerObj.GetComponentsInChildren<Limb>(true))
            {
                var renderer = limb.GetComponent<SpriteRenderer>();
                if (renderer == null) continue;
                if (_originalLimbSprites.TryGetValue(renderer.GetInstanceID(), out var orig) && orig != null)
                    renderer.sprite = orig;
            }

            foreach (var tail in playerObj.GetComponentsInChildren<TailScript>(true))
            {
                var renderer = tail.GetComponent<SpriteRenderer>();
                if (renderer != null && _originalTailSprites.TryGetValue(renderer.GetInstanceID(), out var orig) && orig != null)
                    renderer.sprite = orig;

                var deform = tail.GetComponent<TailFlowDeform>();
                if (deform != null) UnityEngine.Object.Destroy(deform);
            }

            foreach (var face in playerObj.GetComponentsInChildren<FacialExpression>(true))
            {
                if (!_originalFaces.TryGetValue(face.GetInstanceID(), out var f)) continue;
                face.defaultHead = f.DefaultHead;
                face.defaultHeadMouth = f.DefaultHeadMouth;
                face.defaultHeadMouthHalf = f.DefaultHeadMouthHalf;
                face.eyesGone = f.EyesGone;
                face.eyesGoneHealed = f.EyesGoneHealed;
                if (face.disfiguredHead != null && f.DisfiguredHead != null)
                {
                    int n = Mathf.Min(face.disfiguredHead.Length, f.DisfiguredHead.Length);
                    for (int i = 0; i < n; i++) face.disfiguredHead[i] = f.DisfiguredHead[i];
                }
                if (face.disfiguredHeadHeal != null && f.DisfiguredHeadHeal != null)
                {
                    int n = Mathf.Min(face.disfiguredHeadHeal.Length, f.DisfiguredHeadHeal.Length);
                    for (int i = 0; i < n; i++) face.disfiguredHeadHeal[i] = f.DisfiguredHeadHeal[i];
                }
                if (face.eyeList != null && f.Eyes != null)
                {
                    int n = Mathf.Min(face.eyeList.Count, f.Eyes.Length);
                    for (int i = 0; i < n; i++) face.eyeList[i] = f.Eyes[i];
                }
            }

            DestroyModContainers(playerObj);
            _vanillaApplied.Add(playerObj);
        }

        /// <summary>删掉模组挂在角色上的容器：翅膀 / 配件 / 区域光。</summary>
        private static void DestroyModContainers(GameObject playerObj)
        {
            var doomed = new List<GameObject>();
            foreach (var tf in playerObj.GetComponentsInChildren<Transform>(true))
            {
                if (tf == null) continue;
                string n = tf.name;
                if (n == "HwWings" || n == "HwAccessories" || n.StartsWith("HwZones_", System.StringComparison.Ordinal))
                    doomed.Add(tf.gameObject);
            }
            foreach (var go in doomed) UnityEngine.Object.Destroy(go);
        }

        /// <summary>
        /// 「使用原版皮肤」开关变化后重跑一遍所有已接管的角色：
        /// 已套皮肤的按开关决定继续套还是恢复原版；已恢复原版的按开关决定是否重新套回去。
        /// </summary>
        public static void RefreshAll()
        {
            var targets = new List<GameObject>();
            foreach (var kv in _byChara) if (kv.Key != null) targets.Add(kv.Key);
            foreach (var go in _vanillaApplied) if (go != null && !targets.Contains(go)) targets.Add(go);

            string fallback = SkinSync.Settings != null ? SkinSync.Settings.CurrentSkin.Value : null;
            foreach (var go in targets)
            {
                string skin = _byChara.TryGetValue(go, out var name) && !string.IsNullOrEmpty(name) ? name : fallback;
                if (string.IsNullOrEmpty(skin)) continue;
                ApplySkinToPlayer(go, skin);
            }
            ModLog.Info($"原版皮肤开关变化：已刷新 {targets.Count} 个角色");
        }
        public static void RefreshSidedSprites(GameObject playerObj, bool isRight)
        {
            if (playerObj == null) return;
            if (!_byChara.TryGetValue(playerObj, out var character)) return;
            if (!_skinCache.TryGetValue(character, out var spriteDict)) return;

            Body body = playerObj.GetComponentInChildren<Body>(true);
            int charType = body != null ? body.charType : 0;

            float fitScale = GlobalFitScale(spriteDict);
            foreach (var limb in playerObj.GetComponentsInChildren<Limb>(true))
                ReplaceLimbSprite(limb, spriteDict, isRight, charType, fitScale);
            foreach (var tail in playerObj.GetComponentsInChildren<TailScript>(true))
                ReplaceTailSprite(tail, spriteDict, isRight, charType, fitScale);
            foreach (var face in playerObj.GetComponentsInChildren<FacialExpression>(true))
                ReplaceFacialExpressionSprites(face, spriteDict, isRight, charType, fitScale);

            AccessoryAttacher.RefreshSidedSprites(playerObj, spriteDict, isRight);
        }

        private static void EnsureLimbDirWatcher(GameObject playerObj)
        {
            if (playerObj == null) return;
            if (playerObj.GetComponent<LimbDirWatcher>() == null)
                playerObj.AddComponent<LimbDirWatcher>();
        }

        /// <summary>
        /// 报告肢体贴图覆盖率。皮肤只含某一个角色的贴图名时，换到其他角色会全部不命中并静默回退原图，
        /// 这里把它变成一条可见的 warning。
        /// </summary>
        private static void ReportCoverage(string characterName, GameObject playerObj)
        {
            if (_lastLimbTotal <= 0) return;

            if (_lastLimbApplied == 0)
            {
                Body body = playerObj != null ? playerObj.GetComponentInChildren<Body>(true) : null;
                SkinSyncMod.ModLog.Warning($"皮肤 {characterName} 没有任何肢体贴图能对应当前角色"
                    + $"（charType={body?.charType}，肢体 {_lastLimbTotal} 个），已全部保留原图。"
                    + "该皮肤可能是为其他角色画的；要支持多角色，请把各角色命名的贴图放进同一个皮肤目录。");
                return;
            }

            if (_lastLimbApplied < _lastLimbTotal)
            {
                SkinSyncMod.ModLog.Debug($"皮肤 {characterName}：替换 {_lastLimbApplied}/{_lastLimbTotal} 个肢体，"
                    + $"其余 {_lastLimbTotal - _lastLimbApplied} 个无同名贴图，保留原图。");
            }
        }

        /// <summary>遍历当前场景所有 Body，对每个挂载点调用 ApplySkinToPlayer；用于单机模式无固定 localPlayerObject 时兜底应用皮肤。</summary>
        public static int ApplyToScene(string characterName)
        {
            if (string.IsNullOrEmpty(characterName)) return 0;
            int count = 0;
            foreach (var body in UnityEngine.Object.FindObjectsOfType<Body>())
            {
                if (body == null) continue;
                GameObject host = body.transform.parent != null ? body.transform.parent.gameObject : body.gameObject;
                ApplySkinToPlayer(host, characterName);
                count++;
            }
            return count;
        }

        /// <summary>统一 sprite 选择：朝右优先 R_ 套；无命中返回 null。实际逻辑在 Resolve.SidedSpriteLookup，这里保留入口供配件模块复用。</summary>
        internal static Sprite ResolveSidedSprite(Dictionary<string, Sprite> dict, string baseName, char side, bool isRight)
            => Resolve.SidedSpriteLookup.Find(dict, baseName, side, isRight);

        private static bool ReplaceLimbSprite(Limb limb, Dictionary<string, Sprite> dict, bool isRight, int charType, float scale)
        {
            var renderer = limb.GetComponent<SpriteRenderer>();
            if (renderer == null) return false;
            int id = renderer.GetInstanceID();
            if (!_originalLimbSprites.ContainsKey(id) && renderer.sprite != null)
                _originalLimbSprites[id] = renderer.sprite;
            if (renderer.sprite == null && !_originalLimbSprites.TryGetValue(id, out _)) return false;

            Sprite reference = _originalLimbSprites.TryGetValue(id, out var orig) ? orig : renderer.sprite;
            string baseSprite = reference?.name ?? string.Empty;
            string limbName = limb.gameObject.name ?? string.Empty;
            char lastChar = limbName.Length > 0 ? limbName[limbName.Length - 1] : '\0';
            char side = (lastChar == 'F' || lastChar == 'B') ? lastChar : '\0';

            // 三层解析（当前角色名 → 部位键名 → 其他角色同名部位）全部由 CharacterCatalog 驱动，
            // 不再维护任何硬编码映射表；新增角色时这里不用改。
            var outcome = Resolve.SkinResolver.ResolveByName(baseSprite, dict, charType, side, isRight);
            renderer.sprite = outcome.Hit ? FitToTarget(outcome.Sprite, reference, scale) : reference;
            return outcome.Hit;
        }

        /// <summary>
        /// 把皮肤贴图重采样到目标像素尺寸。
        /// 皮肤 PNG 的宽高比与游戏原图系统性不一致（手部原图 6x4 / 皮肤 6x12，下臂原图 4x14 / 皮肤 12x14），
        /// 等比缩放无法同时对齐两个方向，只能宽高各自缩放。像素游戏里“占位正确”远重要于“比例不变”。
        /// </summary>
        /// <param name="skin">皮肤里的贴图</param>
        /// <param name="reference">当前角色该部位的原图（提供 pivot / ppu，也是表情的尺寸基准）</param>
        /// <param name="useCurrentCharacterSize">
        /// true = 按当前角色原图尺寸（表情专用：眼睛画布恒等于头画布，必须贴合当前角色的头）；
        /// false = 按皮肤文件名所属角色的原生尺寸（肢体/尾巴：一套 experiment 皮肤在任何角色上保持作者画的外观）。
        /// </param>
        /// <summary>
        /// 皮肤贴图统一缩放：所有部件用同一个全局倍率，只缩整体，不逐部位对齐。
        /// </summary>
        /// <remarks>
        /// 逐部位对齐（每块单独拉到原图画布或碰撞体）会破坏作者画的相对比例：
        /// 头被压扁、手臂变粗，而眼睛与头因为画布基准不同而错位。
        /// 改成「原样加载 + 一个全局倍率」后，头、眼睛、嘴共用同一变换，
        /// 内容在画布中的相对位置完全不变，因此不会错位。
        /// </remarks>
        /// <param name="skin">皮肤里的贴图（整张画布，不裁 alpha）</param>
        /// <param name="reference">游戏原图，只用来取目标画布尺寸与 pivot / ppu</param>
        /// <param name="scale">全局倍率，由 ComputeGlobalFitScale 统一算出</param>
        private static Sprite FitToTarget(Sprite skin, Sprite reference, float scale)
        {
            if (skin == null || reference == null) return skin;
            if (!NormalizeSizeEnabled) return skin;
            // 倍率 1 = 不上倍率：皮肤按自己的画布原样显示（即旧的加载逻辑）。
            if (Mathf.Abs(scale - 1f) < 0.001f) return skin;

            int targetW = Mathf.Max(1, Mathf.RoundToInt(reference.rect.width * scale));
            int targetH = Mathf.Max(1, Mathf.RoundToInt(reference.rect.height * scale));

            int srcW = Mathf.RoundToInt(skin.textureRect.width);
            int srcH = Mathf.RoundToInt(skin.textureRect.height);
            if (srcW == targetW && srcH == targetH) return skin;

            long key = ((long)skin.texture.GetInstanceID() << 40) ^ ((long)targetW << 20) ^ (uint)targetH;
            if (_fittedSprites.TryGetValue(key, out var cached) && cached != null) return cached;

            // 多次换皮肤时缓存会累积；已挂到 renderer 上的 Sprite 不受清空影响
            if (_fittedSprites.Count > 128) _fittedSprites.Clear();

            // 整画布重采样：保留「内容画在画布哪个位置」的信息，这是头与眼睛能对齐的关键。
            Texture2D resampled = ResampleRegion(skin.texture, skin.textureRect, targetW, targetH);
            if (resampled == null) return skin;

            // pivot 用原图的归一化值（实测三角色肢体均为 0.5/0.5），ppu 也用原图的：
            // 世界尺寸 = 目标画布 / ppu = 原图画布 × scale / ppu。
            var pivot = new Vector2(reference.pivot.x / reference.rect.width,
                                    reference.pivot.y / reference.rect.height);
            var sprite = Sprite.Create(resampled, new Rect(0f, 0f, targetW, targetH), pivot, reference.pixelsPerUnit);
            sprite.name = skin.name;
            _fittedSprites[key] = sprite;
            ModLog.Debug($"[归一] {skin.name} 画布 {srcW}x{srcH} → {targetW}x{targetH}（全局倍率 {scale:F3}）");
            return sprite;
        }

        /// <summary>
        /// 算全局缩放倍率。
        /// 目前**停用**：先按皮肤原画布原样加载（旧逻辑），确认基线效果再接倍率。
        /// 要启用时把下面的 return 1f 换成实际计算（口径 = 所有肢体的原图画布高之和 / 皮肤画布高之和）。
        /// </summary>
        private static float GlobalFitScale(Dictionary<string, Sprite> dict)
        {
            if (dict == null || dict.Count == 0) return 1f;
            return 1f;
        }

        /// <summary>备用的倍率计算：按所有肢体高度之和的比例取一个统一值。</summary>
        private static float ComputeFitScaleByHeight(Dictionary<string, Sprite> dict)
        {
            if (dict == null || dict.Count == 0) return 1f;

            float wantSum = 0f;
            float gotSum = 0f;
            foreach (var kv in dict)
            {
                if (kv.Value == null) continue;
                string lower = kv.Key.ToLowerInvariant();
                // 只算肢体：表情与头同画布，重复计入会把倍率带偏；翅膀/配件游戏原版没有对应物。
                if (lower.Contains("disfigured") || lower.Contains("mouth") || lower.Contains("eye")) continue;
                if (!(lower.Contains("torso") || lower.Contains("arm") || lower.Contains("hand")
                      || lower.Contains("thigh") || lower.Contains("crus") || lower.Contains("foot")
                      || lower.Contains("head") || lower.Contains("tail"))) continue;
                if (!Character.CharacterCatalog.TryGetSpriteSize(kv.Key, out Vector2Int original)) continue;

                wantSum += original.y;
                gotSum += kv.Value.textureRect.height;
            }

            if (gotSum < 1f || wantSum < 1f) return 1f;
            return Mathf.Clamp(wantSum / gotSum, 0.2f, 2f);
        }

        /// <summary>最近邻重采样指定区域，保持像素感（双线性会把像素画糊掉）。</summary>
        /// <param name="src">源纹理</param>
        /// <param name="region">要重采样的纹理区域（像素坐标，左下原点）</param>
        private static Texture2D ResampleRegion(Texture2D src, Rect region, int dstW, int dstH)
        {
            if (src == null || dstW <= 0 || dstH <= 0) return null;
            int srcW = Mathf.RoundToInt(region.width);
            int srcH = Mathf.RoundToInt(region.height);
            if (srcW <= 0 || srcH <= 0) return null;
            int originX = Mathf.RoundToInt(region.x);
            int originY = Mathf.RoundToInt(region.y);

            try
            {
                Color32[] srcPixels = src.GetPixels32();
                if (srcPixels == null) return null;
                int stride = src.width;
                if (srcPixels.Length < stride * src.height) return null;

                var dst = new Texture2D(dstW, dstH, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                };
                var dstPixels = new Color32[dstW * dstH];
                for (int y = 0; y < dstH; y++)
                {
                    int sy = originY + (int)((long)y * srcH / dstH);
                    if (sy >= originY + srcH) sy = originY + srcH - 1;
                    int srcRow = sy * stride;
                    int dstRow = y * dstW;
                    for (int x = 0; x < dstW; x++)
                    {
                        int sx = originX + (int)((long)x * srcW / dstW);
                        if (sx >= originX + srcW) sx = originX + srcW - 1;
                        dstPixels[dstRow + x] = srcPixels[srcRow + sx];
                    }
                }
                dst.SetPixels32(dstPixels);
                // makeNoLongerReadable 必须为 false：TailFlowDeform 会 GetPixels32()
                dst.Apply(false, false);
                return dst;
            }
            catch (System.Exception ex)
            {
                SkinSyncMod.ModLog.Warning("重采样皮肤贴图失败: " + ex.Message);
                return null;
            }
        }

        private static Dictionary<string, Sprite> LoadCharacterSprites(string character)
        {
            var dict = new Dictionary<string, Sprite>();
            string basePath = SkinPathResolver.GetSkinDir(character);
            if (!Directory.Exists(basePath))
            {
                if (TryLoadFromMemoryPack(character, dict)) return dict;
                if (TryRequestSkinPack(character))
                    SkinSyncMod.ModLog.Info($"本机缺少皮肤 {character}，已向其他玩家请求皮肤包");
                else
                    SkinSyncMod.ModLog.Info($"本机缺少皮肤目录 {character}（{basePath}）；请将该皮肤包放入 plugins/CustomSprites/{character}");
                return dict;
            }
            var baseSizes = BaseSizesLoader.Load(character);
            foreach (var filePath in Directory.GetFiles(basePath, "*.png", SearchOption.AllDirectories))
            {
                string fileName = Path.GetFileNameWithoutExtension(filePath);
                if (dict.ContainsKey(fileName)) continue;
                BuildSpriteInto(dict, fileName, File.ReadAllBytes(filePath), baseSizes);
            }
            return dict;
        }

        /// <summary>每个皮肤文件的非透明内容包围盒（左下原点），供肢体裁白边用；表情不能裁。</summary>
        private static readonly Dictionary<string, Rect> _contentRects = new Dictionary<string, Rect>(System.StringComparer.Ordinal);

        private static void BuildSpriteInto(Dictionary<string, Sprite> dict, string fileName, byte[] data, Dictionary<string, Vector2Int> baseSizes)
        {
            if (dict.ContainsKey(fileName)) return;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            if (!ImageConversion.LoadImage(tex, data)) return;
            float ppu = PIXELS_PER_UNIT;
            if (baseSizes != null && baseSizes.TryGetValue(fileName, out var baseSize) && baseSize.x > 0)
                ppu = PIXELS_PER_UNIT * (tex.width / (float)baseSize.x);

            // Sprite 保留整张画布：表情（眼睛/嘴/鼻血）只占画布一小块，位置本身有意义，
            // 裁了会让眼睛跑到脸中间。白边裁切在 FitToTarget 里按用途决定（肢体才裁）。
            _contentRects[fileName] = ComputeContentRect(tex);
            var sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), ppu);
            sprite.name = fileName;
            dict[fileName] = sprite;
        }

        /// <summary>计算纹理中非透明像素的包围盒（左下原点，与 Sprite rect 一致）；全透明时退回整张纹理。</summary>
        private static Rect ComputeContentRect(Texture2D tex)
        {
            int w = tex.width, h = tex.height;
            var full = new Rect(0f, 0f, w, h);
            Color32[] pixels;
            try { pixels = tex.GetPixels32(); }
            catch { return full; }
            if (pixels == null || pixels.Length < w * h) return full;

            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    if (pixels[row + x].a <= 8) continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
            if (maxX < 0) return full;
            return new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        /// <summary>缺磁盘目录时，从内存皮肤包按文件名找文件，用解析器把字节转 JSON 配置；缺则返回 default。</summary>
        private static T LoadJsonFromMemoryPack<T>(string character, string fileName, System.Func<string, T> parser) where T : class
        {
            var pack = SkinPackCodec.GetInMemory(character);
            if (pack == null) return null;
            foreach (var kv in pack)
            {
                if (kv.Key.Equals(fileName, System.StringComparison.OrdinalIgnoreCase))
                    return parser(System.Text.Encoding.UTF8.GetString(kv.Value));
            }
            return null;
        }

        /// <summary>缺磁盘目录时，从 SkinPackCodec 内存缓存（实验性皮肤包同步）构建 sprite 字典；无缓存返回 false。</summary>
        private static bool TryLoadFromMemoryPack(string character, Dictionary<string, Sprite> dict)
        {
            var pack = SkinPackCodec.GetInMemory(character);
            if (pack == null) return false;
            Dictionary<string, Vector2Int> baseSizes = null;
            foreach (var kv in pack)
                if (kv.Key.EndsWith("baseSizes.json", System.StringComparison.OrdinalIgnoreCase))
                    baseSizes = BaseSizesLoader.ParseContent(System.Text.Encoding.UTF8.GetString(kv.Value));
            foreach (var kv in pack)
            {
                if (!kv.Key.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase)) continue;
                BuildSpriteInto(dict, Path.GetFileNameWithoutExtension(kv.Key), kv.Value, baseSizes);
            }
            return dict.Count > 0;
        }

        /// <summary>开启实验性同步且处于多人会话时，向其他玩家请求该皮肤包；每个皮肤只请求一次。</summary>
        private static bool TryRequestSkinPack(string character)
        {
            if (SkinSync.Settings == null || !SkinSync.Settings.EnableSkinPackSync.Value) return false;
            if (!SessionConsumer.IsAvailable || !SessionConsumer.IsRunning) return false;
            if (!_requestedPacks.Add(character)) return false;
            Network.MultiplayerSender.SendSkinPackRequest(character);
            return true;
        }

        /// <summary>皮肤包到达后清该皮肤缓存，对正在使用它的玩家重新应用。</summary>
        public static void ReapplyForSkin(string skinID)
        {
            if (string.IsNullOrEmpty(skinID)) return;
            _skinCache.Remove(skinID);
            _requestedPacks.Remove(skinID);
            var targets = new List<GameObject>();
            foreach (var kv in _byChara)
                if (kv.Value == skinID && kv.Key != null) targets.Add(kv.Key);
            foreach (var go in targets)
            {
                _byChara.Remove(go);
                ApplySkinToPlayer(go, skinID);
            }
        }

        private static void ReplaceTailSprite(TailScript tail, Dictionary<string, Sprite> dict, bool isRight, int charType, float scale)
        {
            var renderer = tail.GetComponent<SpriteRenderer>();
            if (renderer == null) return;
            int id = renderer.GetInstanceID();
            if (!_originalTailSprites.ContainsKey(id) && renderer.sprite != null)
                _originalTailSprites[id] = renderer.sprite;
            Sprite reference = _originalTailSprites.TryGetValue(id, out var orig) ? orig : renderer.sprite;
            if (reference == null) return;

            var outcome = Resolve.SkinResolver.ResolveByName(reference.name, dict, charType, '\0', isRight);
            renderer.sprite = outcome.Hit ? FitToTarget(outcome.Sprite, reference, scale) : reference;
        }

        private static void ReplaceFacialExpressionSprites(FacialExpression face, Dictionary<string, Sprite> dict, bool isRight, int charType, float scale)
        {
            int id = face.GetInstanceID();
            if (!_originalFaces.TryGetValue(id, out var orig))
            {
                orig = new FacialOriginal
                {
                    DefaultHead = face.defaultHead,
                    DefaultHeadMouth = face.defaultHeadMouth,
                    DefaultHeadMouthHalf = face.defaultHeadMouthHalf,
                    EyesGone = face.eyesGone,
                    EyesGoneHealed = face.eyesGoneHealed,
                    DisfiguredHead = (Sprite[])(face.disfiguredHead?.Clone() ?? new Sprite[0]),
                    DisfiguredHeadHeal = (Sprite[])(face.disfiguredHeadHeal?.Clone() ?? new Sprite[0]),
                    Eyes = face.eyeList != null ? face.eyeList.ToArray() : new Eye[0],
                };
                _originalFaces[id] = orig;
            }

            face.defaultHead = GetReplacement(orig.DefaultHead, dict, isRight, charType, scale);
            face.defaultHeadMouth = GetReplacement(orig.DefaultHeadMouth, dict, isRight, charType, scale);
            face.defaultHeadMouthHalf = GetReplacement(orig.DefaultHeadMouthHalf, dict, isRight, charType, scale);
            face.eyesGone = GetReplacement(orig.EyesGone, dict, isRight, charType, scale);
            face.eyesGoneHealed = GetReplacement(orig.EyesGoneHealed, dict, isRight, charType, scale);

            int dhCount = Mathf.Min(face.disfiguredHead.Length, orig.DisfiguredHead.Length);
            for (int i = 0; i < dhCount; i++)
                face.disfiguredHead[i] = GetReplacement(orig.DisfiguredHead[i], dict, isRight, charType, scale);
            int dhhCount = Mathf.Min(face.disfiguredHeadHeal.Length, orig.DisfiguredHeadHeal.Length);
            for (int i = 0; i < dhhCount; i++)
                face.disfiguredHeadHeal[i] = GetReplacement(orig.DisfiguredHeadHeal[i], dict, isRight, charType, scale);
            int eyeCount = Mathf.Min(face.eyeList.Count, orig.Eyes.Length);
            for (int i = 0; i < eyeCount; i++)
            {
                var eye = orig.Eyes[i];
                eye.front = GetReplacement(eye.front, dict, isRight, charType, scale);
                eye.back = GetReplacement(eye.back, dict, isRight, charType, scale);
                face.eyeList[i] = eye;
            }
        }

        /// <param name="isWholeFace">
        /// true = 整张脸的贴图（defaultHead / 各嘴角 / 毁容头）：必须与 Head 肢体用完全相同的处理，
        /// 否则 FacialExpression.Update() 每帧覆写 sprite，会把肢体刚归一好的头又换回另一个尺寸。
        /// false = 眼睛类：眼睛内容只是画布内一小块（experimentEyeOpen 画布 26x12、内容仅 6x4），
        /// 裁白边会让眼睛跑到脸中间，所以保留整张画布，只按该贴图自身的画布尺寸归一。
        /// </param>
        private static Sprite GetReplacement(Sprite original, Dictionary<string, Sprite> dict, bool isRight, int charType, float scale)
        {
            if (original == null) return null;
            var outcome = Resolve.SkinResolver.ResolveByName(original.name, dict, charType, '\0', isRight);
            // 头、眼睛、嘴全部走同一个 FitToTarget：同一全局倍率、同一整画布基准，
            // 所以眼睛不会因为基准不同而错位。
            return outcome.Hit ? FitToTarget(outcome.Sprite, original, scale) : original;
        }

        /// <summary>
        /// 将 4 张翼按 wings.json 配置挂载到 UpTorso 子节点，下翼以上翼为父形成关节链。
        /// </summary>
        private static void EnsureWingsAttached(GameObject playerObj, Dictionary<string, Sprite> dict, WingsConfigLoader.Config cfg, int remoteCharType = 0)
        {
            if (playerObj == null) return;
            Body body = playerObj.GetComponentInChildren<Body>(true);
            if (body == null) return;

            Transform upTorsoTf = FindLimbTransform(body, "UpTorso");
            Transform parent = upTorsoTf != null ? upTorsoTf : body.transform;

            Transform existing = parent.Find("HwWings");
            if (existing != null) UnityEngine.Object.Destroy(existing.gameObject);

            Sprite wingUL = dict.TryGetValue("wingUL", out var ul) ? ul : null;
            Sprite wingUR = dict.TryGetValue("wingUR", out var ur) ? ur : null;
            Sprite wingDL = dict.TryGetValue("wingDL", out var dl) ? dl : null;
            Sprite wingDR = dict.TryGetValue("wingDR", out var dr) ? dr : null;
            if (wingUL == null && wingUR == null && wingDL == null && wingDR == null) return;

            var container = new GameObject("HwWings");
            container.transform.SetParent(parent, worldPositionStays: false);
            container.transform.localPosition = Vector3.zero;
            container.transform.localScale = Vector3.one;

            var bodyRenderer = body.GetComponentInChildren<SpriteRenderer>();
            int sortLayer = bodyRenderer != null ? bodyRenderer.sortingLayerID : 0;
            Material litMat = bodyRenderer != null ? bodyRenderer.sharedMaterial : null;

            float wingScale = ComputeWingScale(body, wingUL ?? wingUR ?? wingDL ?? wingDR, remoteCharType);

            GameObject upL = null;
            GameObject upR = null;
            float leftUpperHalfPx = (wingUL != null ? wingUL.rect.height * 0.5f : 16f) * wingScale;
            float rightUpperHalfPx = (wingUR != null ? wingUR.rect.height * 0.5f : 16f) * wingScale;
            if (wingUL != null) upL = AttachWing(container.transform, "WingUL", wingUL, cfg.WingUL, sortLayer, isLeft: true, litMat: litMat, scale: wingScale);
            if (wingUR != null) upR = AttachWing(container.transform, "WingUR", wingUR, cfg.WingUR, sortLayer, isLeft: false, litMat: litMat, scale: wingScale);
            if (wingDL != null && upL != null) AttachWing(upL.transform, "WingDL", wingDL, cfg.WingDL, sortLayer, isLeft: true, isLower: true, upperHalfPx: leftUpperHalfPx, litMat: litMat, scale: wingScale);
            else if (wingDL != null) AttachWing(container.transform, "WingDL", wingDL, cfg.WingDL, sortLayer, isLeft: true, isLower: true, upperHalfPx: leftUpperHalfPx, litMat: litMat, scale: wingScale);
            if (wingDR != null && upR != null) AttachWing(upR.transform, "WingDR", wingDR, cfg.WingDR, sortLayer, isLeft: false, isLower: true, upperHalfPx: rightUpperHalfPx, litMat: litMat, scale: wingScale);
            else if (wingDR != null) AttachWing(container.transform, "WingDR", wingDR, cfg.WingDR, sortLayer, isLeft: false, isLower: true, upperHalfPx: rightUpperHalfPx, litMat: litMat, scale: wingScale);
        }

        /// <summary>翅膀缩放：只是「缩小一点」。内容高 21px 缩到躯干高 1.5 倍（Experiment 10px → 15px）。</summary>
        /// <param name="remoteCharType">远端上报的真实角色类型，非 0 时优先于 body.charType。</param>
        private static float ComputeWingScale(Body body, Sprite wingReference, int remoteCharType = 0)
        {
            if (body == null || wingReference == null) return 1f;

            int charType = remoteCharType != 0 ? remoteCharType : body.charType;
            var profile = Character.CharacterCatalog.ForCharType(charType);
            var torso = profile != null ? profile.Find(Character.PartKeyNames.UpTorso) : null;
            if (torso == null) return 1f;

            float targetHeight = torso.PixelSize.y * 1.5f;
            float actualHeight = wingReference.textureRect.height;
            if (_contentRects.TryGetValue(wingReference.name, out Rect content) && content.height >= 1f)
                actualHeight = content.height;
            if (actualHeight <= 0.0001f) return 1f;

            float scale = targetHeight / actualHeight;
            if (scale <= 0f || float.IsNaN(scale) || float.IsInfinity(scale)) return 1f;
            return Mathf.Clamp(scale, 0.2f, 2f);
        }

        private static Transform FindLimbTransform(Body body, string limbName)
        {
            foreach (var tf in body.GetComponentsInChildren<Transform>(true))
            {
                if (tf.name == limbName) return tf;
                if (tf.name.EndsWith(limbName, System.StringComparison.OrdinalIgnoreCase)) return tf;
            }
            foreach (var sr in body.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (sr != null && sr.sprite != null && sr.sprite.name.Contains(limbName)) return sr.transform;
            }
            return null;
        }

        private static GameObject AttachWing(Transform parent, string name, Sprite sprite, WingsConfigLoader.Piece piece,
            int sortLayer, bool isLeft, bool isLower = false, float upperHalfPx = 16f, Material litMat = null,
            float scale = 1f)
        {
            // 坐标完全沿用原版：上翅 (piece.X-6, piece.Y+10) 像素，根部就落在背部；下翅挂在上翅下。
            var go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: false);
            float localPxX = isLower ? piece.X : (piece.X - UPTORSO_OFFSET_X);
            float localPxY = isLower ? piece.Y : (piece.Y - UPTORSO_OFFSET_Y);
            float lx = localPxX / PIXELS_PER_UNIT;
            float ly = -localPxY / PIXELS_PER_UNIT;
            if (isLower) ly += -upperHalfPx / PIXELS_PER_UNIT;
            go.transform.localPosition = new Vector3(lx, ly, 0f);
            go.transform.localScale = Vector3.one * scale;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingLayerID = sortLayer;
            sr.sortingOrder = piece.ZOrder;
            if (litMat != null) sr.sharedMaterial = litMat;

            var script = go.AddComponent<WingScript>();
            script.isLeft = isLeft;
            script.baseAngleDeg = -piece.Rotation;
            ShadowAttacher.Ensure(go);
            return go;
        }
    }
}
