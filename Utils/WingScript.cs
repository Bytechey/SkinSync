using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>
    /// 单只翅膀的姿态控制器。
    /// - 静态角度由 wings.json 的 Rotation + 前后翼偏移（frontOffset/backOffset）共同决定。
    /// - 动态摆动方向左右一致（同相），摆动幅度可独立调节（amplitudeScale）。
    /// - 自动根据角色朝向交换左右翅膀的渲染层级。
    /// </summary>
    public class WingScript : MonoBehaviour
    {
        public bool isLeft;
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
            float dynamicAngle = isFront ? (- spreadDelta * frontAmplitudeScale): (-spreadDelta * backAmplitudeScale);
            float dyn = (restAngleDeg + dynamicAngle);
            float staticOffset = isFront ? frontOffsetZ : backOffsetZ;
            float finalAngle = baseAngleDeg + staticOffset + dyn;
            float xOffset = isFront ? frontOffsetX : backOffsetX;
            float yOffset = isFront ? frontOffsetY : backOffsetY;
            transform.localEulerAngles = new Vector3(xOffset, yOffset, finalAngle);

            UpdateSortingOrder();
        }

        /// <summary>
        /// 根据角色朝向动态调整翅膀的渲染层级。
        /// </summary>
        private void UpdateSortingOrder()
        {
            if (_renderer == null || body == null) return;

            int offset = body.isRight ? (isLeft ? -1 : 0) : (isLeft ? 0 : -1);
            _renderer.sortingOrder = _baseSortingOrder + offset;
        }
    }
}
