using System;
using UnityEngine;
using UnityEngine.Events;

namespace BossRush
{
    /// <summary>独立可破坏部位；没有角色身份，不进波次、击杀统计或掉落。</summary>
    internal sealed class EclipseBloomPetal : MonoBehaviour
    {
        private EclipseBloomAbilityController owner;
        private HealthSimpleBase health;
        private int index;
        private bool subscribed;
        private bool broken;

        internal static EclipseBloomPetal Create(EclipseBloomAbilityController controller, int petalIndex,
            Vector3 position, float hitPoints, Teams team)
        {
            GameObject go = new GameObject("EclipseBloom_PetalHit_" + petalIndex);
            go.SetActive(false);
            try
            {
                go.transform.position = position;
                int layer = LayerMask.NameToLayer("DamageReceiver");
                if (layer < 0) throw new InvalidOperationException("petal_damage_layer_missing");
                go.layer = layer;
                EclipseBloomPetal petal = go.AddComponent<EclipseBloomPetal>();
                petal.owner = controller;
                petal.index = petalIndex;
                CapsuleCollider collider = go.AddComponent<CapsuleCollider>();
                collider.direction = 2;
                collider.height = 3.4f;
                collider.radius = 0.78f;
                collider.center = new Vector3(0f, 0.10f, -0.05f);
                collider.isTrigger = false;
                Rigidbody rigidbody = go.AddComponent<Rigidbody>();
                rigidbody.isKinematic = true;
                rigidbody.useGravity = false;
                DamageReceiver receiver = go.AddComponent<DamageReceiver>();
                receiver.useSimpleHealth = true;
                receiver.OnHurtEvent = new UnityEvent<DamageInfo>();
                receiver.OnDeadEvent = new UnityEvent<DamageInfo>();
                petal.health = go.AddComponent<HealthSimpleBase>();
                petal.health.team = team;
                petal.health.maxHealthValue = hitPoints;
                petal.health.dmgReceiver = receiver;
                receiver.simpleHealth = petal.health;
                petal.health.OnDeadEvent += petal.OnBroken;
                petal.health.OnHurtEvent += petal.OnHurt;
                petal.subscribed = true;
                go.SetActive(true);
                IgnoreContact(collider, CharacterMainControl.Main);
                IgnoreContact(collider, controller.GetComponent<CharacterMainControl>());
                return petal;
            }
            catch
            {
                UnityEngine.Object.Destroy(go);
                throw;
            }
        }

        private static void IgnoreContact(Collider collider, CharacterMainControl character)
        {
            if (character == null) return;
            Collider other = character.GetComponent<Collider>();
            if (other != null) Physics.IgnoreCollision(collider, other, true);
        }

        private void OnHurt(DamageInfo info)
        {
            if (!broken && owner != null) owner.NotifyPetalHurt(index, info.damagePoint);
        }

        private void OnBroken(DamageInfo info)
        {
            if (broken) return;
            broken = true;
            if (owner != null) owner.BreakPetal(index);
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (subscribed && health != null) { health.OnDeadEvent -= OnBroken; health.OnHurtEvent -= OnHurt; }
            subscribed = false;
            owner = null;
        }
    }
}
