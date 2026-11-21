// SkinSync Interop
// Copyright (c) 2026 Bytechey
// SPDX-License-Identifier: MIT

using System;

namespace SkinSyncMod.Interop
{
    internal sealed partial class TogetherProvider
    {
        private sealed class TogetherWriter : IInteropWriter
        {
            private readonly TogetherProvider _provider;
            private readonly object _native;

            internal TogetherWriter(TogetherProvider provider, object native)
            {
                _provider = provider;
                _native = native;
            }

            object IInteropWriter.NativeHandle => _native;

            void IInteropWriter.PutInt(int value) => _provider.Put(_native, _provider._putInt, value);

            void IInteropWriter.PutUInt(uint value) => _provider.Put(_native, _provider._putUInt, value);

            void IInteropWriter.PutUShort(ushort value) => _provider.PutUShort(_native, value);

            void IInteropWriter.PutBool(bool value) => _provider.Put(_native, _provider._putBool, value);

            void IInteropWriter.PutFloat(float value) => _provider.Put(_native, _provider._putFloat, value);

            void IInteropWriter.PutString(string value) => _provider.PutString(_native, value);

            void IInteropWriter.PutBytes(byte[] value) => _provider.PutBytes(_native, value);

            void IInteropWriter.PutVector2(float x, float y)
            {
                _provider.Put(_native, _provider._putFloat, x);
                _provider.Put(_native, _provider._putFloat, y);
            }
        }

        private sealed class TogetherReader : IInteropReader
        {
            private readonly TogetherProvider _provider;
            private readonly object _native;

            internal TogetherReader(TogetherProvider provider, object native)
            {
                _provider = provider;
                _native = native;
            }

            object IInteropReader.NativeHandle => _native;

            int IInteropReader.RemainingBytes => _provider.ReadRemainingBytes(_native);

            int IInteropReader.GetInt() => _provider.Get(_native, _provider._getInt, 0);

            uint IInteropReader.GetUInt() => _provider.Get(_native, _provider._getUInt, 0u);

            ushort IInteropReader.GetUShort() => _provider.GetUShort(_native);

            bool IInteropReader.GetBool() => _provider.Get(_native, _provider._getBool, false);

            float IInteropReader.GetFloat() => _provider.Get(_native, _provider._getFloat, 0f);

            string IInteropReader.GetString() => _provider.Get(_native, _provider._getString, string.Empty);

            byte[] IInteropReader.GetBytes() => _provider.Get(_native, _provider._getBytes, Array.Empty<byte>());

            (float x, float y) IInteropReader.GetVector2()
            {
                float x = _provider.Get(_native, _provider._getFloat, 0f);
                float y = _provider.Get(_native, _provider._getFloat, 0f);
                return (x, y);
            }
        }

        private sealed class ServerReceiver
        {
            private readonly TogetherProvider _provider;
            private readonly Action<InteropPlayerRef, IInteropReader> _handler;

            internal ServerReceiver(TogetherProvider provider, Action<InteropPlayerRef, IInteropReader> handler)
            {
                _provider = provider;
                _handler = handler;
            }

            private void Invoke(object player, object reader)
            {
                _handler(_provider.ToRef(player, _provider.IsLocalPlayer(player)), new TogetherReader(_provider, reader));
            }
        }

        private sealed class ClientReceiver
        {
            private readonly Action<IInteropReader> _handler;
            private readonly TogetherProvider _provider;

            internal ClientReceiver(TogetherProvider provider, Action<IInteropReader> handler)
            {
                _provider = provider;
                _handler = handler;
            }

            private void Invoke(object reader)
            {
                _handler(new TogetherReader(_provider, reader));
            }
        }

        private sealed class PlayerLeftReceiver
        {
            private readonly TogetherProvider _provider;
            private readonly Action<InteropPlayerRef> _handler;

            internal PlayerLeftReceiver(TogetherProvider provider, Action<InteropPlayerRef> handler)
            {
                _provider = provider;
                _handler = handler;
            }

            private void Invoke(object player)
            {
                _handler(_provider.ToRef(player, _provider.IsLocalPlayer(player)));
            }
        }
    }
}
