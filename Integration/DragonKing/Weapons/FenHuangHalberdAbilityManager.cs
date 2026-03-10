using System.Reflection;
using BossRush.Common.Equipment;
using Duckov.Utilities;
using UnityEngine;

namespace BossRush
{
    public class FenHuangHalberdAbilityManager
        : EquipmentAbilityManager<FenHuangHalberdConfig, FenHuangHalberdAction>
    {
        private const int PreviewFragmentCount = 20;

        private readonly FenHuangHalberdConfig configInstance = new FenHuangHalberdConfig();
        private MethodInfo cachedReadValueAsButtonMethod;
        private MethodInfo cachedWasReleasedThisFrameMethod;

        private bool isPreviewing;
        private bool previewValid;
        private Vector3 previewLandingPoint;
        private GameObject previewObject;
        private FenHuangLeapPreview previewController;

        protected override void Update()
        {
            SuppressVanillaAdsIfNeeded();

            if (!abilityEnabled)
            {
                StopPreview();
                return;
            }

            HandleLeapInput();
        }

        private void LateUpdate()
        {
            SuppressVanillaAdsIfNeeded();
        }

        protected override FenHuangHalberdConfig GetConfig()
        {
            return configInstance;
        }

        protected override string GetInputActionName()
        {
            return "ADS";
        }

        protected override FenHuangHalberdAction CreateAbilityAction()
        {
            return actionObject.AddComponent<FenHuangHalberdAction>();
        }

        protected override bool IsInputPressedFallback()
        {
            return Input.GetMouseButtonDown(1);
        }

        protected override bool OnBeforeTryExecute()
        {
            return IsHoldingHalberd(CharacterMainControl.Main);
        }

        protected override void OnManagerInitialized()
        {
            FenHuangHalberdAction.SetConfig(configInstance);
            EnsureDragonKingAssetsLoaded();
            LogIfVerbose("焚皇断界戟右键技能管理器已初始化");
        }

        protected override void OnDestroy()
        {
            StopPreview();
            base.OnDestroy();
        }

        public override void OnSceneChanged()
        {
            StopPreview();
            EnsureDragonKingAssetsLoaded();
            base.OnSceneChanged();
        }

        public override void RebindToCharacter(CharacterMainControl character)
        {
            StopPreview();
            base.RebindToCharacter(character);
        }

        private void HandleLeapInput()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || targetCharacter == null || abilityAction == null)
            {
                StopPreview();
                return;
            }

            GetAdsInputState(out bool adsHeld, out bool adsPressed, out bool adsReleased);

            if (!isPreviewing)
            {
                if (!OnBeforeTryExecute())
                {
                    return;
                }

                if (adsPressed && CanStartPreview())
                {
                    BeginPreview();
                }
            }

            if (!isPreviewing)
            {
                return;
            }

            if (!IsHoldingHalberd(player))
            {
                LogIfVerbose("预览期间检测到武器不再是焚皇断界戟，取消跃击预览");
                StopPreview();
                return;
            }

            UpdatePreview();

            if (adsReleased || !adsHeld)
            {
                ReleasePreview();
            }
        }

        private void GetAdsInputState(out bool held, out bool pressed, out bool released)
        {
            held = false;
            pressed = false;
            released = false;

            try
            {
                held = Input.GetMouseButton(1);
                pressed = Input.GetMouseButtonDown(1);
                released = Input.GetMouseButtonUp(1);
            }
            catch
            {
            }

            if (held || pressed || released)
            {
                return;
            }

            if (!inputActionCached)
            {
                TryCacheInputAction();
            }

            pressed = IsInputPressed();
            held = IsAdsInputHeld();
            released = IsAdsReleasedThisFrame();
        }

        private static bool IsHoldingHalberd(CharacterMainControl player)
        {
            if (player == null)
            {
                return false;
            }

            ItemAgent_MeleeWeapon melee = player.GetMeleeWeapon();
            if (melee != null && melee.Item != null && melee.Item.TypeID == FenHuangHalberdIds.WeaponTypeId)
            {
                return true;
            }

            var holdItemAgent = player.CurrentHoldItemAgent;
            return holdItemAgent != null && holdItemAgent.Item != null && holdItemAgent.Item.TypeID == FenHuangHalberdIds.WeaponTypeId;
        }

        private bool CanStartPreview()
        {
            if (!abilityEnabled || abilityAction == null || targetCharacter == null || IsActionRunning)
            {
                return false;
            }

            EnsureDragonKingAssetsLoaded();
            return abilityAction.IsReady();
        }

        private void BeginPreview()
        {
            EnsurePreviewController();

            isPreviewing = true;
            previewValid = false;
            UpdatePreview();
            LogIfVerbose("检测到 ADS 输入，进入跃击预览");
        }

        private void ReleasePreview()
        {
            bool shouldExecute = isPreviewing && previewValid && abilityAction != null && !IsActionRunning;

            if (shouldExecute)
            {
                abilityAction.ConfigureLeapTarget(previewLandingPoint);
                LogIfVerbose("检测到 ADS 松开，尝试执行能力...");
                bool success = TryExecuteAbility();
                LogIfVerbose("能力执行结果: " + (success ? "成功" : "失败"));
            }
            else if (isPreviewing)
            {
                LogIfVerbose("检测到 ADS 松开，但落点无效或轨迹被阻挡");
            }

            StopPreview();
        }

        private void StopPreview()
        {
            isPreviewing = false;
            previewValid = false;

            if (previewObject != null)
            {
                Destroy(previewObject);
                previewObject = null;
                previewController = null;
            }
        }

        private void EnsurePreviewController()
        {
            if (previewObject != null && previewController != null)
            {
                return;
            }

            previewObject = new GameObject("FenHuangLeapPreview");
            previewObject.transform.SetParent(transform, false);
            previewController = previewObject.AddComponent<FenHuangLeapPreview>();
            previewController.Initialize();
        }

        private void UpdatePreview()
        {
            if (targetCharacter == null)
            {
                StopPreview();
                return;
            }

            Vector3 aimPoint = GetClampedAimPoint(targetCharacter);
            Vector3 resolvedLandingPoint = ResolveGroundPoint(aimPoint, targetCharacter.transform.position.y);
            Vector3 hitPoint = resolvedLandingPoint;
            Vector3 previewOrigin = GetPreviewOrigin(targetCharacter);

            Vector3[] previewPoints = FenHuangLeapMath.BuildTrajectory(
                previewOrigin,
                resolvedLandingPoint,
                FenHuangHalberdConfig.LeapVerticalSpeed,
                PreviewFragmentCount,
                GetPreviewObstacleLayers(),
                ref hitPoint,
                out bool hitObstacle,
                out float travelTime
            );

            previewLandingPoint = resolvedLandingPoint;
            previewValid = !hitObstacle && travelTime > 0.05f && IsLandingPointValid(previewLandingPoint);

            Vector3 markerPoint = hitObstacle ? hitPoint : previewLandingPoint;
            if (previewController != null)
            {
                previewController.UpdatePreview(previewPoints, markerPoint, previewValid);
            }
        }

        private static Vector3 GetPreviewOrigin(CharacterMainControl player)
        {
            if (player == null)
            {
                return Vector3.zero;
            }

            if (player.CurrentUsingAimSocket != null)
            {
                return player.CurrentUsingAimSocket.position;
            }

            if (player.characterModel != null)
            {
                if (player.characterModel.MeleeWeaponSocket != null)
                {
                    return player.characterModel.MeleeWeaponSocket.position;
                }

                if (player.characterModel.RightHandSocket != null)
                {
                    return player.characterModel.RightHandSocket.position;
                }
            }

            return player.transform.position + Vector3.up * 1.1f;
        }

        private Vector3 GetClampedAimPoint(CharacterMainControl player)
        {
            Vector3 aimPoint = player.GetCurrentAimPoint();
            Vector3 origin = player.transform.position;
            Vector3 flatOffset = aimPoint - origin;
            flatOffset.y = 0f;

            if (flatOffset.sqrMagnitude < 0.001f)
            {
                flatOffset = player.CurrentAimDirection;
                flatOffset.y = 0f;
            }

            if (flatOffset.sqrMagnitude < 0.001f)
            {
                flatOffset = player.transform.forward;
                flatOffset.y = 0f;
            }

            flatOffset.Normalize();

            float distance = Vector3.Distance(
                new Vector3(origin.x, 0f, origin.z),
                new Vector3(aimPoint.x, 0f, aimPoint.z)
            );
            distance = Mathf.Min(distance, FenHuangHalberdConfig.LeapPreviewMaxRange);

            Vector3 result = origin + flatOffset * distance;
            result.y = aimPoint.y;
            return result;
        }

        private Vector3 ResolveGroundPoint(Vector3 point, float fallbackY)
        {
            RaycastHit hit;
            Vector3 samplePoint = point + Vector3.up * 8f;
            int groundMask = GameplayDataSettings.Layers.groundLayerMask;

            if (Physics.Raycast(samplePoint, Vector3.down, out hit, 30f, groundMask))
            {
                return hit.point;
            }

            point.y = fallbackY;
            return point;
        }

        private bool IsLandingPointValid(Vector3 position)
        {
            // 只检测墙壁等不可通过的障碍物，不检测角色、地面、触发器
            int wallMask;
            try
            {
                wallMask = GameplayDataSettings.Layers.wallLayerMask;
            }
            catch
            {
                wallMask = LayerMask.GetMask("Wall");
            }

            if (wallMask == 0)
            {
                return true;
            }

            float checkRadius = 0.5f;
            float checkHeight = 2f;
            Collider[] hitColliders = Physics.OverlapCapsule(
                position,
                position + Vector3.up * checkHeight,
                checkRadius,
                wallMask
            );

            foreach (Collider col in hitColliders)
            {
                if (col == null)
                {
                    continue;
                }

                if (col.isTrigger)
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        private void SuppressVanillaAdsIfNeeded()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null)
            {
                return;
            }

            bool holdingHalberd = IsHoldingHalberd(player);
            if (!holdingHalberd && !IsActionRunning && !isPreviewing)
            {
                return;
            }

            GetAdsInputState(out bool adsHeld, out bool adsPressed, out bool adsReleased);
            if (!adsHeld && !adsPressed && !adsReleased && !IsActionRunning && !isPreviewing)
            {
                return;
            }

            try
            {
                if (!InputManager.InputActived)
                {
                    return;
                }
            }
            catch
            {
                return;
            }

            try
            {
                if (Duckov.UI.View.ActiveView != null)
                {
                    return;
                }
            }
            catch
            {
            }

            try
            {
                if (LevelManager.Instance != null && LevelManager.Instance.InputManager != null)
                {
                    LevelManager.Instance.InputManager.SetAdsInput(false);
                }
            }
            catch
            {
            }
        }

        private bool IsAdsInputHeld()
        {
            if (!inputActionCached)
            {
                TryCacheInputAction();
            }

            if (cachedInputAction != null)
            {
                try
                {
                    if (cachedReadValueAsButtonMethod == null)
                    {
                        cachedReadValueAsButtonMethod = BossRush.Common.Utils.ReflectionCache.GetMethod(
                            cachedInputAction.GetType(),
                            "ReadValueAsButton",
                            BindingFlags.Public | BindingFlags.Instance
                        );
                    }

                    if (cachedReadValueAsButtonMethod != null)
                    {
                        object result = cachedReadValueAsButtonMethod.Invoke(cachedInputAction, null);
                        if (result is bool pressed && pressed)
                        {
                            return true;
                        }
                    }
                }
                catch
                {
                }
            }

            try
            {
                return Input.GetMouseButton(1);
            }
            catch
            {
                return false;
            }
        }

        private bool IsAdsReleasedThisFrame()
        {
            if (!inputActionCached)
            {
                TryCacheInputAction();
            }

            if (cachedInputAction != null)
            {
                try
                {
                    if (cachedWasReleasedThisFrameMethod == null)
                    {
                        cachedWasReleasedThisFrameMethod = BossRush.Common.Utils.ReflectionCache.GetMethod(
                            cachedInputAction.GetType(),
                            "WasReleasedThisFrame",
                            BindingFlags.Public | BindingFlags.Instance
                        );
                    }

                    if (cachedWasReleasedThisFrameMethod != null)
                    {
                        object result = cachedWasReleasedThisFrameMethod.Invoke(cachedInputAction, null);
                        if (result is bool released && released)
                        {
                            return true;
                        }
                    }
                }
                catch
                {
                }
            }

            try
            {
                return Input.GetMouseButtonUp(1);
            }
            catch
            {
                return false;
            }
        }

        private void EnsureDragonKingAssetsLoaded()
        {
            if (DragonKingAssetManager.IsLoaded)
            {
                return;
            }

            try
            {
                string modPath = ModBehaviour.GetModPath();
                if (!string.IsNullOrEmpty(modPath))
                {
                    DragonKingAssetManager.LoadAssetBundleSync(modPath);
                }
            }
            catch (System.Exception e)
            {
                LogIfVerbose("加载龙王特效资源失败: " + e.Message);
            }
        }

        private static int GetPreviewObstacleLayers()
        {
            // 只检测墙壁和迷雾遮挡，不检测地面
            // 抛物线下降段必然穿过地面层，如果包含 groundLayerMask 会导致 hitObstacle 始终为 true
            try
            {
                return GameplayDataSettings.Layers.wallLayerMask |
                       GameplayDataSettings.Layers.fowBlockLayers;
            }
            catch
            {
                return LayerMask.GetMask("Wall", "Default");
            }
        }

        private static int GetGroundObstacleLayerMask()
        {
            // 排除 Ground 层和 Character 层，只检测真正的障碍物（墙壁等）
            int excludeMask = 0;
            int groundLayer = LayerMask.NameToLayer("Ground");
            if (groundLayer >= 0)
            {
                excludeMask |= (1 << groundLayer);
            }
            int characterLayer = LayerMask.NameToLayer("Character");
            if (characterLayer >= 0)
            {
                excludeMask |= (1 << characterLayer);
            }
            int triggerLayer = LayerMask.NameToLayer("Ignore Raycast");
            if (triggerLayer >= 0)
            {
                excludeMask |= (1 << triggerLayer);
            }
            // 只保留墙壁等实体障碍物
            try
            {
                return GameplayDataSettings.Layers.wallLayerMask;
            }
            catch
            {
                return LayerMask.GetMask("Wall", "Default");
            }
        }
    }

    internal class FenHuangLeapPreview : MonoBehaviour
    {
        private const int MarkerSegments = 24;

        private LineRenderer trajectoryLine;
        private LineRenderer landingRing;
        private Light markerLight;
        private Material trajectoryMaterial;
        private Material ringMaterial;
        private float pulseTime;

        public void Initialize()
        {
            trajectoryLine = CreateLineRenderer("Trajectory", 0.12f, 0.05f);
            landingRing = CreateLineRenderer("LandingRing", 0.06f, 0.06f);
            landingRing.loop = true;

            markerLight = gameObject.AddComponent<Light>();
            markerLight.type = LightType.Point;
            markerLight.range = 2.6f;
            markerLight.intensity = 2.2f;
            markerLight.shadows = LightShadows.None;
        }

        public void UpdatePreview(Vector3[] points, Vector3 landingPoint, bool valid)
        {
            if (trajectoryLine == null || landingRing == null)
            {
                return;
            }

            if (points == null || points.Length == 0)
            {
                trajectoryLine.positionCount = 0;
            }
            else
            {
                trajectoryLine.positionCount = points.Length;
                trajectoryLine.SetPositions(points);
            }

            Color color = valid ? new Color(1f, 0.55f, 0.15f, 0.95f) : new Color(1f, 0.18f, 0.18f, 0.95f);
            trajectoryLine.startColor = color;
            trajectoryLine.endColor = color;
            landingRing.startColor = color;
            landingRing.endColor = color;

            UpdateLandingRing(landingPoint, valid);

            if (markerLight != null)
            {
                markerLight.transform.position = landingPoint + Vector3.up * 0.35f;
                markerLight.color = color;
            }
        }

        private void Update()
        {
            pulseTime += Time.deltaTime * 4f;
            float pulse = 1f + Mathf.Sin(pulseTime) * 0.08f;

            if (landingRing != null)
            {
                landingRing.widthMultiplier = 0.06f * pulse;
            }

            if (markerLight != null)
            {
                markerLight.intensity = 2.2f + Mathf.Sin(pulseTime) * 0.35f;
            }
        }

        private void UpdateLandingRing(Vector3 landingPoint, bool valid)
        {
            if (landingRing == null)
            {
                return;
            }

            float radius = valid ? 0.85f : 0.95f;
            landingRing.positionCount = MarkerSegments;

            for (int i = 0; i < MarkerSegments; i++)
            {
                float angle = 360f * i / MarkerSegments;
                Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * radius;
                landingRing.SetPosition(i, landingPoint + offset + Vector3.up * 0.05f);
            }
        }

        private LineRenderer CreateLineRenderer(string childName, float startWidth, float endWidth)
        {
            GameObject child = new GameObject(childName);
            child.transform.SetParent(transform, false);

            LineRenderer line = child.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 0;
            line.numCapVertices = 4;
            line.numCornerVertices = 4;
            line.startWidth = startWidth;
            line.endWidth = endWidth;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.textureMode = LineTextureMode.Stretch;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            Material material = new Material(shader);
            material.color = new Color(1f, 0.55f, 0.15f, 0.95f);
            line.material = material;

            if (trajectoryMaterial == null)
            {
                trajectoryMaterial = material;
            }
            else
            {
                ringMaterial = material;
            }

            return line;
        }

        private void OnDestroy()
        {
            if (trajectoryMaterial != null)
            {
                Destroy(trajectoryMaterial);
            }

            if (ringMaterial != null)
            {
                Destroy(ringMaterial);
            }
        }
    }
}
