using BepInEx;
using HarmonyLib;
using SkinSyncMod.Network;
using SkinSyncMod.Patches;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace SkinSyncMod
{
    [BepInPlugin("com.Bytechey.skinsync", "Skin Sync Mod", "1.0.10")]
    [BepInDependency("KrokoshaCasualtiesMP", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(SaveManagerGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public class SkinSync : BaseUnityPlugin
    {
        private const string SaveManagerGuid = "com.casualtiesUnknown.saveManager";

        internal static Harmony harmony;

        private List<string> availableSkins = new List<string>();
        private int currentSkinIndex = 0;

        private GameObject localPlayerObject;
        private object localNetBodyBox;
        private uint localNetId;
        private GameObject localChara;

        internal static SkinSyncSettings Settings { get; private set; }
        internal static bool IsMultiplayerSession => KrokoshaBridge.IsAvailable;

        /// <summary>从 BepInPlugin attribute 动态读取的 mod 版本号；读取失败返回空串。</summary>
        internal static string Version
        {
            get
            {
                if (_version != null) return _version;
                var attr = (BepInPlugin)System.Attribute.GetCustomAttribute(typeof(SkinSync), typeof(BepInPlugin));
                _version = attr != null ? attr.Version.ToString() : "";
                return _version;
            }
        }
        private static string _version;

        private static SkinSync _instance;

        private SkinSyncWindow _window;
        private InGameOverlay _overlay;

        private void Awake()
        {
            _instance = this;
            ModLog.Init(Logger);
            gameObject.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
            harmony = new Harmony("com.Bytechey.skinsync");
            try { if (System.IO.Directory.Exists(SkinPathResolver.SyncCacheRoot)) System.IO.Directory.Delete(SkinPathResolver.SyncCacheRoot, true); }
            catch (System.Exception ex) { ModLog.Warning("clear SyncCacheRoot failed: " + ex.Message); }
            KrokoshaBridge.Init();
            RegisterPatches();
            Settings = new SkinSyncSettings(Config);
            ModLog.ShowInConsole = Settings.ShowLogInConsole.Value;
            Settings.ShowLogInConsole.SettingChanged += (_, __) => ModLog.ShowInConsole = Settings.ShowLogInConsole.Value;
            SkinSyncMod.Patches.UpdateChecker.Enabled = Settings.AcceptUpdateNotice.Value;
            Settings.AcceptUpdateNotice.SettingChanged += (_, __) => SkinSyncMod.Patches.UpdateChecker.Enabled = Settings.AcceptUpdateNotice.Value;
            ScanAvailableSkins();
            ModLog.Info($"SkinSync Mod loaded. Found {availableSkins.Count} skins. Krokosha={(KrokoshaBridge.IsAvailable ? "yes" : "no")}.");
            if (KrokoshaBridge.IsAvailable)
            {
                gameObject.AddComponent<SkinSyncMod.Patches.SkinCacheBroadcaster>();
                gameObject.AddComponent<SkinSyncMod.Patches.PendingSkinApplier>();
            }
            gameObject.AddComponent<SkinSyncMod.Patches.UpdateChecker>();
            gameObject.AddComponent<EquipmentHider>();
            EquipmentHider.Active = Settings.HideGameWearables.Value;
            gameObject.AddComponent<AccessoryEnforcer>();
            AccessoryEnforcer.Active = Settings.RequireEquipmentForAccessories.Value;
            BloodRenderConfig.Enabled = Settings.RenderCustomBlood.Value;
            Settings.RenderCustomBlood.SettingChanged += (_, __) => BloodRenderConfig.Enabled = Settings.RenderCustomBlood.Value;
            TailDeformConfig.Enabled = Settings.TailDeformEnabled.Value;
            Settings.TailDeformEnabled.SettingChanged += (_, __) => TailDeformConfig.Enabled = Settings.TailDeformEnabled.Value;

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
                onOpened: () => UiBlocker.Block(),
                onClosed: () => UiBlocker.Unblock());

            bool saveManagerInstalled = BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(SaveManagerGuid);
            if (!saveManagerInstalled)
            {
                MenuButtonInjector.Setup(() => _window.OpenPanel());
            }
            else
            {
                Patches.SaveManagerTabBridge.Register(_window);
            }

            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
            Application.quitting += OnApplicationQuitting;
        }

        private void RegisterPatches()
        {
            harmony.CreateClassProcessor(typeof(SkinSyncAdaptiveButtonOverlayActiveGuard)).Patch();
            harmony.CreateClassProcessor(typeof(SkinSyncAdaptiveButtonClickedGuard)).Patch();
            harmony.CreateClassProcessor(typeof(SkinSyncPlayerCameraHandleInputGuard)).Patch();
            harmony.CreateClassProcessor(typeof(SkinSyncPreRunScriptStartPatch)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.BleedSpawnTranspiler)).Patch();
            harmony.CreateClassProcessor(typeof(Patches.LimbAwakeBleedColorPatch)).Patch();

            if (KrokoshaBridge.IsAvailable)
            {
                if (KrokoshaBridge.InvokeServerMessageMethod != null)
                    harmony.CreateClassProcessor(typeof(ServerMessageInterceptor)).Patch();
                if (KrokoshaBridge.InvokeClientMessageMethod != null)
                    harmony.CreateClassProcessor(typeof(ClientMessageInterceptor)).Patch();
            }
        }

        /// <summary>场景切换时清掉"已应用"标记，让 ApplyPersistentSkinIfNeeded 在新场景重发一次（含多人广播）。
        /// 主菜单（PreGen）也会触发清空，但 ApplyPersistentSkinIfNeeded 自身会等到本地玩家就绪——主菜单没本地 NetBody 时不会发出。</summary>
        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            _persistentApplied = null;
            _localSkinReportedNetId = 0;
            localPlayerObject = null;
            localNetBodyBox = null;
            localNetId = 0;
            localChara = null;
        }

        private static bool _quitting;
        private void OnApplicationQuitting()
        {
            if (_quitting) return;
            _quitting = true;
            try { MenuButtonInjector.Dispose(); } catch { }
            try { UiBlocker.Unblock(); } catch { }
            try { UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded; } catch { }
            try { Application.quitting -= OnApplicationQuitting; } catch { }
        }

        private void ScanAvailableSkins()
        {
            availableSkins.Clear();
            SkinPathResolver.Invalidate();
            string customSpritesPath = Path.Combine(Paths.PluginPath, "CustomSprites");
            if (!Directory.Exists(customSpritesPath))
            {
                ModLog.Warning($"CustomSprites folder not found at {customSpritesPath}");
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
                ModLog.Warning("No skin folders found in CustomSprites.");
            else
                ModLog.Info($"Available skins: {string.Join(", ", availableSkins)}");
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
            UiBlocker.EnforceBlocked();

            if (SkinSyncSettings.TriggeredThisFrame(Settings.TogglePanelHotkey))
            {
                if (_window.Open) _window.ClosePanel();
                else _window.OpenPanel();
            }
            if (_window.Open && Input.GetKeyDown(KeyCode.Escape))
            {
                _window.ClosePanel();
            }

            if (availableSkins.Count == 0) return;

            ApplyPersistentSkinIfNeeded();

            if (!TryGetLocalPlayer())
                return;

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
                ModLog.Info("Rescanned skins.");
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
            try { UiBlocker.Unblock(); } catch { }
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
            _switchAppliedToScene = false;
            SwitchToSkin(cached);
            if (localNetBodyBox != null || localPlayerObject != null || _switchAppliedToScene)
            {
                _persistentApplied = cached;
            }
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
            string path = System.IO.Path.Combine(SkinPathResolver.GetSkinDir(skin), "accessories.json");
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
            if (localNetBodyBox == null) return;
            // 取合并后值（用户只改一个字段时其他字段也要发完整快照）。
            string accPath = System.IO.Path.Combine(SkinPathResolver.GetSkinDir(skin), "accessories.json");
            var entries = AccessoryConfigLoader.Load(accPath);
            AccessoryConfigLoader.Entry baseEntry = null;
            foreach (var e in entries) { if (e.Id == accId) { baseEntry = e; break; } }
            if (baseEntry == null) return;
            var ov = Settings.GetAccessoryOverride(skin, accId);
            MultiplayerSender.SendAccessory(
                netId: localNetId,
                skinID: skin,
                accId: accId,
                enabled: ov?.Enabled ?? baseEntry.Enabled,
                offX: ov?.OffX ?? baseEntry.OffX,
                offY: ov?.OffY ?? baseEntry.OffY,
                rotation: ov?.Rotation ?? baseEntry.Rotation,
                zOrder: ov?.ZOrder ?? baseEntry.ZOrder);
        }

        /// <summary>把 settings 里 skin 的尾巴覆盖应用到全局 TailDeformConfig static 字段。null 时清空覆盖回默认。</summary>
        internal static void ApplyTailOverrideToConfig(SkinSyncSettings.TailDeformOverride ov)
        {
            // 默认值（与 TailDeformConfig 出厂相同）
            TailDeformConfig.FrontGuard = ov?.FrontGuard ?? true;
            TailDeformConfig.Segments = ov?.Segments ?? 10;
            TailDeformConfig.ConstraintIters = ov?.ConstraintIters ?? 6;
            TailDeformConfig.Damping = ov?.Damping ?? 0.45f;
            TailDeformConfig.SpeedDamping = ov?.SpeedDamping ?? 0.05f;
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
                FrontGuard = TailDeformConfig.FrontGuard,
                Segments = TailDeformConfig.Segments,
                ConstraintIters = TailDeformConfig.ConstraintIters,
                Damping = TailDeformConfig.Damping,
                SpeedDamping = TailDeformConfig.SpeedDamping,
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
            if (localNetBodyBox == null) return;
            MultiplayerSender.SendTailFromConfig(localNetId, skin);
        }

        private bool TryGetLocalPlayer()
        {
            if (localNetBodyBox == null && KrokoshaBridge.IsAvailable)
            {
                if (KrokoshaBridge.TryGetLocalNetBody(out object box, out uint nid, out GameObject chara))
                {
                    localNetBodyBox = box;
                    localNetId = nid;
                    localChara = chara;
                    ModLog.Info($"多人模式：找到本地 NetBody (netId {localNetId})");
                    ReportLocalSkinIfNeeded();
                }
            }

            if (localNetBodyBox != null)
                return true;

            if (localPlayerObject == null)
            {
                Body playerBody = FindObjectOfType<Body>();
                if (playerBody != null)
                {
                    localPlayerObject = playerBody.transform.parent?.gameObject ?? playerBody.gameObject;
                    ModLog.Info($"单机模式：通过 Body 找到本地玩家：{localPlayerObject.name}");
                }
                else
                {
                    localPlayerObject = GameObject.FindGameObjectWithTag("Player");
                    if (localPlayerObject != null)
                        ModLog.Info($"单机模式：通过 Tag 找到本地玩家：{localPlayerObject.name}");
                }
            }

            return localPlayerObject != null;
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
                ulong steamId = KrokoshaBridge.TryGetLocalSteamId();
                if (steamId != 0UL) SkinCacheStore.Set(steamId, skinID);
            }
            catch (System.Exception ex)
            {
                ModLog.Warning("write SkinCacheStore failed: " + ex.Message);
            }

            // 预览缓存失效——切皮肤后再展开角色 tab 立即看新缩略图。
            SkinPreviewRenderer.Invalidate(skinID);

            // 多人模式逻辑——但主菜单（PreGen）场景不发广播
            if (localNetBodyBox != null)
            {
                // 本地应用皮肤
                SkinApplier.ApplySkinToPlayer(localChara, skinID);
                if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "PreGen")
                {
                    MultiplayerSender.SendSkinChange(localNetId, skinID);
                    _localSkinReportedNetId = localNetId;
                    ModLog.Info($"角色加载模式：多人 — 切到 {skinID} 并已广播");
                }
                else
                {
                    ModLog.Info($"角色加载模式：多人主菜单 — 仅本地应用 {skinID}，不广播");
                }
            }
            else if (localPlayerObject != null)
            {
                SkinApplier.ApplySkinToPlayer(localPlayerObject, skinID);
                ModLog.Info($"角色加载模式：单机 — 切到 {skinID}（基于本地玩家）");
            }
            else
            {
                int n = SkinApplier.ApplyToScene(skinID);
                if (n > 0)
                {
                    ModLog.Info($"角色加载模式：单机兜底 — 切到 {skinID}（场景扫到 {n} 个 Body）");
                    _switchAppliedToScene = true;
                }
                else
                {
                    _switchAppliedToScene = false;
                }
            }
        }

        private bool _switchAppliedToScene;
        private uint _localSkinReportedNetId;

        /// <summary>子端进入游戏拿到本地 NetBody 后，把当前皮肤上报一次给服务端建表；只对相同 netId 报一次。</summary>
        private void ReportLocalSkinIfNeeded()
        {
            if (localNetBodyBox == null) return;
            if (_localSkinReportedNetId == localNetId) return;
            string cached = Settings != null ? Settings.CurrentSkin.Value : null;
            if (string.IsNullOrEmpty(cached)) return;
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "PreGen") return;
            MultiplayerSender.SendSkinChange(localNetId, cached);
            _localSkinReportedNetId = localNetId;
            ModLog.Info($"多人模式：本地皮肤已上报 (netId {localNetId} → {cached})");
        }
    }
}
