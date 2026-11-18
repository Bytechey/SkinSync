// SkinSync Interop
// Copyright (c) 2026 Bytechey
// SPDX-License-Identifier: MIT

using System;

namespace SkinSyncMod.Interop.Consumers
{
    /// <summary>查询联机模组可用性与当前网络会话状态。</summary>
    internal static class SessionConsumer
    {
        internal static bool IsAvailable => InteropBootstrap.Initialize().Find(InteropCapability.Session) != null;

        internal static bool IsRunning => InteropBootstrap.Initialize().Session.Info.IsRunning;

        internal static bool IsServer => InteropBootstrap.Initialize().Session.Info.IsServer;

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
