using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>
    /// 单只翅膀的姿态控制器：依据 body 的运动状态在 baseAngleDeg 上叠加展开角。
    /// </summary>
    public class WingScript : MonoBehaviour
    {
        public bool isLeft;
        public float baseAngleDeg = 0f;
        public float restAngleDeg = 18f;
        public float maxAngleDeg = 110f;
        public float crouchSpread = 0.30f;
        public float jumpSpread = 0.55f;
        public float fallSpread = 1.0f;
        public float lerpSpeed = 8f;

        private Body body;
        private float currentSpread;

        private void Start()
        {
            body = GetComponentInParent<Body>();
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
            float dyn = isLeft ? -(restAngleDeg + spreadDelta) : (restAngleDeg + spreadDelta);
            if (!body.isRight) dyn = -dyn;

            transform.localEulerAngles = new Vector3(0f, 0f, baseAngleDeg + dyn);
        }
    }
}
