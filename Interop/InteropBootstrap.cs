// SkinSync Interop
// Copyright (c) 2026 Bytechey
// SPDX-License-Identifier: MIT

using System.Collections.Generic;

namespace SkinSyncMod.Interop
{
    /// <summary>探测目标插件、登记提供方并输出一次自检摘要，重复调用幂等。</summary>
    /// <remarks>
    /// 本 mod 只登记 TogetherProvider；追加其他提供方时在此登记，并把 RequiredCapabilities 改成本 mod 实际消费的能力位。
    /// </remarks>
    internal static class InteropBootstrap
    {
        /// <summary>SkinSync 消费的能力位。</summary>
        private const InteropCapability RequiredCapabilities =
            InteropCapability.Session
            | InteropCapability.Roster
            | InteropCapability.Channel
            | InteropCapability.Events
            | InteropCapability.TypeRegistry;

        private static IInteropRegistry _registry;

        /// <summary>首次调用时创建登记表、登记提供方并输出自检摘要；后续调用返回同一实例。</summary>
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

        /// <summary>Debug 构建用：逐个提供方输出可用性与声明的全部能力位。</summary>
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

        /// <summary>已初始化的登记表；未初始化时返回空实现。</summary>
        internal static IInteropRegistry Current => _registry ?? NullInteropRegistry.Instance;
    }
}
