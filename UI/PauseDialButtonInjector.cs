using System;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SkinSyncMod
{
    /// <summary>独立运行模式下在局内暂停菜单表盘外围注入像素风皮肤同步按钮，嵌入 CuSaveManager 时保持禁用。</summary>
    internal static class PauseDialButtonInjector
    {
        internal const string InjectedObjectName = "SkinSync_PauseDialButton";

        private const int IconTexSize = 40;
        private const float IconPixelsPerUnit = 100f / 3f;
        private const int GlyphStartCoord = 8;
        private const int GlyphGridSize = 24;

        // 精确复刻原版 pauseMenuSettings 40x40 圆盘上下对称的 20 行左边界跨度表。
        private static readonly int[] CircleOuterLeft =
        {
            15, 12, 10, 9, 7, 6, 5, 4, 4, 3,
            2, 2, 1, 1, 1, 0, 0, 0, 0, 0,
        };

        // 24x24 EXP 兽耳剪影 + 右上星芒点阵（第 0 行映射至高 y=31，第 23 行映射至底部 y=8，保证兽耳与星芒正立不倒）。
        private static readonly string[] SkinSyncGlyphRowsTopDown =
        {
            "........................",
            "..#.........#......#....",
            "..##.......##.....###...",
            "..###.....###....#####..",
            "..####...####.....###...",
            "..#####.#####......#....",
            "..###########...........",
            ".##############.........",
            ".##############.........",
            ".####KK###KK####........",
            ".####KK###KK####........",
            ".####KK###KK####........",
            ".###############........",
            "################........",
            ".######KK######.........",
            "..############..........",
            "...##########...........",
            ".....######.............",
            "....########............",
            "...##########...........",
            "...##########...........",
            "..############..........",
            "........................",
            "........................",
        };

        private static bool _enabled;
        private static SkinSyncSettings _cfg;
        private static ConfigFile _rawConfig;
        private static Action _onClick;

        private static PauseHandler _currentHandler;
        private static GameObject _injectedObject;
        private static UITooltip _tooltip;
        private static Texture2D _iconTexture;
        private static Sprite _iconSprite;

        /// <summary>当前关卡暂停容器内是否已在独立模式下挂载表盘按钮。</summary>
        internal static bool IsMounted =>
            _enabled
            && _injectedObject != null
            && _currentHandler != null
            && _currentHandler.pauseContainer != null
            && _injectedObject.transform.parent == _currentHandler.pauseContainer.transform;

        internal static void Setup(SkinSyncSettings cfg, ConfigFile rawConfig, Action onClick)
        {
            _cfg = cfg;
            _rawConfig = rawConfig;
            _onClick = onClick;
            _enabled = cfg != null && onClick != null;
            if (!_enabled) return;

            if (PauseHandler.main != null && PauseHandler.main.pauseContainer != null)
            {
                OnPauseHandlerStarted(PauseHandler.main);
            }
        }

        internal static void Dispose()
        {
            _enabled = false;

            if (_injectedObject != null)
            {
                var beh = _injectedObject.GetComponent<PauseDialButtonBehaviour>();
                if (beh != null) beh.FlushPendingConfigImmediately();
                try { UnityEngine.Object.Destroy(_injectedObject); } catch { }
            }

            if (_iconSprite != null)
            {
                try { UnityEngine.Object.Destroy(_iconSprite); } catch { }
            }

            if (_iconTexture != null)
            {
                try { UnityEngine.Object.Destroy(_iconTexture); } catch { }
            }

            _injectedObject = null;
            _tooltip = null;
            _currentHandler = null;
            _iconSprite = null;
            _iconTexture = null;
            _onClick = null;
            _cfg = null;
            _rawConfig = null;
        }

        internal static void RefreshTooltipText()
        {
            if (_tooltip == null) return;
            _tooltip.skipLocale = true;
            _tooltip.localeName = string.Empty;
            _tooltip.localeDesc = string.Empty;
            _tooltip.tipName = SkinSyncI18n.T("app.menu_button");
            _tooltip.tipDesc = SkinSyncI18n.T("tip.pause_dial_desc");
        }

        internal static void OnPauseHandlerStarted(PauseHandler handler)
        {
            if (!_enabled) return;

            try
            {
                InjectOnce(handler);
            }
            catch (Exception ex)
            {
                ModLog.Warning($"暂停表盘按钮注入失败：{ex.Message}");
            }
        }

        internal static void NotifyClicked()
        {
            try
            {
                _onClick?.Invoke();
            }
            catch (Exception ex)
            {
                ModLog.Warning($"暂停表盘按钮点击异常：{ex.Message}");
            }
        }

        private static void InjectOnce(PauseHandler handler)
        {
            if (!_enabled) return;
            if (handler == null || handler.pauseContainer == null) return;

            Transform parent = handler.pauseContainer.transform;
            Transform existing = parent.Find(InjectedObjectName);
            if (existing != null)
            {
                _currentHandler = handler;
                _injectedObject = existing.gameObject;
                _tooltip = _injectedObject.GetComponent<UITooltip>();
                var existingBeh = _injectedObject.GetComponent<PauseDialButtonBehaviour>();
                if (existingBeh != null)
                {
                    existingBeh.Bind(handler, _cfg, _rawConfig);
                }
                RefreshTooltipText();
                return;
            }

            // 独立挂载到 pauseContainer 下而不写入 handler.buttons，规避原版 PauseHandler.Update 对额外按钮不 Clamp01 造成的 1.256x 坐标过冲。
            var go = new GameObject(
                InjectedObjectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button),
                typeof(UITooltip),
                typeof(PauseDialButtonBehaviour));

            go.layer = handler.pauseContainer.layer;
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, worldPositionStays: false);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(PauseDialLayoutSolver.FallbackBtnSize, PauseDialLayoutSolver.FallbackBtnSize);
            rt.localRotation = Quaternion.identity;
            rt.localScale = Vector3.zero;
            rt.SetAsLastSibling();

            var img = go.GetComponent<Image>();
            img.sprite = GetOrCreateIconSprite();
            img.type = Image.Type.Simple;
            img.preserveAspect = true;
            img.raycastTarget = true;
            img.color = Color.white;

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.ColorTint;
            btn.colors = ResolveVanillaButtonColors(handler);
            btn.navigation = new Navigation { mode = Navigation.Mode.None };

            var tooltip = go.GetComponent<UITooltip>();
            _tooltip = tooltip;
            RefreshTooltipText();

            var beh = go.GetComponent<PauseDialButtonBehaviour>();
            beh.Bind(handler, _cfg, _rawConfig);
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(beh.HandleLeftClick);

            _currentHandler = handler;
            _injectedObject = go;
            beh.SyncImmediate();

            ModLog.Info($"暂停表盘按钮已挂载：parent={parent.name} sibling={rt.GetSiblingIndex()}");
        }

        private static ColorBlock ResolveVanillaButtonColors(PauseHandler handler)
        {
            if (handler.buttons != null)
            {
                for (int i = 0; i < handler.buttons.Count; i++)
                {
                    RectTransform candidate = handler.buttons[i];
                    if (candidate == null) continue;
                    Button refBtn = candidate.GetComponent<Button>();
                    if (refBtn != null) return refBtn.colors;
                }
            }

            return new ColorBlock
            {
                normalColor = Color.white,
                highlightedColor = new Color(0.6887f, 0.6887f, 0.6887f, 1f),
                pressedColor = new Color(0.4151f, 0.4151f, 0.4151f, 1f),
                selectedColor = new Color(0.9608f, 0.9608f, 0.9608f, 1f),
                disabledColor = new Color(0.7843f, 0.7843f, 0.7843f, 0.502f),
                colorMultiplier = 1f,
                fadeDuration = 0f,
            };
        }

        private static Sprite GetOrCreateIconSprite()
        {
            if (_iconSprite != null && _iconTexture != null) return _iconSprite;

            var tex = new Texture2D(IconTexSize, IconTexSize, TextureFormat.RGBA32, mipChain: false)
            {
                name = "SkinSync_PauseDialIcon",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                // 避免跨关卡 Resources.UnloadUnusedAssets 回收运行期生成的常驻贴图。
                hideFlags = HideFlags.HideAndDontSave,
            };

            var pixels = new Color32[IconTexSize * IconTexSize];
            var clear = new Color32(0, 0, 0, 0);
            var black = new Color32(0, 0, 0, 255);
            var white = new Color32(255, 255, 255, 255);

            for (int y = 0; y < IconTexSize; y++)
            {
                for (int x = 0; x < IconTexSize; x++)
                {
                    int idx = y * IconTexSize + x;
                    if (!IsInsideCircle(x, y))
                    {
                        pixels[idx] = clear;
                        continue;
                    }

                    bool isBorder = !IsInsideCircle(x - 1, y)
                        || !IsInsideCircle(x + 1, y)
                        || !IsInsideCircle(x, y - 1)
                        || !IsInsideCircle(x, y + 1);
                    pixels[idx] = isBorder ? white : black;
                }
            }

            // Texture2D 原点 (0,0) 在左下角，故用 y = startY + (h - 1 - ry) 翻转行序，使兽耳与星芒正立朝上。
            int h = GlyphGridSize;
            for (int ry = 0; ry < SkinSyncGlyphRowsTopDown.Length && ry < h; ry++)
            {
                string line = SkinSyncGlyphRowsTopDown[ry];
                int y = GlyphStartCoord + (h - 1 - ry);
                for (int rx = 0; rx < line.Length && rx < GlyphGridSize; rx++)
                {
                    char ch = line[rx];
                    int x = GlyphStartCoord + rx;
                    if (ch == '#')
                    {
                        pixels[y * IconTexSize + x] = white;
                    }
                    else if (ch == 'K')
                    {
                        pixels[y * IconTexSize + x] = black;
                    }
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, false);

            var sprite = Sprite.Create(
                tex,
                new Rect(0f, 0f, IconTexSize, IconTexSize),
                new Vector2(0.5f, 0.5f),
                IconPixelsPerUnit,
                1u,
                SpriteMeshType.FullRect);
            sprite.name = "SkinSync_PauseDialIconSprite";
            sprite.hideFlags = HideFlags.HideAndDontSave;

            _iconTexture = tex;
            _iconSprite = sprite;
            return sprite;
        }

        private static bool IsInsideCircle(int x, int y)
        {
            if (x < 0 || x >= IconTexSize || y < 0 || y >= IconTexSize) return false;
            int symRow = y < 20 ? y : (IconTexSize - 1 - y);
            int left = CircleOuterLeft[symRow];
            int right = IconTexSize - 1 - left;
            return x >= left && x <= right;
        }
    }

    internal sealed class PauseDialButtonBehaviour :
        MonoBehaviour,
        IBeginDragHandler,
        IDragHandler,
        IEndDragHandler,
        IScrollHandler,
        IPointerEnterHandler
    {
        private const float ScrollSaveDebounceSeconds = 0.35f;
        private const int ScaleStepPercent = 5;
        private const int MinScalePercent = 60;
        private const int MaxScalePercent = 160;

        private PauseHandler _handler;
        private SkinSyncSettings _cfg;
        private ConfigFile _rawConfig;
        private RectTransform _rect;
        private Button _button;
        private UITooltip _tooltip;

        private bool _dragging;
        private PointerEventData.InputButton _activeDragButton;
        private float _suppressClickUntil;

        private bool _autoSaveSuspended;
        private bool _savedSaveOnConfigSet = true;
        private bool _configDirty;
        private float _pendingSaveAt;

        internal void Bind(PauseHandler handler, SkinSyncSettings cfg, ConfigFile rawConfig)
        {
            _handler = handler;
            _cfg = cfg;
            _rawConfig = rawConfig;
            _rect = transform as RectTransform;
            _button = GetComponent<Button>();
            _tooltip = GetComponent<UITooltip>();
        }

        internal void SyncImmediate()
        {
            UpdateLayoutAndAnimation();
        }

        internal void FlushPendingConfigImmediately()
        {
            _dragging = false;
            FlushAndRestoreAutoSave();
        }

        internal void HandleLeftClick()
        {
            if (_dragging || Time.unscaledTime < _suppressClickUntil) return;
            if (!CanInteract()) return;

            // PlayerCamera.PlayUISound(string, float) 在原版程序集中为静态方法，需先判空单例再以类型名调用，且零 AudioModule 依赖。
            try
            {
                if (PlayerCamera.main != null)
                {
                    PlayerCamera.PlayUISound("miniClick", 1f);
                }
            }
            catch { }

            PauseDialButtonInjector.NotifyClicked();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_tooltip == null) return;
            _tooltip.skipLocale = true;
            _tooltip.tipName = SkinSyncI18n.T("app.menu_button");
            _tooltip.tipDesc = SkinSyncI18n.T("tip.pause_dial_desc");
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (!CanInteract() || _cfg == null || !IsAllowedDragButton(eventData)) return;

            _dragging = true;
            _activeDragButton = eventData.button;
            _suppressClickUntil = Time.unscaledTime + 0.15f;
            SuspendAutoSaveIfNeeded();
            ApplyDragPointer(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_dragging || eventData == null || eventData.button != _activeDragButton) return;
            if (!CanInteract() || _cfg == null)
            {
                EndDragInternal();
                return;
            }

            _suppressClickUntil = Time.unscaledTime + 0.15f;
            ApplyDragPointer(eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!_dragging) return;
            EndDragInternal();
        }

        public void OnScroll(PointerEventData eventData)
        {
            if (!CanInteract() || _cfg == null || eventData == null) return;
            float scrollY = eventData.scrollDelta.y;
            if (Mathf.Abs(scrollY) < 0.01f) return;

            int delta = scrollY > 0f ? ScaleStepPercent : -ScaleStepPercent;
            int current = _cfg.PauseDialScalePercent.Value;
            int snapped = Mathf.RoundToInt(current / (float)ScaleStepPercent) * ScaleStepPercent + delta;
            if (snapped == current) snapped = current + delta;
            int next = Mathf.Clamp(snapped, MinScalePercent, MaxScalePercent);
            if (next == current) return;

            SuspendAutoSaveIfNeeded();
            _cfg.PauseDialScalePercent.Value = next;
            _configDirty = true;
            _pendingSaveAt = Time.unscaledTime + ScrollSaveDebounceSeconds;
        }

        private void LateUpdate()
        {
            if (!_dragging && _autoSaveSuspended && Time.unscaledTime >= _pendingSaveAt)
            {
                FlushAndRestoreAutoSave();
            }

            UpdateLayoutAndAnimation();
        }

        private void OnDisable()
        {
            _dragging = false;
            FlushAndRestoreAutoSave();
        }

        private void OnDestroy()
        {
            _dragging = false;
            FlushAndRestoreAutoSave();
        }

        private void EndDragInternal()
        {
            _dragging = false;
            _suppressClickUntil = Time.unscaledTime + 0.12f;

            if (_cfg != null && _cfg.PauseDialAutoAvoid.Value && _handler != null && _rect != null)
            {
                float userScale = Mathf.Clamp(_cfg.PauseDialScalePercent.Value, MinScalePercent, MaxScalePercent) * 0.01f;
                PauseDialPose resolved = PauseDialLayoutSolver.Solve(
                    _handler,
                    _rect,
                    autoAvoid: true,
                    _cfg.PauseDialAngleDeg.Value,
                    _cfg.PauseDialRadiusOffset.Value,
                    userScale);
                _cfg.PauseDialAngleDeg.Value = Mathf.Clamp(resolved.ResolvedAngleDeg, -180f, 180f);
                _configDirty = true;
            }

            FlushAndRestoreAutoSave();
        }

        private void ApplyDragPointer(PointerEventData eventData)
        {
            if (_handler == null || _handler.pauseContainer == null || _cfg == null) return;
            RectTransform pauseRect = _handler.pauseContainer.transform as RectTransform;
            if (pauseRect == null) return;

            Camera uiCam = ResolveEventCamera(pauseRect, eventData);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                pauseRect, eventData.position, uiCam, out Vector2 pointerLocal))
            {
                return;
            }

            PauseDialMetrics metrics = PauseDialLayoutSolver.SampleMetrics(_handler);
            Vector2 fromCenter = pointerLocal - metrics.DialCenter;
            if (fromCenter.sqrMagnitude <= 1f) return;

            float angleDeg = Mathf.Atan2(fromCenter.y, fromCenter.x) * Mathf.Rad2Deg;
            float rawOffset = fromCenter.magnitude - metrics.BaseOrbitRadius;
            float snappedOffset = PauseDialLayoutSolver.SnapRadiusOffset(rawOffset);

            SuspendAutoSaveIfNeeded();
            _cfg.PauseDialAngleDeg.Value = Mathf.Clamp(angleDeg, -180f, 180f);
            _cfg.PauseDialRadiusOffset.Value = snappedOffset;
            _configDirty = true;
        }

        private void UpdateLayoutAndAnimation()
        {
            if (_rect == null) _rect = transform as RectTransform;
            if (_rect == null || _handler == null) return;

            if (_button != null)
            {
                _button.interactable = CanInteract();
            }

            float rawProgress = _handler.pauseProgress;
            if (rawProgress <= 0.0001f)
            {
                _rect.localScale = Vector3.zero;
                return;
            }

            bool autoAvoid = _cfg == null || _cfg.PauseDialAutoAvoid.Value;
            float angleDeg = _cfg != null ? _cfg.PauseDialAngleDeg.Value : PauseDialLayoutSolver.DefaultAngleDeg;
            float radiusOffset = _cfg != null ? _cfg.PauseDialRadiusOffset.Value : 0f;
            float userScale = _cfg != null
                ? Mathf.Clamp(_cfg.PauseDialScalePercent.Value, MinScalePercent, MaxScalePercent) * 0.01f
                : 1f;

            PauseDialPose pose = PauseDialLayoutSolver.Solve(_handler, _rect, autoAvoid, angleDeg, radiusOffset, userScale);
            _rect.sizeDelta = new Vector2(pose.BaseBtnSize, pose.BaseBtnSize);

            float p = Mathf.Clamp01(rawProgress * 1.12f);
            float scale = pose.EffectiveScale * PauseDialLayoutSolver.EaseInOutQuint(p);
            _rect.localScale = new Vector3(scale, scale, 1f);

            Vector2 radial = pose.SteadyLocalPos - pose.DialCenter;
            _rect.anchoredPosition = new Vector2(
                pose.DialCenter.x + radial.x * (0.5f + 0.5f * PauseDialLayoutSolver.EaseOutCubic(p)),
                pose.DialCenter.y + radial.y * (0.5f + 0.5f * PauseDialLayoutSolver.EaseInOutCubic(p)));
        }

        private void SuspendAutoSaveIfNeeded()
        {
            if (_rawConfig == null || _autoSaveSuspended) return;
            _savedSaveOnConfigSet = _rawConfig.SaveOnConfigSet;
            _rawConfig.SaveOnConfigSet = false;
            _autoSaveSuspended = true;
        }

        private void FlushAndRestoreAutoSave()
        {
            _pendingSaveAt = 0f;
            if (_rawConfig == null)
            {
                _autoSaveSuspended = false;
                _configDirty = false;
                return;
            }

            try
            {
                if (_autoSaveSuspended)
                {
                    _rawConfig.SaveOnConfigSet = _savedSaveOnConfigSet;
                    _autoSaveSuspended = false;
                }

                if (_configDirty)
                {
                    _configDirty = false;
                    _rawConfig.Save();
                }
            }
            catch (Exception ex)
            {
                ModLog.Warning($"保存暂停表盘配置失败：{ex.Message}");
            }
        }

        private static bool CanInteract()
        {
            return InGameOverlay.IsGamePaused() && SettingsMenu.instance == null && !UiBlocker.IsBlocking;
        }

        private static bool IsAllowedDragButton(PointerEventData eventData)
        {
            if (eventData == null) return false;
            if (eventData.button == PointerEventData.InputButton.Right
                || eventData.button == PointerEventData.InputButton.Middle)
            {
                return true;
            }

            return eventData.button == PointerEventData.InputButton.Left
                && (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt));
        }

        private static Camera ResolveEventCamera(RectTransform target, PointerEventData eventData)
        {
            Canvas canvas = target != null ? target.GetComponentInParent<Canvas>() : null;
            Canvas root = canvas != null ? canvas.rootCanvas : null;
            if (root == null || root.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                return null;
            }
            return root.worldCamera != null ? root.worldCamera : (eventData != null ? eventData.pressEventCamera : null);
        }
    }

    [HarmonyPatch(typeof(PauseHandler), "Start")]
    internal static class SkinSyncPauseHandlerStartPatch
    {
        private static void Postfix(PauseHandler __instance)
        {
            PauseDialButtonInjector.OnPauseHandlerStarted(__instance);
        }
    }
}
