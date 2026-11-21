using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>根据角色运动状态与朝向实时更新单片翅膀的旋转角度与渲染排序。</summary>
    public class WingScript : MonoBehaviour
    {
        public bool isLeft;
        public bool isLowerWing;
        public float baseAngleDeg = 0f;

        public float frontOffsetZ = -30f;
        public float backOffsetZ = -45f;
        public float frontOffsetX = 0f;
        public float frontOffsetY = 0f;
        public float backOffsetX = 20f;
        public float backOffsetY = 0f;

        public float restAngleDeg = 18f;
        public float maxAngleDeg = 90f;
        public float crouchSpread = 0.30f;
        public float jumpSpread = 0.55f;
        public float fallSpread = 1.0f;
        public float lerpSpeed = 8f;

        public float frontAmplitudeScale = 0.8f;
        public float backAmplitudeScale = 1.0f;

        private Body body;
        private float currentSpread;
        private SpriteRenderer _renderer;
        private int _baseSortingOrder;

        private void Start()
        {
            body = GetComponentInParent<Body>();
            _renderer = GetComponent<SpriteRenderer>();

            if (_renderer != null)
            {
                _baseSortingOrder = _renderer.sortingOrder;
                UpdateSortingOrder();
            }
        }

        private void Update()
        {
            if (body == null) return;

            float target;
            bool grounded = body.grounded;
            float vy = body.rb != null ? body.rb.velocity.y : 0f;

            if (!grounded && vy < -0.1f) target = fallSpread;
            else if (!grounded && vy > 0.1f) target = jumpSpread;
            else if (body.crouching) target = crouchSpread;
            else target = 0f;

            currentSpread = Mathf.Lerp(currentSpread, target, Time.deltaTime * lerpSpeed);

            float spreadDelta = (maxAngleDeg - restAngleDeg) * currentSpread;

            bool isFront = body.isRight ? !isLeft : isLeft;
            float dynamicAngle = isFront ? (-spreadDelta * frontAmplitudeScale) : (-spreadDelta * backAmplitudeScale);
            float dyn = restAngleDeg + dynamicAngle;
            float staticOffset = isFront ? frontOffsetZ : backOffsetZ;
            float finalAngle = baseAngleDeg + staticOffset + dyn;
            // 下翼已作为上翼子节点继承 3D 倾角，不再重复叠加 X/Y 倾转。
            float xOffset = isLowerWing ? 0f : (isFront ? frontOffsetX : backOffsetX);
            float yOffset = isLowerWing ? 0f : (isFront ? frontOffsetY : backOffsetY);
            transform.localEulerAngles = new Vector3(xOffset, yOffset, finalAngle);

            UpdateSortingOrder();
        }

        private void UpdateSortingOrder()
        {
            if (_renderer == null || body == null) return;

            int offset = body.isRight ? (isLeft ? -1 : 0) : (isLeft ? 0 : -1);
            _renderer.sortingOrder = _baseSortingOrder + offset;
        }
    }
}
