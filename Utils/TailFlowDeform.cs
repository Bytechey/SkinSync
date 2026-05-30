using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>
    /// 用 Verlet 物理链替换尾巴 SpriteRenderer，模拟布条 / 关节链摆动；参数从 TailDeformConfig 读取。
    /// </summary>
    [DisallowMultipleComponent]
    public class TailFlowDeform : MonoBehaviour
    {
        private SpriteRenderer _sr;
        private Body _body;
        private GameObject _meshGo;
        private MeshFilter _mf;
        private MeshRenderer _mr;
        private Mesh _mesh;
        private Material _mat;

        private Vector2[] _bonePos;
        private Vector2[] _bonePrev;
        private Vector2[] _smoothPos;
        private float _segLen;
        private bool _physicsInited;
        private float _windPhase;
        private float _smoothBaseAmp;

        private int _cachedSpriteId;
        private Vector3[] _verts;
        private Vector2[] _uvs;
        private int[] _tris;
        private float _axisLen;
        private float _halfWide;
        private float[] _segHalf;
        private float[] _segCenter;

        private void Awake()
        {
            _sr = GetComponent<SpriteRenderer>();
            _body = GetComponentInParent<Body>();
            BuildMeshChild();
        }

        private void BuildMeshChild()
        {
            _meshGo = new GameObject("TailDeformMesh");
            Transform parent = _body != null ? _body.transform : transform;
            _meshGo.transform.SetParent(parent, worldPositionStays: false);
            _meshGo.transform.localPosition = Vector3.zero;
            _meshGo.transform.localRotation = Quaternion.identity;
            _meshGo.transform.localScale = Vector3.one;
            _mf = _meshGo.AddComponent<MeshFilter>();
            _mr = _meshGo.AddComponent<MeshRenderer>();
            var refSr = FindLitMaterialSource();
            _mat = refSr != null && refSr.sharedMaterial != null
                ? new Material(refSr.sharedMaterial)
                : new Material(Shader.Find("Sprites/Default"));
            _mr.sharedMaterial = _mat;
            _mesh = new Mesh { name = "TailDeformMesh" };
            _mesh.MarkDynamic();
            _mf.sharedMesh = _mesh;
            ShadowAttacher.Ensure(_meshGo);
        }

        private SpriteRenderer FindLitMaterialSource()
        {
            if (_body == null || _body.limbs == null) return null;
            for (int i = 0; i < _body.limbs.Length; i++)
            {
                var limb = _body.limbs[i];
                if (limb == null) continue;
                var sr = limb.GetComponent<SpriteRenderer>();
                if (sr != null && sr.sharedMaterial != null) return sr;
            }
            return null;
        }

        /// <summary>
        /// 按当前 sprite 重建 ribbon 拓扑：逐段扫描实际像素求主轴范围与每段半宽 / 垂直中心，UV 贴合内容；扫描失败回退整张 bounds 等宽。
        /// </summary>
        private void RebuildForSprite(Sprite sprite)
        {
            _cachedSpriteId = sprite.GetInstanceID();
            var bounds = sprite.bounds;
            bool axisIsHorizontal = bounds.size.x >= bounds.size.y;
            int n = Mathf.Max(2, TailDeformConfig.Segments);

            int vCount = (n + 1) * 2;
            int tCount = n * 6;
            _verts = new Vector3[vCount];
            _uvs = new Vector2[vCount];
            _tris = new int[tCount];
            _bonePos = new Vector2[n + 1];
            _bonePrev = new Vector2[n + 1];
            _segHalf = new float[n + 1];
            _segCenter = new float[n + 1];

            for (int i = 0; i < n; i++)
            {
                int v0 = i * 2;
                int v1 = i * 2 + 1;
                int v2 = (i + 1) * 2;
                int v3 = (i + 1) * 2 + 1;
                int ti = i * 6;
                _tris[ti + 0] = v0; _tris[ti + 1] = v2; _tris[ti + 2] = v1;
                _tris[ti + 3] = v1; _tris[ti + 4] = v2; _tris[ti + 5] = v3;
            }

            var tex = sprite.texture;
            var texRect = sprite.textureRect;
            if (!BuildProfileFromPixels(sprite, axisIsHorizontal, n))
                BuildFallbackProfile(bounds, axisIsHorizontal, n, tex, texRect);

            _mat.mainTexture = tex;
            _mr.sortingLayerID = _sr.sortingLayerID;
            _mr.sortingOrder = _sr.sortingOrder;
            _mat.color = _sr.color;
            _physicsInited = false;
        }

        /// <summary>逐段扫描贴图像素，填充主轴方向 / 长度、每段半宽 _segHalf、垂直中心 _segCenter 与逐段 UV；纹理不可读返回 false。</summary>
        private bool BuildProfileFromPixels(Sprite sprite, bool axisIsHorizontal, int n)
        {
            var tex = sprite.texture;
            Color32[] pixels;
            try { pixels = tex.GetPixels32(); }
            catch (System.Exception e)
            {
                SkinSyncMod.ModLog.Warning($"tail texture not readable, fallback to bounds ribbon: {e.Message}");
                return false;
            }

            var texRect = sprite.textureRect;
            int rx = Mathf.RoundToInt(texRect.x);
            int ry = Mathf.RoundToInt(texRect.y);
            int rw = Mathf.RoundToInt(texRect.width);
            int rh = Mathf.RoundToInt(texRect.height);
            if (rw <= 0 || rh <= 0) return false;
            int texW = tex.width;
            float texWf = tex.width;
            float texHf = tex.height;
            float ppu = sprite.pixelsPerUnit;

            int axisCount = axisIsHorizontal ? rw : rh;
            int crossCount = axisIsHorizontal ? rh : rw;

            int firstAxis = -1, lastAxis = -1;
            for (int a = 0; a < axisCount; a++)
            {
                if (AxisLineHasPixel(pixels, texW, rx, ry, axisIsHorizontal, a, crossCount))
                {
                    if (firstAxis < 0) firstAxis = a;
                    lastAxis = a;
                }
            }
            if (firstAxis < 0) return false;

            float pivotAxisPx = axisIsHorizontal ? sprite.pivot.x : sprite.pivot.y;
            bool rootAtLow = Mathf.Abs(firstAxis - pivotAxisPx) <= Mathf.Abs(lastAxis - pivotAxisPx);

            int contentLenPx = lastAxis - firstAxis + 1;
            _axisLen = contentLenPx / ppu;
            _segLen = _axisLen / n;

            float pivotCrossPx = axisIsHorizontal ? sprite.pivot.y : sprite.pivot.x;

            for (int i = 0; i <= n; i++)
            {
                float tt = (float)i / n;
                int axisPx = rootAtLow
                    ? firstAxis + Mathf.RoundToInt(tt * (contentLenPx - 1))
                    : lastAxis - Mathf.RoundToInt(tt * (contentLenPx - 1));

                int lo, hi;
                ScanCrossExtent(pixels, texW, rx, ry, axisIsHorizontal, axisPx, crossCount, out lo, out hi);
                if (hi < lo)
                {
                    _segHalf[i] = 0f;
                    _segCenter[i] = 0f;
                }
                else
                {
                    float crossMidPx = (lo + hi + 1) * 0.5f;
                    _segHalf[i] = ((hi - lo + 1) * 0.5f) / ppu;
                    _segCenter[i] = (crossMidPx - pivotCrossPx) / ppu;
                }

                float axisUV, crossLoUV, crossHiUV;
                if (axisIsHorizontal)
                {
                    axisUV = (rx + axisPx + 0.5f) / texWf;
                    crossLoUV = (hi < lo ? ry : ry + lo) / texHf;
                    crossHiUV = (hi < lo ? ry + crossCount : ry + hi + 1) / texHf;
                    _uvs[i * 2 + 0] = new Vector2(axisUV, crossLoUV);
                    _uvs[i * 2 + 1] = new Vector2(axisUV, crossHiUV);
                }
                else
                {
                    axisUV = (ry + axisPx + 0.5f) / texHf;
                    crossLoUV = (hi < lo ? rx : rx + lo) / texWf;
                    crossHiUV = (hi < lo ? rx + crossCount : rx + hi + 1) / texWf;
                    _uvs[i * 2 + 0] = new Vector2(crossLoUV, axisUV);
                    _uvs[i * 2 + 1] = new Vector2(crossHiUV, axisUV);
                }
            }
            return true;
        }

        private static bool AxisLineHasPixel(Color32[] pixels, int texW, int rx, int ry, bool axisIsHorizontal, int a, int crossCount)
        {
            for (int c = 0; c < crossCount; c++)
            {
                int px = axisIsHorizontal ? rx + a : rx + c;
                int py = axisIsHorizontal ? ry + c : ry + a;
                if (pixels[py * texW + px].a > 10) return true;
            }
            return false;
        }

        private static void ScanCrossExtent(Color32[] pixels, int texW, int rx, int ry, bool axisIsHorizontal, int a, int crossCount, out int lo, out int hi)
        {
            lo = int.MaxValue; hi = int.MinValue;
            for (int c = 0; c < crossCount; c++)
            {
                int px = axisIsHorizontal ? rx + a : rx + c;
                int py = axisIsHorizontal ? ry + c : ry + a;
                if (pixels[py * texW + px].a > 10)
                {
                    if (c < lo) lo = c;
                    if (c > hi) hi = c;
                }
            }
            if (hi < lo) { lo = 0; hi = -1; }
        }

        private void BuildFallbackProfile(Bounds bounds, bool axisIsHorizontal, int n, Texture2D tex, Rect texRect)
        {
            if (axisIsHorizontal)
            {
                _axisLen = bounds.extents.x;
                _halfWide = bounds.extents.y;
            }
            else
            {
                _axisLen = bounds.extents.y;
                _halfWide = bounds.extents.x;
            }
            _segLen = _axisLen / n;

            float u0 = texRect.x / tex.width;
            float u1 = (texRect.x + texRect.width) / tex.width;
            float v0n = texRect.y / tex.height;
            float v1n = (texRect.y + texRect.height) / tex.height;
            float uMid = (u0 + u1) * 0.5f;
            float vMid = (v0n + v1n) * 0.5f;
            for (int i = 0; i <= n; i++)
            {
                float t = (float)i / n;
                _segHalf[i] = _halfWide;
                _segCenter[i] = 0f;
                if (axisIsHorizontal)
                {
                    float u = Mathf.Lerp(uMid, u0, t);
                    _uvs[i * 2 + 0] = new Vector2(u, v0n);
                    _uvs[i * 2 + 1] = new Vector2(u, v1n);
                }
                else
                {
                    float v = Mathf.Lerp(vMid, v0n, t);
                    _uvs[i * 2 + 0] = new Vector2(u0, v);
                    _uvs[i * 2 + 1] = new Vector2(u1, v);
                }
            }
        }

        private void InitBonesAt(Vector2 root, Vector2 worldAxisDir)
        {
            int n = _bonePos.Length;
            for (int i = 0; i < n; i++)
            {
                _bonePos[i] = root + worldAxisDir * (_segLen * i);
                _bonePrev[i] = _bonePos[i];
            }
            _physicsInited = true;
        }

        /// <summary>
        /// 每帧步进物理链并写入 ribbon mesh：固定子步迭代 Verlet + 距离 / 角度约束 + 前侧约束 + EMA 平滑。
        /// </summary>
        private void LateUpdate()
        {
            if (_sr == null || _sr.sprite == null) return;

            if (!TailDeformConfig.Enabled)
            {
                if (!_sr.enabled) _sr.enabled = true;
                if (_mr.enabled) _mr.enabled = false;
                return;
            }
            if (!_mr.enabled) _mr.enabled = true;

            int wantSeg = Mathf.Max(2, TailDeformConfig.Segments);
            if (_sr.sprite.GetInstanceID() != _cachedSpriteId || _bonePos == null || _bonePos.Length != wantSeg + 1)
                RebuildForSprite(_sr.sprite);

            if (_sr.enabled) _sr.enabled = false;
            _mat.color = _sr.color;
            _mr.sortingOrder = _sr.sortingOrder;

            Vector2 root = (Vector2)transform.position;
            bool faceRight = _body != null ? _body.isRight : true;
            Vector2 worldAxis = faceRight ? Vector2.left : Vector2.right;

            if (!_physicsInited) InitBonesAt(root, worldAxis);

            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            int n = _bonePos.Length;
            float playerSpeed = (_body != null && _body.rb != null) ? Mathf.Abs(_body.rb.velocity.x) : 0f;
            Vector2 gravity = new Vector2(TailDeformConfig.GravityX, TailDeformConfig.GravityY);
            float baseDamping = Mathf.Clamp01(TailDeformConfig.Damping);
            float speedDamping = Mathf.Max(0f, TailDeformConfig.SpeedDamping);
            float damping = Mathf.Clamp01(baseDamping + Mathf.Min(playerSpeed * speedDamping, 0.5f));
            float maxBendRad = Mathf.Max(0f, TailDeformConfig.MaxBendDeg) * Mathf.Deg2Rad;
            float stiffness = Mathf.Clamp01(TailDeformConfig.Stiffness);
            int iters = Mathf.Max(1, TailDeformConfig.ConstraintIters);
            float maxStep = Mathf.Max(0.001f, TailDeformConfig.MaxStep);
            float smooth = Mathf.Clamp(TailDeformConfig.Smoothness, 0f, 0.99f);

            _bonePos[0] = root;
            _bonePrev[0] = root;
            Vector2 anchor1 = root + worldAxis * _segLen;
            float anchorBlend = Mathf.Clamp01(TailDeformConfig.AnchorFollow);
            _bonePos[1] = Vector2.Lerp(_bonePos[1], anchor1, anchorBlend);
            _bonePrev[1] = Vector2.Lerp(_bonePrev[1], anchor1, anchorBlend);

            Vector2 perpWorld = Vector2.up;
            float windFreq = TailDeformConfig.WindFreq + playerSpeed * 0.15f;
            float baseAmp = TailDeformConfig.WindAmp + playerSpeed * TailDeformConfig.SpeedDisturb;
            _windPhase += dt * windFreq;
            _smoothBaseAmp = Mathf.Lerp(_smoothBaseAmp, baseAmp, 0.2f);

            float maxFixedDt = Mathf.Max(0.005f, TailDeformConfig.MaxFixedDt);
            int subSteps = Mathf.Max(1, Mathf.CeilToInt(dt / maxFixedDt));
            float subDt = dt / subSteps;
            for (int step = 0; step < subSteps; step++)
            {
                for (int i = 2; i < n; i++)
                {
                    Vector2 vel = (_bonePos[i] - _bonePrev[i]) * (1f - damping);
                    if (vel.magnitude > maxStep) vel = vel.normalized * maxStep;
                    _bonePrev[i] = _bonePos[i];
                    Vector2 next = _bonePos[i] + vel + gravity * (subDt * subDt);
                    float t = (float)i / (n - 1);
                    float waveAmp = _smoothBaseAmp * t * t;
                    float wave = Mathf.Sin(_windPhase + t * Mathf.PI * 2f) * waveAmp;
                    next += perpWorld * wave;
                    Vector2 disp = next - _bonePos[i];
                    if (disp.magnitude > maxStep) next = _bonePos[i] + disp.normalized * maxStep;
                    _bonePos[i] = next;
                }

                for (int it = 0; it < iters; it++)
                {
                    for (int i = 1; i < n - 1; i++)
                    {
                        Vector2 prevDir = _bonePos[i] - _bonePos[i - 1];
                        Vector2 nextDir = _bonePos[i + 1] - _bonePos[i];
                        if (prevDir.sqrMagnitude < 1e-6f || nextDir.sqrMagnitude < 1e-6f) continue;
                        float prevAng = Mathf.Atan2(prevDir.y, prevDir.x) * Mathf.Rad2Deg;
                        float nextAng = Mathf.Atan2(nextDir.y, nextDir.x) * Mathf.Rad2Deg;
                        float ang = Mathf.DeltaAngle(prevAng, nextAng) * Mathf.Deg2Rad;
                        if (Mathf.Abs(ang) <= maxBendRad) continue;
                        float clamped = Mathf.Clamp(ang, -maxBendRad, maxBendRad);
                        float c = Mathf.Cos(clamped);
                        float s = Mathf.Sin(clamped);
                        Vector2 baseDir = prevDir.normalized;
                        Vector2 rotated = new Vector2(baseDir.x * c - baseDir.y * s, baseDir.x * s + baseDir.y * c);
                        _bonePos[i + 1] = _bonePos[i] + rotated * nextDir.magnitude;
                    }
                    for (int i = 1; i < n; i++)
                    {
                        Vector2 d = _bonePos[i] - _bonePos[i - 1];
                        float len = d.magnitude;
                        if (len < 1e-5f) { _bonePos[i] = _bonePos[i - 1] + worldAxis * _segLen; continue; }
                        float diff = (len - _segLen) * stiffness;
                        Vector2 fix = d * (diff / len);
                        _bonePos[i] -= fix;
                    }
                }
            }

            if (TailDeformConfig.FrontGuard)
            {
                float margin = TailDeformConfig.FrontGuardMargin;
                for (int i = 2; i < n; i++)
                {
                    Vector2 rel = _bonePos[i] - root;
                    float along = Vector2.Dot(rel, worldAxis);
                    if (along < -margin)
                    {
                        Vector2 perpComp = rel - worldAxis * along;
                        _bonePos[i] = root + worldAxis * (-margin) + perpComp;
                    }
                }
            }

            if (_smoothPos == null || _smoothPos.Length != n)
            {
                _smoothPos = new Vector2[n];
                for (int i = 0; i < n; i++) _smoothPos[i] = _bonePos[i];
            }
            for (int i = 0; i < n; i++)
                _smoothPos[i] = Vector2.Lerp(_bonePos[i], _smoothPos[i], smooth);

            Vector2 axisPerp = new Vector2(-worldAxis.y, worldAxis.x);
            float perpSign = axisPerp.y >= 0f ? 1f : -1f;

            Transform meshTf = _meshGo.transform;
            for (int i = 0; i < n; i++)
            {
                Vector2 dir;
                if (i < n - 1) dir = (_smoothPos[i + 1] - _smoothPos[i]).normalized;
                else dir = (_smoothPos[i] - _smoothPos[i - 1]).normalized;
                if (dir.sqrMagnitude < 1e-6f) dir = worldAxis;
                Vector2 perp = new Vector2(-dir.y, dir.x) * perpSign;
                Vector2 center = _smoothPos[i] + perp * _segCenter[i];
                Vector2 left = center - perp * _segHalf[i];
                Vector2 right = center + perp * _segHalf[i];
                _verts[i * 2 + 0] = meshTf.InverseTransformPoint(left);
                _verts[i * 2 + 1] = meshTf.InverseTransformPoint(right);
            }

            _mesh.Clear();
            _mesh.vertices = _verts;
            _mesh.uv = _uvs;
            _mesh.triangles = _tris;
            _mesh.RecalculateBounds();
        }

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
            if (_mat != null) Destroy(_mat);
            if (_meshGo != null) Destroy(_meshGo);
            if (_sr != null) _sr.enabled = true;
        }
    }
}
