// SkinSync Interop
// Copyright (c) 2026 Bytechey
// SPDX-License-Identifier: MIT

using System;

namespace SkinSyncMod.Interop.Consumers
{
    /// <summary>会话消费者：把契约会话状态收敛成 SkinSync 需要的三个判据。</summary>
    /// <remarks>
    /// 目标程序集：CasualtiesMP.dll，BepInPlugin GUID "CasualtiesMP"（Together 5.5.0）。
    /// 依赖的目标成员全名：Together.Net.IsRunning、Together.Net.IsServer、Together.Net.IsRunningSteam。
    /// 三个成员都由提供方在首次访问时解析，本消费者只读契约结果，不接触目标类型。
    /// 传输方式为 best-effort 推断值（"Steam" / "Direct"），只用于诊断，不得据此判定会话有效性。
    /// </remarks>
    internal static class SessionConsumer
    {
        /// <summary>目标插件是否可用；等价于存在可用的会话提供方。</summary>
        internal static bool IsAvailable
        {
            get { return InteropBootstrap.Initialize().Find(InteropCapability.Session) != null; }
        }

        /// <summary>会话是否运行中，对应 Together.Net.IsRunning。</summary>
        internal static bool IsRunning
        {
            get { return InteropBootstrap.Initialize().Session.Info.IsRunning; }
        }

        /// <summary>本端是否为服务端（Host 或专用服务端），对应 Together.Net.IsServer。</summary>
        internal static bool IsServer
        {
            get { return InteropBootstrap.Initialize().Session.Info.IsServer; }
        }

        /// <summary>当前会话是否走 Steam 传输，对应 Together.Net.IsRunningSteam。</summary>
        internal static bool IsSteam
        {
            get
            {
                return string.Equals(
                    InteropBootstrap.Initialize().Session.Info.Transport.Name,
                    "Steam",
                    StringComparison.Ordinal);
            }
        }
    }
}
