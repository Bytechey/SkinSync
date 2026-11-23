using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace SkinSyncMod
{
    internal struct PauseDialMetrics
    {
        internal Vector2 DialCenter;
        internal float DialRadius;
        internal float BaseBtnSize;
        internal float BaseOrbitRadius;
        internal Rect CanvasBoundsInPause;
    }

    internal struct PauseDialPose
    {
        internal Vector2 SteadyLocalPos;
        internal Vector2 DialCenter;
        internal float BaseBtnSize;
        internal float EffectiveScale;
        internal float ResolvedAngleDeg;
    }

    /// <summary>在 PauseMenu 稳态局部空间内采样表盘几何与前序模组障碍物，并求解无震荡环形按钮位姿。</summary>
    internal static class PauseDialLayoutSolver
    {
        internal const float DefaultAngleDeg = 166.5f;
        internal const float MinRadiusOffset = -60f;
        internal const float MaxRadiusOffset = 160f;
        internal const float FallbackDialRadius = 300f;
        internal const float FallbackBtnSize = 120f;

        private const float SnapOrbitThreshold = 14f;
        private const float MinInnerGap = 12f;
        private const float ObstacleClearance = 10f;
        private const float ManualEdgeMargin = 14f;
        private const float AutoAvoidEdgeMargin = 16f;
        private const float AngleStepDeg = 5f;
        private const int TotalScanSteps = 72;

        private static readonly float[] CrowdFactors = { 1.0f, 0.9f, 0.8f, 0.72f };
        private static readonly FieldInfo ButtonPosField = AccessTools.Field(typeof(PauseHandler), "buttonPos");

        private static readonly List<CircleObstacle> Circles = new List<CircleObstacle>(16);
        private static readonly List<Rect> RectObstacles = new List<Rect>(16);
        private static readonly HashSet<RectTransform> KnownButtons = new HashSet<RectTransform>();

        private struct CircleObstacle
        {
            internal Vector2 Center;
            internal float Radius;

            internal CircleObstacle(Vector2 center, float radius)
            {
                Center = center;
                Radius = radius;
            }
        }

        internal static float SnapRadiusOffset(float rawOffset)
        {
            if (float.IsNaN(rawOffset) || float.IsInfinity(rawOffset)) return 0f;
            if (Mathf.Abs(rawOffset) <= SnapOrbitThreshold) return 0f;
            return Mathf.Clamp(rawOffset, MinRadiusOffset, MaxRadiusOffset);
        }

        internal static float NormalizeAngleDeg(float angleDeg)
        {
            if (float.IsNaN(angleDeg) || float.IsInfinity(angleDeg)) return DefaultAngleDeg;
            float wrapped = Mathf.Repeat(angleDeg + 180f, 360f) - 180f;
            return wrapped;
        }

        internal static PauseDialMetrics SampleMetrics(PauseHandler handler)
        {
            var metrics = new PauseDialMetrics
            {
                DialCenter = Vector2.zero,
                DialRadius = FallbackDialRadius,
                BaseBtnSize = FallbackBtnSize,
                BaseOrbitRadius = FallbackDialRadius + FallbackBtnSize * 1.02f,
                CanvasBoundsInPause = FallbackCanvasRect(),
            };

            if (handler == null || handler.pauseContainer == null) return metrics;

            RectTransform pauseRect = handler.pauseContainer.transform as RectTransform;
            if (pauseRect == null) return metrics;

            RectTransform canvasRect = pauseRect.parent as RectTransform;
            Rect canvasLocalRect = GetValidContainerRect(canvasRect);
            Vector2 pauseOriginInCanvas = ComputeChildPivotInParent(pauseRect, canvasLocalRect);
            metrics.CanvasBoundsInPause = new Rect(
                canvasLocalRect.xMin - pauseOriginInCanvas.x,
                canvasLocalRect.yMin - pauseOriginInCanvas.y,
                canvasLocalRect.width,
                canvasLocalRect.height);

            Rect pauseLocalRect = pauseRect.rect;
            if (pauseLocalRect.width <= 1f || pauseLocalRect.height <= 1f)
            {
                pauseLocalRect = metrics.CanvasBoundsInPause;
            }

            if (handler.center != null)
            {
                Rect centerSteady = ComputeSteadyRectInParent(handler.center, pauseLocalRect);
                metrics.DialCenter = centerSteady.center;

                // PauseHandler.Update 在展开期间会缩放 center.localScale，故必须取未缩放的 rect 尺寸。
                float diameter = Mathf.Max(centerSteady.width, centerSteady.height);
                metrics.DialRadius = diameter > 10f ? diameter * 0.5f : FallbackDialRadius;
            }

            if (handler.buttons != null)
            {
                for (int i = 0; i < handler.buttons.Count; i++)
                {
                    RectTransform btn = handler.buttons[i];
                    if (btn == null) continue;
                    float btnSize = ReadUnscaledButtonSize(btn);
                    if (btnSize > 10f)
                    {
                        metrics.BaseBtnSize = btnSize;
                        if (btn.gameObject.activeSelf) break;
                    }
                }
            }

            metrics.BaseOrbitRadius = SampleSteadyOrbitRadius(handler, metrics.DialCenter, metrics.DialRadius, metrics.BaseBtnSize);
            return metrics;
        }

        internal static PauseDialPose Solve(
            PauseHandler handler,
            RectTransform selfRect,
            bool autoAvoid,
            float preferredAngleDeg,
            float radiusOffset,
            float userScale)
        {
            PauseDialMetrics metrics = SampleMetrics(handler);
            float safeUserScale = Mathf.Clamp(float.IsNaN(userScale) ? 1f : userScale, 0.6f, 1.6f);
            float safeOffset = Mathf.Clamp(float.IsNaN(radiusOffset) ? 0f : radiusOffset, MinRadiusOffset, MaxRadiusOffset);
            float targetAngle = NormalizeAngleDeg(preferredAngleDeg);

            if (!autoAvoid || handler == null || handler.pauseContainer == null)
            {
                float candRadius = metrics.BaseBtnSize * 0.5f * safeUserScale;
                float minOrbit = metrics.DialRadius + candRadius + MinInnerGap;
                float orbit = Mathf.Max(minOrbit, metrics.BaseOrbitRadius + safeOffset);
                Vector2 rawPos = PointOnOrbit(metrics.DialCenter, orbit, targetAngle);
                Vector2 clamped = ClampCircleToBounds(rawPos, candRadius, metrics.CanvasBoundsInPause, ManualEdgeMargin);
                return new PauseDialPose
                {
                    SteadyLocalPos = clamped,
                    DialCenter = metrics.DialCenter,
                    BaseBtnSize = metrics.BaseBtnSize,
                    EffectiveScale = safeUserScale,
                    ResolvedAngleDeg = targetAngle,
                };
            }

            CollectObstacles(handler, selfRect, metrics);

            float bestPenetration = float.MaxValue;
            float bestAngleDelta = float.MaxValue;
            Vector2 bestPos = Vector2.zero;
            float bestAngle = targetAngle;
            float fallbackScale = safeUserScale * CrowdFactors[CrowdFactors.Length - 1];

            for (int fi = 0; fi < CrowdFactors.Length; fi++)
            {
                float effScale = safeUserScale * CrowdFactors[fi];
                float candRadius = metrics.BaseBtnSize * 0.5f * effScale;
                float minOrbit = metrics.DialRadius + candRadius + MinInnerGap;
                float orbit = Mathf.Max(minOrbit, metrics.BaseOrbitRadius + safeOffset);

                for (int step = 0; step < TotalScanSteps; step++)
                {
                    float deltaDeg = GetAlternatingDeltaDeg(step);
                    float candAngle = NormalizeAngleDeg(targetAngle + deltaDeg);
                    Vector2 candPos = PointOnOrbit(metrics.DialCenter, orbit, candAngle);
                    float penetration = ComputeMaxPenetration(candPos, candRadius, metrics.CanvasBoundsInPause, AutoAvoidEdgeMargin);

                    if (penetration <= 0.001f)
                    {
                        return new PauseDialPose
                        {
                            SteadyLocalPos = candPos,
                            DialCenter = metrics.DialCenter,
                            BaseBtnSize = metrics.BaseBtnSize,
                            EffectiveScale = effScale,
                            ResolvedAngleDeg = candAngle,
                        };
                    }

                    // 仅在最小拥挤档记录全局穿透最小后备位姿，兼顾偏离角最小原则。
                    if (fi == CrowdFactors.Length - 1)
                    {
                        float absDelta = Mathf.Abs(deltaDeg);
                        if (penetration < bestPenetration - 0.01f
                            || (Mathf.Abs(penetration - bestPenetration) <= 0.01f && absDelta < bestAngleDelta))
                        {
                            bestPenetration = penetration;
                            bestAngleDelta = absDelta;
                            bestPos = candPos;
                            bestAngle = candAngle;
                        }
                    }
                }
            }

            float fallbackRadius = metrics.BaseBtnSize * 0.5f * fallbackScale;
            Vector2 safeFallbackPos = ClampCircleToBounds(bestPos, fallbackRadius, metrics.CanvasBoundsInPause, AutoAvoidEdgeMargin);
            return new PauseDialPose
            {
                SteadyLocalPos = safeFallbackPos,
                DialCenter = metrics.DialCenter,
                BaseBtnSize = metrics.BaseBtnSize,
                EffectiveScale = fallbackScale,
                ResolvedAngleDeg = bestAngle,
            };
        }

        internal static float EaseOutCubic(float t)
        {
            float inv = 1f - t;
            return 1f - inv * inv * inv;
        }

        internal static float EaseInOutCubic(float t)
        {
            if (t < 0.5f) return 4f * t * t * t;
            float inv = -2f * t + 2f;
            return 1f - (inv * inv * inv) * 0.5f;
        }

        internal static float EaseInOutQuint(float t)
        {
            if (t < 0.5f)
            {
                float t2 = t * t;
                return 16f * t2 * t2 * t;
            }
            float inv = -2f * t + 2f;
            float inv2 = inv * inv;
            return 1f - (inv2 * inv2 * inv) * 0.5f;
        }

        private static float SampleSteadyOrbitRadius(PauseHandler handler, Vector2 dialCenter, float dialRadius, float baseBtnSize)
        {
            float fallback = dialRadius + baseBtnSize * 1.02f;
            if (handler.buttons == null || handler.buttons.Count == 0) return fallback;

            List<Vector2> steadyPositions = null;
            try
            {
                steadyPositions = ButtonPosField?.GetValue(handler) as List<Vector2>;
            }
            catch { }

            float sum = 0f;
            int count = 0;

            if (steadyPositions != null && steadyPositions.Count > 0)
            {
                int limit = Mathf.Min(handler.buttons.Count, steadyPositions.Count);
                for (int i = 0; i < limit; i++)
                {
                    RectTransform btn = handler.buttons[i];
                    if (btn == null || !btn.gameObject.activeSelf) continue;
                    float d = Vector2.Distance(steadyPositions[i], dialCenter);
                    if (d > dialRadius * 0.5f)
                    {
                        sum += d;
                        count++;
                    }
                }
            }
            else if (handler.pauseProgress <= 0.0001f || handler.pauseProgress >= 0.999f)
            {
                // 仅在未处于入场/退场位移动画中途时才允许回退读取按钮当前坐标。
                for (int i = 0; i < handler.buttons.Count; i++)
                {
                    RectTransform btn = handler.buttons[i];
                    if (btn == null || !btn.gameObject.activeSelf) continue;
                    float d = Vector2.Distance(btn.anchoredPosition, dialCenter);
                    if (d > dialRadius * 0.5f)
                    {
                        sum += d;
                        count++;
                    }
                }
            }

            return count > 0 ? (sum / count) : fallback;
        }

        private static void CollectObstacles(PauseHandler handler, RectTransform selfRect, PauseDialMetrics metrics)
        {
            Circles.Clear();
            RectObstacles.Clear();
            KnownButtons.Clear();

            Circles.Add(new CircleObstacle(metrics.DialCenter, metrics.DialRadius));

            RectTransform pauseRect = handler.pauseContainer.transform as RectTransform;
            if (pauseRect == null) return;

            List<Vector2> steadyPositions = null;
            try
            {
                steadyPositions = ButtonPosField?.GetValue(handler) as List<Vector2>;
            }
            catch { }

            if (handler.buttons != null)
            {
                for (int i = 0; i < handler.buttons.Count; i++)
                {
                    RectTransform btn = handler.buttons[i];
                    if (btn == null) continue;
                    KnownButtons.Add(btn);
                    if (!btn.gameObject.activeSelf) continue;

                    float btnRadius = ReadUnscaledButtonSize(btn) * 0.5f;
                    if (btnRadius <= 4f) btnRadius = metrics.BaseBtnSize * 0.5f;

                    Vector2 steadyPos;
                    if (steadyPositions != null && i < steadyPositions.Count)
                    {
                        steadyPos = steadyPositions[i];
                    }
                    else
                    {
                        steadyPos = ComputeSteadyRectInParent(btn, metrics.CanvasBoundsInPause).center;
                    }

                    Circles.Add(new CircleObstacle(steadyPos, btnRadius));
                }
            }

            Rect pauseLocalRect = pauseRect.rect;
            if (pauseLocalRect.width <= 1f || pauseLocalRect.height <= 1f)
            {
                pauseLocalRect = metrics.CanvasBoundsInPause;
            }

            RectTransform bgRect = handler.background != null ? handler.background.rectTransform : null;
            float canvasW = metrics.CanvasBoundsInPause.width;
            float canvasH = metrics.CanvasBoundsInPause.height;
            int selfSiblingIdx = selfRect != null ? selfRect.GetSiblingIndex() : -1;

            int pauseChildCount = pauseRect.childCount;
            for (int i = 0; i < pauseChildCount; i++)
            {
                RectTransform child = pauseRect.GetChild(i) as RectTransform;
                if (child == null || !child.gameObject.activeSelf) continue;
                if (child == selfRect || child == handler.center || child == bgRect || KnownButtons.Contains(child)) continue;

                if (IsInjectedPauseDialButton(child.name))
                {
                    // 单向避让：仅把挂载顺序早于自身的其他模组表盘按钮视为已占位的圆形障碍物，杜绝双向互相推挤震荡。
                    if (selfRect == null || child.GetSiblingIndex() >= selfSiblingIdx) continue;
                    Circles.Add(SamplePrecedingDialButtonObstacle(handler, child, pauseLocalRect, metrics));
                    continue;
                }

                if (IsExcludedNodeName(child.name)) continue;

                Rect childSteady = ComputeSteadyRectInParent(child, pauseLocalRect);
                if (childSteady.width <= 2f || childSteady.height <= 2f) continue;
                if (IsFullScreenLayer(childSteady.width, childSteady.height, canvasW, canvasH)) continue;

                if (childSteady.width > childSteady.height * 1.35f || childSteady.height > childSteady.width * 1.35f)
                {
                    RectObstacles.Add(childSteady);
                }
                else
                {
                    Circles.Add(new CircleObstacle(childSteady.center, Mathf.Max(childSteady.width, childSteady.height) * 0.5f));
                }
            }

            // 采集父级 Canvas 下渲染顺序位于 PauseMenu 之上的活跃面板（如 QoL TimerPanel 与 SaveManagerPanel）。
            RectTransform canvasRect = pauseRect.parent as RectTransform;
            if (canvasRect == null) return;

            Rect canvasLocalRect = GetValidContainerRect(canvasRect);
            Vector2 pauseOriginInCanvas = ComputeChildPivotInParent(pauseRect, canvasLocalRect);
            int pauseSiblingIdx = pauseRect.GetSiblingIndex();
            int canvasChildCount = canvasRect.childCount;

            for (int i = pauseSiblingIdx + 1; i < canvasChildCount; i++)
            {
                RectTransform sibling = canvasRect.GetChild(i) as RectTransform;
                if (sibling == null || sibling == pauseRect || !sibling.gameObject.activeSelf) continue;
                if (IsExcludedNodeName(sibling.name)) continue;

                Rect siblingInCanvas = ComputeSteadyRectInParent(sibling, canvasLocalRect);
                if (siblingInCanvas.width <= 2f || siblingInCanvas.height <= 2f) continue;

                if (IsFullScreenLayer(siblingInCanvas.width, siblingInCanvas.height, canvasW, canvasH))
                {
                    CollectNestedPanelsFromFullScreenRoot(sibling, siblingInCanvas, pauseOriginInCanvas, canvasW, canvasH);
                    continue;
                }

                RectObstacles.Add(OffsetRect(siblingInCanvas, -pauseOriginInCanvas));
            }
        }

        private static CircleObstacle SamplePrecedingDialButtonObstacle(
            PauseHandler handler,
            RectTransform child,
            Rect pauseLocalRect,
            PauseDialMetrics metrics)
        {
            Rect childSteady = ComputeSteadyRectInParent(child, pauseLocalRect);
            float baseRadius = Mathf.Max(childSteady.width, childSteady.height) * 0.5f;
            if (baseRadius <= 2f) baseRadius = metrics.BaseBtnSize * 0.5f;

            Vector2 pos = childSteady.center;
            float rawProgress = handler.pauseProgress;
            float estScale = 1f;

            // 展开/收起动画中途将前序按钮当前坐标与缩放逆推回 p=1 稳态值，确保首帧即锁定稳定避让角。
            if (rawProgress > 0.001f && rawProgress < 0.999f)
            {
                float p = Mathf.Clamp01(rawProgress * 1.12f);
                float fx = 0.5f + 0.5f * EaseOutCubic(p);
                float fy = 0.5f + 0.5f * EaseInOutCubic(p);
                pos = new Vector2(
                    metrics.DialCenter.x + (pos.x - metrics.DialCenter.x) / fx,
                    metrics.DialCenter.y + (pos.y - metrics.DialCenter.y) / fy);

                float quint = EaseInOutQuint(p);
                float rawScale = Mathf.Abs(child.localScale.x);
                if (quint >= 0.05f && rawScale > 0.001f)
                {
                    estScale = Mathf.Clamp(rawScale / quint, 0.72f, 1.6f);
                }
            }
            else if (rawProgress >= 0.999f)
            {
                float rawScale = Mathf.Abs(child.localScale.x);
                if (rawScale > 0.05f)
                {
                    estScale = Mathf.Clamp(rawScale, 0.72f, 1.6f);
                }
            }

            float radius = Mathf.Max(baseRadius * 0.72f, baseRadius * estScale);
            return new CircleObstacle(pos, radius);
        }

        private static void CollectNestedPanelsFromFullScreenRoot(
            RectTransform fullScreenRoot,
            Rect rootRectInCanvas,
            Vector2 pauseOriginInCanvas,
            float canvasW,
            float canvasH)
        {
            // 若全屏层本身带有不透明遮罩（如过场黑幕），不再将其当作透明挂点容器拆解。
            Image rootImg = fullScreenRoot.GetComponent<Image>();
            if (rootImg != null && rootImg.enabled && rootImg.color.a > 0.5f) return;

            Rect rootLocalRect = fullScreenRoot.rect;
            if (rootLocalRect.width <= 1f || rootLocalRect.height <= 1f)
            {
                rootLocalRect = new Rect(-rootRectInCanvas.width * 0.5f, -rootRectInCanvas.height * 0.5f,
                    rootRectInCanvas.width, rootRectInCanvas.height);
            }

            Vector2 rootPivotInCanvas = new Vector2(
                rootRectInCanvas.xMin + fullScreenRoot.pivot.x * rootRectInCanvas.width,
                rootRectInCanvas.yMin + fullScreenRoot.pivot.y * rootRectInCanvas.height);

            int subCount = fullScreenRoot.childCount;
            for (int j = 0; j < subCount; j++)
            {
                RectTransform sub = fullScreenRoot.GetChild(j) as RectTransform;
                if (sub == null || !sub.gameObject.activeSelf || IsExcludedNodeName(sub.name)) continue;

                Rect subInRoot = ComputeSteadyRectInParent(sub, rootLocalRect);
                if (subInRoot.width <= 2f || subInRoot.height <= 2f) continue;
                if (IsFullScreenLayer(subInRoot.width, subInRoot.height, canvasW, canvasH)) continue;

                Vector2 offsetToPause = rootPivotInCanvas - pauseOriginInCanvas;
                RectObstacles.Add(OffsetRect(subInRoot, offsetToPause));
            }
        }

        private static bool IsInjectedPauseDialButton(string name)
        {
            return !string.IsNullOrEmpty(name)
                && name.EndsWith("_PauseDialButton", StringComparison.Ordinal);
        }

        private static bool IsExcludedNodeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return name.IndexOf("Blindness", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Tooltip", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("SettingsMenu", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("_MenuButton", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsFullScreenLayer(float width, float height, float canvasW, float canvasH)
        {
            return width >= canvasW * 0.8f && height >= canvasH * 0.8f;
        }

        private static float ReadUnscaledButtonSize(RectTransform rt)
        {
            Rect r = rt.rect;
            float size = Mathf.Max(r.width, r.height);
            if (size <= 1f) size = Mathf.Max(rt.sizeDelta.x, rt.sizeDelta.y);
            return size;
        }

        private static Rect GetValidContainerRect(RectTransform container)
        {
            if (container != null)
            {
                Rect r = container.rect;
                if (r.width > 10f && r.height > 10f) return r;
            }
            return FallbackCanvasRect();
        }

        private static Rect FallbackCanvasRect()
        {
            // 原版 CanvasScaler 采用 1920x1080 且 matchWidthOrHeight=1.0（固定高度 1080，宽度随屏幕比例伸缩）。
            const float refHeight = 1080f;
            float aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : (16f / 9f);
            float refWidth = Mathf.Clamp(refHeight * aspect, 1080f, 5120f);
            return new Rect(-refWidth * 0.5f, -refHeight * 0.5f, refWidth, refHeight);
        }

        /// <summary>在忽略运行期 localScale 动画的前提下，根据 anchorMin/Max、pivot、anchoredPosition 与 rect 计算子节点在父容器局部空间的稳态矩形。</summary>
        private static Rect ComputeSteadyRectInParent(RectTransform child, Rect parentRect)
        {
            Vector2 anchorMinPt = new Vector2(
                parentRect.xMin + child.anchorMin.x * parentRect.width,
                parentRect.yMin + child.anchorMin.y * parentRect.height);
            Vector2 anchorMaxPt = new Vector2(
                parentRect.xMin + child.anchorMax.x * parentRect.width,
                parentRect.yMin + child.anchorMax.y * parentRect.height);
            Vector2 anchorSpan = anchorMaxPt - anchorMinPt;

            Vector2 pivotInParent = new Vector2(
                anchorMinPt.x + child.pivot.x * anchorSpan.x + child.anchoredPosition.x,
                anchorMinPt.y + child.pivot.y * anchorSpan.y + child.anchoredPosition.y);

            Vector2 size = child.rect.size;
            if (size.x <= 0.5f || size.y <= 0.5f)
            {
                size = anchorSpan + child.sizeDelta;
            }
            size.x = Mathf.Max(0f, size.x);
            size.y = Mathf.Max(0f, size.y);

            return new Rect(
                pivotInParent.x - child.pivot.x * size.x,
                pivotInParent.y - child.pivot.y * size.y,
                size.x,
                size.y);
        }

        private static Vector2 ComputeChildPivotInParent(RectTransform child, Rect parentRect)
        {
            Vector2 anchorMinPt = new Vector2(
                parentRect.xMin + child.anchorMin.x * parentRect.width,
                parentRect.yMin + child.anchorMin.y * parentRect.height);
            Vector2 anchorMaxPt = new Vector2(
                parentRect.xMin + child.anchorMax.x * parentRect.width,
                parentRect.yMin + child.anchorMax.y * parentRect.height);
            Vector2 anchorSpan = anchorMaxPt - anchorMinPt;
            return new Vector2(
                anchorMinPt.x + child.pivot.x * anchorSpan.x + child.anchoredPosition.x,
                anchorMinPt.y + child.pivot.y * anchorSpan.y + child.anchoredPosition.y);
        }

        private static Rect OffsetRect(Rect r, Vector2 delta)
        {
            return new Rect(r.x + delta.x, r.y + delta.y, r.width, r.height);
        }

        private static float GetAlternatingDeltaDeg(int step)
        {
            if (step <= 0) return 0f;
            int rank = (step + 1) / 2;
            float sign = (step & 1) == 1 ? -1f : 1f;
            return sign * rank * AngleStepDeg;
        }

        private static Vector2 PointOnOrbit(Vector2 center, float radius, float angleDeg)
        {
            float rad = angleDeg * Mathf.Deg2Rad;
            return new Vector2(
                center.x + Mathf.Cos(rad) * radius,
                center.y + Mathf.Sin(rad) * radius);
        }

        private static Vector2 ClampCircleToBounds(Vector2 pos, float radius, Rect bounds, float edgeMargin)
        {
            float pad = radius + edgeMargin;
            float minX = bounds.xMin + pad;
            float maxX = bounds.xMax - pad;
            float minY = bounds.yMin + pad;
            float maxY = bounds.yMax - pad;

            float x = minX <= maxX ? Mathf.Clamp(pos.x, minX, maxX) : bounds.center.x;
            float y = minY <= maxY ? Mathf.Clamp(pos.y, minY, maxY) : bounds.center.y;
            return new Vector2(x, y);
        }

        private static float ComputeMaxPenetration(Vector2 center, float radius, Rect canvasBounds, float edgeMargin)
        {
            float maxPen = 0f;

            float pad = radius + edgeMargin;
            maxPen = Mathf.Max(maxPen, (canvasBounds.xMin + pad) - center.x);
            maxPen = Mathf.Max(maxPen, center.x - (canvasBounds.xMax - pad));
            maxPen = Mathf.Max(maxPen, (canvasBounds.yMin + pad) - center.y);
            maxPen = Mathf.Max(maxPen, center.y - (canvasBounds.yMax - pad));

            for (int i = 0; i < Circles.Count; i++)
            {
                CircleObstacle c = Circles[i];
                float requiredDist = radius + c.Radius + ObstacleClearance;
                float dist = Vector2.Distance(center, c.Center);
                if (dist < requiredDist)
                {
                    maxPen = Mathf.Max(maxPen, requiredDist - dist);
                }
            }

            for (int i = 0; i < RectObstacles.Count; i++)
            {
                Rect expanded = ExpandRect(RectObstacles[i], ObstacleClearance);
                float pen = CircleRectPenetration(center, radius, expanded);
                if (pen > maxPen)
                {
                    maxPen = pen;
                }
            }

            return maxPen;
        }

        private static Rect ExpandRect(Rect r, float pad)
        {
            return new Rect(r.xMin - pad, r.yMin - pad, r.width + pad * 2f, r.height + pad * 2f);
        }

        private static float CircleRectPenetration(Vector2 center, float radius, Rect rect)
        {
            if (center.x >= rect.xMin && center.x <= rect.xMax && center.y >= rect.yMin && center.y <= rect.yMax)
            {
                float dLeft = center.x - rect.xMin;
                float dRight = rect.xMax - center.x;
                float dBottom = center.y - rect.yMin;
                float dTop = rect.yMax - center.y;
                float distToEdge = Mathf.Min(Mathf.Min(dLeft, dRight), Mathf.Min(dBottom, dTop));
                return radius + distToEdge;
            }

            float closestX = Mathf.Clamp(center.x, rect.xMin, rect.xMax);
            float closestY = Mathf.Clamp(center.y, rect.yMin, rect.yMax);
            float dx = center.x - closestX;
            float dy = center.y - closestY;
            float dist = Mathf.Sqrt(dx * dx + dy * dy);
            return dist < radius ? (radius - dist) : 0f;
        }
    }
}
