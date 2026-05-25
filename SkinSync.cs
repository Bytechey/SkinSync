using BepInEx;
using HarmonyLib;
using KrokoshaCasualtiesMP;
using LiteNetLib;
using LiteNetLib.Utils;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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

        private void Awake()
        {
            harmony = new Harmony("com.Bytechey.skinsync");
            harmony.PatchAll();
            ScanAvailableSkins();
            Logger.LogInfo($"SkinSync Mod loaded. Found {availableSkins.Count} skins.");

            // 尾巴形变调试面板（按 F9 切显隐）。卸载方式：删除下面这一行 + 删除 TailDebugPanel.cs 重 build。
            // gameObject.AddComponent<TailDebugPanel>();
            gameObject.AddComponent<SkinSyncMod.Patches.SkinCacheBroadcaster>();
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

            Regex skinPattern = new Regex(@"^st\d+$", RegexOptions.IgnoreCase);
            var dirs = Directory.GetDirectories(customSpritesPath)
                                .Select(Path.GetFileName)
                                .Where(name => skinPattern.IsMatch(name))
                                .OrderBy(name => int.Parse(name.Substring(2)))
                                .ToList();
            availableSkins.AddRange(dirs);

            if (availableSkins.Count == 0)
                Logger.LogWarning("No stX skin folders found in CustomSprites.");
            else
                Logger.LogInfo($"Available skins: {string.Join(", ", availableSkins)}");
        }

        private void Update()
        {
            if (availableSkins.Count == 0) return;

            // 尝试获取本地玩家（自动识别模式）
            if (!TryGetLocalPlayer())
                return;

            // 按键切换
            if (Input.GetKeyDown(KeyCode.F6))
            {
                currentSkinIndex = (currentSkinIndex + 1) % availableSkins.Count;
                SwitchToSkin(availableSkins[currentSkinIndex]);
            }
            else if (Input.GetKeyDown(KeyCode.F7))
            {
                currentSkinIndex = (currentSkinIndex - 1 + availableSkins.Count) % availableSkins.Count;
                SwitchToSkin(availableSkins[currentSkinIndex]);
            }
            else if (Input.GetKeyDown(KeyCode.F8))
            {
                ScanAvailableSkins();
                if (availableSkins.Count > 0)
                {
                    currentSkinIndex = 0;
                    SwitchToSkin(availableSkins[0]);
                }
                Logger.LogInfo("Rescanned skins.");
            }
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

        private void SwitchToSkin(string skinID)
        {
            // 多人模式逻辑
            if (localNetBody != null)
            {
                // 本地应用皮肤
                SkinApplier.ApplySkinToPlayer(localNetBody.chara, skinID);
                // 发送网络同步消息
                SkinChangeMessage msg = new SkinChangeMessage { skinID = skinID, netId = localNetBody.netId };
                NetDataWriter writer = new NetDataWriter();
                msg.WriteTo(writer);
                Net.Client_Send(DeliveryMethod.ReliableOrdered, writer);
                Logger.LogInfo($"Multiplayer: switched to {skinID} and sent network message.");
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