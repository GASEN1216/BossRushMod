// ============================================================================
// DuckNpcFaceCodec.cs - 捏脸数据的序列化、夹取与几何补全
// ============================================================================
// 模块说明：
//   CustomFaceSettingData 是官方的纯 [Serializable] struct，本身就能 JSON 双向
//   （CustomFaceSettingData.DataToJson / JsonToData）。本文件在此之上补三件事：
//
//   1. **几何补全**（ApplyBaselineGeometry）——最关键的一件。
//      CustomFacePartInfo 里的 radius / heightOffset 不在官方捏脸 UI 里，
//      只存在于 preset 资产。手写 JSON 时漏掉这两项，JsonUtility 会填 0，
//      结果五官全糊在头部中心。所以任何外来脸数据都必须先用官方基线补齐几何。
//
//   2. **范围夹取**（Clamp）——按官方 CustomFaceUI.Init() 里逐个滑条的
//      真实取值区间夹取，保证程序生成的脸落在「玩家自己也能捏出来」的范围内。
//
//   3. **可授权字段的边界说明**——官方 UI 只暴露一部分字段，见 Clamp 的注释。
//      不在清单里的字段一律沿用基线，不要程序化改写。
//
//   本文件不碰角色、不碰场景，纯数据变换，便于将来被 blueprint 层和 F3 工具共用。
// ============================================================================

using System;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 捏脸数据编解码器。纯静态、无状态。
    /// </summary>
    internal static class DuckNpcFaceCodec
    {
        // ====================================================================
        // 官方 CustomFaceUI.Init() 的滑条区间
        // ====================================================================
        // 这些数字不是拍的，是从官方捏脸界面逐个滑条抄下来的。
        // 改动前请对照 鸭科夫源码/TeamSoda.Duckov.Core/CustomFaceUI.cs 的 Init()。

        private const float HeadSizeMin = 0.6f;
        private const float HeadSizeMax = 1.4f;
        private const float ForeheadHeightMin = 0f;
        private const float ForeheadHeightMax = 0.6f;
        private const float ForeheadRoundMin = 0.35f;
        private const float ForeheadRoundMax = 1f;

        private const float FacePartDistanceAngleMin = 0f;
        private const float FacePartDistanceAngleMax = 90f;
        private const float FacePartHeightMin = -0.3f;
        private const float FacePartHeightMax = 0.3f;
        private const float FacePartScaleMin = 0.3f;
        private const float FacePartScaleMax = 4f;
        private const float FacePartTwistMin = -90f;
        private const float FacePartTwistMax = 90f;

        private const float MouthLeftRightAngleMin = -50f;
        private const float MouthLeftRightAngleMax = 50f;

        private const float WingScaleMin = 0.5f;
        private const float WingScaleMax = 2f;
        private const float TailScaleMin = 0.3f;
        private const float TailScaleMax = 2f;
        private const float FootScaleMin = 0.5f;
        private const float FootScaleMax = 1.5f;

        // ====================================================================
        // 序列化
        // ====================================================================

        /// <summary>
        /// 序列化为 JSON。走官方 DataToJson，浮点已按官方规则截到 3 位小数。
        /// </summary>
        internal static string ToJson(CustomFaceSettingData face)
        {
            try
            {
                return face.DataToJson();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DuckNpc] [WARNING] 捏脸数据序列化失败: " + e.Message);
                return string.Empty;
            }
        }

        /// <summary>
        /// 从 JSON 反序列化。
        /// </summary>
        /// <remarks>
        /// 官方 JsonToData 在解析异常时会 Debug.LogError("捏脸参数违法") 并返回 false，
        /// 但 JsonUtility 对「结构不对但语法合法」的 JSON 不抛异常，只会静默填默认值。
        /// 所以这里先做一次结构探测，避免把一坨零当成合法的脸接受。
        /// </remarks>
        internal static bool TryFromJson(string json, out CustomFaceSettingData face)
        {
            face = default(CustomFaceSettingData);

            if (string.IsNullOrEmpty(json))
            {
                return false;
            }

            // 结构探测：官方 struct 的顶层一定有 headSetting 字段。
            // 缺它说明这不是一份捏脸数据，别让 JsonUtility 静默返回全零。
            if (json.IndexOf("headSetting", StringComparison.Ordinal) < 0)
            {
                ModBehaviour.DevLog("[DuckNpc] [WARNING] 捏脸 JSON 缺少 headSetting 字段，拒绝解析");
                return false;
            }

            try
            {
                if (!CustomFaceSettingData.JsonToData(json, out face))
                {
                    return false;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DuckNpc] [WARNING] 捏脸 JSON 解析异常: " + e.Message);
                return false;
            }

            face.savedSetting = true;
            return true;
        }

        // ====================================================================
        // 几何补全
        // ====================================================================

        /// <summary>
        /// 用官方基线补齐外来脸数据里**捏脸 UI 不暴露**的几何字段（radius / heightOffset）。
        /// </summary>
        /// <remarks>
        /// 这是接收任何非官方来源脸数据的必经步骤。radius 决定部件离头部中心多远，
        /// heightOffset 决定基准高度，两者都只在 preset 资产里，手写 JSON 必然漏。
        ///
        /// eyebrow 的 heightOffset 是个例外：官方 RefreshAll() 每次都会把它强制覆盖成
        /// eyePart.partInfo.height，所以这里写什么都会被官方推平，补它只为让落盘数据自洽。
        /// </remarks>
        internal static void ApplyBaselineGeometry(ref CustomFaceSettingData face, CustomFaceSettingData baseline)
        {
            CopyGeometry(ref face.hairInfo, baseline.hairInfo);
            CopyGeometry(ref face.eyeInfo, baseline.eyeInfo);
            CopyGeometry(ref face.eyebrowInfo, baseline.eyebrowInfo);
            CopyGeometry(ref face.mouthInfo, baseline.mouthInfo);
            CopyGeometry(ref face.tailInfo, baseline.tailInfo);
            CopyGeometry(ref face.footInfo, baseline.footInfo);
            CopyGeometry(ref face.wingInfo, baseline.wingInfo);
        }

        private static void CopyGeometry(ref CustomFacePartInfo target, CustomFacePartInfo baseline)
        {
            target.radius = baseline.radius;
            target.heightOffset = baseline.heightOffset;
        }

        // ====================================================================
        // 范围夹取
        // ====================================================================

        /// <summary>
        /// 把脸数据夹取到官方捏脸 UI 的合法区间。
        /// </summary>
        /// <remarks>
        /// 官方 UI 每个部件真正开放的字段（不在清单里的一律沿用基线，别程序化改写）：
        ///
        /// | 部件    | 开放字段 |
        /// | ------- | -------- |
        /// | 头      | mainColor / headScaleOffset / foreheadHeight / foreheadRound |
        /// | hair    | id / color |
        /// | eye     | id / color / distanceAngle / height / scale / twist |
        /// | eyebrow | id / color / distanceAngle / height / scale / twist |
        /// | mouth   | id / color / scale / height / leftRightAngle / twist |
        /// | tail    | id / color / scale |
        /// | wing    | id / color / scale |
        /// | foot    | id / scale（**没有** color 选择器） |
        ///
        /// 夹取只收紧，不放宽：超出区间的值会被拉回边界，而不是被判非法。
        /// </remarks>
        internal static void Clamp(ref CustomFaceSettingData face)
        {
            // —— 头 ——
            // headScaleOffset 在官方 UI 里是 (滑条值 - 1)，滑条 0.6~1.4 → 偏移 -0.4~0.4
            face.headSetting.headScaleOffset = Mathf.Clamp(
                face.headSetting.headScaleOffset, HeadSizeMin - 1f, HeadSizeMax - 1f);
            face.headSetting.foreheadHeight = Mathf.Clamp(
                face.headSetting.foreheadHeight, ForeheadHeightMin, ForeheadHeightMax);
            face.headSetting.foreheadRound = Mathf.Clamp(
                face.headSetting.foreheadRound, ForeheadRoundMin, ForeheadRoundMax);
            face.headSetting.mainColor = OpaqueColor(face.headSetting.mainColor);

            // —— hair：只有款式和颜色，尺寸/角度沿用基线 ——
            face.hairInfo.color = OpaqueColor(face.hairInfo.color);

            // —— eye / eyebrow：六项全开 ——
            ClampFaceFeature(ref face.eyeInfo);
            ClampFaceFeature(ref face.eyebrowInfo);

            // —— mouth：没有 distanceAngle，但多一个 leftRightAngle ——
            face.mouthInfo.color = OpaqueColor(face.mouthInfo.color);
            face.mouthInfo.scale = Mathf.Clamp(face.mouthInfo.scale, FacePartScaleMin, FacePartScaleMax);
            face.mouthInfo.height = Mathf.Clamp(face.mouthInfo.height, FacePartHeightMin, FacePartHeightMax);
            face.mouthInfo.twist = Mathf.Clamp(face.mouthInfo.twist, FacePartTwistMin, FacePartTwistMax);
            face.mouthInfo.leftRightAngle = Mathf.Clamp(
                face.mouthInfo.leftRightAngle, MouthLeftRightAngleMin, MouthLeftRightAngleMax);

            // —— tail / wing：款式 + 颜色 + 尺寸 ——
            face.tailInfo.color = OpaqueColor(face.tailInfo.color);
            face.tailInfo.scale = Mathf.Clamp(face.tailInfo.scale, TailScaleMin, TailScaleMax);
            face.wingInfo.color = OpaqueColor(face.wingInfo.color);
            face.wingInfo.scale = Mathf.Clamp(face.wingInfo.scale, WingScaleMin, WingScaleMax);

            // —— foot：款式 + 尺寸，颜色不开放，沿用基线 ——
            face.footInfo.scale = Mathf.Clamp(face.footInfo.scale, FootScaleMin, FootScaleMax);
        }

        private static void ClampFaceFeature(ref CustomFacePartInfo info)
        {
            info.color = OpaqueColor(info.color);
            info.distanceAngle = Mathf.Clamp(info.distanceAngle, FacePartDistanceAngleMin, FacePartDistanceAngleMax);
            info.height = Mathf.Clamp(info.height, FacePartHeightMin, FacePartHeightMax);
            info.scale = Mathf.Clamp(info.scale, FacePartScaleMin, FacePartScaleMax);
            info.twist = Mathf.Clamp(info.twist, FacePartTwistMin, FacePartTwistMax);
        }

        /// <summary>
        /// 官方 CustomFacePart.SetInfo 会把 alpha 强制拍成 1，这里提前对齐，
        /// 避免落盘数据里留下一个永远不生效的 alpha 值误导后来人。
        /// </summary>
        private static Color OpaqueColor(Color color)
        {
            color.a = 1f;
            return color;
        }

        // ====================================================================
        // 组合入口
        // ====================================================================

        /// <summary>
        /// 接收外来脸数据的标准流程：先用基线补几何，再夹取到官方区间。
        /// </summary>
        internal static void Normalize(ref CustomFaceSettingData face, CustomFaceSettingData baseline)
        {
            ApplyBaselineGeometry(ref face, baseline);
            Clamp(ref face);
            face.savedSetting = true;
        }

        /// <summary>
        /// 从 JSON 解析并归一化到官方基线。基线取不到时只做夹取。
        /// </summary>
        internal static bool TryFromJsonNormalized(string json, out CustomFaceSettingData face)
        {
            if (!TryFromJson(json, out face))
            {
                return false;
            }

            CustomFaceSettingData baseline;
            if (DuckNpcFaceCatalog.TryGetDefaultFace(out baseline))
            {
                Normalize(ref face, baseline);
            }
            else
            {
                ModBehaviour.DevLog("[DuckNpc] [WARNING] 取不到官方捏脸基线，几何字段未补全");
                Clamp(ref face);
            }

            return true;
        }
    }
}
