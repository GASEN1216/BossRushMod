using System;
using System.Collections.Generic;
using BossRush.Common.Equipment;
using UnityEngine;

namespace BossRush
{
    public class FenHuangHalberdAction : EquipmentAbilityAction
    {
        private enum LeapState
        {
            None,
            Takeoff,
            Leaping,
            LandingFire,
            Recovery
        }

        private static readonly Collider[] hitBuffer = new Collider[24];
        private static FenHuangHalberdConfig configInstance;
        private static Sprite cachedExplosionSprite;

        private readonly HashSet<int> detonatedTargets = new HashSet<int>();

        private Vector3 leapStartPoint;
        private Vector3 leapTargetPoint;
        private Vector3 leapVelocity;
        private Vector3 leapDirection;
        private float leapTravelTime;
        private bool leapTargetPrepared;
        private LeapState leapState;
        private float phaseTime;
        private bool movementWasEnabled = true;
        private int landingPillarIndex;
        private float nextLandingPillarTime;

        public static void SetConfig(FenHuangHalberdConfig cfg)
        {
            configInstance = cfg;
        }

        public void ConfigureLeapTarget(Vector3 landingPoint)
        {
            leapTargetPoint = landingPoint;
            leapTargetPrepared = true;
        }

        protected override EquipmentAbilityConfig GetConfig()
        {
            if (configInstance == null)
            {
                configInstance = new FenHuangHalberdConfig();
            }

            return configInstance;
        }

        public override bool CanMove()
        {
            return false;
        }

        public override bool CanControlAim()
        {
            return false;
        }

        protected override bool IsReadyInternal()
        {
            if (characterController == null)
            {
                return false;
            }

            ItemAgent_MeleeWeapon melee = characterController.GetMeleeWeapon();
            if (melee != null && melee.Item != null)
            {
                return melee.Item.TypeID == FenHuangHalberdIds.WeaponTypeId;
            }

            var holdItemAgent = characterController.CurrentHoldItemAgent;
            if (holdItemAgent != null && holdItemAgent.Item != null)
            {
                return holdItemAgent.Item.TypeID == FenHuangHalberdIds.WeaponTypeId;
            }

            return leapTargetPrepared;
        }

        protected override bool OnAbilityStart()
        {
            if (characterController == null || characterController.movementControl == null)
            {
                return false;
            }

            if (!leapTargetPrepared)
            {
                LogIfVerbose("龙皇跃击启动失败：未设置落点");
                return false;
            }

            EnsureDragonKingAssetsLoaded();

            leapStartPoint = SnapToGround(characterController.transform.position, characterController.transform.position.y);
            leapTargetPoint = SnapToGround(leapTargetPoint, leapStartPoint.y);

            Vector3 flatDirection = leapTargetPoint - leapStartPoint;
            flatDirection.y = 0f;
            if (flatDirection.sqrMagnitude < 0.001f)
            {
                flatDirection = characterController.CurrentAimDirection;
                flatDirection.y = 0f;
            }
            if (flatDirection.sqrMagnitude < 0.001f)
            {
                flatDirection = characterController.transform.forward;
                flatDirection.y = 0f;
            }

            leapDirection = flatDirection.normalized;
            leapVelocity = FenHuangLeapMath.CalculateLaunchVelocityWithFixedTime(
                leapStartPoint,
                leapTargetPoint,
                1.0f
            );
            leapTravelTime = 1.0f;

            if (leapTravelTime <= 0.01f || float.IsNaN(leapTravelTime) || float.IsInfinity(leapTravelTime))
            {
                LogIfVerbose("龙皇跃击启动失败：无法计算跳跃轨迹");
                return false;
            }

            characterController.SetMoveInput(Vector3.zero);
            characterController.SetForceMoveVelocity(Vector3.zero);
            characterController.movementControl.ForceTurnTo(leapDirection);

            if (characterController.characterModel != null)
            {
                characterController.characterModel.ForcePlayAttackAnimation();
            }

            movementWasEnabled = characterController.movementControl.MovementEnabled;
            characterController.movementControl.MovementEnabled = false;
            PauseGroundConstraint(FenHuangHalberdConfig.TotalActionDuration + leapTravelTime + 0.5f);

            leapState = LeapState.Takeoff;
            phaseTime = 0f;
            landingPillarIndex = 0;
            nextLandingPillarTime = 0f;
            detonatedTargets.Clear();

            LogIfVerbose("龙皇跃击 - 开始释放！");
            return true;
        }

        protected override void OnAbilityUpdate(float deltaTime)
        {
            phaseTime += deltaTime;

            switch (leapState)
            {
                case LeapState.Takeoff:
                    UpdateTakeoff();
                    break;

                case LeapState.Leaping:
                    UpdateLeaping();
                    break;

                case LeapState.LandingFire:
                    UpdateLandingFire();
                    break;

                case LeapState.Recovery:
                    UpdateRecovery();
                    break;
            }
        }

        protected override void OnAbilityStop()
        {
            RestoreMovementControl();
            leapState = LeapState.None;
            leapTargetPrepared = false;
            phaseTime = 0f;
            landingPillarIndex = 0;
            nextLandingPillarTime = 0f;
            detonatedTargets.Clear();
            LogIfVerbose("龙皇跃击 - 技能结束");
        }

        protected override bool ShouldAutoConsumeStamina()
        {
            return false;
        }

        protected override bool CanUseHandWhileActive()
        {
            return false;
        }

        private void UpdateTakeoff()
        {
            if (characterController == null)
            {
                StopAction();
                return;
            }

            characterController.SetPosition(leapStartPoint);
            characterController.movementControl.ForceTurnTo(leapDirection);

            if (phaseTime >= FenHuangHalberdConfig.FissureCastTime)
            {
                leapState = LeapState.Leaping;
                phaseTime = 0f;
            }
        }

        private void UpdateLeaping()
        {
            if (characterController == null)
            {
                StopAction();
                return;
            }

            float flightTime = Mathf.Min(phaseTime, leapTravelTime);
            Vector3 position = FenHuangLeapMath.EvaluatePosition(leapStartPoint, leapVelocity, flightTime);
            characterController.SetPosition(position);
            characterController.movementControl.ForceTurnTo(leapDirection);

            if (phaseTime >= leapTravelTime)
            {
                BeginLanding();
            }
        }

        private void BeginLanding()
        {
            if (characterController == null)
            {
                StopAction();
                return;
            }

            characterController.SetPosition(leapTargetPoint);
            RestoreMovementControl();
            characterController.SetMoveInput(Vector3.zero);
            characterController.SetForceMoveVelocity(Vector3.zero);
            characterController.movementControl.ForceTurnTo(leapDirection);

            CreateDetonationEffect(leapTargetPoint);
            DealLandingImpactDamage(leapTargetPoint);

            leapState = LeapState.LandingFire;
            phaseTime = 0f;
            landingPillarIndex = 0;
            nextLandingPillarTime = 0f;

            LogIfVerbose("龙皇跃击 - 已抵达落点，开始生成火柱");
        }

        private void UpdateLandingFire()
        {
            while (landingPillarIndex < FenHuangHalberdConfig.FirePillarCount && phaseTime >= nextLandingPillarTime)
            {
                SpawnLandingPillar(landingPillarIndex);
                landingPillarIndex++;
                nextLandingPillarTime += FenHuangHalberdConfig.FirePillarInterval;
            }

            if (landingPillarIndex >= FenHuangHalberdConfig.FirePillarCount && phaseTime >= nextLandingPillarTime)
            {
                leapState = LeapState.Recovery;
                phaseTime = 0f;
            }
        }

        private void UpdateRecovery()
        {
            if (phaseTime >= FenHuangHalberdConfig.LeapLandingRecoverTime)
            {
                StopAction();
            }
        }

        private void SpawnLandingPillar(int index)
        {
            try
            {
                Vector3 pillarPos = GetLandingPillarPosition(index);

                GameObject pillarObj = new GameObject("FenHuang_FirePillar_" + index);
                pillarObj.transform.position = pillarPos;

                FenHuangFirePillarVisual visual = pillarObj.AddComponent<FenHuangFirePillarVisual>();
                visual.Initialize(FenHuangHalberdConfig.FirePillarDuration);

                PlayerLavaZone lavaComponent = pillarObj.AddComponent<PlayerLavaZone>();
                lavaComponent.Initialize(
                    FenHuangHalberdConfig.FirePillarDamage,
                    FenHuangHalberdConfig.FirePillarDamageInterval,
                    FenHuangHalberdConfig.FirePillarDuration,
                    FenHuangHalberdConfig.FirePillarRadius
                );

                UnityEngine.Object.Destroy(pillarObj, FenHuangHalberdConfig.FirePillarDuration + 0.1f);
                CheckDetonation(pillarPos);
            }
            catch (Exception e)
            {
                LogIfVerbose("火柱生成异常: " + e.Message + "\n" + e.StackTrace);
            }
        }

        private Vector3 GetLandingPillarPosition(int index)
        {
            if (index == 0)
            {
                return leapTargetPoint;
            }

            int ringCount = Mathf.Max(1, FenHuangHalberdConfig.FirePillarCount - 1);
            float angle = 360f * (index - 1) / ringCount;
            Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * FenHuangHalberdConfig.LandingFireRingRadius;
            return SnapToGround(leapTargetPoint + offset, leapTargetPoint.y);
        }

        private void CheckDetonation(Vector3 position)
        {
            try
            {
                int layerMask = -1;
                try
                {
                    layerMask = Duckov.Utilities.GameplayDataSettings.Layers.damageReceiverLayerMask;
                }
                catch
                {
                    layerMask = ~0;
                }

                int hitCount = Physics.OverlapSphereNonAlloc(
                    position,
                    FenHuangHalberdConfig.FirePillarRadius + 0.5f,
                    hitBuffer,
                    layerMask
                );

                for (int i = 0; i < hitCount; i++)
                {
                    Collider col = hitBuffer[i];
                    if (col == null)
                    {
                        continue;
                    }

                    DamageReceiver receiver = col.GetComponent<DamageReceiver>();
                    if (receiver == null)
                    {
                        continue;
                    }

                    if (!Team.IsEnemy(Teams.player, receiver.Team))
                    {
                        continue;
                    }

                    int targetId = receiver.GetInstanceID();
                    if (detonatedTargets.Contains(targetId))
                    {
                        continue;
                    }

                    int markCount = DragonFlameMarkTracker.GetMarkCount(receiver);
                    if (markCount <= 0)
                    {
                        continue;
                    }

                    TriggerDetonation(receiver, markCount);
                    detonatedTargets.Add(targetId);
                }
            }
            catch (Exception e)
            {
                LogIfVerbose("爆燃检测异常: " + e.Message);
            }
        }

        private void TriggerDetonation(DamageReceiver target, int markCount)
        {
            try
            {
                int consumed = DragonFlameMarkTracker.ConsumeMark(target);
                if (consumed <= 0)
                {
                    return;
                }

                float detonationDamage = consumed * FenHuangHalberdConfig.DetonationDamagePerMark;

                if (target.health != null)
                {
                    DamageInfo damageInfo = new DamageInfo(characterController);
                    damageInfo.damageValue = detonationDamage;
                    damageInfo.damageType = DamageTypes.normal;
                    damageInfo.damagePoint = target.transform.position;
                    damageInfo.AddElementFactor(ElementTypes.fire, 1f);
                    target.health.Hurt(damageInfo);
                }

                CreateDetonationEffect(target.transform.position);
                LogIfVerbose("爆燃！消耗 " + consumed + " 层印记，造成 " + detonationDamage + " 伤害");
            }
            catch (Exception e)
            {
                LogIfVerbose("爆燃执行异常: " + e.Message);
            }
        }

        private void RestoreMovementControl()
        {
            if (characterController == null || characterController.movementControl == null)
            {
                return;
            }

            characterController.movementControl.MovementEnabled = movementWasEnabled;
        }

        private void DealLandingImpactDamage(Vector3 center)
        {
            try
            {
                int layerMask = -1;
                try
                {
                    layerMask = Duckov.Utilities.GameplayDataSettings.Layers.damageReceiverLayerMask;
                }
                catch
                {
                    layerMask = ~0;
                }

                int hitCount = Physics.OverlapSphereNonAlloc(
                    center,
                    FenHuangHalberdConfig.LandingImpactRadius,
                    hitBuffer,
                    layerMask
                );

                for (int i = 0; i < hitCount; i++)
                {
                    Collider col = hitBuffer[i];
                    if (col == null) continue;

                    DamageReceiver receiver = col.GetComponent<DamageReceiver>();
                    if (receiver == null) continue;

                    if (!Team.IsEnemy(Teams.player, receiver.Team)) continue;

                    if (receiver.health == null || receiver.health.IsDead) continue;

                    DamageInfo dmg = new DamageInfo(characterController);
                    dmg.damageValue = FenHuangHalberdConfig.LandingImpactDamage;
                    dmg.damageType = DamageTypes.normal;
                    dmg.damagePoint = receiver.transform.position;
                    dmg.AddElementFactor(ElementTypes.fire, 1f);
                    receiver.health.Hurt(dmg);
                }

                LogIfVerbose("砸落伤害 - 命中 " + hitCount + " 个目标");
            }
            catch (Exception e)
            {
                LogIfVerbose("砸落伤害异常: " + e.Message);
            }
        }

        private Vector3 SnapToGround(Vector3 position, float fallbackY)
        {
            RaycastHit hit;
            Vector3 sample = position + Vector3.up * 8f;

            if (Physics.Raycast(sample, Vector3.down, out hit, 30f, Duckov.Utilities.GameplayDataSettings.Layers.groundLayerMask))
            {
                return hit.point;
            }

            position.y = fallbackY;
            return position;
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
            catch (Exception e)
            {
                LogIfVerbose("加载龙王特效资源失败: " + e.Message);
            }
        }

        private static void CreateDetonationEffect(Vector3 position)
        {
            try
            {
                GameObject fx = new GameObject("FenHuang_Detonation");
                fx.transform.position = position + Vector3.up * 0.8f;

                Light light = fx.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1f, 0.45f, 0.05f);
                light.intensity = 10f;
                light.range = 5f;
                light.shadows = LightShadows.None;

                // Add Particle System
                ParticleSystem ps = fx.AddComponent<ParticleSystem>();
                ParticleSystemRenderer psRenderer = fx.GetComponent<ParticleSystemRenderer>();
                
                // Configure Material
                Material mat = new Material(Shader.Find("Sprites/Default"));
                mat.color = new Color(1f, 0.35f, 0.05f, 1f);
                psRenderer.material = mat;
                psRenderer.renderMode = ParticleSystemRenderMode.Billboard;

                // Configure Main Module
                var main = ps.main;
                main.duration = 0.5f;
                main.loop = false;
                main.startLifetime = 0.6f;
                main.startSpeed = new ParticleSystem.MinMaxCurve(5f, 12f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.2f, 0.5f);
                main.startColor = new Color(1f, 0.4f, 0.05f, 1f);
                main.playOnAwake = true;

                // Configure Emission Module
                var emission = ps.emission;
                emission.rateOverTime = 0;
                emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, 40) });

                // Configure Shape Module
                var shape = ps.shape;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.5f;

                // Configure Color Over Lifetime
                var colorOverLifetime = ps.colorOverLifetime;
                colorOverLifetime.enabled = true;
                Gradient gradient = new Gradient();
                gradient.SetKeys(
                    new GradientColorKey[] { new GradientColorKey(new Color(1f, 0.6f, 0.1f), 0.0f), new GradientColorKey(new Color(1f, 0.1f, 0f), 1.0f) },
                    new GradientAlphaKey[] { new GradientAlphaKey(1.0f, 0.0f), new GradientAlphaKey(1.0f, 0.7f), new GradientAlphaKey(0.0f, 1.0f) }
                );
                colorOverLifetime.color = gradient;

                // Configure Size Over Lifetime
                var sizeOverLifetime = ps.sizeOverLifetime;
                sizeOverLifetime.enabled = true;
                AnimationCurve curve = new AnimationCurve();
                curve.AddKey(0.0f, 1.0f);
                curve.AddKey(1.0f, 0.0f);
                sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1.0f, curve);

                // Configure Limit Velocity over Lifetime (Drag)
                var limitVelocity = ps.limitVelocityOverLifetime;
                limitVelocity.enabled = true;
                limitVelocity.limit = 0;
                limitVelocity.dampen = 0.2f;

                DetonationFader fader = fx.AddComponent<DetonationFader>();
                // SpriteRenderer is no longer used for the explosion, pass null
                fader.Initialize(FenHuangHalberdConfig.DetonationEffectDuration, light, null);
                
                // Destroy object after particles finish
                UnityEngine.Object.Destroy(fx, 1.0f);
            }
            catch
            {
            }
        }
    }

    internal static class FenHuangLeapMath
    {
        private static readonly RaycastHit[] hitCache = new RaycastHit[4];

        public static float CalculateTravelTime(Vector3 start, Vector3 target, float verticalSpeed)
        {
            float gravity = Mathf.Max(Physics.gravity.magnitude, 0.001f);
            float upTime = verticalSpeed / gravity;
            float discriminant = upTime * verticalSpeed * 0.5f + start.y - target.y;
            float downTime = Mathf.Sqrt(Mathf.Max(0.001f, 2f * Mathf.Abs(discriminant) / gravity));
            return Mathf.Max(0.001f, upTime + downTime);
        }

        public static Vector3 CalculateLaunchVelocity(Vector3 start, Vector3 target, float verticalSpeed)
        {
            float totalTime = CalculateTravelTime(start, target, verticalSpeed);

            Vector3 startFlat = start;
            startFlat.y = 0f;

            Vector3 targetFlat = target;
            targetFlat.y = 0f;

            Vector3 direction = targetFlat - startFlat;
            float distance = direction.magnitude;
            if (distance > 0.001f)
            {
                direction /= distance;
            }
            else
            {
                direction = Vector3.zero;
            }

            float horizontalSpeed = distance / totalTime;
            return direction * horizontalSpeed + Vector3.up * verticalSpeed;
        }

        public static Vector3 CalculateLaunchVelocityWithFixedTime(Vector3 start, Vector3 target, float fixedTime)
        {
            float gravity = Mathf.Max(Physics.gravity.magnitude, 0.001f);

            Vector3 startFlat = start;
            startFlat.y = 0f;

            Vector3 targetFlat = target;
            targetFlat.y = 0f;

            Vector3 direction = targetFlat - startFlat;
            float distance = direction.magnitude;
            if (distance > 0.001f)
            {
                direction /= distance;
            }
            else
            {
                direction = Vector3.zero;
            }

            float horizontalSpeed = distance / fixedTime;
            
            // h = v0*t + 0.5*a*t^2 => v0 = (h - 0.5*a*t^2)/t
            float heightDiff = target.y - start.y;
            // gravity is positive magnitude, but acts negatively on y
            float verticalSpeed = (heightDiff - 0.5f * (-gravity) * fixedTime * fixedTime) / fixedTime;

            return direction * horizontalSpeed + Vector3.up * verticalSpeed;
        }

        public static Vector3 EvaluatePosition(Vector3 start, Vector3 velocity, float time)
        {
            return start + velocity * time + 0.5f * Physics.gravity * time * time;
        }

        public static Vector3[] BuildTrajectory(
            Vector3 start,
            Vector3 target,
            float verticalSpeed,
            int fragmentCount,
            int obstacleLayers,
            ref Vector3 hitPoint,
            out bool hitObstacle,
            out float travelTime)
        {
            travelTime = CalculateTravelTime(start, target, verticalSpeed);
            Vector3 velocity = CalculateLaunchVelocity(start, target, verticalSpeed);
            Vector3[] points = new Vector3[fragmentCount + 1];

            hitObstacle = false;

            for (int i = 0; i <= fragmentCount; i++)
            {
                float time = travelTime * i / fragmentCount;
                points[i] = EvaluatePosition(start, velocity, time);

                if (i > 0 && i < points.Length - 1 && !hitObstacle)
                {
                    Vector3 from = points[i - 1];
                    Vector3 to = points[i];
                    if (CheckObstacle(from, to, obstacleLayers, ref hitPoint))
                    {
                        hitObstacle = true;
                        hitPoint = from + (to - from).normalized * (hitPoint - from).magnitude;
                    }
                }

                if (hitObstacle)
                {
                    points[i] = hitPoint;
                }
            }

            return points;
        }

        private static bool CheckObstacle(Vector3 from, Vector3 to, int obstacleLayers, ref Vector3 hitPoint)
        {
            Vector3 direction = to - from;
            float distance = direction.magnitude;
            if (distance <= 0.001f)
            {
                return false;
            }

            direction /= distance;
            int hitCount = Physics.SphereCastNonAlloc(from, 0.2f, direction, hitCache, distance, obstacleLayers);
            if (hitCount > 0)
            {
                hitPoint = hitCache[0].point;
                return true;
            }

            return false;
        }
    }

    public class DetonationFader : MonoBehaviour
    {
        private float duration;
        private Light pointLight;
        private SpriteRenderer spriteRenderer;
        private float elapsed;
        private float startIntensity;
        private Color startColor;
        private Vector3 startScale;

        public void Initialize(float dur, Light light, SpriteRenderer sr)
        {
            duration = dur;
            pointLight = light;
            spriteRenderer = sr;
            elapsed = 0f;
            startIntensity = light != null ? light.intensity : 0f;
            startColor = sr != null ? sr.color : Color.white;
            startScale = transform.localScale;
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            float scale = Mathf.Lerp(1f, 2f, t);
            transform.localScale = new Vector3(startScale.x * scale, startScale.y * scale, 1f);

            float alpha = Mathf.Lerp(startColor.a, 0f, t * t);
            if (spriteRenderer != null)
            {
                spriteRenderer.color = new Color(startColor.r, startColor.g, startColor.b, alpha);
            }

            if (pointLight != null)
            {
                pointLight.intensity = Mathf.Lerp(startIntensity, 0f, t);
            }

            if (elapsed >= duration)
            {
                Destroy(gameObject);
            }
        }
    }

    public class FenHuangFirePillarVisual : MonoBehaviour
    {
        private float duration;
        private float elapsed;
        private Transform coreRoot;
        private Light pointLight;
        private Material coreMaterial;
        private Material auraMaterial;

        public void Initialize(float lifeTime)
        {
            duration = lifeTime;

            if (coreRoot == null)
            {
                BuildVisual();
            }

            elapsed = 0f;
        }

        private void BuildVisual()
        {
            coreRoot = new GameObject("FenHuangFireVisual").transform;
            coreRoot.SetParent(transform, false);

            GameObject inner = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            inner.name = "InnerCore";
            inner.transform.SetParent(coreRoot, false);
            inner.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            inner.transform.localScale = new Vector3(0.55f, 0.95f, 0.55f);
            Destroy(inner.GetComponent<Collider>());

            GameObject outer = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            outer.name = "OuterAura";
            outer.transform.SetParent(coreRoot, false);
            outer.transform.localPosition = new Vector3(0f, 0.75f, 0f);
            outer.transform.localScale = new Vector3(0.95f, 0.75f, 0.95f);
            Destroy(outer.GetComponent<Collider>());

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            coreMaterial = new Material(shader);
            coreMaterial.color = new Color(1f, 0.45f, 0.05f, 0.92f);

            auraMaterial = new Material(shader);
            auraMaterial.color = new Color(1f, 0.2f, 0.02f, 0.38f);

            Renderer innerRenderer = inner.GetComponent<Renderer>();
            if (innerRenderer != null)
            {
                innerRenderer.material = coreMaterial;
                innerRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                innerRenderer.receiveShadows = false;
            }

            Renderer outerRenderer = outer.GetComponent<Renderer>();
            if (outerRenderer != null)
            {
                outerRenderer.material = auraMaterial;
                outerRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                outerRenderer.receiveShadows = false;
            }

            pointLight = gameObject.AddComponent<Light>();
            pointLight.type = LightType.Point;
            pointLight.color = new Color(1f, 0.42f, 0.08f);
            pointLight.intensity = 3.6f;
            pointLight.range = 3.2f;
            pointLight.shadows = LightShadows.None;
        }

        private void Update()
        {
            if (duration <= 0f || coreRoot == null)
            {
                return;
            }

            elapsed += Time.deltaTime;
            float normalized = Mathf.Clamp01(elapsed / duration);
            float pulse = 1f + Mathf.Sin(Time.time * 12f) * 0.08f;

            coreRoot.localScale = new Vector3(pulse, 1f + normalized * 0.15f, pulse);

            if (pointLight != null)
            {
                pointLight.intensity = Mathf.Lerp(3.6f, 0.8f, normalized);
            }

            if (coreMaterial != null)
            {
                Color color = coreMaterial.color;
                color.a = Mathf.Lerp(0.92f, 0.2f, normalized);
                coreMaterial.color = color;
            }

            if (auraMaterial != null)
            {
                Color color = auraMaterial.color;
                color.a = Mathf.Lerp(0.38f, 0.08f, normalized);
                auraMaterial.color = color;
            }
        }

        private void OnDestroy()
        {
            if (coreMaterial != null)
            {
                Destroy(coreMaterial);
            }

            if (auraMaterial != null)
            {
                Destroy(auraMaterial);
            }
        }
    }
}

