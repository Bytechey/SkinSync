using SkinSyncMod.Interop.Consumers;
using SkinSyncMod.Network;
using UnityEngine;

namespace SkinSyncMod
{
    public partial class SkinSync
    {
        private uint _localSkinReportedNetId;
        private GameObject _reportedLocalChara;
        private int _reportedLocalCharType = -1;

        private void ReportLocalSkinIfNeeded()
        {
            if (localNetBodyBox == null || localChara == null || _activeSceneName == "PreGen") return;
            if (SessionConsumer.IsRunning && !SessionConsumer.IsServer && !_worldReadySentForScene)
            {
                Network.MultiplayerSender.SendClientWorldReady();
                _worldReadySentForScene = true;
            }
            int curCharType = ReadLocalCharType(localChara);
            if (_reportedLocalChara == localChara && _reportedLocalCharType == curCharType && _localSkinReportedNetId != 0) return;
            string cached = Settings != null ? Settings.CurrentSkin.Value : null;
            if (string.IsNullOrEmpty(cached) || !TryGetLocalNetId(out uint netId)) return;
            if (_localSkinReportedNetId == netId && _reportedLocalCharType == curCharType && _reportedLocalChara == localChara) return;

            MultiplayerSender.SendSkinChange(netId, cached);
            SyncSavedOverridesOnEnter(cached);
            _localSkinReportedNetId = netId;
            _reportedLocalChara = localChara;
            _reportedLocalCharType = curCharType;
            ModLog.Info($"多人模式：本地皮肤已上报 (netId {netId}, charType {curCharType} -> {cached})");
        }

        /// <summary>响应联机角色生成或同 netId 换物种重建网格通知，为新躯体恢复对应皮肤与覆盖参数。</summary>
        internal static void OnExternalBodyReadyOrRebuilt(Body body, uint netId, int charType, bool isRebuild)
        {
            if (body == null) return;

            GameObject chara = null;
            bool isLocal = false;
            if (netId != 0) RosterConsumer.TryGetNetBodyFromId(netId, out _, out chara, out isLocal);
            else RosterConsumer.TryResolveBodyIdentity(body, out netId, out chara, out isLocal);

            GameObject targetGo = chara != null ? chara : body.gameObject;
            int resolvedCharType = charType >= 0 ? charType : body.charType;

            if (isLocal)
            {
                if (_instance == null) return;
                if (RosterConsumer.TryGetLocalNetBody(out object freshBox, out _, out _) && freshBox != null)
                    _instance.localNetBodyBox = freshBox;
                _instance.localChara = targetGo;
                _instance._localBody = body;

                string localSkin = _instance.ResolveCurrentSkin();
                if (string.IsNullOrEmpty(localSkin)) return;

                if (!isRebuild && _instance._appliedTargetObject == targetGo && _instance._appliedCharType == resolvedCharType
                    && _instance._persistentApplied == localSkin && SkinApplier.GetAssignedSkin(targetGo) == localSkin)
                {
                    _instance.ReportLocalSkinIfNeeded();
                    return;
                }

                _instance._appliedTargetObject = targetGo;
                _instance._appliedCharType = resolvedCharType;
                _instance._persistentApplied = localSkin;
                SkinApplier.ApplySkinToPlayer(targetGo, localSkin, resolvedCharType);
                _instance.ReportLocalSkinIfNeeded();
                return;
            }

            if (netId == 0 || !SkinMessageHandlers.TryGetRecordedRemoteSkin(netId, out string remoteSkin))
            {
                SkinApplier.EnsureUnassignedRemoteVanilla(targetGo, resolvedCharType);
                return;
            }
            if (!isRebuild && SkinApplier.GetAssignedSkin(targetGo) == remoteSkin) return;

            AccessoryAttacher.BindPendingOverrides(netId, targetGo);
            TailDeformConfig.BindPendingOverrides(netId, targetGo);
            SkinApplier.ApplySkinToPlayer(targetGo, remoteSkin, resolvedCharType);
            ModLog.Info($"同 netId 躯体重建恢复：netId {netId} (charType {resolvedCharType}) -> {remoteSkin}");
        }

        private void SyncSavedOverridesOnEnter(string skin)
        {
            if (Settings == null || Settings.IsPassiveSyncMode) return;
            var accOverrides = Settings.GetAllAccessoryOverridesForSkin(skin);
            if (Settings.SyncAccessories.Value && accOverrides != null && accOverrides.Count > 0)
            {
                foreach (var kv in accOverrides) BroadcastAccessoryDelta(skin, kv.Key);
            }
            if (Settings.SyncTailDeform.Value && (Settings.GetTailOverride(skin) != null || !Settings.TailDeformEnabled.Value))
                BroadcastTailDelta(skin);
        }
    }
}
