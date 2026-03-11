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

        private readonly HashSet<int> detonatedTargets = new HashSet<int>();

        private Vector3 leapStartPoint;
        private Vector3 leapTargetPoint;
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
            leapTravelTime = 0.3f;

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
            float t = flightTime / leapTravelTime;
            
            // Linear horizontal interpolation
            Vector3 startFlat = leapStartPoint; startFlat.y = 0f;
            Vector3 targetFlat = leapTargetPoint; targetFlat.y = 0f;
            Vector3 currentFlat = Vector3.Lerp(startFlat, targetFlat, t);
            
            // Sine wave vertical interpolation for a forced 3.5 meter jump height
            float baseHeight = Mathf.Lerp(leapStartPoint.y, leapTargetPoint.y, t);
            float jumpOffset = Mathf.Sin(t * Mathf.PI) * 3.5f;
            
            Vector3 position = currentFlat + Vector3.up * (baseHeight + jumpOffset);

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

            BossRushAudioManager.Instance.PlayHalberdZadiSFX();

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

                DragonBreathWeaponConfig.TryAddFireEffectsToGraphic(fx);

                ParticleSystem[] particles = fx.GetComponentsInChildren<ParticleSystem>(true);
                foreach (var ps in particles)
                {
                    var main = ps.main;
                    main.loop = false;
                    main.duration = 0.5f;
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(10f, 25f);
                    main.startSizeMultiplier *= 3f;

                    var em = ps.emission;
                    em.rateOverDistance = 0;
                    em.rateOverTime = 0;
                    em.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, 60) });

                    var shape = ps.shape;
                    shape.shapeType = ParticleSystemShapeType.Sphere;
                    shape.radius = 1.5f;

                    var limitVelocity = ps.limitVelocityOverLifetime;
                    limitVelocity.enabled = true;
                    limitVelocity.limit = 0;
                    limitVelocity.dampen = 0.15f;
                    
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    ps.Play(true);
                }

                Light[] lights = fx.GetComponentsInChildren<Light>(true);
                Light light = null;
                foreach (var l in lights)
                {
                    l.intensity = 8f;
                    l.range = 6f;
                    l.color = new Color(1f, 0.45f, 0.05f);
                    light = l;
                }

                if (light == null)
                {
                    light = fx.AddComponent<Light>();
                    light.type = LightType.Point;
                    light.color = new Color(1f, 0.45f, 0.05f);
                    light.intensity = 8f;
                    light.range = 6f;
                }

                DetonationFader fader = fx.AddComponent<DetonationFader>();
                fader.Initialize(FenHuangHalberdConfig.DetonationEffectDuration, light, null);
                
                UnityEngine.Object.Destroy(fx, 1.5f);
            }
            catch
            {
            }
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
            transform.localScale = new Vector3(startScale.x * scale, startScale.y * scale, startScale.z * scale);

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
        private ParticleSystem[] cachedParticles;
        private bool emissionStopped;

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

            DragonBreathWeaponConfig.TryAddFireEffectsToGraphic(coreRoot.gameObject);

            ParticleSystem[] particles = coreRoot.GetComponentsInChildren<ParticleSystem>(true);
            cachedParticles = particles;
            foreach (var ps in particles)
            {
                var main = ps.main;
                main.loop = true;
                main.startSpeed = new ParticleSystem.MinMaxCurve(5f, 9f);
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.4f);
                main.startSizeMultiplier = 1.2f;
                
                var shape = ps.shape;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 10f;
                shape.radius = 0.5f;
                
                ps.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

                var em = ps.emission;
                em.rateOverDistance = 0;
                em.rateOverTime = new ParticleSystem.MinMaxCurve(50f);

                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.Play(true);
            }

            Light[] lights = coreRoot.GetComponentsInChildren<Light>(true);
            if (lights.Length > 0)
            {
                pointLight = lights[0];
                pointLight.intensity = 4f;
                pointLight.range = 4f;
                pointLight.color = new Color(1f, 0.42f, 0.08f);
            }
            else
            {
                pointLight = gameObject.AddComponent<Light>();
                pointLight.type = LightType.Point;
                pointLight.color = new Color(1f, 0.42f, 0.08f);
                pointLight.intensity = 4f;
                pointLight.range = 4f;
            }
        }

        private void Update()
        {
            if (duration <= 0f || coreRoot == null)
            {
                return;
            }

            elapsed += Time.deltaTime;
            float normalized = Mathf.Clamp01(elapsed / duration);

            if (pointLight != null)
            {
                pointLight.intensity = Mathf.Lerp(4f, 0f, normalized);
            }

            if (normalized >= 0.8f && !emissionStopped)
            {
                emissionStopped = true;
                if (cachedParticles != null)
                {
                    foreach (var ps in cachedParticles)
                    {
                        var em = ps.emission;
                        em.enabled = false;
                    }
                }
            }
        }

        private void OnDestroy()
        {
        }
    }
}

