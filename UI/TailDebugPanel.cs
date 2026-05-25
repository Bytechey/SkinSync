using UnityEngine;

namespace SkinSyncMod
{
    /// <summary>
    /// F9 切显隐的尾巴形变实时调参面板。
    /// </summary>
    [DisallowMultipleComponent]
    public class TailDebugPanel : MonoBehaviour
    {
        public KeyCode ToggleKey = KeyCode.F9;
        private bool _open;
        private Vector2 _scroll;
        private Rect _windowRect = new Rect(20f, 80f, 360f, 600f);

        private void Update()
        {
            if (Input.GetKeyDown(ToggleKey)) _open = !_open;
        }

        private void OnGUI()
        {
            if (!_open) return;
            _windowRect = GUILayout.Window(0xC0DEU.GetHashCode(), _windowRect, DrawWindow, "尾巴形变调试 (F9)");
        }

        private void DrawWindow(int id)
        {
            _scroll = GUILayout.BeginScrollView(_scroll);

            TailDeformConfig.Enabled = GUILayout.Toggle(TailDeformConfig.Enabled, "启用形变（关闭 = 显示原 sprite）");
            TailDeformConfig.FrontGuard = GUILayout.Toggle(TailDeformConfig.FrontGuard, "前侧约束（防止尾巴飘到身体正面）");

            GUILayout.Space(6f);
            TailDeformConfig.Segments = (int)Slider("分段数 Segments", TailDeformConfig.Segments, 4f, 40f, 1f);
            TailDeformConfig.ConstraintIters = (int)Slider("约束迭代 Iters", TailDeformConfig.ConstraintIters, 1f, 20f, 1f);

            GUILayout.Space(4f);
            TailDeformConfig.Damping = Slider("阻尼 Damping", TailDeformConfig.Damping, 0f, 5f, 0.01f);
            TailDeformConfig.Stiffness = Slider("距离约束 Stiffness", TailDeformConfig.Stiffness, 0f, 5f, 0.01f);
            TailDeformConfig.MaxBendDeg = Slider("最大弯角 MaxBendDeg", TailDeformConfig.MaxBendDeg, 0f, 180f, 1f);
            TailDeformConfig.AnchorFollow = Slider("第一节软跟随 AnchorFollow", TailDeformConfig.AnchorFollow, 0f, 5f, 0.01f);

            GUILayout.Space(4f);
            TailDeformConfig.Smoothness = Slider("平滑度 Smoothness", TailDeformConfig.Smoothness, 0f, 0.99f, 0.01f);
            TailDeformConfig.MaxStep = Slider("单步最大位移 MaxStep", TailDeformConfig.MaxStep, 0.001f, 2f, 0.005f);
            TailDeformConfig.MaxFixedDt = Slider("子步上限 MaxFixedDt", TailDeformConfig.MaxFixedDt, 0.005f, 0.1f, 0.001f);
            TailDeformConfig.FrontGuardMargin = Slider("前侧裕度 FrontGuardMargin", TailDeformConfig.FrontGuardMargin, 0f, 1f, 0.01f);

            GUILayout.Space(4f);
            TailDeformConfig.GravityX = Slider("重力 X", TailDeformConfig.GravityX, -10f, 10f, 0.1f);
            TailDeformConfig.GravityY = Slider("重力 Y", TailDeformConfig.GravityY, -20f, 10f, 0.1f);

            GUILayout.Space(4f);
            TailDeformConfig.WindFreq = Slider("风频率 WindFreq", TailDeformConfig.WindFreq, 0f, 20f, 0.05f);
            TailDeformConfig.WindAmp = Slider("风幅度 WindAmp", TailDeformConfig.WindAmp, 0f, 1f, 0.001f);
            TailDeformConfig.SpeedDisturb = Slider("速度扰动 SpeedDisturb", TailDeformConfig.SpeedDisturb, 0f, 0.5f, 0.001f);

            GUILayout.Space(8f);
            if (GUILayout.Button("重置默认"))
            {
                TailDeformConfig.Segments = 10;
                TailDeformConfig.ConstraintIters = 6;
                TailDeformConfig.Damping = 0.45f;
                TailDeformConfig.Stiffness = 1.0f;
                TailDeformConfig.GravityX = 0f;
                TailDeformConfig.GravityY = -1.2f;
                TailDeformConfig.WindFreq = 1.2f;
                TailDeformConfig.WindAmp = 0.012f;
                TailDeformConfig.SpeedDisturb = 0.005f;
                TailDeformConfig.MaxBendDeg = 18f;
                TailDeformConfig.AnchorFollow = 1.0f;
                TailDeformConfig.Smoothness = 0.55f;
                TailDeformConfig.FrontGuard = true;
                TailDeformConfig.FrontGuardMargin = 0.05f;
                TailDeformConfig.MaxStep = 0.15f;
                TailDeformConfig.MaxFixedDt = 0.02f;
                TailDeformConfig.Enabled = true;
            }
            if (GUILayout.Button("关闭面板")) _open = false;

            GUILayout.EndScrollView();
            GUI.DragWindow();
        }

        private static float Slider(string label, float value, float min, float max, float step)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(200f));
            float v = GUILayout.HorizontalSlider(value, min, max, GUILayout.ExpandWidth(true));
            GUILayout.Label(v.ToString("F3"), GUILayout.Width(56f));
            GUILayout.EndHorizontal();
            if (step > 0f) v = Mathf.Round(v / step) * step;
            return v;
        }
    }
}
