using System;
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
        private const float PackRequestForwardCooldownSec = 5f;
        private const float CallerWarningCooldownSec = 5f;
        private const float WorldReadyCooldownSec = 1f;
        private const float ClientTokenRefillPerSec = 20f;
        private const float ClientTokenBurstLimit = 40f;
        private const int MaxAccessoryIdLength = 64;

        private readonly struct CallerSkinKey : IEquatable<CallerSkinKey>
        {
            public readonly uint CallerId;
            public readonly string SkinId;

            public CallerSkinKey(uint callerId, string skinId)
            {
                CallerId = callerId;
                SkinId = skinId ?? string.Empty;
            }

            public bool Equals(CallerSkinKey other)
                => CallerId == other.CallerId && string.Equals(SkinId, other.SkinId, StringComparison.Ordinal);

            public override bool Equals(object obj) => obj is CallerSkinKey other && Equals(other);

            public override int GetHashCode()
                => ((int)CallerId * 397) ^ StringComparer.Ordinal.GetHashCode(SkinId);
        }

        private struct ClientRateBucket
        {
            public float Tokens;
            public float LastRefillAt;
            public float NextWarnAt;
        }

        private static readonly Dictionary<uint, string> _remoteSkinByNetId = new Dictionary<uint, string>();
        private static readonly Dictionary<string, HashSet<uint>> _pendingPackRequesters
            = new Dictionary<string, HashSet<uint>>(StringComparer.Ordinal);
        private static readonly Dictionary<string, float> _lastForwardedRequestAt
            = new Dictionary<string, float>(StringComparer.Ordinal);
        private static readonly Dictionary<CallerSkinKey, float> _lastPackRequestAt
            = new Dictionary<CallerSkinKey, float>();
        private static readonly Dictionary<uint, ClientRateBucket> _clientRateBuckets
            = new Dictionary<uint, ClientRateBucket>();
        private static readonly Dictionary<uint, float> _lastWorldReadyAt
            = new Dictionary<uint, float>();
        private static readonly Dictionary<uint, float> _nextCallerWarnAt
            = new Dictionary<uint, float>();

        static SkinMessageHandlers()
        {
            SkinPackChunkAssembler.CanAcceptInboundSkin = IsAuthorizedPackUpload;
        }

        internal static void OnSceneChanged(bool preserveActiveSession = false)
        {
            if (preserveActiveSession) return;
            _remoteSkinByNetId.Clear();
            _pendingPackRequesters.Clear();
            _lastForwardedRequestAt.Clear();
            _lastPackRequestAt.Clear();
            _lastWorldReadyAt.Clear();
            _clientRateBuckets.Clear();
            _nextCallerWarnAt.Clear();
            SkinPackChunkAssembler.Clear();
            SkinPackCodec.TrimSessionMemoryCache();
        }

        private static bool TryConsumeClientToken(uint callerId)
        {
            float now = Time.realtimeSinceStartup;
            if (!_clientRateBuckets.TryGetValue(callerId, out ClientRateBucket bucket))
            {
                _clientRateBuckets[callerId] = new ClientRateBucket
                {
                    Tokens = ClientTokenBurstLimit - 1f,
                    LastRefillAt = now,
                    NextWarnAt = 0f,
                };
                return true;
            }

            float elapsed = Mathf.Max(0f, now - bucket.LastRefillAt);
            bucket.Tokens = Mathf.Min(ClientTokenBurstLimit, bucket.Tokens + elapsed * ClientTokenRefillPerSec);
            bucket.LastRefillAt = now;

            if (bucket.Tokens < 1f)
            {
                if (now >= bucket.NextWarnAt)
                {
                    bucket.NextWarnAt = now + CallerWarningCooldownSec;
                    SkinSyncMod.ModLog.Warning($"服务端：client {callerId} 皮肤同步发包速率超限，已丢弃超额数据包。");
                }
                _clientRateBuckets[callerId] = bucket;
                return false;
            }

            bucket.Tokens -= 1f;
            _clientRateBuckets[callerId] = bucket;
            return true;
        }

        private static void WarnCallerThrottled(uint callerId, string message)
        {
            float now = Time.realtimeSinceStartup;
            if (_nextCallerWarnAt.TryGetValue(callerId, out float nextAt) && now < nextAt)
            {
                return;
            }
            _nextCallerWarnAt[callerId] = now + CallerWarningCooldownSec;
            SkinSyncMod.ModLog.Warning(message);
        }

        private static bool IsAuthorizedPackUpload(uint callerId, string skinId)
        {
            if (!SkinPathResolver.IsSafeSkinName(skinId))
            {
                return false;
            }

            bool wornByCaller = RosterConsumer.TryGetNetBodyOfClient(callerId, out uint cNetId)
                && TryGetRecordedRemoteSkin(cNetId, out string worn)
                && worn == skinId;
            if (wornByCaller || _pendingPackRequesters.ContainsKey(skinId))
            {
                return true;
            }

            WarnCallerThrottled(callerId, $"服务端：拒绝来自 client {callerId} 未经请求且未穿着的皮肤包 {skinId}。");
            return false;
        }

        internal static bool TryGetRecordedRemoteSkin(uint netId, out string skinId)
        {
            skinId = null;
            if (netId == 0) return false;
            if (_remoteSkinByNetId.TryGetValue(netId, out skinId) && !string.IsNullOrEmpty(skinId)) return true;
            return Patches.SkinCacheBroadcaster.TryGetRecordedSkin(netId, out skinId);
        }

        internal static void CopyRecordedRemoteSkinsTo(Dictionary<uint, string> target)
        {
            if (target == null) return;
            foreach (var kv in _remoteSkinByNetId)
            {
                if (kv.Key != 0 && !string.IsNullOrEmpty(kv.Value) && !target.ContainsKey(kv.Key))
                {
                    target[kv.Key] = kv.Value;
                }
            }
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
                // 按 (皮肤名, 内容哈希) 解析出本地真正该渲染的 id：
                // 同名同内容 → 用名字；已有该哈希的收包 → 用 hash；都没有 → 先拉包，到齐后再应用。
                string localId = ContentSkinResolver.ResolveLocal(msg.skinID, msg.contentHash);
                if (localId != null)
                {
                    RecordRemoteSkin(msg.netId, localId);
                    ApplySkinToPlayer(msg.netId, localId, msg.charType);
                }
                else
                {
                    // 按哈希记录本次目标：皮肤包到齐后可据此重新应用（渲染用的是 hash 形态的 id）。
                    RecordRemoteSkin(msg.netId, msg.contentHash);
                    SkinSyncMod.ModLog.Info($"本地缺少皮肤内容 {msg.skinID}（hash={msg.contentHash}），已请求皮肤包，到齐后补应用（netId {msg.netId}）。");
                }
            }
        }

        internal static void OnServerSkinMessage(uint callerId, IInteropReader reader)
        {
            SkinChangeMessage msg = SkinMessageCodec.ReadSkinChange(reader);
            if (!TryConsumeClientToken(callerId)) return;

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
                CommsTrace.HostRejected(callerId, msg.netId, msg.skinID, $"netId 不一致（当前实体 netId {callerNetId}）");
                WarnCallerThrottled(callerId, $"服务端：忽略 client {callerId} 的皮肤切换请求（上报 netId {msg.netId} 与当前实体 netId {callerNetId} 不一致）。");
                return;
            }

            Patches.SkinCacheBroadcaster.NotifyClientWorldReady(callerId, replayImmediately: false);

            bool isHostLocal = RosterConsumer.TryGetLocalNetBody(out _, out uint hostLocalNetId, out _)
                && hostLocalNetId != 0
                && hostLocalNetId == msg.netId;
            if (!isHostLocal)
            {
                RecordRemoteSkin(msg.netId, msg.skinID);
                ApplySkinToPlayer(msg.netId, msg.skinID, msg.charType);
            }

            IInteropWriter writer = ChannelConsumer.CreateWriter(SkinNetworkIDs.SkinChangeMessageId);
            SkinMessageCodec.WriteSkinChange(writer, msg);
            int recipients = ChannelConsumer.ForwardExcept((ushort)callerId, writer, SkinNetworkIDs.SkinChangeMessageId);
            CommsTrace.HostBroadcast(msg.skinID, msg.netId, recipients, callerId);

            Patches.SkinCacheBroadcaster.RecordSkin(msg.netId, msg.skinID);
        }

        internal static void OnServerClientWorldReady(uint callerId, IInteropReader reader)
        {
            if (reader != null && reader.RemainingBytes >= 4)
            {
                reader.GetUInt();
            }
            if (!TryConsumeClientToken(callerId)) return;

            float now = Time.realtimeSinceStartup;
            if (_lastWorldReadyAt.TryGetValue(callerId, out float lastAt) && now - lastAt < WorldReadyCooldownSec)
            {
                return;
            }
            _lastWorldReadyAt[callerId] = now;
            Patches.SkinCacheBroadcaster.NotifyClientWorldReady(callerId, replayImmediately: true);
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
            if (!SkinPathResolver.IsSafeSkinName(msg.skinID)
                || string.IsNullOrEmpty(msg.accId)
                || msg.accId.Length > MaxAccessoryIdLength)
            {
                return;
            }
            ApplyAccessoryOverride(msg);
        }

        internal static void OnServerAccessorySync(uint callerId, IInteropReader reader)
        {
            var msg = SkinMessageCodec.ReadAccessory(reader);
            if (!TryConsumeClientToken(callerId)) return;
            if (!SkinPathResolver.IsSafeSkinName(msg.skinID)
                || string.IsNullOrEmpty(msg.accId)
                || msg.accId.Length > MaxAccessoryIdLength)
            {
                return;
            }
            if (!RosterConsumer.TryGetNetBodyOfClient(callerId, out uint callerNetId))
            {
                InteropDiagnostics.WarnOnce("SkinMessageHandler.AccessoryCallerNetId", $"服务端无法反查客户端 {callerId} 的 netId，配件同步请求已拒绝。");
                return;
            }

            if (callerNetId != msg.netId)
            {
                WarnCallerThrottled(callerId, $"服务端：忽略 client {callerId} 的配件同步请求（上报 netId {msg.netId} 与当前实体 netId {callerNetId} 不一致）。");
                return;
            }

            SanitizeAccessoryMessage(ref msg);
            ApplyAccessoryOverride(msg);
            Patches.SkinCacheBroadcaster.RecordAccessory(msg);

            IInteropWriter writer = ChannelConsumer.CreateWriter(SkinNetworkIDs.AccessorySyncMessageId);
            SkinMessageCodec.WriteAccessory(writer, msg);
            ChannelConsumer.ForwardExcept((ushort)callerId, writer, SkinNetworkIDs.AccessorySyncMessageId);
        }

        private static void SanitizeAccessoryMessage(ref AccessorySyncMessage msg)
        {
            msg.offX = Mathf.Clamp(msg.offX, -2048, 2048);
            msg.offY = Mathf.Clamp(msg.offY, -2048, 2048);
            msg.zOrder = Mathf.Clamp(msg.zOrder, -100, 100);
            msg.rotation = SanitizeFloat(msg.rotation, -3600f, 3600f, 0f);
        }

        private static void ApplyAccessoryOverride(AccessorySyncMessage msg)
        {
            if (SkinSync.Settings == null || msg.netId == 0) return;
            int offX = Mathf.Clamp(msg.offX, -2048, 2048);
            int offY = Mathf.Clamp(msg.offY, -2048, 2048);
            int zOrder = Mathf.Clamp(msg.zOrder, -100, 100);
            float rot = SanitizeFloat(msg.rotation, -3600f, 3600f, 0f);

            if (!RosterConsumer.TryGetNetBodyFromId(msg.netId, out _, out GameObject chara, out bool isLocal) || chara == null)
            {
                AccessoryAttacher.StagePendingRemoteOverride(msg.netId, msg.skinID, msg.accId, msg.enabled, offX, offY, rot, zOrder);
                return;
            }

            if (isLocal) return;

            AccessoryAttacher.StagePendingRemoteOverride(msg.netId, msg.skinID, msg.accId, msg.enabled, offX, offY, rot, zOrder);
            AccessoryAttacher.SetRemoteOverride(chara, msg.skinID, msg.accId, msg.enabled, offX, offY, rot, zOrder);

            if (SkinApplier.GetAssignedSkin(chara) == msg.skinID
                && !AccessoryAttacher.TryApplySingleRemoteOverride(chara, msg.skinID, msg.accId, msg.enabled, offX, offY, rot, zOrder))
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
            if (!TryConsumeClientToken(callerId)) return;
            if (!SkinPathResolver.IsSafeSkinName(msg.skinID)) return;
            if (!RosterConsumer.TryGetNetBodyOfClient(callerId, out uint callerNetId))
            {
                InteropDiagnostics.WarnOnce("SkinMessageHandler.TailCallerNetId", $"服务端无法反查客户端 {callerId} 的 netId，尾巴同步请求已拒绝。");
                return;
            }

            if (callerNetId != msg.netId)
            {
                WarnCallerThrottled(callerId, $"服务端：忽略 client {callerId} 的尾巴同步请求（上报 netId {msg.netId} 与当前实体 netId {callerNetId} 不一致）。");
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
            if (!TryConsumeClientToken(callerId)) return;
            if (!SkinPathResolver.IsSafeSkinName(msg.skinID)) return;

            float now = Time.realtimeSinceStartup;
            var reqKey = new CallerSkinKey(callerId, msg.skinID);
            if (_lastPackRequestAt.TryGetValue(reqKey, out float lastReqAt)
                && now - lastReqAt < PackRequestForwardCooldownSec)
            {
                return;
            }
            _lastPackRequestAt[reqKey] = now;

            bool hostSyncEnabled = SkinSync.Settings != null && SkinSync.Settings.EnableSkinPackSync.Value;
            bool localPersistAllow = SkinSync.Settings != null && SkinSync.Settings.AllowPeersPersistMyPack.Value;
            if ((hostSyncEnabled || !SkinPathResolver.HasLocalCustomSkin(msg.skinID))
                && SkinPackCodec.TryPackForShare(msg.skinID, localPersistAllow, out byte[] local, out bool effectiveAllow))
            {
                if (MultiplayerSender.ServerSendSkinPackDataToClient(callerId, local, effectiveAllow))
                {
                    SkinSyncMod.ModLog.Info($"服务端：已把皮肤包 {msg.skinID} 直接回给 client {callerId}（allowPersist={effectiveAllow}）");
                }
                return;
            }

            bool hadActiveWaiters = _pendingPackRequesters.TryGetValue(msg.skinID, out HashSet<uint> waiters)
                && waiters != null
                && waiters.Count > 0;
            if (waiters == null)
            {
                waiters = new HashSet<uint>();
                _pendingPackRequesters[msg.skinID] = waiters;
            }
            waiters.Add(callerId);

            // 仅当存在未交割等待者且仍在合并窗口内时抑制重复广播，避免上一轮刚交割后新加入客机的请求被静默吞掉。
            if (hadActiveWaiters
                && _lastForwardedRequestAt.TryGetValue(msg.skinID, out float lastAt)
                && now - lastAt < PackRequestForwardCooldownSec)
            {
                return;
            }
            _lastForwardedRequestAt[msg.skinID] = now;

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

            if (!SkinPackCodec.TryPackForShare(
                msg.skinID,
                SkinSync.Settings.AllowPeersPersistMyPack.Value,
                out byte[] local,
                out bool effectiveAllow))
            {
                return;
            }
            if (MultiplayerSender.SendSkinPackData(local, effectiveAllow))
            {
                SkinSyncMod.ModLog.Info($"客户端：响应皮肤包请求 {msg.skinID}（{local.Length} bytes, allowPersist={effectiveAllow}）");
            }
        }

        internal static void OnServerSkinPackData(uint callerId, IInteropReader reader)
        {
            var msg = SkinMessageCodec.ReadSkinPackData(reader);
            if (!TryConsumeClientToken(callerId)) return;
            if (msg.payload == null || msg.payload.Length == 0 || msg.payload.Length > ushort.MaxValue) return;
            ProcessServerCompleteSkinPack(callerId, msg.payload, msg.allowPersist);
        }

        internal static void OnServerSkinPackChunk(uint callerId, IInteropReader reader)
        {
            var msg = SkinMessageCodec.ReadSkinPackChunk(reader);
            if (!SkinPackChunkAssembler.IsExpectedFollowUpChunk(callerId, msg)
                && !TryConsumeClientToken(callerId))
            {
                return;
            }
            if (SkinPackChunkAssembler.TryPushChunk(callerId, msg, out byte[] complete, out bool allowPersist))
            {
                ProcessServerCompleteSkinPack(callerId, complete, allowPersist);
            }
        }

        private static void ProcessServerCompleteSkinPack(uint callerId, byte[] payload, bool allowPersist)
        {
            if (payload == null || !SkinPackCodec.TryPeekSkinId(payload, payload.Length, out string peekedId))
            {
                return;
            }
            if (!IsAuthorizedPackUpload(callerId, peekedId))
            {
                return;
            }

            bool hostSyncEnabled = SkinSync.Settings != null && SkinSync.Settings.EnableSkinPackSync.Value;
            bool hostHasLocal = SkinPathResolver.HasLocalCustomSkin(peekedId);
            string skinID;
            bool unpackedForHost = false;

            if (!hostSyncEnabled || hostHasLocal)
            {
                if (!SkinPackCodec.ValidatePack(payload, out skinID)) return;
                SkinSyncMod.ModLog.Info($"服务端：收到 client {callerId} 的皮肤包 {skinID}（{payload.Length} bytes），仅安全校验并中继单播（hostSync={hostSyncEnabled}, hostHasLocal={hostHasLocal}）。");
            }
            else
            {
                bool persist = SkinSync.Settings.SkinPackPersistOnDisk.Value && allowPersist;
                // 走带内容寻址的解包：落盘一律写 _received/<hash>，不再写 CustomSprites/<名字>。
                if (!SkinPackCodec.UnpackAndCache(payload, null, persist, out skinID, out _, remoteAllowPersist: allowPersist)) return;
                unpackedForHost = true;
                SkinSyncMod.ModLog.Info($"服务端：收到 client {callerId} 的皮肤包 {skinID}（{payload.Length} bytes, allowPersist={allowPersist}），本机 persist={persist}");
            }

            if (_pendingPackRequesters.TryGetValue(skinID, out HashSet<uint> waiters))
            {
                _pendingPackRequesters.Remove(skinID);
                bool hasLocalCid = RosterConsumer.TryGetLocalClientId(out uint hostCid);
                foreach (uint waiter in waiters)
                {
                    if (waiter == callerId || (hasLocalCid && waiter == hostCid))
                    {
                        continue;
                    }
                    MultiplayerSender.ServerSendSkinPackDataToClient(waiter, payload, allowPersist);
                }
                waiters.Clear();
            }

            if (unpackedForHost)
            {
                SkinApplier.ReapplyForSkin(skinID);
            }
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
                ProcessClientCompleteSkinPack(complete, allowPersist, msg.contentHash);
            }
        }

        private static void ProcessClientCompleteSkinPack(byte[] payload, bool allowPersist, string expectedHash = null)
        {
            bool persist = SkinSync.Settings.SkinPackPersistOnDisk.Value && allowPersist;
            // 走带哈希校验的解包：本机有同名自有皮肤时按内容寻址另存到 _received/<hash>，不再整包丢弃。
            if (!SkinPackCodec.UnpackAndCache(payload, expectedHash, persist,
                    out string skinID, out string actualHash, remoteAllowPersist: allowPersist))
            {
                if (SkinPackCodec.TryPeekSkinId(payload, payload != null ? payload.Length : 0, out string peeked)
                    && !SkinPathResolver.HasLocalCustomSkin(peeked))
                {
                    SkinApplier.ClearRequestedPack(peeked);
                }
                return;
            }
            SkinSyncMod.ModLog.Info($"客户端：收到皮肤包 {skinID}（hash={actualHash}，{payload.Length} bytes, allowPersist={allowPersist}），本机 persist={persist}");

            ContentSkinResolver.OnPackArrived(actualHash);
            SkinApplier.ReapplyForSkin(skinID);
            // 同名不同内容时渲染用的是 hash 形态的 id，这里再按它刷一次。
            if (!string.IsNullOrEmpty(actualHash)) SkinApplier.ReapplyForSkin(actualHash);
        }
    }
}
