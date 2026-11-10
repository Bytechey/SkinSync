// SkinSync Interop
// Copyright (c) 2026 Bytechey
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;

namespace SkinSyncMod.Interop
{
    /// <summary>按能力登记提供方并按优先级解析；未解析到的能力回落到空实现。</summary>
    internal sealed class InteropRegistry : IInteropRegistry
    {
        private readonly List<IInteropProvider> _providers = new List<IInteropProvider>();

        /// <inheritdoc/>
        void IInteropRegistry.Register(IInteropProvider provider)
        {
            if (provider == null)
            {
                return;
            }

            _providers.Add(provider);
        }

        /// <inheritdoc/>
        IReadOnlyList<IInteropProvider> IInteropRegistry.Providers => _providers;

        /// <inheritdoc/>
        IInteropProvider IInteropRegistry.Find(InteropCapability capability)
        {
            return Resolve<IInteropProvider>(capability);
        }

        /// <inheritdoc/>
        IInteropSession IInteropRegistry.Session
        {
            get { return Resolve<IInteropSession>(InteropCapability.Session) ?? NullInteropSession.Instance; }
        }

        /// <inheritdoc/>
        IInteropRoster IInteropRegistry.Roster
        {
            get { return Resolve<IInteropRoster>(InteropCapability.Roster) ?? NullInteropRoster.Instance; }
        }

        /// <inheritdoc/>
        IInteropChannel IInteropRegistry.Channel
        {
            get { return Resolve<IInteropChannel>(InteropCapability.Channel) ?? NullInteropChannel.Instance; }
        }

        /// <inheritdoc/>
        IInteropEvents IInteropRegistry.Events
        {
            get { return Resolve<IInteropEvents>(InteropCapability.Events) ?? NullInteropEvents.Instance; }
        }

        /// <inheritdoc/>
        IInteropTypeRegistry IInteropRegistry.Types
        {
            get { return Resolve<IInteropTypeRegistry>(InteropCapability.TypeRegistry) ?? NullInteropTypeRegistry.Instance; }
        }

        /// <inheritdoc/>
        IInteropMemberAccess IInteropRegistry.Members
        {
            get { return Resolve<IInteropMemberAccess>(InteropCapability.MemberAccess) ?? NullInteropMemberAccess.Instance; }
        }

        /// <inheritdoc/>
        IInteropRawPacketChannel IInteropRegistry.RawPackets
        {
            get { return Resolve<IInteropRawPacketChannel>(InteropCapability.RawPacketChannel) ?? NullInteropRawPacketChannel.Instance; }
        }

        /// <inheritdoc/>
        IInteropMethodInterception IInteropRegistry.Interception
        {
            get { return Resolve<IInteropMethodInterception>(InteropCapability.MethodInterception) ?? NullInteropMethodInterception.Instance; }
        }

        /// <inheritdoc/>
        IInteropPatchGate IInteropRegistry.PatchGate
        {
            get { return Resolve<IInteropPatchGate>(InteropCapability.PatchGate) ?? NullInteropPatchGate.Instance; }
        }

        /// <inheritdoc/>
        IInteropSaveKey IInteropRegistry.SaveKey
        {
            get { return Resolve<IInteropSaveKey>(InteropCapability.SaveKey) ?? NullInteropSaveKey.Instance; }
        }

        /// <inheritdoc/>
        IInteropWorldGenHook IInteropRegistry.WorldGen
        {
            get { return Resolve<IInteropWorldGenHook>(InteropCapability.WorldGenHook) ?? NullInteropWorldGenHook.Instance; }
        }

        /// <inheritdoc/>
        IInteropSettingsHub IInteropRegistry.Settings
        {
            get { return Resolve<IInteropSettingsHub>(InteropCapability.SettingsHub) ?? NullInteropSettingsHub.Instance; }
        }

        /// <summary>取声明了指定能力、当前可用、实现了 <typeparamref name="T"/> 的最高优先级提供方。</summary>
        /// <remarks>优先级相同时后登记者优先，保证后加载的 mod 可以覆盖先加载的实现。</remarks>
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
