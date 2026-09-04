// ============================================================================
// DuckNpcFaceRandomizer.cs - 随机捏脸生成
// ============================================================================
// 模块说明：
//   在官方基线之上随机化「捏脸 UI 开放的那些字段」，生成一张合法但明显不同的脸。
//
//   两个档位：
//     - Varied：像正常玩家捏出来的脸，用于批量生成路人 NPC。
//     - Exaggerated：**故意往区间两端推**，用于验证"生成的脸确实不一样"。
//       一眼能看出差别是这个档位唯一的目标，不追求好看。
//
//   两条硬约束：
//     1. **部件 ID 必须来自 DuckNpcFaceCatalog.EnumeratePartIds() 的真实枚举结果。**
//        实测 hair 的 ID 是 0,1,2,3,4,6,...,18 —— **缺 5**，不连续。
//        用 Random.Range(0, totalCount) 会取到不存在的 5，而官方 GetPartPrefab
//        找不到时**静默回落 parts[0]**，于是随机结果异常偏向 0 号且不报任何错。
//     2. **必须在官方基线之上改**，不能从 default(CustomFaceSettingData) 起手，
//        否则 radius/heightOffset 全零，五官糊在头中心（见 DuckNpcFaceCodec）。
// ============================================================================

using System;
using UnityEngine;

namespace BossRush
{
    /// <summary>随机脸的强度档位。</summary>
    internal enum DuckNpcFaceWildness
    {
        /// <summary>像正常玩家捏的脸。</summary>
        Varied,

        /// <summary>故意推向区间两端，用于肉眼验证差异。</summary>
        Exaggerated
    }

    /// <summary>
    /// 随机捏脸生成器。纯静态、无状态。
    /// </summary>
    internal static class DuckNpcFaceRandomizer
    {
        /// <summary>
        /// 在官方默认基线之上生成一张随机脸。取不到基线时返回 false。
        /// </summary>
        internal static bool TryCreate(DuckNpcFaceWildness wildness, out CustomFaceSettingData face)
        {
            return TryCreateSeeded(wildness, 0, out face);
        }

        /// <summary>
        /// 带种子的随机脸：同一个 seed 永远生成同一张脸。
        /// seed 传 0 表示"每次都不同"，走全局随机流。
        /// </summary>
        /// <remarks>
        /// 蓝图 NPC 需要"每次进图长得一样"，但把整张脸的 JSON 写进配置太笨重，
        /// 一个 int 种子就够了。
        ///
        /// 实现上**必须存档并还原 UnityEngine.Random.state**：
        /// Random.InitState 会重置全局随机流，直接调用会污染同一帧里其他系统
        /// （掉落、变异词条、刷怪抖动）的随机序列。
        /// </remarks>
        internal static bool TryCreateSeeded(DuckNpcFaceWildness wildness, int seed, out CustomFaceSettingData face)
        {
            CustomFaceSettingData baseline;
            if (!DuckNpcFaceCatalog.TryGetDefaultFace(out baseline))
            {
                face = default(CustomFaceSettingData);
                ModBehaviour.DevLog("[DuckNpc] [WARNING] 取不到官方捏脸基线，无法生成随机脸");
                return false;
            }

            if (seed == 0)
            {
                face = Create(baseline, wildness);
                return true;
            }

            UnityEngine.Random.State saved = UnityEngine.Random.state;
            try
            {
                UnityEngine.Random.InitState(seed);
                face = Create(baseline, wildness);
            }
            finally
            {
                // 无论成功失败都要还原，否则全局随机流被这颗种子劫持。
                UnityEngine.Random.state = saved;
            }
            return true;
        }

        /// <summary>
        /// 在给定基线之上生成一张随机脸。基线只提供 radius/heightOffset 等
        /// 捏脸 UI 不开放的几何字段，其余字段全部被随机值覆盖。
        /// </summary>
        internal static CustomFaceSettingData Create(CustomFaceSettingData baseline, DuckNpcFaceWildness wildness)
        {
            CustomFaceSettingData face = baseline;
            bool extreme = wildness == DuckNpcFaceWildness.Exaggerated;

            // —— 部件款式：从真实 ID 表里抽 ——
            face.hairID = PickPartId(CustomFacePartTypes.hair, baseline.hairID);
            face.eyeID = PickPartId(CustomFacePartTypes.eye, baseline.eyeID);
            face.eyebrowID = PickPartId(CustomFacePartTypes.eyebrow, baseline.eyebrowID);
            face.mouthID = PickPartId(CustomFacePartTypes.mouth, baseline.mouthID);
            face.tailID = PickPartId(CustomFacePartTypes.tail, baseline.tailID);
            face.footID = PickPartId(CustomFacePartTypes.foot, baseline.footID);
            face.wingID = PickPartId(CustomFacePartTypes.wing, baseline.wingID);

            // —— 头：体色 + 体型 ——
            // 体色是最抢眼的一项，夸张档直接给高饱和随机色。
            face.headSetting.mainColor = RandomColor(extreme ? 0.85f : 0.45f, extreme ? 1f : 0.9f);
            face.headSetting.headScaleOffset = extreme
                ? PickExtreme(-0.4f, 0.4f)
                : UnityEngine.Random.Range(-0.22f, 0.22f);
            face.headSetting.foreheadHeight = extreme
                ? PickExtreme(0f, 0.6f)
                : UnityEngine.Random.Range(0f, 0.3f);
            face.headSetting.foreheadRound = extreme
                ? PickExtreme(0.35f, 1f)
                : UnityEngine.Random.Range(0.6f, 1f);

            // —— 眼 ——
            face.eyeInfo.color = RandomColor(extreme ? 0.8f : 0.3f, 1f);
            face.eyeInfo.scale = extreme
                ? PickExtreme(0.35f, 4f)
                : UnityEngine.Random.Range(0.8f, 1.6f);
            face.eyeInfo.distanceAngle = extreme
                ? UnityEngine.Random.Range(10f, 90f)
                : UnityEngine.Random.Range(35f, 62f);
            face.eyeInfo.height = extreme
                ? PickExtreme(-0.3f, 0.3f)
                : UnityEngine.Random.Range(-0.08f, 0.16f);
            face.eyeInfo.twist = extreme
                ? UnityEngine.Random.Range(-90f, 90f)
                : UnityEngine.Random.Range(-20f, 20f);

            // —— 眉 ——
            // 注意：eyebrow 的 heightOffset 会被官方 RefreshAll() 强制覆盖成
            // eyePart.height，这里不用管它。
            face.eyebrowInfo.color = RandomColor(extreme ? 0.7f : 0.25f, extreme ? 1f : 0.5f);
            face.eyebrowInfo.scale = extreme
                ? PickExtreme(0.4f, 4f)
                : UnityEngine.Random.Range(0.8f, 1.5f);
            face.eyebrowInfo.distanceAngle = extreme
                ? UnityEngine.Random.Range(10f, 90f)
                : UnityEngine.Random.Range(30f, 55f);
            face.eyebrowInfo.height = extreme
                ? PickExtreme(-0.3f, 0.3f)
                : UnityEngine.Random.Range(0f, 0.18f);
            face.eyebrowInfo.twist = extreme
                ? UnityEngine.Random.Range(-90f, 90f)
                : UnityEngine.Random.Range(-25f, 25f);

            // —— 嘴 ——
            face.mouthInfo.color = RandomColor(extreme ? 0.8f : 0.4f, 1f);
            face.mouthInfo.scale = extreme
                ? PickExtreme(0.35f, 4f)
                : UnityEngine.Random.Range(0.6f, 1.4f);
            face.mouthInfo.height = extreme
                ? PickExtreme(-0.3f, 0.3f)
                : UnityEngine.Random.Range(-0.05f, 0.12f);
            face.mouthInfo.leftRightAngle = extreme
                ? PickExtreme(-50f, 50f)
                : UnityEngine.Random.Range(-12f, 12f);
            face.mouthInfo.twist = extreme
                ? UnityEngine.Random.Range(-90f, 90f)
                : UnityEngine.Random.Range(-15f, 15f);

            // —— 尾 / 翅 / 脚 ——
            face.tailInfo.color = RandomColor(extreme ? 0.85f : 0.5f, 1f);
            face.tailInfo.scale = extreme
                ? PickExtreme(0.3f, 2f)
                : UnityEngine.Random.Range(0.7f, 1.4f);

            face.wingInfo.color = RandomColor(extreme ? 0.85f : 0.5f, 1f);
            face.wingInfo.scale = extreme
                ? PickExtreme(0.5f, 2f)
                : UnityEngine.Random.Range(0.8f, 1.3f);

            // foot 没有颜色选择器，只随机尺寸。
            face.footInfo.scale = extreme
                ? PickExtreme(0.5f, 1.5f)
                : UnityEngine.Random.Range(0.8f, 1.2f);

            // 最后统一夹取到官方区间 + 补几何，保证结果一定合法。
            DuckNpcFaceCodec.Normalize(ref face, baseline);
            return face;
        }

        // ====================================================================
        // 工具
        // ====================================================================

        /// <summary>
        /// 从某类部件的真实 ID 表里随机抽一个。表为空时返回 fallback。
        /// </summary>
        private static int PickPartId(CustomFacePartTypes type, int fallback)
        {
            int[] ids = DuckNpcFaceCatalog.EnumeratePartIds(type);
            if (ids == null || ids.Length == 0)
            {
                return fallback;
            }
            return ids[UnityEngine.Random.Range(0, ids.Length)];
        }

        /// <summary>
        /// 往区间两端推：随机取靠近 min 或靠近 max 的一段，避开中间地带。
        /// 夸张档要的是"一眼看出不同"，落在中位数上就白随机了。
        /// </summary>
        private static float PickExtreme(float min, float max)
        {
            float span = max - min;
            // 两端各取 25% 的窄带
            if (UnityEngine.Random.value < 0.5f)
            {
                return UnityEngine.Random.Range(min, min + span * 0.25f);
            }
            return UnityEngine.Random.Range(max - span * 0.25f, max);
        }

        /// <summary>
        /// 随机色。走 HSV 保证色相铺开、饱和度和明度可控，
        /// 直接随机 RGB 会大量产出灰扑扑的颜色。
        /// </summary>
        private static Color RandomColor(float minSaturation, float maxValue)
        {
            float h = UnityEngine.Random.value;
            float s = UnityEngine.Random.Range(minSaturation, 1f);
            float v = UnityEngine.Random.Range(Mathf.Max(0.35f, maxValue - 0.45f), maxValue);
            Color color = Color.HSVToRGB(h, s, v);
            color.a = 1f;
            return color;
        }
    }
}
