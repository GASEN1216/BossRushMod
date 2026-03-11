using Duckov.Utilities;
using UnityEngine;

namespace BossRush
{
    internal static class FenHuangHalberdRuntime
    {
        private const float AimEpsilonSqr = 0.001f;
        private const float PreviewOriginHeight = 1.1f;
        private const float GroundSampleHeight = 8f;
        private const float GroundRayDistance = 30f;
        private const float LeapArcHeight = 3.5f;

        private static int? cachedGroundMask;
        private static int? cachedWallMask;
        private static int? cachedPreviewObstacleMask;
        private static int? cachedDamageReceiverMask;

        public static bool IsHoldingHalberd(CharacterMainControl character)
        {
            if (character == null)
            {
                return false;
            }

            ItemAgent_MeleeWeapon melee = character.GetMeleeWeapon();
            if (melee != null && melee.Item != null && melee.Item.TypeID == FenHuangHalberdIds.WeaponTypeId)
            {
                return true;
            }

            DuckovItemAgent holdItemAgent = character.CurrentHoldItemAgent;
            return holdItemAgent != null &&
                   holdItemAgent.Item != null &&
                   holdItemAgent.Item.TypeID == FenHuangHalberdIds.WeaponTypeId;
        }

        public static bool EnsureDragonKingAssetsLoaded()
        {
            if (DragonKingAssetManager.IsLoaded)
            {
                return true;
            }

            try
            {
                string modPath = ModBehaviour.GetModPath();
                if (!string.IsNullOrEmpty(modPath))
                {
                    return DragonKingAssetManager.LoadAssetBundleSync(modPath);
                }
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[FenHuangHalberd] 加载龙王资源失败: " + e.Message);
            }

            return DragonKingAssetManager.IsLoaded;
        }

        public static Vector3 GetPreviewOrigin(CharacterMainControl character)
        {
            if (character == null)
            {
                return Vector3.zero;
            }

            if (character.CurrentUsingAimSocket != null)
            {
                return character.CurrentUsingAimSocket.position;
            }

            if (character.characterModel != null)
            {
                if (character.characterModel.MeleeWeaponSocket != null)
                {
                    return character.characterModel.MeleeWeaponSocket.position;
                }

                if (character.characterModel.RightHandSocket != null)
                {
                    return character.characterModel.RightHandSocket.position;
                }
            }

            return character.transform.position + Vector3.up * PreviewOriginHeight;
        }

        /// <summary>
        /// 获取角色当前瞄准方向上的目标位置，不限制距离，允许跳到屏幕任意位置。
        /// </summary>
        public static Vector3 GetAimPoint(CharacterMainControl character)
        {
            if (character == null)
            {
                return Vector3.zero;
            }

            Vector3 origin = character.transform.position;
            Vector3 aimPoint = character.GetCurrentAimPoint();
            Vector3 flatDirection = aimPoint - origin;
            flatDirection.y = 0f;

            if (flatDirection.sqrMagnitude < AimEpsilonSqr)
            {
                flatDirection = character.CurrentAimDirection;
                flatDirection.y = 0f;
            }

            if (flatDirection.sqrMagnitude < AimEpsilonSqr)
            {
                flatDirection = character.transform.forward;
                flatDirection.y = 0f;
            }

            flatDirection.Normalize();

            float distance = Vector3.Distance(
                new Vector3(origin.x, 0f, origin.z),
                new Vector3(aimPoint.x, 0f, aimPoint.z)
            );

            Vector3 result = origin + flatDirection * distance;
            result.y = aimPoint.y;
            return result;
        }



        public static Vector3 SnapToGround(Vector3 position, float fallbackY)
        {
            RaycastHit hit;
            Vector3 sample = position + Vector3.up * GroundSampleHeight;

            if (Physics.Raycast(sample, Vector3.down, out hit, GroundRayDistance, GroundLayerMask))
            {
                return hit.point;
            }

            position.y = fallbackY;
            return position;
        }

        public static void FillTrajectory(Vector3[] points, Vector3 start, Vector3 target)
        {
            if (points == null || points.Length == 0)
            {
                return;
            }

            int lastIndex = points.Length - 1;
            for (int i = 0; i <= lastIndex; i++)
            {
                float t = lastIndex > 0 ? (float)i / lastIndex : 1f;
                points[i] = EvaluateTrajectoryPoint(start, target, t);
            }
        }

        public static Vector3 EvaluateTrajectoryPoint(Vector3 start, Vector3 target, float t)
        {
            Vector3 startFlat = start;
            startFlat.y = 0f;
            Vector3 targetFlat = target;
            targetFlat.y = 0f;

            Vector3 currentFlat = Vector3.Lerp(startFlat, targetFlat, Mathf.Clamp01(t));
            float baseHeight = Mathf.Lerp(start.y, target.y, Mathf.Clamp01(t));
            float jumpOffset = Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI) * LeapArcHeight;
            return currentFlat + Vector3.up * (baseHeight + jumpOffset);
        }

        public static DamageReceiver TryGetDamageReceiver(Component source)
        {
            if (source == null)
            {
                return null;
            }

            DamageReceiver receiver = source.GetComponent<DamageReceiver>();
            if (receiver != null)
            {
                return receiver;
            }

            receiver = source.GetComponentInParent<DamageReceiver>();
            if (receiver != null)
            {
                return receiver;
            }

            return source.GetComponentInChildren<DamageReceiver>();
        }

        public static int GroundLayerMask
        {
            get
            {
                if (!cachedGroundMask.HasValue)
                {
                    try
                    {
                        cachedGroundMask = GameplayDataSettings.Layers.groundLayerMask;
                    }
                    catch
                    {
                        cachedGroundMask = LayerMask.GetMask("Ground", "Default");
                    }
                }

                return cachedGroundMask.Value;
            }
        }

        public static int WallLayerMask
        {
            get
            {
                if (!cachedWallMask.HasValue)
                {
                    try
                    {
                        cachedWallMask = GameplayDataSettings.Layers.wallLayerMask;
                    }
                    catch
                    {
                        cachedWallMask = LayerMask.GetMask("Wall");
                    }
                }

                return cachedWallMask.Value;
            }
        }

        public static int PreviewObstacleLayerMask
        {
            get
            {
                if (!cachedPreviewObstacleMask.HasValue)
                {
                    try
                    {
                        cachedPreviewObstacleMask = GameplayDataSettings.Layers.wallLayerMask |
                                                    GameplayDataSettings.Layers.fowBlockLayers;
                    }
                    catch
                    {
                        cachedPreviewObstacleMask = LayerMask.GetMask("Wall", "Default");
                    }
                }

                return cachedPreviewObstacleMask.Value;
            }
        }

        public static int DamageReceiverLayerMask
        {
            get
            {
                if (!cachedDamageReceiverMask.HasValue)
                {
                    try
                    {
                        cachedDamageReceiverMask = GameplayDataSettings.Layers.damageReceiverLayerMask;
                    }
                    catch
                    {
                        cachedDamageReceiverMask = ~0;
                    }
                }

                return cachedDamageReceiverMask.Value;
            }
        }
    }
}
