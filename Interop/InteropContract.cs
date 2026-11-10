// SkinSync Interop
// Copyright (c) 2026 Bytechey
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using HarmonyLib;

namespace SkinSyncMod.Interop
{
    /// <summary>互操作能力位，用于提供方声明与调用方查询。</summary>
    [Flags]
    internal enum InteropCapability
    {
        /// <summary>未声明任何能力。</summary>
        None = 0,

        /// <summary>会话状态：是否运行、本端是否服务端、传输方式。</summary>
        Session = 1 << 0,

        /// <summary>玩家名册。</summary>
        Roster = 1 << 1,

        /// <summary>具名通道收发。</summary>
        Channel = 1 << 2,

        /// <summary>玩家加入/离开事件。</summary>
        Events = 1 << 3,

        /// <summary>目标程序集类型解析。</summary>
        TypeRegistry = 1 << 4,

        /// <summary>跨程序集成员访问。</summary>
        MemberAccess = 1 << 5,

        /// <summary>自分配消息号的裸通道。</summary>
        RawPacketChannel = 1 << 6,

        /// <summary>方法级拦截。</summary>
        MethodInterception = 1 << 7,

        /// <summary>补丁闸门。</summary>
        PatchGate = 1 << 8,

        /// <summary>存档键。</summary>
        SaveKey = 1 << 9,

        /// <summary>世界生成钩子。</summary>
        WorldGenHook = 1 << 10,

        /// <summary>设置分区登记。</summary>
        SettingsHub = 1 << 11,

        /// <summary>全部能力位。</summary>
        All = Session | Roster | Channel | Events | TypeRegistry | MemberAccess | RawPacketChannel | MethodInterception | PatchGate | SaveKey | WorldGenHook | SettingsHub,
    }

    /// <summary>会话快照。</summary>
    internal readonly struct InteropSessionInfo
    {
        /// <summary>无会话时的快照。</summary>
        internal static readonly InteropSessionInfo None = new InteropSessionInfo(false, false, InteropTransport.None);

        /// <summary>用会话状态构造快照。</summary>
        internal InteropSessionInfo(bool isRunning, bool isServer, InteropTransport transport)
        {
            IsRunning = isRunning;
            IsServer = isServer;
            Transport = transport;
        }

        /// <summary>会话是否运行中。</summary>
        internal bool IsRunning { get; }

        /// <summary>本端是否为服务端。</summary>
        internal bool IsServer { get; }

        /// <summary>当前传输方式。</summary>
        internal InteropTransport Transport { get; }
    }

    /// <summary>传输方式标识。</summary>
    internal readonly struct InteropTransport
    {
        /// <summary>无传输。</summary>
        internal static readonly InteropTransport None = new InteropTransport(string.Empty);

        private readonly bool _present;

        /// <summary>用传输名构造标识，名称为空时等价于 <see cref="None"/>。</summary>
        internal InteropTransport(string name)
        {
            Name = name ?? string.Empty;
            _present = !string.IsNullOrEmpty(Name);
        }

        /// <summary>传输名，例如 "Steam"；无传输时为空字符串。</summary>
        internal string Name { get; }

        /// <summary>是否存在传输。</summary>
        internal bool IsAvailable => _present;

        /// <summary>返回传输名，无传输时返回 "none"。</summary>
        public override string ToString()
        {
            return _present ? Name : "none";
        }
    }

    /// <summary>玩家在目标插件中的身份。</summary>
    internal readonly struct InteropPlayerRef : IEquatable<InteropPlayerRef>
    {
        /// <summary>无玩家时的引用。</summary>
        internal static readonly InteropPlayerRef None = default;

        private readonly bool _present;

        /// <summary>用身份数据构造引用；名称不可用时传 null。</summary>
        internal InteropPlayerRef(ushort clientId, ulong steamId, string name, bool isLocal)
        {
            ClientId = clientId;
            SteamId = steamId;
            Name = name;
            IsLocal = isLocal;
            _present = true;
        }

        /// <summary>目标插件的客户端 ID。</summary>
        internal ushort ClientId { get; }

        /// <summary>Steam ID，非 Steam 传输时为 0。</summary>
        internal ulong SteamId { get; }

        /// <summary>玩家名；提供方拿不到名称时为 null。</summary>
        internal string Name { get; }

        /// <summary>是否为本机玩家。</summary>
        internal bool IsLocal { get; }

        /// <summary>是否指向一个存在的玩家。</summary>
        internal bool IsValid => _present;

        /// <summary>按客户端 ID 与 Steam ID 比较。</summary>
        public bool Equals(InteropPlayerRef other)
        {
            return _present == other._present && ClientId == other.ClientId && SteamId == other.SteamId;
        }

        /// <summary>按客户端 ID 与 Steam ID 比较。</summary>
        public override bool Equals(object obj)
        {
            return obj is InteropPlayerRef other && Equals(other);
        }

        /// <summary>按客户端 ID 与 Steam ID 生成哈希。</summary>
        public override int GetHashCode()
        {
            return ((int)ClientId << 16) ^ SteamId.GetHashCode();
        }

        /// <summary>返回客户端 ID 与 Steam ID，无玩家时返回 "none"。</summary>
        public override string ToString()
        {
            return _present ? "client=" + ClientId + " steam=" + SteamId : "none";
        }
    }

    /// <summary>与传输库无关的写入器；字段布局由本接口固定。</summary>
    internal interface IInteropWriter
    {
        /// <summary>写入 32 位有符号整数。</summary>
        void PutInt(int value);

        /// <summary>写入 32 位无符号整数。</summary>
        void PutUInt(uint value);

        /// <summary>写入 16 位无符号整数。</summary>
        /// <remarks>对应目标传输层的 16 位原语；缺少它会让消费者被迫用 32 位代替，改变线上字节格式。</remarks>
        void PutUShort(ushort value);

        /// <summary>写入布尔值。</summary>
        void PutBool(bool value);

        /// <summary>写入 32 位浮点数。</summary>
        void PutFloat(float value);

        /// <summary>写入字符串。</summary>
        /// <remarks>按单字节字符编码写入，只保证 ASCII 范围；非 ASCII 文本应改用 UTF-8 编码后走 <see cref="PutBytes"/>。</remarks>
        void PutString(string value);

        /// <summary>写入带长度前缀的字节数组。</summary>
        /// <remarks>长度前缀为 2 字节无符号整数，长度上限 65535。</remarks>
        void PutBytes(byte[] value);

        /// <summary>按 x、y 顺序写入两个 32 位浮点数。</summary>
        void PutVector2(float x, float y);

        /// <summary>底层写入器对象，仅供提供方交给目标程序集自己的方法；调用方不应依赖其类型。</summary>
        object NativeHandle { get; }
    }

    /// <summary>与传输库无关的读取器；读取位置由底层读取器维护。</summary>
    internal interface IInteropReader
    {
        /// <summary>读取 32 位有符号整数。</summary>
        int GetInt();

        /// <summary>读取 32 位无符号整数。</summary>
        uint GetUInt();

        /// <summary>读取 16 位无符号整数。</summary>
        /// <remarks>对应目标传输层的 16 位原语；缺少它会让消费者被迫用 32 位代替，改变线上字节格式。</remarks>
        ushort GetUShort();

        /// <summary>读取布尔值。</summary>
        bool GetBool();

        /// <summary>读取 32 位浮点数。</summary>
        float GetFloat();

        /// <summary>读取字符串。</summary>
        /// <remarks>与 <see cref="IInteropWriter.PutString"/> 对应，按单字节字符编码读取。</remarks>
        string GetString();

        /// <summary>读取带长度前缀的字节数组。</summary>
        byte[] GetBytes();

        /// <summary>按 x、y 顺序读取两个 32 位浮点数。</summary>
        (float x, float y) GetVector2();

        /// <summary>剩余可读字节数；底层不提供该值时返回 0。</summary>
        int RemainingBytes { get; }

        /// <summary>底层读取器对象，仅供提供方使用；调用方不应依赖其类型。</summary>
        object NativeHandle { get; }
    }

    /// <summary>提供方描述：名称、可用性、能力位与优先级。</summary>
    internal interface IInteropProvider
    {
        /// <summary>提供方名称，用于日志与诊断。</summary>
        string Name { get; }

        /// <summary>目标插件当前是否可用；不可用的提供方不参与能力解析。</summary>
        bool IsAvailable { get; }

        /// <summary>本提供方声明能提供的能力位。</summary>
        InteropCapability Capabilities { get; }

        /// <summary>能力解析时的优先级，数值大者优先；相同优先级取后登记者。</summary>
        int Priority { get; }
    }

    /// <summary>提供方登记与按能力取用；未实现的能力一律返回空实现。</summary>
    internal interface IInteropRegistry
    {
        /// <summary>登记提供方；按登记顺序保存，不做去重。</summary>
        void Register(IInteropProvider provider);

        /// <summary>已登记的全部提供方，含当前不可用者。</summary>
        IReadOnlyList<IInteropProvider> Providers { get; }

        /// <summary>取声明了指定能力且当前可用的最高优先级提供方；没有时返回 null。</summary>
        IInteropProvider Find(InteropCapability capability);

        /// <summary>会话能力；未登记时返回空实现。</summary>
        IInteropSession Session { get; }

        /// <summary>名册能力；未登记时返回空实现。</summary>
        IInteropRoster Roster { get; }

        /// <summary>具名通道能力；未登记时返回空实现。</summary>
        IInteropChannel Channel { get; }

        /// <summary>事件能力；未登记时返回空实现。</summary>
        IInteropEvents Events { get; }

        /// <summary>类型解析能力；未登记时返回空实现。</summary>
        IInteropTypeRegistry Types { get; }

        /// <summary>成员访问能力；未登记时返回空实现。</summary>
        IInteropMemberAccess Members { get; }

        /// <summary>裸消息号通道能力；未登记时返回空实现。</summary>
        IInteropRawPacketChannel RawPackets { get; }

        /// <summary>方法拦截能力；未登记时返回空实现。</summary>
        IInteropMethodInterception Interception { get; }

        /// <summary>补丁闸门能力；未登记时返回空实现。</summary>
        IInteropPatchGate PatchGate { get; }

        /// <summary>存档键能力；未登记时返回空实现。</summary>
        IInteropSaveKey SaveKey { get; }

        /// <summary>世界生成钩子能力；未登记时返回空实现。</summary>
        IInteropWorldGenHook WorldGen { get; }

        /// <summary>设置分区能力；未登记时返回空实现。</summary>
        IInteropSettingsHub Settings { get; }
    }

    /// <summary>会话状态：是否运行、本端是否服务端、传输方式。</summary>
    internal interface IInteropSession
    {
        /// <summary>当前会话快照；无会话时返回 <see cref="InteropSessionInfo.None"/>。</summary>
        InteropSessionInfo Info { get; }
    }

    /// <summary>玩家名册：本地玩家、按 clientId 查询、枚举、按角色对象反查。</summary>
    internal interface IInteropRoster
    {
        /// <summary>本机玩家；尚未创建时返回 <see cref="InteropPlayerRef.None"/>。</summary>
        InteropPlayerRef LocalPlayer { get; }

        /// <summary>按 clientId 查询玩家。</summary>
        bool TryGetPlayer(ushort clientId, out InteropPlayerRef player);

        /// <summary>当前名册快照；每次调用返回新列表。</summary>
        IReadOnlyList<InteropPlayerRef> GetPlayers();

        /// <summary>按角色对象反查玩家；角色可以是目标插件的角色组件或其身体对象。</summary>
        bool TryGetPlayerByActor(object actor, out InteropPlayerRef player);
    }

    /// <summary>按通道名收发消息；通道名由目标插件的命名写入器解析。</summary>
    internal interface IInteropChannel
    {
        /// <summary>为指定通道创建写入器；不可用时返回丢弃写入的空实现。</summary>
        IInteropWriter CreateWriter(string channel);

        /// <summary>把写入器内容发给服务端；不可用时静默返回。</summary>
        void SendToServer(IInteropWriter writer, bool reliable = true);

        /// <summary>服务端把写入器内容广播给所有客户端；不可用时静默返回。</summary>
        void Broadcast(IInteropWriter writer, bool reliable = true);

        /// <summary>服务端把写入器内容单播给指定 clientId；不可用时静默返回。</summary>
        void SendToClient(ushort clientId, IInteropWriter writer, bool reliable = true);

        /// <summary>登记通道接收器；为 null 的处理器不登记，全部请求都成功才返回 true。</summary>
        bool Register(string channel, Action<InteropPlayerRef, IInteropReader> onServer, Action<IInteropReader> onClient);
    }

    /// <summary>玩家加入/离开事件订阅。</summary>
    internal interface IInteropEvents
    {
        /// <summary>订阅玩家加入；目标插件没有可用事件源时返回 false。</summary>
        bool TrySubscribePlayerJoined(Action<InteropPlayerRef> handler);

        /// <summary>订阅玩家离开；目标插件没有可用事件源时返回 false。</summary>
        bool TrySubscribePlayerLeft(Action<InteropPlayerRef> handler);
    }

    /// <summary>按全名解析目标插件程序集内的类型。</summary>
    internal interface IInteropTypeRegistry
    {
        /// <summary>只查目标程序集，不扫描 AppDomain；名称不带程序集限定。</summary>
        bool TryResolveType(string fullName, out Type type);
    }

    /// <summary>跨程序集成员访问。</summary>
    /// <remarks>
    /// 这是给目标插件私有静态状态用的逃生通道：成员按名称解析，调用方必须自行处理成员缺失。
    /// 重载按「形参个数 + 剥掉 ref/in/out 后的元素类型」匹配，out 形参从 args 数组回读。
    /// 所有方法以返回值表示成功与否，不抛异常；目标插件升级后成员可能消失，调用方不得假设其长期存在。
    /// </remarks>
    internal interface IInteropMemberAccess
    {
        /// <summary>读取静态字段或静态属性。</summary>
        bool TryReadStatic(Type type, string member, out object value);

        /// <summary>写入静态字段或静态属性。</summary>
        bool TryWriteStatic(Type type, string member, object value);

        /// <summary>调用静态方法。</summary>
        bool TryInvokeStatic(Type type, string method, out object result, params object[] args);

        /// <summary>读取实例字段或实例属性。</summary>
        bool TryReadInstance(object target, string member, out object value);

        /// <summary>写入实例字段或实例属性。</summary>
        bool TryWriteInstance(object target, string member, object value);

        /// <summary>调用实例方法。</summary>
        bool TryInvokeInstance(object target, string method, out object result, params object[] args);
    }

    /// <summary>目标插件的 Harmony 标识，用于 <see cref="IInteropPatchGate.SortId"/> 与 Harmony 排序特性。</summary>
    internal static class InteropPatchOrder
    {
        /// <summary>Together 的 BepInEx 插件 GUID 与 Harmony 标识。</summary>
        internal const string Together = "CasualtiesMP";
    }

    /// <summary>自分配消息号的裸通道。</summary>
    internal interface IInteropRawPacketChannel
    {
        /// <summary>按名称解析消息号；未解析时返回 0。</summary>
        int ResolveMessageId(string name);

        /// <summary>为消息号创建写入器；不可用时返回丢弃写入的空实现。</summary>
        IInteropWriter CreateWriter(int messageId);

        /// <summary>把写入器内容发给服务端；不可用时静默返回。</summary>
        void SendToServer(int messageId, IInteropWriter writer, bool reliable);

        /// <summary>服务端把写入器内容广播给所有客户端；不可用时静默返回。</summary>
        void Broadcast(int messageId, IInteropWriter writer, bool reliable);

        /// <summary>登记消息号接收器，回调参数为发送方 clientId 与负载；登记失败返回 false。</summary>
        bool RegisterReceiver(int messageId, Action<uint, IInteropReader> onMessage);
    }

    /// <summary>方法级拦截：替换返回值或在方法前判定。</summary>
    internal interface IInteropMethodInterception
    {
        /// <summary>用 <paramref name="replacement"/> 的返回值替换目标方法。</summary>
        bool TryReplaceMethod(Type type, string method, Func<object[], object> replacement);

        /// <summary>在目标方法前执行 <paramref name="prefix"/>，返回 true 表示短路原方法。</summary>
        bool TryPrefixMethod(Type type, string method, Func<object[], bool> prefix);

        /// <summary>列出该类型上被登记的补丁所有者标识。</summary>
        IReadOnlyList<string> DescribePatchedOwners(Type type);
    }

    /// <summary>补丁闸门：patch 顺序标识、临时屏蔽、按所有者屏蔽。</summary>
    internal interface IInteropPatchGate
    {
        /// <summary>patch 执行顺序标识，数值小者先执行。</summary>
        int Order { get; }

        /// <summary>Harmony 排序标识，对应目标插件的插件 GUID；无目标时为空字符串。</summary>
        string SortId { get; }

        /// <summary>本端 patch 是否处于临时屏蔽状态。</summary>
        bool IsSuppressed { get; }

        /// <summary>临时屏蔽本端 patch，释放返回值后恢复。</summary>
        IDisposable Suppress();

        /// <summary>在外部补丁的 prefix 上再挂 prefix 并短路，释放返回值后移除。</summary>
        IDisposable SuppressOwner(Harmony harmony, string ownerId, string typeName, string methodName);

        /// <summary>查询指定所有者是否已在目标方法上挂过补丁。</summary>
        bool IsOwnerPatched(string ownerId, string typeName, string methodName);
    }

    /// <summary>当前存档命名空间。</summary>
    internal interface IInteropSaveKey
    {
        /// <summary>当前存档的命名空间 key；无存档时为空字符串。</summary>
        string CurrentKey { get; }
    }

    /// <summary>世界生成钩子：种子与生成阶段。</summary>
    internal interface IInteropWorldGenHook
    {
        /// <summary>读取世界种子。</summary>
        bool TryGetSeed(out int seed);

        /// <summary>世界是否正在生成。</summary>
        bool IsGenerating { get; }

        /// <summary>订阅生成阶段变化；参数为 true 表示开始生成。</summary>
        bool TrySubscribeGeneration(Action<bool> handler);
    }

    /// <summary>向宿主设置面板登记设置分区。</summary>
    internal interface IInteropSettingsHub
    {
        /// <summary>登记一个设置分区；<paramref name="draw"/> 负责绘制分区内容。</summary>
        bool TryRegisterSection(string id, string title, Action draw);
    }

    /// <summary>丢弃全部写入的写入器空实现。</summary>
    internal sealed class NullInteropWriter : IInteropWriter
    {
        /// <summary>唯一实例。</summary>
        internal static readonly NullInteropWriter Instance = new NullInteropWriter();

        /// <inheritdoc/>
        object IInteropWriter.NativeHandle => null;

        /// <inheritdoc/>
        void IInteropWriter.PutInt(int value) { }

        /// <inheritdoc/>
        void IInteropWriter.PutUInt(uint value) { }

        /// <inheritdoc/>
        void IInteropWriter.PutUShort(ushort value) { }

        /// <inheritdoc/>
        void IInteropWriter.PutBool(bool value) { }

        /// <inheritdoc/>
        void IInteropWriter.PutFloat(float value) { }

        /// <inheritdoc/>
        void IInteropWriter.PutString(string value) { }

        /// <inheritdoc/>
        void IInteropWriter.PutBytes(byte[] value) { }

        /// <inheritdoc/>
        void IInteropWriter.PutVector2(float x, float y) { }
    }

    /// <summary>返回默认值的读取器空实现。</summary>
    internal sealed class NullInteropReader : IInteropReader
    {
        /// <summary>唯一实例。</summary>
        internal static readonly NullInteropReader Instance = new NullInteropReader();

        /// <inheritdoc/>
        object IInteropReader.NativeHandle => null;

        /// <inheritdoc/>
        int IInteropReader.RemainingBytes => 0;

        /// <inheritdoc/>
        int IInteropReader.GetInt() => 0;

        /// <inheritdoc/>
        uint IInteropReader.GetUInt() => 0;

        /// <inheritdoc/>
        ushort IInteropReader.GetUShort() => 0;

        /// <inheritdoc/>
        bool IInteropReader.GetBool() => false;

        /// <inheritdoc/>
        float IInteropReader.GetFloat() => 0f;

        /// <inheritdoc/>
        string IInteropReader.GetString() => string.Empty;

        /// <inheritdoc/>
        byte[] IInteropReader.GetBytes() => Array.Empty<byte>();

        /// <inheritdoc/>
        (float x, float y) IInteropReader.GetVector2() => (0f, 0f);
    }

    /// <summary>不提供任何能力的提供方空实现。</summary>
    internal sealed class NullInteropProvider : IInteropProvider
    {
        /// <summary>唯一实例。</summary>
        internal static readonly NullInteropProvider Instance = new NullInteropProvider();

        /// <inheritdoc/>
        string IInteropProvider.Name => "none";

        /// <inheritdoc/>
        bool IInteropProvider.IsAvailable => false;

        /// <inheritdoc/>
        InteropCapability IInteropProvider.Capabilities => InteropCapability.None;

        /// <inheritdoc/>
        int IInteropProvider.Priority => 0;
    }

    /// <summary>不登记任何提供方的登记表空实现。</summary>
    internal sealed class NullInteropRegistry : IInteropRegistry
    {
        /// <summary>唯一实例。</summary>
        internal static readonly NullInteropRegistry Instance = new NullInteropRegistry();

        /// <inheritdoc/>
        void IInteropRegistry.Register(IInteropProvider provider) { }

        /// <inheritdoc/>
        IReadOnlyList<IInteropProvider> IInteropRegistry.Providers => Array.Empty<IInteropProvider>();

        /// <inheritdoc/>
        IInteropProvider IInteropRegistry.Find(InteropCapability capability) => null;

        /// <inheritdoc/>
        IInteropSession IInteropRegistry.Session => NullInteropSession.Instance;

        /// <inheritdoc/>
        IInteropRoster IInteropRegistry.Roster => NullInteropRoster.Instance;

        /// <inheritdoc/>
        IInteropChannel IInteropRegistry.Channel => NullInteropChannel.Instance;

        /// <inheritdoc/>
        IInteropEvents IInteropRegistry.Events => NullInteropEvents.Instance;

        /// <inheritdoc/>
        IInteropTypeRegistry IInteropRegistry.Types => NullInteropTypeRegistry.Instance;

        /// <inheritdoc/>
        IInteropMemberAccess IInteropRegistry.Members => NullInteropMemberAccess.Instance;

        /// <inheritdoc/>
        IInteropRawPacketChannel IInteropRegistry.RawPackets => NullInteropRawPacketChannel.Instance;

        /// <inheritdoc/>
        IInteropMethodInterception IInteropRegistry.Interception => NullInteropMethodInterception.Instance;

        /// <inheritdoc/>
        IInteropPatchGate IInteropRegistry.PatchGate => NullInteropPatchGate.Instance;

        /// <inheritdoc/>
        IInteropSaveKey IInteropRegistry.SaveKey => NullInteropSaveKey.Instance;

        /// <inheritdoc/>
        IInteropWorldGenHook IInteropRegistry.WorldGen => NullInteropWorldGenHook.Instance;

        /// <inheritdoc/>
        IInteropSettingsHub IInteropRegistry.Settings => NullInteropSettingsHub.Instance;
    }

    /// <summary>无会话的会话空实现。</summary>
    internal sealed class NullInteropSession : IInteropSession
    {
        /// <summary>唯一实例。</summary>
        internal static readonly NullInteropSession Instance = new NullInteropSession();

        /// <inheritdoc/>
        InteropSessionInfo IInteropSession.Info => InteropSessionInfo.None;
    }

    /// <summary>空名册的空实现。</summary>
    internal sealed class NullInteropRoster : IInteropRoster
    {
        /// <summary>唯一实例。</summary>
        internal static readonly NullInteropRoster Instance = new NullInteropRoster();

        /// <inheritdoc/>
        InteropPlayerRef IInteropRoster.LocalPlayer => InteropPlayerRef.None;

        /// <inheritdoc/>
        bool IInteropRoster.TryGetPlayer(ushort clientId, out InteropPlayerRef player)
        {
            player = InteropPlayerRef.None;
            return false;
        }

        /// <inheritdoc/>
        IReadOnlyList<InteropPlayerRef> IInteropRoster.GetPlayers() => Array.Empty<InteropPlayerRef>();

        /// <inheritdoc/>
        bool IInteropRoster.TryGetPlayerByActor(object actor, out InteropPlayerRef player)
        {
            player = InteropPlayerRef.None;
            return false;
        }
    }

    /// <summary>不建写入器、不发送的通道空实现。</summary>
    internal sealed class NullInteropChannel : IInteropChannel
    {
        /// <summary>唯一实例。</summary>
        internal static readonly NullInteropChannel Instance = new NullInteropChannel();

        /// <inheritdoc/>
        IInteropWriter IInteropChannel.CreateWriter(string channel) => NullInteropWriter.Instance;

        /// <inheritdoc/>
        void IInteropChannel.SendToServer(IInteropWriter writer, bool reliable) { }

        /// <inheritdoc/>
        void IInteropChannel.Broadcast(IInteropWriter writer, bool reliable) { }

        /// <inheritdoc/>
        void IInteropChannel.SendToClient(ushort clientId, IInteropWriter writer, bool reliable) { }

        /// <inheritdoc/>
        bool IInteropChannel.Register(string channel, Action<InteropPlayerRef, IInteropReader> onServer, Action<IInteropReader> onClient) => false;
    }

    /// <summary>没有事件源的事件空实现。</summary>
    internal sealed class NullInteropEvents : IInteropEvents
    {
        /// <summary>唯一实例。</summary>
        internal static readonly NullInteropEvents Instance = new NullInteropEvents();

        /// <inheritdoc/>
        bool IInteropEvents.TrySubscribePlayerJoined(Action<InteropPlayerRef> handler) => false;

        /// <inheritdoc/>
        bool IInteropEvents.TrySubscribePlayerLeft(Action<InteropPlayerRef> handler) => false;
    }

    /// <summary>解析不到任何类型的类型注册表空实现。</summary>
    internal sealed class NullInteropTypeRegistry : IInteropTypeRegistry
    {
        /// <summary>唯一实例。</summary>
        internal static readonly NullInteropTypeRegistry Instance = new NullInteropTypeRegistry();

        /// <inheritdoc/>
        bool IInteropTypeRegistry.TryResolveType(string fullName, out Type type)
        {
            type = null;
            return false;
        }
    }

    /// <summary>拒绝全部成员访问的空实现。</summary>
    internal sealed class NullInteropMemberAccess : IInteropMemberAccess
    {
        /// <summary>唯一实例。</summary>
        internal static readonly NullInteropMemberAccess Instance = new NullInteropMemberAccess();

        /// <inheritdoc/>
        bool IInteropMemberAccess.TryReadStatic(Type type, string member, out object value)
        {
            value = null;
            return false;
        }

        /// <inheritdoc/>
        bool IInteropMemberAccess.TryWriteStatic(Type type, string member, object value) => false;

        /// <inheritdoc/>
        bool IInteropMemberAccess.TryReadInstance(object target, string member, out object value)
        {
            value = null;
            return false;
        }

        /// <inheritdoc/>
        bool IInteropMemberAccess.TryWriteInstance(object target, string member, object value) => false;

        /// <inheritdoc/>
        bool IInteropMemberAccess.TryInvokeStatic(Type type, string method, out object result, params object[] args)
        {
            result = null;
            return false;
        }

        /// <inheritdoc/>
        bool IInteropMemberAccess.TryInvokeInstance(object target, string method, out object result, params object[] args)
        {
            result = null;
            return false;
        }
    }

    /// <summary>不解析消息号、不发送的裸通道空实现。</summary>
    internal sealed class NullInteropRawPacketChannel : IInteropRawPacketChannel
    {
        /// <summary>唯一实例。</summary>
        internal static readonly NullInteropRawPacketChannel Instance = new NullInteropRawPacketChannel();

        /// <inheritdoc/>
        int IInteropRawPacketChannel.ResolveMessageId(string name) => 0;

        /// <inheritdoc/>
        IInteropWriter IInteropRawPacketChannel.CreateWriter(int messageId) => NullInteropWriter.Instance;

        /// <inheritdoc/>
        void IInteropRawPacketChannel.SendToServer(int messageId, IInteropWriter writer, bool reliable) { }

        /// <inheritdoc/>
        void IInteropRawPacketChannel.Broadcast(int messageId, IInteropWriter writer, bool reliable) { }

        /// <inheritdoc/>
        bool IInteropRawPacketChannel.RegisterReceiver(int messageId, Action<uint, IInteropReader> onMessage) => false;
    }

    /// <summary>不做任何拦截的方法拦截空实现。</summary>
    internal sealed class NullInteropMethodInterception : IInteropMethodInterception
    {
        /// <summary>唯一实例。</summary>
        internal static readonly NullInteropMethodInterception Instance = new NullInteropMethodInterception();

        /// <inheritdoc/>
        bool IInteropMethodInterception.TryReplaceMethod(Type type, string method, Func<object[], object> replacement) => false;

        /// <inheritdoc/>
        bool IInteropMethodInterception.TryPrefixMethod(Type type, string method, Func<object[], bool> prefix) => false;

        /// <inheritdoc/>
        IReadOnlyList<string> IInteropMethodInterception.DescribePatchedOwners(Type type) => Array.Empty<string>();
    }

    /// <summary>不屏蔽任何 patch、也不登记任何所有者的补丁闸门空实现。</summary>
    internal sealed class NullInteropPatchGate : IInteropPatchGate
    {
        /// <summary>唯一实例。</summary>
        internal static readonly NullInteropPatchGate Instance = new NullInteropPatchGate();

        private static readonly IDisposable NoopScope = new NullScope();

        /// <inheritdoc/>
        int IInteropPatchGate.Order => 0;

        /// <inheritdoc/>
        string IInteropPatchGate.SortId => string.Empty;

        /// <inheritdoc/>
        bool IInteropPatchGate.IsSuppressed => false;

        /// <inheritdoc/>
        IDisposable IInteropPatchGate.Suppress() => NoopScope;

        /// <inheritdoc/>
        IDisposable IInteropPatchGate.SuppressOwner(Harmony harmony, string ownerId, string typeName, string methodName) => NoopScope;

        /// <inheritdoc/>
        bool IInteropPatchGate.IsOwnerPatched(string ownerId, string typeName, string methodName) => false;

        private sealed class NullScope : IDisposable
        {
            /// <inheritdoc/>
            public void Dispose() { }
        }
    }

    /// <summary>空存档键的空实现。</summary>
    internal sealed class NullInteropSaveKey : IInteropSaveKey
    {
        /// <summary>唯一实例。</summary>
        internal static readonly NullInteropSaveKey Instance = new NullInteropSaveKey();

        /// <inheritdoc/>
        string IInteropSaveKey.CurrentKey => string.Empty;
    }

    /// <summary>不提供种子与生成阶段的钩子空实现。</summary>
    internal sealed class NullInteropWorldGenHook : IInteropWorldGenHook
    {
        /// <summary>唯一实例。</summary>
        internal static readonly NullInteropWorldGenHook Instance = new NullInteropWorldGenHook();

        /// <inheritdoc/>
        bool IInteropWorldGenHook.TryGetSeed(out int seed)
        {
            seed = 0;
            return false;
        }

        /// <inheritdoc/>
        bool IInteropWorldGenHook.IsGenerating => false;

        /// <inheritdoc/>
        bool IInteropWorldGenHook.TrySubscribeGeneration(Action<bool> handler) => false;
    }

    /// <summary>拒绝登记分区的设置面板空实现。</summary>
    internal sealed class NullInteropSettingsHub : IInteropSettingsHub
    {
        /// <summary>唯一实例。</summary>
        internal static readonly NullInteropSettingsHub Instance = new NullInteropSettingsHub();

        /// <inheritdoc/>
        bool IInteropSettingsHub.TryRegisterSection(string id, string title, Action draw) => false;
    }
}
