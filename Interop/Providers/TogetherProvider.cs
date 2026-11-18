// SkinSync Interop
// Copyright (c) 2026 Bytechey
// SPDX-License-Identifier: MIT

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace SkinSyncMod.Interop
{
    /// <summary>通过反射对接 CasualtiesMP（Together）联机模组的会话、名册与具名通道。</summary>
    internal sealed partial class TogetherProvider : IInteropProvider, IInteropSession, IInteropRoster, IInteropChannel, IInteropEvents, IInteropTypeRegistry
    {
        private const string TargetAssemblyName = "CasualtiesMP";

        private readonly object _sync = new object();
        private readonly List<Delegate> _callbacks = new List<Delegate>();

        private volatile bool _resolved;
        private volatile bool _available;
        private volatile bool _ushortDegraded;

        private Assembly _assembly;
        private Type _knetidType;
        private Type _writerType;

        private PropertyInfo _netIsRunning;
        private PropertyInfo _netIsServer;
        private PropertyInfo _netIsRunningSteam;
        private MethodInfo _netClientSend;
        private MethodInfo _netServerSendTo;
        private MethodInfo _netServerSendToClients;

        private MethodInfo _createNamedWriter;
        private MethodInfo _registerServerReceiver;
        private MethodInfo _registerClientReceiver;

        private PropertyInfo _netBodyBody;
        private MethodInfo _tryGetPlayerAndNetBody;

        private FieldInfo _scavLocalPlayer;
        private PropertyInfo _scavPlayerId;
        private FieldInfo _scavSteamId;
        private FieldInfo _scavClientIdToPlayerDict;
        private EventInfo _scavPlayerLeft;

        private FieldInfo _knetidId;
        private ConstructorInfo _knetidCtor;

        private MethodInfo _putInt;
        private MethodInfo _putUInt;
        private MethodInfo _putUShort;
        private MethodInfo _putBool;
        private MethodInfo _putFloat;
        private MethodInfo _putString;
        private MethodInfo _putBytes;
        private MethodInfo _getInt;
        private MethodInfo _getUInt;
        private MethodInfo _getUShort;
        private MethodInfo _getBool;
        private MethodInfo _getFloat;
        private MethodInfo _getString;
        private MethodInfo _getBytes;
        private PropertyInfo _readerAvailableBytes;

        private object _deliveryReliable;
        private object _deliveryUnreliable;

        string IInteropProvider.Name => _ushortDegraded ? "Together(ushort降级)" : "Together";

        bool IInteropProvider.IsAvailable => EnsureResolved();

        InteropCapability IInteropProvider.Capabilities =>
            InteropCapability.Session
            | InteropCapability.Roster
            | InteropCapability.Channel
            | InteropCapability.Events
            | InteropCapability.TypeRegistry;

        int IInteropProvider.Priority => 0;

        InteropSessionInfo IInteropSession.Info
        {
            get
            {
                if (!EnsureResolved())
                {
                    return InteropSessionInfo.None;
                }

                bool running = ReadBool(_netIsRunning);
                bool server = ReadBool(_netIsServer);
                InteropTransport transport = running
                    ? new InteropTransport(ReadBool(_netIsRunningSteam) ? "Steam" : "Direct")
                    : InteropTransport.None;
                return new InteropSessionInfo(running, server, transport);
            }
        }

        InteropPlayerRef IInteropRoster.LocalPlayer
        {
            get
            {
                if (!EnsureResolved())
                {
                    return InteropPlayerRef.None;
                }

                object local = InteropReflection.ReadMember(_scavLocalPlayer, null);
                return local == null ? InteropPlayerRef.None : ToRef(local, true);
            }
        }

        bool IInteropRoster.TryGetPlayer(ushort clientId, out InteropPlayerRef player)
        {
            player = InteropPlayerRef.None;
            if (!EnsureResolved() || !TryReadPlayer(clientId, out object raw))
            {
                return false;
            }

            player = ToRef(raw, clientId, IsLocalPlayer(raw));
            return true;
        }

        IReadOnlyList<InteropPlayerRef> IInteropRoster.GetPlayers()
        {
            List<InteropPlayerRef> players = new List<InteropPlayerRef>();
            if (!EnsureResolved())
            {
                return players;
            }

            IDictionary dictionary = ReadClientIdToPlayerDict();
            if (dictionary == null)
            {
                return players;
            }

            object local = InteropReflection.ReadMember(_scavLocalPlayer, null);
            foreach (DictionaryEntry entry in dictionary)
            {
                if (entry.Value == null)
                {
                    continue;
                }

                players.Add(ToRef(entry.Value, ReadClientId(entry.Key), ReferenceEquals(entry.Value, local)));
            }

            return players;
        }

        bool IInteropRoster.TryGetPlayerByActor(object actor, out InteropPlayerRef player)
        {
            player = InteropPlayerRef.None;
            if (actor == null || !EnsureResolved())
            {
                return false;
            }

            IDictionary dictionary = ReadClientIdToPlayerDict();
            if (dictionary == null)
            {
                return false;
            }

            object local = InteropReflection.ReadMember(_scavLocalPlayer, null);
            foreach (DictionaryEntry entry in dictionary)
            {
                object[] args = { entry.Key, null, null };
                if (!InteropReflection.TryInvoke(_tryGetPlayerAndNetBody, null, args, out object result))
                {
                    continue;
                }

                if (!(result is bool found) || !found || args[2] == null)
                {
                    continue;
                }

                if (!ReferenceEquals(args[2], actor) && !ReferenceEquals(InteropReflection.ReadMember(_netBodyBody, args[2]), actor))
                {
                    continue;
                }

                player = ToRef(args[1], ReadClientId(entry.Key), ReferenceEquals(args[1], local));
                return true;
            }

            return false;
        }

        IInteropWriter IInteropChannel.CreateWriter(string channel)
        {
            if (string.IsNullOrEmpty(channel) || !EnsureResolved())
            {
                return NullInteropWriter.Instance;
            }

            if (!InteropReflection.TryInvoke(_createNamedWriter, null, new object[] { channel }, out object writer) || writer == null)
            {
                return NullInteropWriter.Instance;
            }

            return new TogetherWriter(this, writer);
        }

        void IInteropChannel.SendToServer(IInteropWriter writer, bool reliable)
        {
            object native = ReadWriter(writer);
            if (native == null || !EnsureResolved())
            {
                return;
            }

            object delivery = reliable ? _deliveryReliable : _deliveryUnreliable;
            InteropReflection.TryInvoke(_netClientSend, null, new object[] { delivery, native }, out object ignored);
        }

        void IInteropChannel.Broadcast(IInteropWriter writer, bool reliable)
        {
            object native = ReadWriter(writer);
            if (native == null || !EnsureResolved())
            {
                return;
            }

            Array clientIds = BuildClientIdArray();
            if (clientIds == null)
            {
                return;
            }

            object delivery = reliable ? _deliveryReliable : _deliveryUnreliable;
            InteropReflection.TryInvoke(_netServerSendToClients, null, new object[] { delivery, native, clientIds }, out object ignored);
        }

        void IInteropChannel.SendToClient(ushort clientId, IInteropWriter writer, bool reliable)
        {
            object native = ReadWriter(writer);
            if (native == null || !EnsureResolved())
            {
                return;
            }

            object id = MakeKnetid(clientId);
            if (id == null)
            {
                return;
            }

            object delivery = reliable ? _deliveryReliable : _deliveryUnreliable;
            InteropReflection.TryInvoke(_netServerSendTo, null, new object[] { delivery, native, id }, out object ignored);
        }

        bool IInteropChannel.Register(string channel, Action<InteropPlayerRef, IInteropReader> onServer, Action<IInteropReader> onClient)
        {
            if (string.IsNullOrEmpty(channel) || (onServer == null && onClient == null) || !EnsureResolved())
            {
                return false;
            }

            bool registered = true;
            if (onServer != null)
            {
                registered &= RegisterServerReceiver(channel, onServer);
            }

            if (onClient != null)
            {
                registered &= RegisterClientReceiver(channel, onClient);
            }

            return registered;
        }

        bool IInteropEvents.TrySubscribePlayerJoined(Action<InteropPlayerRef> handler)
        {
            return false;
        }

        bool IInteropEvents.TrySubscribePlayerLeft(Action<InteropPlayerRef> handler)
        {
            if (handler == null || !EnsureResolved())
            {
                return false;
            }

            PlayerLeftReceiver receiver = new PlayerLeftReceiver(this, handler);
            MethodInfo callback = InteropReflection.FindMethod(typeof(PlayerLeftReceiver), "Invoke", typeof(object));
            Delegate bound = InteropReflection.BindDelegate(_scavPlayerLeft.EventHandlerType, receiver, callback);
            if (bound == null)
            {
                return false;
            }

            try
            {
                _scavPlayerLeft.AddEventHandler(null, bound);
            }
            catch (Exception)
            {
                return false;
            }

            lock (_sync) { _callbacks.Add(bound); }
            return true;
        }

        bool IInteropTypeRegistry.TryResolveType(string fullName, out Type type)
        {
            type = null;
            EnsureResolved();
            if (_assembly == null)
            {
                return false;
            }

            type = InteropReflection.FindType(_assembly, fullName);
            return type != null;
        }

        /// <summary>解析并缓存目标程序集成员；程序集未加载时不置完成标志以便后续重试。</summary>
        private bool EnsureResolved()
        {
            if (_resolved)
            {
                return _available;
            }

            lock (_sync)
            {
                if (_resolved)
                {
                    return _available;
                }

                Assembly assembly = FindTargetAssembly();
                if (assembly == null)
                {
                    return false;
                }

                List<string> missing = new List<string>();
                _available = Resolve(assembly, missing);
                if (!_available)
                {
                    InteropDiagnostics.WarnOnce("TogetherProvider", "CasualtiesMP 成员缺失：" + string.Join(", ", missing));
                }
                else
                {
                    ReportPrimitiveDegradation();
                    ReportResolvedMembers();
                }

                // 所有成员字段写入完成后再发布标志，保证锁外读取可见。
                _resolved = true;
            }

            return _available;
        }

        /// <summary>清除缓存并重新执行成员反射解析。</summary>
        internal bool TryRefresh()
        {
            lock (_sync)
            {
                _resolved = false;
                _available = false;
            }

            return EnsureResolved();
        }

        private void ReportPrimitiveDegradation()
        {
            _ushortDegraded = _putUShort == null || _getUShort == null;
            if (_putUShort == null)
            {
                InteropDiagnostics.WarnOnce("TogetherProvider.PutUShort", "目标写入器缺少 Put(ushort)：PutUShort 降级为 32 位写入");
            }

            if (_getUShort == null)
            {
                InteropDiagnostics.WarnOnce("TogetherProvider.GetUShort", "目标读取器缺少 GetUShort()：GetUShort 返回 0");
            }
        }

        [System.Diagnostics.Conditional("DEBUG")]
        private void ReportResolvedMembers()
        {
            int count = 0;
            FieldInfo[] fields = typeof(TogetherProvider).GetFields(BindingFlags.Instance | BindingFlags.NonPublic);
            for (int i = 0; i < fields.Length; i++)
            {
                if (typeof(MemberInfo).IsAssignableFrom(fields[i].FieldType) && fields[i].GetValue(this) != null)
                {
                    count++;
                }
            }

            SkinSyncMod.ModLog.Debug("TogetherProvider: 已解析 " + count + " 个成员");
        }

        private bool Resolve(Assembly assembly, List<string> missing)
        {
            _assembly = assembly;
            Type netType = Require(InteropReflection.FindType(_assembly, "Together.Net"), "Together.Net", missing);
            Type multiplayerType = Require(InteropReflection.FindType(_assembly, "Together.Multiplayer"), "Together.Multiplayer", missing);
            Type netBodyType = Require(InteropReflection.FindType(_assembly, "Together.NetBody"), "Together.NetBody", missing);
            Type scavPlayerType = Require(InteropReflection.FindType(_assembly, "Together.ScavPlayer"), "Together.ScavPlayer", missing);
            _knetidType = Require(InteropReflection.FindType(_assembly, "Together.knetid"), "Together.knetid", missing);
            if (netType == null || multiplayerType == null || netBodyType == null || scavPlayerType == null || _knetidType == null)
            {
                return false;
            }

            _createNamedWriter = Require(InteropReflection.FindMethod(multiplayerType, "CreateNamedWriter", typeof(string)), "Together.Multiplayer.CreateNamedWriter(string)", missing);
            if (_createNamedWriter != null)
            {
                _writerType = _createNamedWriter.ReturnType;
            }

            _registerServerReceiver = Require(InteropReflection.FindMethodByCount(multiplayerType, "RegisterCustomServerReceiver", 2), "Together.Multiplayer.RegisterCustomServerReceiver", missing);
            Type readerType = ReceiverReaderType(_registerServerReceiver);
            if (readerType == null)
            {
                missing.Add("Together.Multiplayer.RegisterCustomServerReceiver 的 NetDataReader 形参");
            }
            else
            {
                _registerClientReceiver = Require(InteropReflection.FindMethod(multiplayerType, "RegisterCustomClientReceiver", typeof(string), typeof(Action<>).MakeGenericType(readerType)), "Together.Multiplayer.RegisterCustomClientReceiver(string, Action<NetDataReader>)", missing);
            }

            if (_writerType != null)
            {
                _netClientSend = Require(InteropReflection.FindMethod(netType, "Client_Send", null, _writerType), "Together.Net.Client_Send(DeliveryMethod, NetDataWriter)", missing);
                _netServerSendTo = Require(InteropReflection.FindMethod(netType, "Server_SendTo", null, _writerType, _knetidType), "Together.Net.Server_SendTo(DeliveryMethod, NetDataWriter, knetid)", missing);
                _netServerSendToClients = Require(InteropReflection.FindMethod(netType, "Server_SendToClients", null, _writerType, typeof(IEnumerable<>).MakeGenericType(_knetidType)), "Together.Net.Server_SendToClients(DeliveryMethod, NetDataWriter, IEnumerable<knetid>)", missing);
            }

            Type deliveryType = _netClientSend == null ? null : InteropReflection.StripByRef(_netClientSend.GetParameters()[0].ParameterType);
            _deliveryReliable = InteropReflection.EnumValue(deliveryType, "ReliableOrdered");
            _deliveryUnreliable = InteropReflection.EnumValue(deliveryType, "Unreliable");
            if (_deliveryReliable == null || _deliveryUnreliable == null)
            {
                missing.Add("DeliveryMethod.ReliableOrdered/Unreliable");
            }

            _netIsRunning = Require(InteropReflection.FindProperty(netType, "IsRunning"), "Together.Net.IsRunning", missing);
            _netIsServer = Require(InteropReflection.FindProperty(netType, "IsServer"), "Together.Net.IsServer", missing);
            _netIsRunningSteam = Require(InteropReflection.FindProperty(netType, "IsRunningSteam"), "Together.Net.IsRunningSteam", missing);

            _netBodyBody = Require(InteropReflection.FindProperty(netBodyType, "body"), "Together.NetBody.body", missing);
            _tryGetPlayerAndNetBody = Require(InteropReflection.FindMethod(scavPlayerType, "TryGetPlayerAndNetBodyFromClientId", _knetidType, scavPlayerType, netBodyType), "Together.ScavPlayer.TryGetPlayerAndNetBodyFromClientId(knetid, out ScavPlayer, out NetBody)", missing);

            _scavLocalPlayer = Require(InteropReflection.FindField(scavPlayerType, "LocalPlayer"), "Together.ScavPlayer.LocalPlayer", missing);
            _scavPlayerId = Require(InteropReflection.FindProperty(scavPlayerType, "playerId"), "Together.ScavPlayer.playerId", missing);
            _scavSteamId = Require(InteropReflection.FindField(scavPlayerType, "SteamId"), "Together.ScavPlayer.SteamId", missing);
            _scavClientIdToPlayerDict = Require(InteropReflection.FindField(scavPlayerType, "ClientIdToPlayerDict"), "Together.ScavPlayer.ClientIdToPlayerDict", missing);
            _scavPlayerLeft = Require(InteropReflection.FindEvent(scavPlayerType, "OnPlayerLeft"), "Together.ScavPlayer.OnPlayerLeft", missing);

            _knetidId = Require(InteropReflection.FindField(_knetidType, "id"), "Together.knetid.id", missing);
            _knetidCtor = Require(InteropReflection.FindConstructor(_knetidType, typeof(ushort)), "Together.knetid(ushort)", missing);

            if (_writerType != null)
            {
                _putInt = Require(InteropReflection.FindMethod(_writerType, "Put", typeof(int)), "NetDataWriter.Put(int)", missing);
                _putUInt = Require(InteropReflection.FindMethod(_writerType, "Put", typeof(uint)), "NetDataWriter.Put(uint)", missing);
                _putUShort = InteropReflection.FindMethod(_writerType, "Put", typeof(ushort));
                _putBool = Require(InteropReflection.FindMethod(_writerType, "Put", typeof(bool)), "NetDataWriter.Put(bool)", missing);
                _putFloat = Require(InteropReflection.FindMethod(_writerType, "Put", typeof(float)), "NetDataWriter.Put(float)", missing);
                _putString = Require(InteropReflection.FindMethod(_writerType, "Put", typeof(string), typeof(int)), "NetDataWriter.Put(string, int)", missing);
                _putBytes = Require(InteropReflection.FindMethod(_writerType, "PutBytesWithLength", typeof(byte[])), "NetDataWriter.PutBytesWithLength(byte[])", missing);
            }

            if (readerType != null)
            {
                _getInt = Require(InteropReflection.FindMethod(readerType, "GetInt"), "NetDataReader.GetInt()", missing);
                _getUInt = Require(InteropReflection.FindMethod(readerType, "GetUInt"), "NetDataReader.GetUInt()", missing);
                _getUShort = InteropReflection.FindMethod(readerType, "GetUShort");
                _getBool = Require(InteropReflection.FindMethod(readerType, "GetBool"), "NetDataReader.GetBool()", missing);
                _getFloat = Require(InteropReflection.FindMethod(readerType, "GetFloat"), "NetDataReader.GetFloat()", missing);
                _getString = Require(InteropReflection.FindMethod(readerType, "GetString"), "NetDataReader.GetString()", missing);
                _getBytes = Require(InteropReflection.FindMethod(readerType, "GetBytesWithLength"), "NetDataReader.GetBytesWithLength()", missing);
                _readerAvailableBytes = Require(InteropReflection.FindProperty(readerType, "AvailableBytes"), "NetDataReader.AvailableBytes", missing);
            }

            return missing.Count == 0;
        }

        private static Assembly FindTargetAssembly()
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                if (string.Equals(assemblies[i].GetName().Name, TargetAssemblyName, StringComparison.Ordinal))
                {
                    return assemblies[i];
                }
            }

            return null;
        }

        private static Type ReceiverReaderType(MethodInfo register)
        {
            if (register == null)
            {
                return null;
            }

            ParameterInfo[] parameters = register.GetParameters();
            if (parameters.Length != 2 || !parameters[1].ParameterType.IsGenericType)
            {
                return null;
            }

            Type[] arguments = parameters[1].ParameterType.GetGenericArguments();
            return arguments.Length == 2 ? arguments[1] : null;
        }

        private static T Require<T>(T member, string name, List<string> missing) where T : class
        {
            if (member == null)
            {
                missing.Add(name);
            }

            return member;
        }

        private static bool ReadBool(MemberInfo member)
        {
            object value = InteropReflection.ReadMember(member, null);
            return value is bool flag && flag;
        }

        private IDictionary ReadClientIdToPlayerDict()
        {
            return InteropReflection.ReadMember(_scavClientIdToPlayerDict, null) as IDictionary;
        }

        private bool TryReadPlayer(ushort clientId, out object player)
        {
            player = null;
            IDictionary dictionary = ReadClientIdToPlayerDict();
            if (dictionary == null)
            {
                return false;
            }

            object key = MakeKnetid(clientId);
            if (key == null || !dictionary.Contains(key))
            {
                return false;
            }

            player = dictionary[key];
            return player != null;
        }

        private object MakeKnetid(ushort clientId)
        {
            InteropReflection.TryConstruct(_knetidCtor, new object[] { clientId }, out object knetid);
            return knetid;
        }

        private ushort ReadClientId(object knetid)
        {
            object id = InteropReflection.ReadMember(_knetidId, knetid);
            return id is ushort value ? value : (ushort)0;
        }

        private bool IsLocalPlayer(object player)
        {
            object local = InteropReflection.ReadMember(_scavLocalPlayer, null);
            return local != null && ReferenceEquals(local, player);
        }

        private InteropPlayerRef ToRef(object player, bool isLocal)
        {
            if (player == null || !InteropReflection.TryReadMember(_scavPlayerId, player, out object playerId) || playerId == null)
            {
                return InteropPlayerRef.None;
            }

            return ToRef(player, ReadClientId(playerId), isLocal);
        }

        private InteropPlayerRef ToRef(object player, ushort clientId, bool isLocal)
        {
            object steamId = InteropReflection.ReadMember(_scavSteamId, player);
            return new InteropPlayerRef(clientId, steamId is ulong id ? id : 0UL, null, isLocal);
        }

        private object ReadWriter(IInteropWriter writer)
        {
            if (writer == null)
            {
                return null;
            }

            object native = writer.NativeHandle;
            return native != null && _writerType != null && _writerType.IsInstanceOfType(native) ? native : null;
        }

        private Array BuildClientIdArray()
        {
            IDictionary dictionary = ReadClientIdToPlayerDict();
            if (dictionary == null || dictionary.Count == 0)
            {
                return null;
            }

            Array ids = Array.CreateInstance(_knetidType, dictionary.Count);
            int index = 0;
            foreach (object key in dictionary.Keys)
            {
                ids.SetValue(key, index);
                index++;
            }

            return ids;
        }

        private static Delegate BindCallback(MethodInfo register, object receiver, MethodInfo callback)
        {
            if (register == null || callback == null)
            {
                return null;
            }

            ParameterInfo[] parameters = register.GetParameters();
            if (parameters.Length != 2)
            {
                return null;
            }

            return InteropReflection.BindDelegate(parameters[1].ParameterType, receiver, callback);
        }

        private bool RegisterServerReceiver(string channel, Action<InteropPlayerRef, IInteropReader> handler)
        {
            ServerReceiver receiver = new ServerReceiver(this, handler);
            MethodInfo callback = InteropReflection.FindMethod(typeof(ServerReceiver), "Invoke", typeof(object), typeof(object));
            return InvokeRegister(_registerServerReceiver, channel, BindCallback(_registerServerReceiver, receiver, callback));
        }

        private bool RegisterClientReceiver(string channel, Action<IInteropReader> handler)
        {
            ClientReceiver receiver = new ClientReceiver(this, handler);
            MethodInfo callback = InteropReflection.FindMethod(typeof(ClientReceiver), "Invoke", typeof(object));
            return InvokeRegister(_registerClientReceiver, channel, BindCallback(_registerClientReceiver, receiver, callback));
        }

        private bool InvokeRegister(MethodInfo register, string channel, Delegate callback)
        {
            if (callback == null)
            {
                return false;
            }

            if (!InteropReflection.TryInvoke(register, null, new object[] { channel, callback }, out object ignored))
            {
                InteropDiagnostics.WarnOnce("TogetherProvider.Register." + channel, "通道接收器登记失败：" + channel);
                return false;
            }

            lock (_sync) { _callbacks.Add(callback); }
            return true;
        }

        private void Put(object writer, MethodInfo method, object value)
        {
            InteropReflection.TryInvoke(method, writer, new object[] { value }, out object ignored);
        }

        private void PutUShort(object writer, ushort value)
        {
            if (_putUShort != null)
            {
                Put(writer, _putUShort, value);
                return;
            }

            Put(writer, _putUInt, (uint)value);
        }

        private void PutString(object writer, string value)
        {
            InteropReflection.TryInvoke(_putString, writer, new object[] { value, 0 }, out object ignored);
        }

        private void PutBytes(object writer, byte[] value)
        {
            byte[] safe = value == null || value.Length > ushort.MaxValue ? Array.Empty<byte>() : value;
            InteropReflection.TryInvoke(_putBytes, writer, new object[] { safe }, out object ignored);
        }

        private T Get<T>(object reader, MethodInfo method, T fallback)
        {
            return InteropReflection.TryInvoke(method, reader, Array.Empty<object>(), out object result) && result is T value ? value : fallback;
        }

        private ushort GetUShort(object reader)
        {
            return _getUShort == null ? (ushort)0 : Get(reader, _getUShort, (ushort)0);
        }

        private int ReadRemainingBytes(object reader)
        {
            object value = InteropReflection.ReadMember(_readerAvailableBytes, reader);
            return value is int count ? count : 0;
        }
    }
}
