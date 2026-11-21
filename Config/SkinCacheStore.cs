namespace SkinSyncMod
{
    /// <summary>保留 SteamID 皮肤缓存的轻量内存兼容入口，不再执行整文件同步磁盘重写。</summary>
    public static class SkinCacheStore
    {
        /// <summary>查询指定 SteamID 记录的皮肤标识，当前无读端消费固定返回空串。</summary>
        public static string Get(ulong steamId) => string.Empty;

        /// <summary>空操作兼容入口，避免切换皮肤或联机换肤时触发同步磁盘重写。</summary>
        public static void Set(ulong steamId, string skinId)
        {
        }
    }
}
