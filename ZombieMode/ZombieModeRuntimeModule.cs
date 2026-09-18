using System;
using System.Collections;
using UnityEngine;

namespace BossRush
{
    internal sealed class ZombieModeRuntimeModule : BossRushRuntimeModuleBase
    {
        private ModBehaviour owner;

        public override string ModuleName
        {
            get { return "ZombieMode"; }
        }

        public override void OnAwake(ModBehaviour owner)
        {
            this.owner = owner;
            // 入场回滚的欠账账本：订阅官方「经济加载完成」，经济一回来就把欠玩家的现金与
            // 邀请函补上（CR-2026-09-11-019）。订阅幂等，OnDestroy 成对退订。
            ZombieModeEntryDebt.Attach();
        }

        // 源码的爆炸复用同一份碰撞与去重缓冲；Hurt / Dead 回调只安排下一帧。
        // 每次请求使用已有 RunOnlyObjects，结束立刻移除，不让高频暴击留下整局协程记录。
        internal static void DeferExplosion(ModBehaviour owner, ZombieModeRunState run, int runId,
            Func<bool> valid, Action apply)
        {
            if (owner == null || run == null || valid == null || !valid()) return;
            CharacterMainControl player = CharacterMainControl.Main;
            LevelManager level = LevelManager.Instance;
            var record = new ZombieModeRunOnlyRecord { RunId = runId, Kind = ZombieModeRunOnlyObjectKind.Coroutine };
            Coroutine pending = null;
            record.CleanupAction = () =>
            {
                if (owner != null && pending != null) owner.StopCoroutine(pending);
                record.CleanupAction = null;
                run.RunOnlyObjects.Remove(record);
            };
            run.RunOnlyObjects.Add(record);
            try
            {
                pending = owner.StartCoroutine(ExplosionNextFrame(owner, run, record, player, level, valid, apply));
            }
            catch (Exception e)
            {
                record.CleanupAction = null;
                run.RunOnlyObjects.Remove(record);
                ModBehaviour.DevLog("[ZombieMode] explosion scheduling failed: " + e.Message);
            }
        }

        private static IEnumerator ExplosionNextFrame(ModBehaviour owner, ZombieModeRunState run,
            ZombieModeRunOnlyRecord record, CharacterMainControl player, LevelManager level,
            Func<bool> valid, Action apply)
        {
            yield return null;
            try
            {
                while (owner != null && record.CleanupAction != null && valid()
                    && player != null && ReferenceEquals(player, CharacterMainControl.Main)
                    && player.Health != null && !player.Health.IsDead
                    && ReferenceEquals(level, LevelManager.Instance))
                {
                    if (!owner.IsZombieModeRuntimePaused())
                    {
                        apply();
                        yield break;
                    }
                    yield return null;
                }
            }
            finally
            {
                record.CleanupAction = null;
                run.RunOnlyObjects.Remove(record);
            }
        }

        internal static void TriggerDoomPulse(CharacterMainControl player, int stacks,
            Action<Vector3, float, float> explode)
        {
            Vector3 center = player.transform.position;
            Vector3 forward = player.transform.forward;
            if (forward.sqrMagnitude <= 0.0001f)
            {
                forward = Vector3.forward;
            }

            float radius = 2.75f;
            float damage = 30f + 10f * (stacks - 1);
            float offsetDistance = 1.5f + 0.25f * (stacks - 1);
            for (int i = 0; i < 3; i++)
            {
                Vector3 offset = Quaternion.Euler(0f, 120f * i, 0f) * forward * offsetDistance;
                explode(center + offset, radius, damage);
            }
        }

        public override void OnDestroy()
        {
            ZombieModeEntryDebt.ResetStaticCaches();
            owner = null;
        }
    }
}
