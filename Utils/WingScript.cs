using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>
    /// 挂在 4 张翼各自 GameObject 上：根据 body 状态算 spread（0=收敛，1=展开），
    /// 在 baseAngleDeg（来自 wings.json 的初始 rotation）之上叠加动画 zAngle。
    /// 上翼 / 下翼分别挂自己的 WingScript（下翼挂在上翼子节点下，旋转自动跟随）。
    /// </summary>
    public class WingScript : MonoBehaviour
    {
        public bool isLeft;
        // 单皮肤 wings.json 给的初始角度（Unity Z 旋转：顺时针为负，故传入时取负值）
        public float baseAngleDeg = 0f;
        // 翼根贴身收敛叠加角（站/走/跑）
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
            if (body.isRight == false) dyn = -dyn;

            transform.localEulerAngles = new Vector3(0f, 0f, baseAngleDeg + dyn);
        }
    }
}
