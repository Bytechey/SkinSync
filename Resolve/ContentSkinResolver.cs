using System.Collections.Generic;

namespace SkinSyncMod
{
    /// <summary>
    /// 把消息里的 (皮肤名, 内容哈希) 解析成本地可渲染的皮肤 id。
    /// </summary>
    /// <remarks>
    /// 皮肤名与内容解耦后的核心判定：
    /// 1. 本地有同名皮肤且其内容哈希与广播一致 → 直接用名字（用户自己那份就是对的）。
    /// 2. 本地已持有该哈希的收包 → 用哈希当 id（渲染时由 SkinPathResolver 指向 _received/&lt;hash&gt;）。
    /// 3. 都没有 → 向对端请求皮肤包（同一内容只请求一次）。
    /// 哈希为空说明对方是旧版本，退回纯按名字的行为。
    /// </remarks>
    internal static class ContentSkinResolver
    {
        /// <summary>已请求过的内容哈希，避免同一份内容反复拉包。</summary>
        private static readonly HashSet<string> _requested = new HashSet<string>(System.StringComparer.Ordinal);

        /// <summary>解析本地可用 id；返回 null 表示内容尚未到本地。</summary>
        internal static string ResolveLocal(string name, string hash)
        {
            // 旧版本消息没有哈希，保持原来的按名字行为。
            if (string.IsNullOrEmpty(hash)) return name;

            if (!string.IsNullOrEmpty(name) && SkinPathResolver.HasLocalCustomSkin(name))
            {
                if (string.Equals(SkinPackCodec.ComputeContentHash(name), hash, System.StringComparison.Ordinal))
                {
                    return name;
                }
            }

            if (SkinPackCodec.HasReceivedPack(hash)) return hash;

            RequestPack(name, hash);
            return null;
        }

        /// <summary>请求指定内容的皮肤包；同一哈希只发一次。</summary>
        private static void RequestPack(string name, string hash)
        {
            if (string.IsNullOrEmpty(name)) return;
            if (!_requested.Add(hash)) return;
            Network.MultiplayerSender.SendSkinPackRequest(name);
        }

        /// <summary>皮肤包到达后清掉去重记录，让后续同名请求可以重试。</summary>
        internal static void OnPackArrived(string hash)
        {
            if (!string.IsNullOrEmpty(hash)) _requested.Remove(hash);
        }

        /// <summary>场景切换时清空去重记录。</summary>
        internal static void Reset() => _requested.Clear();
    }
}
