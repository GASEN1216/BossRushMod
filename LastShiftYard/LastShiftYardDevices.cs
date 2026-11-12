using System;
using System.Collections.Generic;
using BossRush.Utils;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 维修棚的两件现场设备（原型 yard-interactions-data.mjs，第五轮）。归敌人 owner：两件都只作用于巡逻。
    /// - 试车蜂鸣器：32 m 内最多 2 名普通巡逻（刷点类型 regular、流动、没被别的事叫走）走到维修棚西侧查看 10 秒再回原处，冷却 30 秒。
    /// - 检修蒸汽阀：在阀前 7 × 7 × 3.4 m 放 7 秒蒸汽，冷却 25 秒。只遮视线：官方 AI 的视线检查走 fowBlockLayers，
    ///   子弹走 blockBulletLayers（官方 AIMainBrain.CheckObsticle / Projectile），所以放一块 FowBlock 层触发盒，已锁定的扫射和子弹照样穿过。
    ///   看得见的蒸汽借官方烟雾弹（660）爆开后生成的 FowSmoke，寿命改成 7 秒；取不到时只剩遮挡盒，照样生效。
    /// 维修台试灯是纯装饰，不在这里。不产出物品，本趟保留冷却，下趟归零。
    /// </summary>
    internal sealed partial class LastShiftEnemies
    {
        internal const float BuzzerSeconds = 10f, BuzzerCooldown = 30f, BuzzerRadius = 32f, BuzzerReach = 23.5f;
        internal const int BuzzerGuards = 2;
        internal const float SteamSeconds = 7f, SteamCooldown = 25f;
        private const int SmokeGrenadeTypeId = 660;

        private readonly List<GameObject> deviceHosts = new List<GameObject>();
        private BoxCollider steamBlock;
        private GameObject steamFx;
        private GameObject smokePrefab;
        private bool smokeLookedUp;
        private float buzzerReady, steamReady, steamUntil = -1f;

        private void BuildDevices()
        {
            AddDevice("repairBuzzer", .5f, delegate { return L10n.T("按试车蜂鸣器", "Sound the test buzzer"); }, PressBuzzer);
            AddDevice("steamValve", .6f, delegate { return L10n.T("拧开检修蒸汽阀", "Open the service steam valve"); }, OpenSteam);
            try
            {
                int layer = LayerMask.NameToLayer("FowBlock");
                int bulletMask = Duckov.Utilities.GameplayDataSettings.Layers.blockBulletLayers.value | Duckov.Utilities.GameplayDataSettings.Layers.wallLayerMask.value;
                if (layer < 0 || (bulletMask & (1 << layer)) != 0)
                    Debug.LogWarning(LastShiftRaidScene.LogTag + " FowBlock 层不可用或会挡子弹，蒸汽只剩官方烟雾表现");
                else
                {
                    var go = new GameObject("LastShiftSteamBlock");
                    go.layer = layer;
                    go.transform.SetParent(world.Root.transform, false);
                    // three.js 体积中心 (-31, 7)、7 × 7、高 3.4。
                    Vector3 center = Three(-31f, 7f);
                    float ground;
                    if (world.TryGroundAt(center, out ground)) center.y = ground;
                    go.transform.position = center + Vector3.up * 1.7f;
                    steamBlock = go.AddComponent<BoxCollider>();
                    steamBlock.isTrigger = true;
                    steamBlock.size = new Vector3(7f, 3.4f, 7f);
                    go.SetActive(false);
                }
            }
            catch (Exception e) { Debug.LogWarning(LastShiftRaidScene.LogTag + " 蒸汽遮挡盒创建失败：" + e.Message); }
        }

        private void AddDevice(string id, float seconds, Func<string> label, Action action)
        {
            Transform anchor;
            if (!world.Interacts.TryGetValue(id, out anchor)) { Debug.LogWarning(LastShiftRaidScene.LogTag + " 缺少交互标记 " + id); return; }
            var host = new GameObject("LastShiftInteract_" + id);
            host.transform.SetParent(world.Root.transform, false);
            host.transform.position = anchor.position;
            deviceHosts.Add(host);
            var device = NPCInteractionGroupHelper.GetOrCreateStandaloneInteractable<LastShiftDeviceInteractable>(host.transform, "Interact", LastShiftWorld.InteractOffset(id), x =>
            {
                x.Bind(id, seconds, label, delegate { return !closed && valid != null && valid(); }, action);
                SphereCollider sensor = x.GetComponent<SphereCollider>();
                if (sensor != null) { sensor.radius = 1.2f; sensor.isTrigger = true; }
            });
            if (device == null) Debug.LogWarning(LastShiftRaidScene.LogTag + " 交互点创建失败：" + id);
        }

        private void PressBuzzer()
        {
            float now = Time.time;
            if (now < buzzerReady)
            {
                status(L10n.T("蜂鸣器还烫着，再等 ", "The buzzer is still hot. Give it ") + Mathf.CeilToInt(buzzerReady - now) + L10n.T(" 秒。", " s."), false);
                return;
            }
            buzzerReady = now + BuzzerCooldown;
            Transform anchor;
            Vector3 source = world.Interacts.TryGetValue("repairBuzzer", out anchor) ? anchor.position : Three(-40.2f, 5.5f);
            // 原型 targets：维修棚西侧 (-53.2, 5) 与 (-50.8, 5)。
            Vector3 first = Snap(Three(-53.2f, 5f)), second = Snap(Three(-50.8f, 5f));
            float until = now + BuzzerSeconds;
            int sent = 0;
            while (sent < BuzzerGuards)
            {
                Guard pick = null;
                float best = BuzzerRadius * BuzzerRadius;
                foreach (Guard g in guards)
                {
                    if (!IsAlive(g) || g.Ai == null || g.Duty != null || g.Boss || !g.Regular || g.Lured || g.Suspended) continue;
                    float d = (g.Character.transform.position - source).sqrMagnitude;
                    if (d > best) continue;
                    Vector3 target = sent == 0 ? first : second;
                    // 原型只派 10 秒内走得到的人（查看速度 2.4 m/s）；这里按直线距离近似。
                    if ((g.Character.transform.position - target).sqrMagnitude > BuzzerReach * BuzzerReach) continue;
                    best = d; pick = g;
                }
                if (pick == null) break;
                Lure(pick, sent == 0 ? first : second, until);
                sent++;
            }
            status(sent > 0
                ? L10n.T("蜂鸣器尖叫起来，有人往维修棚这边过来了。", "The buzzer shrieks. Someone's coming over to the shed to look.")
                : L10n.T("蜂鸣器叫了半天，没人过来。", "The buzzer shrieks away. Nobody comes."), false);
            Debug.Log(LastShiftRaidScene.LogTag + " BUZZER sent=" + sent);
        }

        private void OpenSteam()
        {
            float now = Time.time;
            if (now < steamReady)
            {
                status(L10n.T("阀门还在回压，再等 ", "The valve is still building pressure. Give it ") + Mathf.CeilToInt(steamReady - now) + L10n.T(" 秒。", " s."), false);
                return;
            }
            steamReady = now + SteamCooldown;
            steamUntil = now + SteamSeconds;
            if (steamBlock != null) steamBlock.gameObject.SetActive(true);
            PlaySteam();
            status(L10n.T("白汽一下涌出来，七八秒里谁也看不清谁。押运长画好的火线照样打得穿。",
                "Steam floods out. For seven or eight seconds nobody can see anybody, though the chief's line of fire still cuts through it."), false);
        }

        private void TickDevices(float now)
        {
            if (steamUntil < 0f || now < steamUntil) return;
            steamUntil = -1f;
            if (steamBlock != null) steamBlock.gameObject.SetActive(false);
            FadeSteam();
        }

        // 到点先撤官方烟雾的遮挡体、让粒子自然散，再销毁；寿命字段的语义没法离线核对，这一步保证 7 秒后不再遮视线。
        private void FadeSteam()
        {
            if (steamFx == null) return;
            try
            {
                FowSmoke smoke = steamFx.GetComponentInChildren<FowSmoke>();
                if (smoke != null)
                {
                    if (smoke.colParent != null) smoke.colParent.gameObject.SetActive(false);
                    if (smoke.particles != null) foreach (ParticleSystem p in smoke.particles) if (p != null) p.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }
                UnityEngine.Object.Destroy(steamFx, smoke != null ? Mathf.Max(.5f, smoke.particleFadeTime) : 0f);
            }
            catch (Exception) { UnityEngine.Object.Destroy(steamFx); }
            steamFx = null;
        }

        // 官方烟雾弹爆开后生成的那团烟（同 Mode E 的 PlaySmokeVFX）：只取一次预制件，临时物品当场销毁。
        private void PlaySteam()
        {
            try
            {
                if (!smokeLookedUp)
                {
                    smokeLookedUp = true;
                    Item item = null;
                    try
                    {
                        item = ItemAssetsCollection.InstantiateSync(SmokeGrenadeTypeId);
                        Skill_Grenade skill = item != null ? item.GetComponentInChildren<Skill_Grenade>() : null;
                        if (skill != null && skill.grenadePfb != null) smokePrefab = skill.grenadePfb.createOnExlode;
                    }
                    finally { if (item != null) UnityEngine.Object.Destroy(item.gameObject); }
                }
                if (smokePrefab == null) return;
                if (steamFx != null) UnityEngine.Object.Destroy(steamFx);
                Vector3 at = steamBlock != null ? steamBlock.transform.position - Vector3.up * 1.7f : Three(-31f, 7f);
                steamFx = UnityEngine.Object.Instantiate(smokePrefab, at, Quaternion.identity, world.Root.transform);
                FowSmoke smoke = steamFx.GetComponentInChildren<FowSmoke>();
                if (smoke != null) smoke.lifeTime = SteamSeconds;
            }
            catch (Exception e) { Debug.LogWarning(LastShiftRaidScene.LogTag + " 蒸汽表现失败（遮挡照常）：" + e.Message); }
        }

        private void DisposeDevices()
        {
            foreach (GameObject host in deviceHosts) if (host != null) UnityEngine.Object.Destroy(host);
            deviceHosts.Clear();
            if (steamFx != null) UnityEngine.Object.Destroy(steamFx);
            steamFx = null;
            if (steamBlock != null) UnityEngine.Object.Destroy(steamBlock.gameObject);
            steamBlock = null;
        }
    }
}
