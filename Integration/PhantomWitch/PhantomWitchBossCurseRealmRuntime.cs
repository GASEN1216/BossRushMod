using System;
using System.Collections.Generic;
using Duckov.Buffs;
using UnityEngine;

namespace BossRush
{
    public sealed class PhantomWitchBossCurseRealmRuntime : MonoBehaviour
    {
        private static readonly Collider[] damageHitBuffer = new Collider[32];
        private static readonly HashSet<int> processedReceiverIds = new HashSet<int>();

        private Vector3 origin;
        private float damagePerTick;
        private float damageInterval;
        private float nextDamageTime;
        private CharacterMainControl caster;
        private PhantomWitchAbilityController controller;
        private GameObject visualMarker;
        private string terminateReason = "expired";
        private bool terminating;
        private int weaponTypeId = PhantomWitchConfig.PlaceholderScytheTypeId;

        public float Radius { get; private set; }
        public float RemainingDuration { get; private set; }
        public PhantomWitchPhase SpawnedInPhase { get; private set; }

        public void Initialize(Vector3 origin, float radius, float duration, PhantomWitchPhase phase)
        {
            this.origin = origin;
            Radius = radius;
            RemainingDuration = duration;
            SpawnedInPhase = phase;
            damagePerTick = PhantomWitchConfig.BossCurseRealmDamagePerTick;
            damageInterval = PhantomWitchConfig.BossCurseRealmDamageInterval;
            nextDamageTime = 0f;

            try
            {
                visualMarker = PhantomWitchAssetManager.CreateBossCurseRealmVisual(origin, radius, duration);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitch] [WARNING] Boss realm visual create failed: " + e.Message);
            }
        }

        internal void BindController(PhantomWitchAbilityController controller)
        {
            this.controller = controller;
            caster = controller != null ? controller.GetBossCharacterForRealmRuntime() : null;
            weaponTypeId = controller != null
                ? controller.GetCurrentWeaponTypeIdForRealmRuntime()
                : PhantomWitchConfig.PlaceholderScytheTypeId;
        }

        public void ForceTerminate(string reason)
        {
            if (terminating)
            {
                return;
            }

            terminating = true;
            terminateReason = reason;
            Destroy(gameObject);
        }

        private void Update()
        {
            if (terminating)
            {
                return;
            }

            RemainingDuration -= Time.deltaTime;
            if (Time.time >= nextDamageTime)
            {
                DealRealmDamage();
                nextDamageTime = Time.time + damageInterval;
            }

            if (RemainingDuration <= 0f)
            {
                ForceTerminate("expired");
            }
        }

        private void DealRealmDamage()
        {
            if (caster == null)
            {
                return;
            }

            int hitCount = Physics.OverlapSphereNonAlloc(
                origin,
                Radius,
                damageHitBuffer,
                FenHuangHalberdRuntime.DamageReceiverLayerMask);

            if (hitCount <= 0)
            {
                return;
            }

            Buff curseBuff = PhantomWitchAssetManager.GetCurseBuff();
            processedReceiverIds.Clear();
            for (int i = 0; i < hitCount; i++)
            {
                Collider col = damageHitBuffer[i];
                if (col == null)
                {
                    continue;
                }

                DamageReceiver receiver = FenHuangHalberdRuntime.TryGetDamageReceiver(col);
                if (receiver == null || receiver.health == null || receiver.health.IsDead)
                {
                    continue;
                }

                int receiverId = receiver.GetInstanceID();
                if (!processedReceiverIds.Add(receiverId))
                {
                    continue;
                }

                if (!Team.IsEnemy(caster.Team, receiver.Team))
                {
                    continue;
                }

                CharacterMainControl targetCharacter = receiver.health.TryGetCharacter();
                if (targetCharacter == caster)
                {
                    continue;
                }

                DamageInfo damageInfo = new DamageInfo(caster);
                damageInfo.damageType = DamageTypes.normal;
                damageInfo.damageValue = damagePerTick;
                damageInfo.damagePoint = receiver.transform.position;
                damageInfo.damageNormal = (receiver.transform.position - origin).normalized;
                damageInfo.fromWeaponItemID = weaponTypeId;
                damageInfo.crit = -1;
                damageInfo.AddElementFactor(ElementTypes.ghost, 1f);
                receiver.Hurt(damageInfo);

                if (curseBuff != null && targetCharacter != null)
                {
                    try
                    {
                        targetCharacter.AddBuff(curseBuff, caster, weaponTypeId);
                        PhantomWitchCurseSweatVfx.TryAttach(targetCharacter.gameObject);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private void OnDestroy()
        {
            if (visualMarker != null)
            {
                try
                {
                    Destroy(visualMarker);
                }
                catch
                {
                }
                visualMarker = null;
            }

            if (controller != null)
            {
                controller.NotifyBossCurseRealmRuntimeEnded(this, terminateReason);
            }
        }
    }
}
