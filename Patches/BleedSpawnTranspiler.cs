using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace SkinSyncMod.Patches
{
    /// <summary>Transpiler patch BleedParticle.Update：在落地血 / 墙血 Instantiate 后按 owner 染色，仅玩家肢体生效，怪物跳过。</summary>
    [HarmonyPatch(typeof(BleedParticle), "Update")]
    public static class BleedSpawnTranspiler
    {
        private static readonly MethodInfo _colorHook = AccessTools.Method(
            typeof(BleedSpawnTranspiler), nameof(ColorSpawnedBlood));

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var ins in instructions)
            {
                yield return ins;
                if (IsInstantiateCall(ins))
                {
                    yield return new CodeInstruction(OpCodes.Dup);
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Call, _colorHook);
                }
            }
        }

        // 匹配 UnityEngine.Object.Instantiate(Object,Vector3,Quaternion)，兼容编译器选用的泛型/非泛型重载。
        private static bool IsInstantiateCall(CodeInstruction ins)
        {
            if (!(ins.operand is MethodInfo mi)) return false;
            if (mi.Name != "Instantiate") return false;
            if (mi.DeclaringType != typeof(Object)) return false;
            var ps = mi.GetParameters();
            return ps.Length == 3 && ps[1].ParameterType == typeof(Vector3) && ps[2].ParameterType == typeof(Quaternion);
        }

        /// <summary>对刚生成的血迹对象按 BleedParticle 的 owner character 染色；解析不到（怪物）则不动。</summary>
        public static void ColorSpawnedBlood(Object spawned, BleedParticle source)
        {
            if (!BloodRenderConfig.Enabled) return;
            if (source == null) return;
            var go = spawned as GameObject;
            if (go == null) return;

            string character = ResolveCharacter(source.transform);
            if (string.IsNullOrEmpty(character)) return;
            if (!BloodAttacher.TryLoadCharacterBlood(character, out var cfg, out _)) return;
            Color32? main = cfg.ParticleStartColor ?? cfg.BloodLight;
            if (!main.HasValue) return;

            var sr = go.GetComponent<SpriteRenderer>();
            if (sr == null) return;
            Color c = (Color)main.Value;
            // 墙血生成当帧会被游戏覆盖为白色，延迟到 LateUpdate 染；地血游戏只改 alpha，可立即染。
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

        private static string ResolveCharacter(Transform t)
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
