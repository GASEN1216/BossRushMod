using System;

namespace BossRush
{
    /// <summary>
    /// 通关奖励箱虚影演出的纯逻辑参数与位移计算。
    /// 保持无 Unity 依赖，便于单独运行逻辑测试。
    /// </summary>
    public static class VictoryRewardShadowMath
    {
        public const float FollowOffsetY = 3.2f;
        public const float HoverAmplitude = 0.12f;
        public const float HoverFrequency = 2f;
        public const float LandingHeightOffset = 0.05f;

        public static float ComputeFollowY(float anchorY, float elapsedSeconds)
        {
            double hover = Math.Sin(elapsedSeconds * HoverFrequency) * HoverAmplitude;
            return anchorY + FollowOffsetY + (float)hover;
        }

        public static float ComputeLandingY(float anchorY, bool hitGround, float groundY)
        {
            float baseY = hitGround ? groundY : anchorY;
            return baseY + LandingHeightOffset;
        }

        public static float MoveTowardsY(float currentY, float targetY, float descendSpeed, float deltaTime)
        {
            if (descendSpeed <= 0f || deltaTime <= 0f)
            {
                return currentY;
            }

            float maxDelta = descendSpeed * deltaTime;
            if (currentY < targetY)
            {
                float steppedUp = currentY + maxDelta;
                return steppedUp > targetY ? targetY : steppedUp;
            }

            float steppedDown = currentY - maxDelta;
            return steppedDown < targetY ? targetY : steppedDown;
        }
    }
}
