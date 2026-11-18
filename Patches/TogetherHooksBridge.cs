using System;
using System.Collections.Generic;
using System.Reflection;
using SkinSyncMod.Interop;
using UnityEngine;

namespace SkinSyncMod.Patches
{
    /// <summary>可选反射对接 Together 官方扩展接口（兼容 Together.Api 与 Together.ModHooks，存量旧版静默跳过）。</summary>
    internal static class TogetherHooksBridge
    {
        private struct BoundEvent
        {
            public EventInfo Event;
            public Delegate Handler;
        }

        private static bool _bindAttempted;
        private static MethodInfo _unregisterInputBlocker;
        private static readonly List<BoundEvent> _boundEvents = new List<BoundEvent>();
        private static readonly List<Delegate> _pinnedDelegates = new List<Delegate>();

        private const string ModId = "com.Bytechey.skinsync";

        /// <summary>尝试解析并挂载可选官方钩子；Together 程序集未就绪时返回 false 供后续重试。</summary>
        internal static bool TryBind()
        {
            if (_bindAttempted) return true;
            if (!Interop.Consumers.SessionConsumer.IsAvailable) return false;
            IInteropRegistry host = InteropBootstrap.Initialize();

            _bindAttempted = true;
            int boundCount = 0;

            if (host.Types.TryResolveType("Together.Api.TogetherPanelApi", out Type panelApiType) && panelApiType != null)
            {
                boundCount += BindPanelApi(panelApiType);
            }

            if (host.Types.TryResolveType("Together.ModHooks", out Type modHooksType) && modHooksType != null)
            {
                boundCount += BindModHooks(modHooksType);
            }

            if (boundCount > 0)
            {
                ModLog.Info($"TogetherHooksBridge: 已可选绑定 {boundCount} 项官方视图/阻塞钩子。");
            }
            else
            {
                ModLog.Debug("TogetherHooksBridge: 当前 CasualtiesMP 未暴露扩展视图钩子，使用本地补丁兼容路径。");
            }

            return true;
        }

        internal static void Dispose()
        {
            for (int i = 0; i < _boundEvents.Count; i++)
            {
                try { _boundEvents[i].Event.RemoveEventHandler(null, _boundEvents[i].Handler); }
                catch { }
            }
            _boundEvents.Clear();

            if (_unregisterInputBlocker != null)
            {
                InteropReflection.TryInvoke(_unregisterInputBlocker, null, new object[] { ModId }, out _);
                _unregisterInputBlocker = null;
            }

            _pinnedDelegates.Clear();
            _bindAttempted = false;
        }

        private static int BindPanelApi(Type panelApiType)
        {
            int count = 0;

            Action<Body, ushort, int, bool> onBody = (body, netBodyId, species, isRebuild) =>
                SkinSync.OnExternalBodyReadyOrRebuilt(body, netBodyId, species, isRebuild);
            if (TrySubscribeStaticEvent(panelApiType, "OnBodyReadyOrRebuilt", onBody))
                count++;

            Action<WoundView, Body, int> onWound = (view, body, _) =>
                WoundViewAttacher.OnTargetApplied(view, body);
            if (TrySubscribeStaticEvent(panelApiType, "OnWoundViewTargetApplied", onWound))
                count++;

            MethodInfo registerBlocker = InteropReflection.FindMethod(
                panelApiType, "RegisterExternalInputBlocker", typeof(string), typeof(Func<bool>));
            if (registerBlocker != null)
            {
                Func<bool> blocker = () => UiBlocker.IsBlocking;
                if (InteropReflection.TryInvoke(registerBlocker, null, new object[] { ModId, blocker }, out _))
                {
                    _pinnedDelegates.Add(blocker);
                    _unregisterInputBlocker = InteropReflection.FindMethod(
                        panelApiType, "UnregisterExternalInputBlocker", typeof(string));
                    count++;
                }
            }

            return count;
        }

        private static int BindModHooks(Type modHooksType)
        {
            int count = 0;

            if (TryBindBodyHook(modHooksType, "OnBodySpawned", isRebuild: false))
                count++;
            if (TryBindBodyHook(modHooksType, "OnBodyMeshRebuilt", isRebuild: true))
                count++;

            Action<WoundView, Body> onWoundChanged = (view, body) =>
                WoundViewAttacher.OnTargetApplied(view, body);
            if (TrySubscribeStaticEvent(modHooksType, "OnWoundViewTargetChanged", onWoundChanged))
                count++;

            return count;
        }

        private static bool TryBindBodyHook(Type hooksType, string eventName, bool isRebuild)
        {
            EventInfo ev = hooksType.GetEvent(eventName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (ev == null || ev.EventHandlerType == null) return false;

            Delegate handler = null;
            if (ev.EventHandlerType == typeof(Action<Body, uint, ushort, bool>))
            {
                handler = (Action<Body, uint, ushort, bool>)((body, netId, _, __) =>
                    SkinSync.OnExternalBodyReadyOrRebuilt(body, netId, body != null ? body.charType : 0, isRebuild));
            }
            else if (ev.EventHandlerType == typeof(Action<uint, GameObject, int>))
            {
                handler = (Action<uint, GameObject, int>)((netId, chara, charType) =>
                {
                    Body body = chara != null ? chara.GetComponentInChildren<Body>(true) : null;
                    SkinSync.OnExternalBodyReadyOrRebuilt(body, netId, charType, isRebuild);
                });
            }

            return handler != null && SubscribeResolvedEvent(ev, handler);
        }

        private static bool TrySubscribeStaticEvent(Type type, string eventName, Delegate handler)
        {
            EventInfo ev = type.GetEvent(eventName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (ev == null || ev.EventHandlerType != handler.GetType()) return false;
            return SubscribeResolvedEvent(ev, handler);
        }

        private static bool SubscribeResolvedEvent(EventInfo ev, Delegate handler)
        {
            try
            {
                ev.AddEventHandler(null, handler);
                _boundEvents.Add(new BoundEvent { Event = ev, Handler = handler });
                _pinnedDelegates.Add(handler);
                return true;
            }
            catch (Exception ex)
            {
                ModLog.Debug($"TogetherHooksBridge: 绑定事件 {ev.Name} 失败：{ex.Message}");
                return false;
            }
        }
    }
}
