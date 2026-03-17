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
        private const float LandingValidationRadius = 0.5f;
        private const float LandingValidationHeight = 2f;

        private readonly FenHuangHalberdConfig configInstance = new FenHuangHalberdConfig();
        private readonly Vector3[] previewPointsBuffer = new Vector3[PreviewFragmentCount + 1];
        private MethodInfo cachedReadValueAsButtonMethod;
        private MethodInfo cachedWasReleasedThisFrameMethod;

        private bool isPreviewing;
        private bool previewValid;
        private Vector3 previewLandingPoint;
        private Vector3 lastPreviewAimPoint;
        private const float PreviewAimChangeSqrThreshold = 0.04f;
        private GameObject previewObject;
        private FenHuangLeapPreview previewController;

        private static readonly Collider[] landingValidationBuffer = new Collider[8];

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
            return FenHuangHalberdRuntime.IsHoldingHalberd(CharacterMainControl.Main);
        }

        protected override void OnManagerInitialized()
        {
            FenHuangHalberdAction.SetConfig(configInstance);
            FenHuangHalberdRuntime.EnsureDragonKingAssetsLoaded();
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
            FenHuangHalberdRuntime.EnsureDragonKingAssetsLoaded();
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

            if (!IsGameplayInputAllowed())
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

            if (!FenHuangHalberdRuntime.IsHoldingHalberd(player))
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

        private bool CanStartPreview()
        {
            if (!abilityEnabled || abilityAction == null || targetCharacter == null || IsActionRunning)
            {
                return false;
            }

            FenHuangHalberdRuntime.EnsureDragonKingAssetsLoaded();
            return abilityAction.IsReady();
        }

        private void BeginPreview()
        {
            EnsurePreviewController();

            isPreviewing = true;
            previewValid = false;
            lastPreviewAimPoint = Vector3.positiveInfinity;
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
                if (!success)
                {
                    ShowInvalidLeapBubble();
                }
            }
            else if (isPreviewing)
            {
                LogIfVerbose("检测到 ADS 松开，但落点无效或轨迹被阻挡");
                ShowInvalidLeapBubble();
            }

            StopPreview();
        }

        private void ShowInvalidLeapBubble()
        {
            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (player != null && player.transform != null)
                {
                    Duckov.UI.DialogueBubbles.DialogueBubblesManager.Show(
                        "这里过不去哦",
                        player.transform,
                        2.5f,
                        false,
                        false,
                        -1f,
                        2f
                    );
                }
            }
            catch (System.Exception e)
            {
                LogIfVerbose("显示过不去气泡异常: " + e.Message);
            }
        }

        private void StopPreview()
        {
            isPreviewing = false;
            previewValid = false;

            if (previewObject != null)
            {
                previewObject.SetActive(false);
            }
        }

        private void EnsurePreviewController()
        {
            if (previewObject != null && previewController != null)
            {
                previewObject.SetActive(true);
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

            Vector3 aimPoint = FenHuangHalberdRuntime.GetAimPoint(targetCharacter);

            if ((aimPoint - lastPreviewAimPoint).sqrMagnitude < PreviewAimChangeSqrThreshold)
            {
                return;
            }
            lastPreviewAimPoint = aimPoint;

            Vector3 resolvedLandingPoint = FenHuangHalberdRuntime.SnapToGround(
                aimPoint,
                targetCharacter.transform.position.y
            );
            Vector3 previewOrigin = FenHuangHalberdRuntime.GetPreviewOrigin(targetCharacter);

            // 使用和实际飞行完全一致的 lerp + sine wave 弧线来构建预览轨迹
            FenHuangHalberdRuntime.FillTrajectory(previewPointsBuffer, previewOrigin, resolvedLandingPoint);

            // 沿实际飞行弧线检测障碍物
            Vector3 hitPoint = resolvedLandingPoint;
            bool hitObstacle = CheckTrajectoryObstacles(
                previewPointsBuffer,
                FenHuangHalberdRuntime.PreviewObstacleLayerMask,
                ref hitPoint
            );

            previewLandingPoint = resolvedLandingPoint;

            float horizontalDist = Vector3.Distance(
                new Vector3(previewOrigin.x, 0f, previewOrigin.z),
                new Vector3(resolvedLandingPoint.x, 0f, resolvedLandingPoint.z)
            );
            previewValid = !hitObstacle && horizontalDist > 0.3f && IsLandingPointValid(previewLandingPoint);

            // 始终在目标终点显示标记（光效、圆环、ban 图标），而非障碍物碰撞点
            Vector3 markerPoint = previewLandingPoint;
            if (previewController != null)
            {
                previewController.UpdatePreview(previewPointsBuffer, markerPoint, previewValid);
            }
        }

        /// <summary>
        /// 构建与实际飞行一致的 sine wave 弧线轨迹点
        /// 和 FenHuangHalberdAction.UpdateLeaping 使用完全相同的计算方式
        /// </summary>
        /// <summary>
        /// 沿轨迹点做 SphereCast 检测障碍物（跳过首尾点）
        /// </summary>
        private static bool CheckTrajectoryObstacles(Vector3[] points, int obstacleLayers, ref Vector3 hitPoint)
        {
            if (points == null || points.Length < 2)
            {
                return false;
            }

            RaycastHit hit;
            for (int i = 1; i < points.Length - 1; i++)
            {
                Vector3 from = points[i - 1];
                Vector3 to = points[i];
                Vector3 direction = to - from;
                float distance = direction.magnitude;
                if (distance <= 0.001f)
                {
                    continue;
                }

                direction /= distance;
                if (Physics.SphereCast(from, 0.2f, direction, out hit, distance, obstacleLayers))
                {
                    hitPoint = hit.point;
                    return true;
                }
            }

            return false;
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
            // 不限制距离，允许玩家跳到屏幕上任意位置

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
            int wallMask = FenHuangHalberdRuntime.WallLayerMask;

            if (wallMask == 0)
            {
                return true;
            }

            int hitCount = Physics.OverlapCapsuleNonAlloc(
                position,
                position + Vector3.up * LandingValidationHeight,
                LandingValidationRadius,
                landingValidationBuffer,
                wallMask
            );

            for (int i = 0; i < hitCount; i++)
            {
                Collider col = landingValidationBuffer[i];
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

        private static bool IsGameplayInputAllowed()
        {
            try
            {
                if (!InputManager.InputActived)
                    return false;
            }
            catch { return false; }

            try
            {
                if (Duckov.UI.View.ActiveView != null)
                    return false;
            }
            catch { }

            return true;
        }

        private void SuppressVanillaAdsIfNeeded()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null)
            {
                return;
            }

            bool holdingHalberd = FenHuangHalberdRuntime.IsHoldingHalberd(player);
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
        private bool currentValid = true;

        // 禁止图标（ban.png）
        private GameObject banIconObject;
        private SpriteRenderer banIconRenderer;
        private Texture2D banTexture;
        private static Texture2D cachedBanTexture;
        private static bool banTextureLoadAttempted;

        public void Initialize()
        {
            trajectoryLine = CreateLineRenderer("Trajectory", 0.12f, 0.05f);
            landingRing = CreateLineRenderer("LandingRing", 0.06f, 0.06f);
            landingRing.loop = true;

            // 创建禁止图标
            InitBanIcon();

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

            currentValid = valid;
            UpdateLandingRing(landingPoint, valid);
            UpdateBanIcon(landingPoint, valid);

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

            // 禁止图标脉冲动画 + billboard 面向摄像机
            if (!currentValid && banIconObject != null)
            {
                float iconScale = 1.2f * pulse;
                banIconObject.transform.localScale = new Vector3(iconScale, iconScale, iconScale);

                Camera cam = Camera.main;
                if (cam != null)
                {
                    banIconObject.transform.rotation = cam.transform.rotation;
                }
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

        private void InitBanIcon()
        {
            banIconObject = new GameObject("BanIcon");
            banIconObject.transform.SetParent(transform, false);

            banIconRenderer = banIconObject.AddComponent<SpriteRenderer>();
            banIconRenderer.sortingOrder = 100;

            // 加载 ban.png 贴图
            LoadBanTexture();

            if (banTexture != null)
            {
                Sprite sprite = Sprite.Create(
                    banTexture,
                    new Rect(0f, 0f, banTexture.width, banTexture.height),
                    new Vector2(0.5f, 0.5f),
                    banTexture.width // 1 像素 = 1 单位，后续用 localScale 控制大小
                );
                banIconRenderer.sprite = sprite;
            }

            banIconObject.SetActive(false);
        }

        private void LoadBanTexture()
        {
            // 优先使用缓存
            if (cachedBanTexture != null)
            {
                banTexture = cachedBanTexture;
                return;
            }

            if (banTextureLoadAttempted)
            {
                return;
            }

            try
            {
                string modPath = ModBehaviour.GetModPath();
                if (string.IsNullOrEmpty(modPath))
                {
                    return;
                }

                banTextureLoadAttempted = true;

                string banPath = System.IO.Path.Combine(modPath, "Assets", "ban.png");
                if (!System.IO.File.Exists(banPath))
                {
                    ModBehaviour.DevLog("[FenHuangHalberd] ban.png 未找到: " + banPath);
                    return;
                }

                byte[] data = System.IO.File.ReadAllBytes(banPath);
                banTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                banTexture.filterMode = FilterMode.Bilinear;
                if (ImageConversion.LoadImage(banTexture, data))
                {
                    cachedBanTexture = banTexture;
                }
                else
                {
                    Destroy(banTexture);
                    banTexture = null;
                }
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[FenHuangHalberd] 加载 ban.png 失败: " + e.Message);
            }
        }

        private void UpdateBanIcon(Vector3 landingPoint, bool valid)
        {
            if (banIconObject == null)
            {
                return;
            }

            if (valid)
            {
                banIconObject.SetActive(false);
                return;
            }

            banIconObject.SetActive(true);
            banIconObject.transform.position = landingPoint + Vector3.up * 1.2f;
            banIconObject.transform.localScale = Vector3.one * 1.2f;

            // 面向摄像机
            Camera cam = Camera.main;
            if (cam != null)
            {
                banIconObject.transform.rotation = cam.transform.rotation;
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

            if (banTexture != null && banTexture != cachedBanTexture)
            {
                Destroy(banTexture);
            }
        }
    }
}
