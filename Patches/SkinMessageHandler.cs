using System.Collections.Generic;
using SkinSyncMod.Interop;
using SkinSyncMod.Interop.Consumers;
using SkinSyncMod.Network;
using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>处理客户端与服务端收到的皮肤切换、配件覆盖、尾巴参数与皮肤包网络消息。</summary>
    internal static class SkinMessageHandlers
    {
        private static readonly HashSet<string> _broadcastedPackIds = new HashSet<string>();
        private static readonly Dictionary<uint, string> _remoteSkinByNetId = new Dictionary<uint, string>();

        internal static void OnSceneChanged()
        {
            _broadcastedPackIds.Clear();
            _remoteSkinByNetId.Clear();
            SkinPackChunkAssembler.Clear();
        }

        internal static bool TryGetRecordedRemoteSkin(uint netId, out string skinId)
        {
            skinId = null;
            if (netId == 0) return false;
            if (_remoteSkinByNetId.TryGetValue(netId, out skinId) && !string.IsNullOrEmpty(skinId)) return true;
            return Patches.SkinCacheBroadcaster.TryGetRecordedSkin(netId, out skinId);
        }

        internal static void DropRemoteNetId(uint netId)
        {
            if (netId == 0) return;
            _remoteSkinByNetId.Remove(netId);
            AccessoryAttacher.DropPendingOverrides(netId);
            TailDeformConfig.DropPendingOverrides(netId);
        }

        private static void RecordRemoteSkin(uint netId, string skinId)
        {
            if (netId == 0 || string.IsNullOrEmpty(skinId)) return;
            if (_remoteSkinByNetId.TryGetValue(netId, out string prev) && prev != skinId)
            {
                AccessoryAttacher.DropPendingOverrides(netId);
                TailDeformConfig.DropPendingOverrides(netId);
            }
            _remoteSkinByNetId[netId] = skinId;
        }

        internal static void OnClientSkinMessage(uint callerId, IInteropReader reader)
        {
            SkinChangeMessage msg = SkinMessageCodec.ReadSkinChange(reader);
            if (!SkinPathResolver.IsSafeSkinName(msg.skinID)) return;

            bool hasLocal = RosterConsumer.TryGetLocalNetBody(out _, out uint localNetId, out _);
            bool isLocalEcho = hasLocal && localNetId != 0 && localNetId == msg.netId;
            CommsTrace.ClientReceive(msg.skinID, msg.netId, msg.charType, isLocalEcho);
#if DEBUG
            bool hasCid = RosterConsumer.TryGetLocalClientId(out uint localCid);
            SkinSyncMod.ModLog.Debug($"客户端接收上下文：local netId {(hasLocal ? localNetId.ToString() : "n/a")}，local clientId {(hasCid ? localCid.ToString() : "n/a")}，echo={isLocalEcho}");
#endif

            if (!isLocalEcho)
            {
                RecordRemoteSkin(msg.netId, msg.skinID);
                ApplySkinToPlayer(msg.netId, msg.skinID, msg.charType);
            }

            if (RosterConsumer.TryGetNetBodyFromId(msg.netId, out _, out _, out bool isLocal) && isLocal)
            {
                if (SkinSync.Settings != null) SkinSync.Settings.CurrentSkin.Value = msg.skinID;
            }
        }

        internal static void OnServerSkinMessage(uint callerId, IInteropReader reader)
        {
            SkinChangeMessage msg = SkinMessageCodec.ReadSkinChange(reader);
            CommsTrace.HostReceive(callerId, msg.skinID, msg.netId, msg.charType);
            if (!SkinPathResolver.IsSafeSkinName(msg.skinID))
            {
                CommsTrace.HostRejected(callerId, msg.netId, msg.skinID, "非法皮肤名称");
                return;
            }

            if (!RosterConsumer.TryGetNetBodyOfClient(callerId, out uint callerNetId))
            {
                CommsTrace.HostRejected(callerId, msg.netId, msg.skinID, "无法反查该 client 的 netId");
                InteropDiagnostics.WarnOnce("SkinMessageHandler.SkinCallerNetId", $"服务端无法反查客户端 {callerId} 的 netId，皮肤切换请求已拒绝。");
                return;
            }

            if (callerNetId != msg.netId)
            {
                CommsTrace.HostRejected(callerId, msg.netId, msg.skinID, $"冒充他人（实际 netId {callerNetId}）");
                SkinSyncMod.ModLog.Warning($"Cheat attempt: client {callerId} (netId {callerNetId}) tried to change skin for {msg.netId}");
                return;
            }

            if (!RosterConsumer.TryGetLocalNetBody(out _, out uint hostLocalNetId, out _) || hostLocalNetId != msg.netId)
            {
                RecordRemoteSkin(msg.netId, msg.skinID);
            }
            ApplySkinToPlayer(msg.netId, msg.skinID, msg.charType);

            IInteropWriter writer = ChannelConsumer.CreateWriter(SkinNetworkIDs.SkinChangeMessageId);
            SkinMessageCodec.WriteSkinChange(writer, msg);
            int recipients = ChannelConsumer.ForwardExcept((ushort)callerId, writer, SkinNetworkIDs.SkinChangeMessageId);
            CommsTrace.HostBroadcast(msg.skinID, msg.netId, recipients, callerId);

            Patches.SkinCacheBroadcaster.RecordSkin(msg.netId, msg.skinID);

            if (RosterConsumer.TryGetPlayerSteamId(callerId, out ulong steamId) && steamId != 0UL)
                SkinCacheStore.Set(steamId, msg.skinID);
        }

        private static void ApplySkinToPlayer(uint netId, string characterName, int charType = 0)
        {
            if (!RosterConsumer.TryGetNetBodyFromId(netId, out _, out GameObject playerObj, out _))
            {
                Patches.PendingSkinApplier.Enqueue(netId, characterName, "NetBody 未注册", charType);
                SkinSyncMod.ModLog.Info($"NetBody netId {netId} 暂未注册，已加入待应用队列：{characterName}");
                return;
            }
            if (playerObj == null)
            {
                Patches.PendingSkinApplier.Enqueue(netId, characterName, "NetBody 已注册但角色对象为空", charType);
                return;
            }
            SkinApplier.ApplySkinToPlayer(playerObj, characterName, charType);
        }

        internal static void OnClientAccessorySync(uint callerId, IInteropReader reader)
        {
            var msg = SkinMessageCodec.ReadAccessory(reader);
            if (!SkinPathResolver.IsSafeSkinName(msg.skinID) || string.IsNullOrEmpty(msg.accId)) return;
            ApplyAccessoryOverride(msg);
        }

        internal static void OnServerAccessorySync(uint callerId, IInteropReader reader)
        {
            var msg = SkinMessageCodec.ReadAccessory(reader);
            if (!SkinPathResolver.IsSafeSkinName(msg.skinID) || string.IsNullOrEmpty(msg.accId)) return;
            if (!RosterConsumer.TryGetNetBodyOfClient(callerId, out uint callerNetId))
            {
                InteropDiagnostics.WarnOnce("SkinMessageHandler.AccessoryCallerNetId", $"服务端无法反查客户端 {callerId} 的 netId，配件同步请求已拒绝。");
                return;
            }

            if (callerNetId != msg.netId)
            {
                SkinSyncMod.ModLog.Warning($"Cheat attempt: client {callerId} (netId {callerNetId}) tried accessory sync for {msg.netId}");
                return;
            }
            ApplyAccessoryOverride(msg);
            Patches.SkinCacheBroadcaster.RecordAccessory(msg);

            IInteropWriter writer = ChannelConsumer.CreateWriter(SkinNetworkIDs.AccessorySyncMessageId);
            SkinMessageCodec.WriteAccessory(writer, msg);
            ChannelConsumer.ForwardExcept((ushort)callerId, writer, SkinNetworkIDs.AccessorySyncMessageId);
        }

        private static void ApplyAccessoryOverride(AccessorySyncMessage msg)
        {
            if (SkinSync.Settings == null || msg.netId == 0) return;
            float rot = SanitizeFloat(msg.rotation, -3600f, 3600f, 0f);

            if (!RosterConsumer.TryGetNetBodyFromId(msg.netId, out _, out GameObject chara, out bool isLocal) || chara == null)
            {
                AccessoryAttacher.StagePendingRemoteOverride(msg.netId, msg.skinID, msg.accId, msg.enabled, msg.offX, msg.offY, rot, msg.zOrder);
                return;
            }

            if (isLocal) return;

            AccessoryAttacher.StagePendingRemoteOverride(msg.netId, msg.skinID, msg.accId, msg.enabled, msg.offX, msg.offY, rot, msg.zOrder);
            AccessoryAttacher.SetRemoteOverride(chara, msg.skinID, msg.accId, msg.enabled, msg.offX, msg.offY, rot, msg.zOrder);

            if (SkinApplier.GetAssignedSkin(chara) == msg.skinID)
            {
                SkinApplier.ApplySkinToPlayer(chara, msg.skinID);
            }
        }

        internal static void OnClientTailSync(uint callerId, IInteropReader reader)
        {
            var msg = SkinMessageCodec.ReadTail(reader);
            if (!SkinPathResolver.IsSafeSkinName(msg.skinID)) return;
            ApplyTailOverride(msg);
        }

        internal static void OnServerTailSync(uint callerId, IInteropReader reader)
        {
            var msg = SkinMessageCodec.ReadTail(reader);
            if (!SkinPathResolver.IsSafeSkinName(msg.skinID)) return;
            if (!RosterConsumer.TryGetNetBodyOfClient(callerId, out uint callerNetId))
            {
                InteropDiagnostics.WarnOnce("SkinMessageHandler.TailCallerNetId", $"服务端无法反查客户端 {callerId} 的 netId，尾巴同步请求已拒绝。");
                return;
            }

            if (callerNetId != msg.netId)
            {
                SkinSyncMod.ModLog.Warning($"Cheat attempt: client {callerId} (netId {callerNetId}) tried tail sync for {msg.netId}");
                return;
            }
            ApplyTailOverride(msg);
            Patches.SkinCacheBroadcaster.RecordTail(msg);

            IInteropWriter writer = ChannelConsumer.CreateWriter(SkinNetworkIDs.TailSyncMessageId);
            SkinMessageCodec.WriteTail(writer, msg);
            ChannelConsumer.ForwardExcept((ushort)callerId, writer, SkinNetworkIDs.TailSyncMessageId);
        }

        private static void ApplyTailOverride(TailSyncMessage msg)
        {
            if (SkinSync.Settings == null || msg.netId == 0) return;

            var p = new TailDeformParams
            {
                Enabled = msg.enabled,
                FrontGuard = msg.frontGuard,
                Segments = UnityEngine.Mathf.Clamp(msg.segments, 4, 40),
                ConstraintIters = UnityEngine.Mathf.Clamp(msg.constraintIters, 1, 20),
                Damping = SanitizeFloat(msg.damping, 0f, 5f, 0.45f),
                SpeedDamping = SanitizeFloat(msg.speedDamping, 0f, 0.5f, 0.05f),
                Stiffness = SanitizeFloat(msg.stiffness, 0f, 5f, 1.0f),
                MaxBendDeg = SanitizeFloat(msg.maxBendDeg, 0f, 180f, 18f),
                AnchorFollow = SanitizeFloat(msg.anchorFollow, 0f, 5f, 1.0f),
                Smoothness = SanitizeFloat(msg.smoothness, 0f, 0.99f, 0.55f),
                MaxStep = SanitizeFloat(msg.maxStep, 0.001f, 2f, 0.15f),
                MaxFixedDt = SanitizeFloat(msg.maxFixedDt, 0.005f, 0.1f, 0.02f),
                FrontGuardMargin = SanitizeFloat(msg.frontGuardMargin, 0f, 1f, 0.05f),
                GravityX = SanitizeFloat(msg.gravityX, -10f, 10f, 0f),
                GravityY = SanitizeFloat(msg.gravityY, -20f, 10f, -1.2f),
                WindFreq = SanitizeFloat(msg.windFreq, 0f, 20f, 1.2f),
                WindAmp = SanitizeFloat(msg.windAmp, 0f, 1f, 0.012f),
                SpeedDisturb = SanitizeFloat(msg.speedDisturb, 0f, 0.5f, 0.005f),
            };

            if (!RosterConsumer.TryGetNetBodyFromId(msg.netId, out _, out GameObject chara, out bool isLocal) || chara == null)
            {
                TailDeformConfig.StagePendingRemoteOverride(msg.netId, msg.skinID, p);
                return;
            }

            if (isLocal) return;

            TailDeformConfig.StagePendingRemoteOverride(msg.netId, msg.skinID, p);
            TailDeformConfig.SetRemoteOverride(chara, msg.skinID, p);
        }

        private static float SanitizeFloat(float value, float min, float max, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return fallback;
            return UnityEngine.Mathf.Clamp(value, min, max);
        }

        internal static void OnServerSkinPackRequest(uint callerId, IInteropReader reader)
        {
            var msg = SkinMessageCodec.ReadSkinPackRequest(reader);
            if (!SkinPathResolver.IsSafeSkinName(msg.skinID)) return;
            if (SkinSync.Settings == null || !SkinSync.Settings.EnableSkinPackSync.Value) return;

            byte[] local = SkinPackCodec.PackLocalSkin(msg.skinID);
            if (local != null)
            {
                bool myAllow = SkinSync.Settings.AllowPeersPersistMyPack.Value;
                if (MultiplayerSender.ServerSendSkinPackDataToClient(callerId, local, myAllow))
                {
                    SkinSyncMod.ModLog.Info($"服务端：已把皮肤包 {msg.skinID} 直接回给 client {callerId}（allowPersist={myAllow}）");
                }
                return;
            }

            IInteropWriter writer = ChannelConsumer.CreateWriter(SkinNetworkIDs.SkinPackRequestMessageId);
            SkinMessageCodec.WriteSkinPackRequest(writer, msg);
            ChannelConsumer.ForwardExcept((ushort)callerId, writer, SkinNetworkIDs.SkinPackRequestMessageId);
            SkinSyncMod.ModLog.Info($"服务端：转发皮肤包请求 {msg.skinID} 给其他 client");
        }

        internal static void OnClientSkinPackRequest(uint callerId, IInteropReader reader)
        {
            var msg = SkinMessageCodec.ReadSkinPackRequest(reader);
            if (!SkinPathResolver.IsSafeSkinName(msg.skinID)) return;
            if (SkinSync.Settings == null || !SkinSync.Settings.EnableSkinPackSync.Value) return;

            byte[] local = SkinPackCodec.PackLocalSkin(msg.skinID);
            if (local == null) return;
            bool myAllow = SkinSync.Settings.AllowPeersPersistMyPack.Value;
            if (MultiplayerSender.SendSkinPackData(local, myAllow))
            {
                SkinSyncMod.ModLog.Info($"客户端：响应皮肤包请求 {msg.skinID}（{local.Length} bytes, allowPersist={myAllow}）");
            }
        }

        internal static void OnServerSkinPackData(uint callerId, IInteropReader reader)
        {
            var msg = SkinMessageCodec.ReadSkinPackData(reader);
            if (SkinSync.Settings == null || !SkinSync.Settings.EnableSkinPackSync.Value) return;
            if (msg.payload == null || msg.payload.Length == 0 || msg.payload.Length > ushort.MaxValue) return;
            ProcessServerCompleteSkinPack(callerId, msg.payload, msg.allowPersist);
        }

        internal static void OnServerSkinPackChunk(uint callerId, IInteropReader reader)
        {
            var msg = SkinMessageCodec.ReadSkinPackChunk(reader);
            if (SkinSync.Settings == null || !SkinSync.Settings.EnableSkinPackSync.Value) return;
            if (SkinPackChunkAssembler.TryPushChunk(callerId, msg, out byte[] complete, out bool allowPersist))
            {
                ProcessServerCompleteSkinPack(callerId, complete, allowPersist);
            }
        }

        private static void ProcessServerCompleteSkinPack(uint callerId, byte[] payload, bool allowPersist)
        {
            bool persist = SkinSync.Settings.SkinPackPersistOnDisk.Value && allowPersist;
            if (!SkinPackCodec.UnpackAndCache(payload, persist, out string skinID)) return;
            SkinSyncMod.ModLog.Info($"服务端：收到 client {callerId} 的皮肤包 {skinID}（{payload.Length} bytes, allowPersist={allowPersist}），本机 persist={persist}");
            if (_broadcastedPackIds.Add(skinID))
            {
                MultiplayerSender.ServerForwardSkinPackExcept(callerId, payload, allowPersist);
            }

            SkinApplier.ReapplyForSkin(skinID);
        }

        internal static void OnClientSkinPackData(uint callerId, IInteropReader reader)
        {
            var msg = SkinMessageCodec.ReadSkinPackData(reader);
            if (SkinSync.Settings == null || !SkinSync.Settings.EnableSkinPackSync.Value) return;
            if (msg.payload == null || msg.payload.Length == 0 || msg.payload.Length > ushort.MaxValue) return;
            ProcessClientCompleteSkinPack(msg.payload, msg.allowPersist);
        }

        internal static void OnClientSkinPackChunk(uint callerId, IInteropReader reader)
        {
            var msg = SkinMessageCodec.ReadSkinPackChunk(reader);
            if (SkinSync.Settings == null || !SkinSync.Settings.EnableSkinPackSync.Value) return;
            if (SkinPackChunkAssembler.TryPushChunk(ushort.MaxValue, msg, out byte[] complete, out bool allowPersist))
            {
                ProcessClientCompleteSkinPack(complete, allowPersist);
            }
        }

        private static void ProcessClientCompleteSkinPack(byte[] payload, bool allowPersist)
        {
            bool persist = SkinSync.Settings.SkinPackPersistOnDisk.Value && allowPersist;
            if (!SkinPackCodec.UnpackAndCache(payload, persist, out string skinID)) return;
            SkinSyncMod.ModLog.Info($"客户端：收到皮肤包 {skinID}（{payload.Length} bytes, allowPersist={allowPersist}），本机 persist={persist}");

            SkinApplier.ReapplyForSkin(skinID);
        }
    }
}
