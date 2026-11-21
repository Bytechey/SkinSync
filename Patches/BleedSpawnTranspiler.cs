using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace SkinSyncMod.Patches
{
    /// <summary>拦截 BleedParticle.Update 生成的落地血与墙面血实例并按角色皮肤血色染色。</summary>
    [HarmonyPatch(typeof(BleedParticle), "Update")]
    public static class BleedSpawnTranspiler
    {
        private static readonly MethodInfo _colorHook = AccessTools.Method(
            typeof(BleedSpawnTranspiler), nameof(ColorSpawnedBlood));

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
        {
            int matched = 0;
            foreach (var ins in instructions)
            {
                yield return ins;
                if (IsInstantiateCall(ins))
                {
                    matched++;
                    yield return new CodeInstruction(OpCodes.Dup);
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Call, _colorHook);
                }
            }

            if (matched != 2)
            {
                ModLog.Warning($"BleedSpawnTranspiler: 预期匹配 2 处 Instantiate，实际匹配 {matched} 处。");
            }
        }

        private static bool IsInstantiateCall(CodeInstruction ins)
        {
            if (!(ins.operand is MethodInfo mi)) return false;
            if (mi.Name != "Instantiate") return false;
            if (mi.DeclaringType != typeof(Object)) return false;
            var ps = mi.GetParameters();
            return ps.Length == 3 && ps[1].ParameterType == typeof(Vector3) && ps[2].ParameterType == typeof(Quaternion);
        }

        /// <summary>按 BleedParticle 所属角色的血液配置为新生成的血迹对象着色。</summary>
        public static void ColorSpawnedBlood(Object spawned, BleedParticle source)
        {
            if (!BloodRenderConfig.Enabled) return;
            if (source == null || source.vomit) return;
            var go = spawned as GameObject;
            if (go == null) return;

            string character = ResolveCharacter(source.transform);
            if (string.IsNullOrEmpty(character)) return;
            if (!BloodAttacher.TryGetBloodConfig(character, out var cfg) || cfg == null) return;
            Color32? main = cfg.ParticleStartColor ?? cfg.BloodLight;
            if (!main.HasValue) return;

            var sr = go.GetComponent<SpriteRenderer>();
            if (sr == null) return;
            Color c = (Color)main.Value;
            // 墙面血迹在生成当帧会被原方法末尾置白，延后至 LateUpdate 染色。
            string n = go.name ?? "";
            if (n.IndexOf("wall", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var tint = go.GetComponent<OneShotBloodTint>() ?? go.AddComponent<OneShotBloodTint>();
                tint.Configure(sr, c);
            }
            else
            {
                sr.color = new Color(c.r, c.g, c.b, sr.color.a);
            }
        }

        /// <summary>沿节点父链向上查找所属角色当前生效的自定义皮肤名。</summary>
        internal static string ResolveCharacter(Transform t)
        {
            while (t != null)
            {
                var name = SkinApplier.GetCharacterByChara(t.gameObject);
                if (!string.IsNullOrEmpty(name)) return name;
                t = t.parent;
            }
            return null;
        }
    }
}
