// SkinSync Interop
// Copyright (c) 2026 Bytechey
// SPDX-License-Identifier: MIT

using System;

namespace SkinSyncMod.Interop.Consumers
{
    /// <summary>查询联机模组可用性与当前网络会话状态。</summary>
    internal static class SessionConsumer
    {
        internal static bool IsAvailable => SkinSync.TogetherInstalled
            && InteropBootstrap.Initialize().Find(InteropCapability.Session) != null;

        internal static bool IsRunning => SkinSync.TogetherInstalled
            && InteropBootstrap.Initialize().Session.Info.IsRunning;

        internal static bool IsServer => SkinSync.TogetherInstalled
            && InteropBootstrap.Initialize().Session.Info.IsServer;

        internal static bool IsSteam
        {
            get
            {
                return SkinSync.TogetherInstalled
                    && string.Equals(
                        InteropBootstrap.Initialize().Session.Info.Transport.Name,
                        "Steam",
                        StringComparison.Ordinal);
            }
        }
    }
}
