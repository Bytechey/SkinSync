// SkinSync Interop
// Copyright (c) 2026 Bytechey
// SPDX-License-Identifier: MIT

using System.Collections.Generic;

namespace SkinSyncMod.Interop
{
    /// <summary>初始化互操作登记表并注册联机提供方。</summary>
    internal static class InteropBootstrap
    {
        private const InteropCapability RequiredCapabilities =
            InteropCapability.Session
            | InteropCapability.Roster
            | InteropCapability.Channel
            | InteropCapability.Events
            | InteropCapability.TypeRegistry;

        private static IInteropRegistry _registry;

        /// <summary>创建并返回全局互操作登记表实例。</summary>
        internal static IInteropRegistry Initialize()
        {
            if (_registry != null)
            {
                return _registry;
            }

            IInteropRegistry registry = new InteropRegistry();
            registry.Register(new TogetherProvider());
            _registry = registry;
            InteropDiagnostics.Report(registry, RequiredCapabilities);
            SkinSyncMod.ModLog.Debug(DescribeProviders(registry));
            return registry;
        }

        private static string DescribeProviders(IInteropRegistry registry)
        {
            IReadOnlyList<IInteropProvider> providers = registry.Providers;
            var text = new System.Text.StringBuilder("Interop 提供方：");
            for (int i = 0; i < providers.Count; i++)
            {
                IInteropProvider provider = providers[i];
                if (i > 0)
                {
                    text.Append("；");
                }

                text.Append(provider.Name)
                    .Append(provider.IsAvailable ? "=可用" : "=不可用")
                    .Append("，能力=[")
                    .Append(InteropDiagnostics.FormatCapabilities(provider.Capabilities))
                    .Append(']');
            }

            return text.ToString();
        }

        internal static IInteropRegistry Current => _registry ?? NullInteropRegistry.Instance;
    }
}
