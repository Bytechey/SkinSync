// SkinSync Interop
// Copyright (c) 2026 Bytechey
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace SkinSyncMod.Interop
{
    /// <summary>提供互操作层的去重警告与启动自检日志输出。</summary>
    internal static class InteropDiagnostics
    {
        private const string Prefix = "[Interop]";

        private static readonly HashSet<string> WarnedKeys = new HashSet<string>(StringComparer.Ordinal);

        private static readonly object WarnedKeysSync = new object();

        private static readonly InteropCapability[] OrderedCapabilities =
        {
            InteropCapability.Session,
            InteropCapability.Roster,
            InteropCapability.Channel,
            InteropCapability.Events,
            InteropCapability.TypeRegistry,
            InteropCapability.MemberAccess,
            InteropCapability.RawPacketChannel,
            InteropCapability.MethodInterception,
            InteropCapability.PatchGate,
            InteropCapability.SaveKey,
            InteropCapability.WorldGenHook,
            InteropCapability.SettingsHub,
        };

        /// <summary>按 key 去重输出单次警告日志。</summary>
        internal static void WarnOnce(string key, string message)
        {
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            lock (WarnedKeysSync)
            {
                if (!WarnedKeys.Add(key))
                {
                    return;
                }
            }

            Debug.LogWarning(Prefix + " " + message);
        }

        /// <summary>输出已注册提供方与能力满足情况的启动摘要。</summary>
        internal static void Report(IInteropRegistry registry, InteropCapability required)
        {
            if (registry == null)
            {
                return;
            }

            IReadOnlyList<IInteropProvider> providers = registry.Providers;
            InteropCapability available = InteropCapability.None;
            StringBuilder providerList = new StringBuilder();
            for (int i = 0; i < providers.Count; i++)
            {
                IInteropProvider provider = providers[i];
                if (provider == null)
                {
                    continue;
                }

                if (providerList.Length > 0)
                {
                    providerList.Append(',');
                }

                bool usable = provider.IsAvailable;
                providerList.Append(provider.Name).Append(usable ? ":ok" : ":off");
                if (usable)
                {
                    available |= provider.Capabilities;
                }
            }

            string line = Prefix
                + " providers=[" + providerList + "]"
                + " capabilities=[" + FormatCapabilities(required & available) + "]"
                + " missing=[" + FormatCapabilities(required & ~available) + "]";
            Debug.Log(line);
        }

        /// <summary>将能力标志位格式化为逗号分隔的名称字符串。</summary>
        internal static string FormatCapabilities(InteropCapability capabilities)
        {
            StringBuilder names = new StringBuilder();
            for (int i = 0; i < OrderedCapabilities.Length; i++)
            {
                InteropCapability capability = OrderedCapabilities[i];
                if ((capabilities & capability) != capability)
                {
                    continue;
                }

                if (names.Length > 0)
                {
                    names.Append(',');
                }

                names.Append(capability.ToString());
            }

            return names.ToString();
        }
    }
}
