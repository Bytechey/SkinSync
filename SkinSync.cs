using BepInEx;
using HarmonyLib;
using KrokoshaCasualtiesMP;
using LiteNetLib;
using LiteNetLib.Utils;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace SkinSyncMod
{
    [BepInPlugin("com.Bytechey.skinsync", "Skin Sync Mod", "1.0.9")]
    public class SkinSync : BaseUnityPlugin
    {
        internal static Harmony harmony;
        private List<string> availableSkins = new List<string>();
        private int currentSkinIndex = 0;

        // 缓存本地玩家对象（单人模式用）
        private GameObject localPlayerObject;
        // 缓存本地 NetBody（多人模式用）
        private NetBody localNetBody;

        internal static SkinSyncSettings Settings { get; private set; }

        private SkinSyncWindow _window;
        private InGameOverlay _overlay;

        private void Awake()
        {
            harmony = new Harmony("com.Bytechey.skinsync");
            harmony.PatchAll();
            Settings = new SkinSyncSettings(Config);
            ScanAvailableSkins();
            Logger.LogInfo($"SkinSync Mod loaded. Found {availableSkins.Count} skins.");

            gameObject.AddComponent<SkinSyncMod.Patches.SkinCacheBroadcaster>();
            gameObject.AddComponent<SkinSyncMod.Patches.UpdateChecker>();
            gameObject.AddComponent<EquipmentHider>();
            EquipmentHider.Active = Settings.HideGameWearables.Value;
            gameObject.AddComponent<AccessoryEnforcer>();
            AccessoryEnforcer.Active = Settings.RequireEquipmentForAccessories.Value;
            gameObject.AddComponent<BloodGroundRecolorer>();

            _overlay = new InGameOverlay();
            _window = new SkinSyncWindow(
                cfg: Settings,
                getSkins: () => new List<string>(availableSkins),
                getCurrentSkin: () =>
                {
                    // 优先取 Settings.CurrentSkin（持久化主源），避免与"主机下发实际应用"的值脱节；
                    // 退一步用 availableSkins[index] 做新装时的初始显示。
                    string s = Settings != null ? Settings.CurrentSkin.Value : "";
                    if (!string.IsNullOrEmpty(s)) return s;
                    return availableSkins.Count > 0 && currentSkinIndex < availableSkins.Count
                        ? availableSkins[currentSkinIndex] : "";
                },
                onApplySkin: ApplySkinByName,
                onRescan: () =>
                {
                    ScanAvailableSkins();
                    if (availableSkins.Count > 0 && !availableSkins.Contains(Settings.CurrentSkin.Value))
                    {
                        currentSkinIndex = 0;
                    }
                },
                getAccessories: GetCurrentAccessoryRows,
                onToggleAccessory: ToggleAccessory,
                onSetAccessoryTransform: SetAccessoryTransform,
                onSaveTail: SaveCurrentTailToSettings,
                onResetTail: ResetCurrentTailToDefaults,
                onOpened: () => UiBlocker.Block(Logger),
                onClosed: () => UiBlocker.Unblock(Logger));

            MenuButtonInjector.Setup(Logger, () => _window.OpenPanel());
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
            Application.quitting += OnApplicationQuitting;
        }

        /// <summary>场景切换时清掉"已应用"标记，让 ApplyPersistentSkinIfNeeded 在新场景重发一次（含多人广播）。
        /// 主菜单（PreGen）也会触发清空，但 ApplyPersistentSkinIfNeeded 自身会等到本地玩家就绪——主菜单没本地 NetBody 时不会发出。</summary>
        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            _persistentApplied = null;
            // 同时让本地玩家缓存失效，跨场景下原引用可能无效。
            localPlayerObject = null;
            localNetBody = null;
        }

        private static bool _quitting;
        private void OnApplicationQuitting()
        {
            if (_quitting) return;
            _quitting = true;
            try { MenuButtonInjector.Dispose(); } catch { }
            try { UiBlocker.Unblock(Logger); } catch { }
            try { UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded; } catch { }
            try { Application.quitting -= OnApplicationQuitting; } catch { }
        }

        private void ScanAvailableSkins()
        {
            availableSkins.Clear();
            string customSpritesPath = Path.Combine(Paths.PluginPath, "CustomSprites");
            if (!Directory.Exists(customSpritesPath))
            {
                Logger.LogWarning($"CustomSprites folder not found at {customSpritesPath}");
                return;
            }

            // 任意目录名都算有效皮肤目录，仅排除以 _ 开头的辅助目录（如 _Accessories）。
            var dirs = Directory.GetDirectories(customSpritesPath)
                                .Select(Path.GetFileName)
                                .Where(name => !string.IsNullOrEmpty(name) && name[0] != '_' && name[0] != '.')
                                .ToList();
            // 自然排序：把字符串里的数字段当整数比，让 st1 / st2 / st10 / st11 按数值顺序排列。
            dirs.Sort(NaturalCompare);
            availableSkins.AddRange(dirs);

            if (availableSkins.Count == 0)
                Logger.LogWarning("No skin folders found in CustomSprites.");
            else
                Logger.LogInfo($"Available skins: {string.Join(", ", availableSkins)}");
        }

        /// <summary>把字符串里的数字段当整数比，剩下按字母（忽略大小写）比；让 st1/st2/st10 按数值顺序。</summary>
        private static int NaturalCompare(string a, string b)
        {
            int ai = 0, bi = 0;
            while (ai < a.Length && bi < b.Length)
            {
                if (char.IsDigit(a[ai]) && char.IsDigit(b[bi]))
                {
                    int aend = ai; while (aend < a.Length && char.IsDigit(a[aend])) aend++;
                    int bend = bi; while (bend < b.Length && char.IsDigit(b[bend])) bend++;
                    if (long.TryParse(a.Substring(ai, aend - ai), out long an)
                        && long.TryParse(b.Substring(bi, bend - bi), out long bn))
                    {
                        if (an != bn) return an < bn ? -1 : 1;
                    }
                    ai = aend; bi = bend;
                }
                else
                {
                    char ca = char.ToLowerInvariant(a[ai]);
                    char cb = char.ToLowerInvariant(b[bi]);
                    if (ca != cb) return ca < cb ? -1 : 1;
                    ai++; bi++;
                }
            }
            return (a.Length - ai) - (b.Length - bi);
        }

        private void Update()
        {
            if (_quitting) return;
            UiBlocker.EnforceBlocked(Logger);
            if (availableSkins.Count == 0) return;

            // 尝试获取本地玩家（自动识别模式）
            if (!TryGetLocalPlayer())
                return;

            // 首次拿到本地玩家时，应用 ConfigFile 里记忆的皮肤
            ApplyPersistentSkinIfNeeded();

            if (SkinSyncSettings.TriggeredThisFrame(Settings.NextSkinHotkey))
            {
                currentSkinIndex = (currentSkinIndex + 1) % availableSkins.Count;
                SwitchToSkin(availableSkins[currentSkinIndex]);
            }
            else if (SkinSyncSettings.TriggeredThisFrame(Settings.PrevSkinHotkey))
            {
                currentSkinIndex = (currentSkinIndex - 1 + availableSkins.Count) % availableSkins.Count;
                SwitchToSkin(availableSkins[currentSkinIndex]);
            }
            else if (SkinSyncSettings.TriggeredThisFrame(Settings.RescanSkinsHotkey))
            {
                ScanAvailableSkins();
                Logger.LogInfo("Rescanned skins.");
            }
            if (SkinSyncSettings.TriggeredThisFrame(Settings.TogglePanelHotkey))
            {
                if (_window.Open) _window.ClosePanel();
                else _window.OpenPanel();
            }
            // ESC 关闭主面板（PlayerCameraHandleInputGuard 已 Prefix 吞游戏 ESC，必须在这里独立处理）
            if (_window.Open && Input.GetKeyDown(KeyCode.Escape))
            {
                _window.ClosePanel();
            }
        }

        private void OnGUI()
        {
            if (_quitting) return;
            _overlay.Draw(() => _window.OpenPanel());
            _window.Draw();
        }

        private void OnDisable()
        {
            try { UiBlocker.Unblock(Logger); } catch { }
        }

        private string _persistentApplied;
        private void ApplyPersistentSkinIfNeeded()
        {
            string cached = Settings.CurrentSkin.Value;
            if (string.IsNullOrEmpty(cached)) return;
            if (_persistentApplied == cached) return;
            if (!availableSkins.Contains(cached)) return;
            int idx = availableSkins.IndexOf(cached);
            currentSkinIndex = idx;
            _persistentApplied = cached;
            SwitchToSkin(cached);
        }

        /// <summary>面板调用：按名字切皮肤（与 Next/Prev 按键走同一路径）。</summary>
        internal void ApplySkinByName(string skinId)
        {
            if (string.IsNullOrEmpty(skinId)) return;
            int idx = availableSkins.IndexOf(skinId);
            if (idx < 0) return;
            currentSkinIndex = idx;
            SwitchToSkin(skinId);
        }

        /// <summary>面板用的"当前皮肤"统一来源——优先 Settings.CurrentSkin，否则 availableSkins[index]。</summary>
        private string ResolveCurrentSkin()
        {
            string s = Settings != null ? Settings.CurrentSkin.Value : null;
            if (!string.IsNullOrEmpty(s) && availableSkins.Contains(s)) return s;
            if (availableSkins.Count > 0 && currentSkinIndex < availableSkins.Count)
                return availableSkins[currentSkinIndex];
            return "";
        }

        /// <summary>面板"当前角色设置"tab 用：构造当前皮肤的配件视图。</summary>
        internal List<SkinSyncWindow.AccessoryRow> GetCurrentAccessoryRows()
        {
            var rows = new List<SkinSyncWindow.AccessoryRow>();
            string skin = ResolveCurrentSkin();
            if (string.IsNullOrEmpty(skin)) return rows;
            string path = System.IO.Path.Combine(Paths.PluginPath, "CustomSprites", skin, "accessories.json");
            var entries = AccessoryConfigLoader.Load(path);
            foreach (var e in entries)
            {
                var ov = Settings.GetAccessoryOverride(skin, e.Id);
                rows.Add(new SkinSyncWindow.AccessoryRow
                {
                    Id = e.Id,
                    Enabled = ov?.Enabled ?? e.Enabled,
                    OffX = ov?.OffX ?? e.OffX,
                    OffY = ov?.OffY ?? e.OffY,
                    Rotation = ov?.Rotation ?? e.Rotation,
                    ZOrder = ov?.ZOrder ?? e.ZOrder,
                    RequireSlot = e.RequireWornSlot,
                });
            }
            return rows;
        }

        /// <summary>面板配件 toggle 回调：写覆盖 + 实时同步运行时挂载状态。</summary>
        internal void ToggleAccessory(string accId, bool enabled)
        {
            string skin = ResolveCurrentSkin();
            if (string.IsNullOrEmpty(skin)) return;
            Settings.SetAccessoryEnabled(skin, accId, enabled);
            AccessoryAttacher.SetEnabled(accId, enabled);
            SkinPreviewRenderer.Invalidate(skin);
            BroadcastAccessoryDelta(skin, accId);
        }

        /// <summary>面板配件 transform 回调：写覆盖 + 实时调挂载 transform。</summary>
        internal void SetAccessoryTransform(string accId, int offX, int offY, float rot, int z)
        {
            string skin = ResolveCurrentSkin();
            if (string.IsNullOrEmpty(skin)) return;
            Settings.SetAccessoryTransform(skin, accId, offX, offY, rot, z);
            AccessoryAttacher.SetTransform(accId, offX, offY, rot, z);
            SkinPreviewRenderer.Invalidate(skin);
            BroadcastAccessoryDelta(skin, accId);
        }

        /// <summary>把当前 acc 的 5 字段（含覆盖合并值）打包发给服务端转发，仅在多人 + 非 PreGen + 同步开关启用时执行。</summary>
        private void BroadcastAccessoryDelta(string skin, string accId)
        {
            if (Settings == null || !Settings.SyncAccessories.Value) return;
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "PreGen") return;
            if (localNetBody == null) return;
            // 取合并后值（用户只改一个字段时其他字段也要发完整快照）。
            string accPath = System.IO.Path.Combine(Paths.PluginPath, "CustomSprites", skin, "accessories.json");
            var entries = AccessoryConfigLoader.Load(accPath);
            AccessoryConfigLoader.Entry baseEntry = null;
            foreach (var e in entries) { if (e.Id == accId) { baseEntry = e; break; } }
            if (baseEntry == null) return;
            var ov = Settings.GetAccessoryOverride(skin, accId);
            var msg = new AccessorySyncMessage
            {
                netId = localNetBody.netId,
                skinID = skin,
                accId = accId,
                enabled = ov?.Enabled ?? baseEntry.Enabled,
                offX = ov?.OffX ?? baseEntry.OffX,
                offY = ov?.OffY ?? baseEntry.OffY,
                rotation = ov?.Rotation ?? baseEntry.Rotation,
                zOrder = ov?.ZOrder ?? baseEntry.ZOrder,
            };
            var writer = new LiteNetLib.Utils.NetDataWriter();
            msg.WriteTo(writer);
            KrokoshaCasualtiesMP.Net.Client_Send(LiteNetLib.DeliveryMethod.ReliableOrdered, writer);
        }

        /// <summary>把 settings 里 skin 的尾巴覆盖应用到全局 TailDeformConfig static 字段。null 时清空覆盖回默认。</summary>
        internal static void ApplyTailOverrideToConfig(SkinSyncSettings.TailDeformOverride ov)
        {
            // 默认值（与 TailDeformConfig 出厂相同）
            TailDeformConfig.Enabled = ov?.Enabled ?? true;
            TailDeformConfig.FrontGuard = ov?.FrontGuard ?? true;
            TailDeformConfig.Segments = ov?.Segments ?? 10;
            TailDeformConfig.ConstraintIters = ov?.ConstraintIters ?? 6;
            TailDeformConfig.Damping = ov?.Damping ?? 0.45f;
            TailDeformConfig.Stiffness = ov?.Stiffness ?? 1.0f;
            TailDeformConfig.MaxBendDeg = ov?.MaxBendDeg ?? 18f;
            TailDeformConfig.AnchorFollow = ov?.AnchorFollow ?? 1.0f;
            TailDeformConfig.Smoothness = ov?.Smoothness ?? 0.55f;
            TailDeformConfig.MaxStep = ov?.MaxStep ?? 0.15f;
            TailDeformConfig.MaxFixedDt = ov?.MaxFixedDt ?? 0.02f;
            TailDeformConfig.FrontGuardMargin = ov?.FrontGuardMargin ?? 0.05f;
            TailDeformConfig.GravityX = ov?.GravityX ?? 0f;
            TailDeformConfig.GravityY = ov?.GravityY ?? -1.2f;
            TailDeformConfig.WindFreq = ov?.WindFreq ?? 1.2f;
            TailDeformConfig.WindAmp = ov?.WindAmp ?? 0.012f;
            TailDeformConfig.SpeedDisturb = ov?.SpeedDisturb ?? 0.005f;
        }

        /// <summary>面板尾巴回调：从当前 TailDeformConfig 读出快照，写回 settings 并落盘。</summary>
        internal void SaveCurrentTailToSettings()
        {
            string skin = ResolveCurrentSkin();
            if (string.IsNullOrEmpty(skin)) return;
            var ov = new SkinSyncSettings.TailDeformOverride
            {
                Enabled = TailDeformConfig.Enabled,
                FrontGuard = TailDeformConfig.FrontGuard,
                Segments = TailDeformConfig.Segments,
                ConstraintIters = TailDeformConfig.ConstraintIters,
                Damping = TailDeformConfig.Damping,
                Stiffness = TailDeformConfig.Stiffness,
                MaxBendDeg = TailDeformConfig.MaxBendDeg,
                AnchorFollow = TailDeformConfig.AnchorFollow,
                Smoothness = TailDeformConfig.Smoothness,
                MaxStep = TailDeformConfig.MaxStep,
                MaxFixedDt = TailDeformConfig.MaxFixedDt,
                FrontGuardMargin = TailDeformConfig.FrontGuardMargin,
                GravityX = TailDeformConfig.GravityX,
                GravityY = TailDeformConfig.GravityY,
                WindFreq = TailDeformConfig.WindFreq,
                WindAmp = TailDeformConfig.WindAmp,
                SpeedDisturb = TailDeformConfig.SpeedDisturb,
            };
            Settings.SetTailOverride(skin, ov);
            BroadcastTailDelta(skin);
        }

        /// <summary>面板"重置默认"回调：删 settings 该 skin 的覆盖记录 + 把 TailDeformConfig 恢复为出厂默认。</summary>
        internal void ResetCurrentTailToDefaults()
        {
            string skin = ResolveCurrentSkin();
            if (string.IsNullOrEmpty(skin)) return;
            Settings.SetTailOverride(skin, null);
            ApplyTailOverrideToConfig(null);
            BroadcastTailDelta(skin);
        }

        /// <summary>把当前 TailDeformConfig 全字段打包发给服务端转发，仅在多人 + 非 PreGen + 同步开关启用时执行。</summary>
        private void BroadcastTailDelta(string skin)
        {
            if (Settings == null || !Settings.SyncTailDeform.Value) return;
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "PreGen") return;
            if (localNetBody == null) return;
            var msg = new TailSyncMessage
            {
                netId = localNetBody.netId,
                skinID = skin,
                enabled = TailDeformConfig.Enabled,
                frontGuard = TailDeformConfig.FrontGuard,
                segments = TailDeformConfig.Segments,
                constraintIters = TailDeformConfig.ConstraintIters,
                damping = TailDeformConfig.Damping,
                stiffness = TailDeformConfig.Stiffness,
                maxBendDeg = TailDeformConfig.MaxBendDeg,
                anchorFollow = TailDeformConfig.AnchorFollow,
                smoothness = TailDeformConfig.Smoothness,
                maxStep = TailDeformConfig.MaxStep,
                maxFixedDt = TailDeformConfig.MaxFixedDt,
                frontGuardMargin = TailDeformConfig.FrontGuardMargin,
                gravityX = TailDeformConfig.GravityX,
                gravityY = TailDeformConfig.GravityY,
                windFreq = TailDeformConfig.WindFreq,
                windAmp = TailDeformConfig.WindAmp,
                speedDisturb = TailDeformConfig.SpeedDisturb,
            };
            var writer = new LiteNetLib.Utils.NetDataWriter();
            msg.WriteTo(writer);
            KrokoshaCasualtiesMP.Net.Client_Send(LiteNetLib.DeliveryMethod.ReliableOrdered, writer);
        }

        private bool TryGetLocalPlayer()
        {
            // 优先尝试多人模式：获取本地 NetBody
            if (localNetBody == null && NetBody.all_instances != null)
            {
                foreach (var nb in NetBody.all_instances)
                {
                    if (nb.is_local)
                    {
                        localNetBody = nb;
                        Logger.LogInfo($"Multiplayer mode: found local NetBody (netId {localNetBody.netId})");
                        break;
                    }
                }
                if (localNetBody == null && NetPlayer.LOCAL_PLAYER?.playerbody != null)
                {
                    localNetBody = NetPlayer.LOCAL_PLAYER.playerbody;
                    Logger.LogInfo("Multiplayer mode: found local NetBody via NetPlayer.LOCAL_PLAYER");
                }
            }

            // 如果成功获取到 NetBody，说明是多人模式，直接返回 true
            if (localNetBody != null)
                return true;

            // 否则尝试单人模式：查找本地玩家 GameObject
            if (localPlayerObject == null)
            {
                // 通过 Body 组件查找
                Body playerBody = FindObjectOfType<Body>();
                if (playerBody != null)
                {
                    localPlayerObject = playerBody.transform.parent?.gameObject ?? playerBody.gameObject;
                    Logger.LogInfo($"Singleplayer mode: found local player object via Body: {localPlayerObject.name}");
                }
                else
                {
                    // 通过标签查找
                    localPlayerObject = GameObject.FindGameObjectWithTag("Player");
                    if (localPlayerObject != null)
                        Logger.LogInfo($"Singleplayer mode: found local player object via Tag: {localPlayerObject.name}");
                }
            }

            return localPlayerObject != null;
        }

        /// <summary>OnEnter 模式一键同步：把当前皮肤的所有配件覆盖 + 尾巴覆盖广播一次。</summary>
        private void BroadcastAllForSkin(string skinID)
        {
            if (Settings == null || localNetBody == null) return;
            // 配件：扫 accessories.json 全部条目，对每个条目按合并值发一份。
            if (Settings.SyncAccessories.Value)
            {
                string accPath = System.IO.Path.Combine(Paths.PluginPath, "CustomSprites", skinID, "accessories.json");
                var entries = AccessoryConfigLoader.Load(accPath);
                foreach (var e in entries)
                {
                    if (e == null || string.IsNullOrEmpty(e.Id)) continue;
                    var ov = Settings.GetAccessoryOverride(skinID, e.Id);
                    var msg = new AccessorySyncMessage
                    {
                        netId = localNetBody.netId,
                        skinID = skinID,
                        accId = e.Id,
                        enabled = ov?.Enabled ?? e.Enabled,
                        offX = ov?.OffX ?? e.OffX,
                        offY = ov?.OffY ?? e.OffY,
                        rotation = ov?.Rotation ?? e.Rotation,
                        zOrder = ov?.ZOrder ?? e.ZOrder,
                    };
                    var writer = new LiteNetLib.Utils.NetDataWriter();
                    msg.WriteTo(writer);
                    KrokoshaCasualtiesMP.Net.Client_Send(LiteNetLib.DeliveryMethod.ReliableOrdered, writer);
                }
            }
            // 尾巴：当前 TailDeformConfig 的全字段。
            if (Settings.SyncTailDeform.Value)
            {
                BroadcastTailDelta(skinID);
            }
            Logger.LogInfo($"OnEnter sync: pushed accessory + tail snapshot for {skinID}");
        }

        private void SwitchToSkin(string skinID)
        {
            // 记忆当前皮肤——下次进游戏直接套上
            if (Settings != null)
            {
                Settings.CurrentSkin.Value = skinID;
                ApplyTailOverrideToConfig(Settings.GetTailOverride(skinID));
            }

            // 同步写入按 SteamID 的缓存（跨主机 / 重连恢复用）。
            try
            {
                ulong steamId = NetPlayer.LOCAL_PLAYER != null ? NetPlayer.LOCAL_PLAYER.steam_id : 0UL;
                if (steamId != 0UL) SkinCacheStore.Set(steamId, skinID);
            }
            catch (System.Exception ex)
            {
                Logger.LogWarning("[SkinSync] write SkinCacheStore failed: " + ex.Message);
            }

            // 预览缓存失效——切皮肤后再展开角色 tab 立即看新缩略图。
            SkinPreviewRenderer.Invalidate(skinID);

            // 多人模式逻辑——但主菜单（PreGen）场景不发广播
            if (localNetBody != null)
            {
                // 本地应用皮肤
                SkinApplier.ApplySkinToPlayer(localNetBody.chara, skinID);
                if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "PreGen")
                {
                    // 发送网络同步消息
                    SkinChangeMessage msg = new SkinChangeMessage { skinID = skinID, netId = localNetBody.netId };
                    NetDataWriter writer = new NetDataWriter();
                    msg.WriteTo(writer);
                    Net.Client_Send(DeliveryMethod.ReliableOrdered, writer);
                    Logger.LogInfo($"Multiplayer: switched to {skinID} and sent network message.");
                    // OnEnter 模式：连同当前皮肤的全部配件覆盖 + 尾巴覆盖一并广播。
                    if (Settings != null && Settings.SyncMode.Value == "OnEnter")
                    {
                        BroadcastAllForSkin(skinID);
                    }
                }
                else
                {
                    Logger.LogInfo($"Main menu: applied {skinID} locally without broadcasting.");
                }
            }
            // 单人模式逻辑
            else if (localPlayerObject != null)
            {
                SkinApplier.ApplySkinToPlayer(localPlayerObject, skinID);
                Logger.LogInfo($"Singleplayer: switched to {skinID} (local only).");
            }
            else
            {
                Logger.LogWarning($"Cannot switch to {skinID}: no valid player object.");
            }
        }
    }
}