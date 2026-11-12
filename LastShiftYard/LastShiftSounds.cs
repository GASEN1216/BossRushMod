using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// COMPAT：末班编组站的提示音。文件由 `tools/gen_lastshift_sfx.py` 程序化生成，放 `Assets/Sounds/LastShift`，
    /// 随正式编译的 `Assets\Sounds` 整树部署。正式构建不引用 FMOD：与潮闸镇 `TidegateSounds` 同一套 `AudioManager.PostCustomSFX`
    /// 反射绑定（官方把事件挂在发声体上做 3D 定位）。文件缺了只记一次警告，玩法照常。
    /// </summary>
    internal static class LastShiftSounds
    {
        internal const string TicketPunch = "ticket_punch.wav";
        internal const string TannoyChime = "tannoy_chime.wav";
        internal const string TrainWhistle = "train_whistle.wav";
        /// <summary>押运长的豌豆哨：封线两声短、响墩一声长、半血三声短（站场哨音的停车 / 注意 / 危险）。</summary>
        internal const string ChiefWhistleTwo = "chief_whistle_two.wav";
        internal const string ChiefWhistleLong = "chief_whistle_long.wav";
        internal const string ChiefWhistleThree = "chief_whistle_three.wav";
        private static MethodInfo postSound;
        private static bool resolved, warned;

        /// <summary>在 <paramref name="emitter"/> 上放一次。发声体为空时什么都不做。</summary>
        internal static void Play(string file, GameObject emitter)
        {
            if (emitter == null) return;
            try
            {
                if (!resolved)
                {
                    resolved = true;
                    postSound = typeof(Duckov.AudioManager).GetMethod("PostCustomSFX", BindingFlags.Public | BindingFlags.Static,
                        null, new[] { typeof(string), typeof(GameObject), typeof(bool) }, null);
                }
                if (postSound == null) throw new InvalidOperationException("官方音效入口缺失");
                string path = Path.Combine(Path.Combine(Path.Combine(ModBehaviour.GetModPath(), "Assets"), "Sounds"), Path.Combine("LastShift", file));
                if (!File.Exists(path)) throw new FileNotFoundException("末班编组站音效未部署", file);
                postSound.Invoke(null, new object[] { path, emitter, false });
            }
            catch (Exception e)
            {
                if (warned) return;
                warned = true;
                Debug.LogWarning(LastShiftRaidScene.LogTag + " 音效不可用（玩法照常）：" + e.Message);
            }
        }

        /// <summary>跟着玩家放（站场广播、提示）。</summary>
        internal static void PlayAtPlayer(string file)
        {
            CharacterMainControl main = CharacterMainControl.Main;
            if (main != null) Play(file, main.gameObject);
        }

        internal static void ResetStaticCaches() { postSound = null; resolved = false; warned = false; }
    }

    /// <summary>
    /// 剪票处的打孔钳：模型里钳柄是 `Mesh_12/13/14` 三块（离线按包围盒对过原型的钳柄），装到一个转轴下，
    /// 剪票时压下再弹回（原型 transfer.mjs 的 handle，最大约 25°）。找不到零件就不动，不影响上车。
    /// </summary>
    internal sealed class LastShiftTicketPunch : MonoBehaviour
    {
        private static readonly string[] HandleParts = { "Mesh_12", "Mesh_13", "Mesh_14" };
        /// <summary>转轴：模型本地坐标（three.js 原型 (.55, 1.53, -.13) 镜像 X）。</summary>
        private static readonly Vector3 Pivot = new Vector3(-0.55f, 1.53f, -0.13f);
        private const float PressAngle = -25f, Duration = 0.75f;
        private Transform pivot;
        private float started = -1f;

        internal static LastShiftTicketPunch Attach(Transform model)
        {
            if (model == null) return null;
            var go = new GameObject("TicketPunchPivot");
            go.transform.SetParent(model, false);
            go.transform.localPosition = Pivot;
            int found = 0;
            foreach (string name in HandleParts)
            {
                Transform part = model.Find(name);
                if (part == null) continue;
                part.SetParent(go.transform, true);
                found++;
            }
            if (found == 0)
            {
                Destroy(go);
                Debug.LogWarning(LastShiftRaidScene.LogTag + " 剪票处模型里找不到打孔钳零件，剪票时钳子不动");
                return null;
            }
            var punch = model.gameObject.AddComponent<LastShiftTicketPunch>();
            punch.pivot = go.transform;
            return punch;
        }

        internal void Press() { started = Time.unscaledTime; }

        private void Update()
        {
            if (pivot == null || started < 0f) return;
            float t = (Time.unscaledTime - started) / Duration;
            if (t >= 1f) { started = -1f; pivot.localRotation = Quaternion.identity; return; }
            pivot.localRotation = Quaternion.Euler(0f, 0f, PressAngle * Mathf.Sin(Mathf.PI * t));
        }
    }
}
