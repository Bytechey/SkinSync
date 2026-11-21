// SkinSync Interop
// Copyright (c) 2026 Bytechey
// SPDX-License-Identifier: MIT

using System.Collections.Generic;

namespace SkinSyncMod.Interop
{
    /// <summary>管理互操作提供方并按优先级解析各能力接口。</summary>
    internal sealed class InteropRegistry : IInteropRegistry
    {
        private readonly List<IInteropProvider> _providers = new List<IInteropProvider>();

        void IInteropRegistry.Register(IInteropProvider provider)
        {
            if (provider == null)
            {
                return;
            }

            _providers.Add(provider);
        }

        IReadOnlyList<IInteropProvider> IInteropRegistry.Providers => _providers;

        IInteropProvider IInteropRegistry.Find(InteropCapability capability)
        {
            return Resolve<IInteropProvider>(capability);
        }

        IInteropSession IInteropRegistry.Session
        {
            get { return Resolve<IInteropSession>(InteropCapability.Session) ?? NullInteropSession.Instance; }
        }

        IInteropRoster IInteropRegistry.Roster
        {
            get { return Resolve<IInteropRoster>(InteropCapability.Roster) ?? NullInteropRoster.Instance; }
        }

        IInteropChannel IInteropRegistry.Channel
        {
            get { return Resolve<IInteropChannel>(InteropCapability.Channel) ?? NullInteropChannel.Instance; }
        }

        IInteropEvents IInteropRegistry.Events
        {
            get { return Resolve<IInteropEvents>(InteropCapability.Events) ?? NullInteropEvents.Instance; }
        }

        IInteropTypeRegistry IInteropRegistry.Types
        {
            get { return Resolve<IInteropTypeRegistry>(InteropCapability.TypeRegistry) ?? NullInteropTypeRegistry.Instance; }
        }

        IInteropMemberAccess IInteropRegistry.Members
        {
            get { return Resolve<IInteropMemberAccess>(InteropCapability.MemberAccess) ?? NullInteropMemberAccess.Instance; }
        }

        IInteropRawPacketChannel IInteropRegistry.RawPackets
        {
            get { return Resolve<IInteropRawPacketChannel>(InteropCapability.RawPacketChannel) ?? NullInteropRawPacketChannel.Instance; }
        }

        IInteropMethodInterception IInteropRegistry.Interception
        {
            get { return Resolve<IInteropMethodInterception>(InteropCapability.MethodInterception) ?? NullInteropMethodInterception.Instance; }
        }

        IInteropPatchGate IInteropRegistry.PatchGate
        {
            get { return Resolve<IInteropPatchGate>(InteropCapability.PatchGate) ?? NullInteropPatchGate.Instance; }
        }

        IInteropSaveKey IInteropRegistry.SaveKey
        {
            get { return Resolve<IInteropSaveKey>(InteropCapability.SaveKey) ?? NullInteropSaveKey.Instance; }
        }

        IInteropWorldGenHook IInteropRegistry.WorldGen
        {
            get { return Resolve<IInteropWorldGenHook>(InteropCapability.WorldGenHook) ?? NullInteropWorldGenHook.Instance; }
        }

        IInteropSettingsHub IInteropRegistry.Settings
        {
            get { return Resolve<IInteropSettingsHub>(InteropCapability.SettingsHub) ?? NullInteropSettingsHub.Instance; }
        }

        private T Resolve<T>(InteropCapability capability) where T : class
        {
            IInteropProvider best = null;
            for (int i = 0; i < _providers.Count; i++)
            {
                IInteropProvider provider = _providers[i];
                if (!provider.IsAvailable)
                {
                    continue;
                }

                if ((provider.Capabilities & capability) == 0)
                {
                    continue;
                }

                if (!(provider is T))
                {
                    continue;
                }

                if (best == null || provider.Priority >= best.Priority)
                {
                    best = provider;
                }
            }

            return best as T;
        }
    }
}
