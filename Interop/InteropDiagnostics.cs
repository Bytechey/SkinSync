// SkinSync Interop
// Copyright (c) 2026 Bytechey
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace SkinSyncMod.Interop
{
    /// <summary>互操作层的统一日志出口与启动自检摘要。</summary>
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

        /// <summary>同一 key 只输出一次警告，用于抑制重复的反射失败日志。</summary>
        /// <remarks>
        /// 只输出消息本身，不输出异常与堆栈。
        /// 调用点分布在互不互斥的路径上（发送、接收器登记、各提供方自己的解析锁），
        /// 因此键集合的读写必须串行化：HashSet 并发 Add 会破坏内部状态。
        /// 日志输出放在锁外，避免宿主日志回调重入本入口时自锁。
        /// </remarks>
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

        /// <summary>输出一行启动摘要，格式为 [Interop] providers=[...] capabilities=[...] missing=[...]。</summary>
        /// <remarks>
        /// providers 列出全部已登记提供方及可用性（ok / off）；
        /// capabilities 为 required 中已被可用提供方满足的位；
        /// missing 为 required 中未被满足的位。
        /// 能力名按枚举声明顺序输出，逗号分隔，不输出堆栈。
        /// </remarks>
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

        /// <summary>把能力位格式化为逗号分隔的枚举名，按枚举声明顺序；无能力时返回空字符串。</summary>
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
