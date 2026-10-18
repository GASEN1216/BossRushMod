using System;
using System.Collections;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class ZombieModeRuntimeModule
    {
        // 源码的爆炸复用同一份碰撞与去重缓冲；Hurt / Dead 回调只安排下一帧。
        // 每次请求使用已有 RunOnlyObjects，结束立刻移除，不让高频暴击留下整局协程记录。
        internal static void DeferExplosion(ModBehaviour owner, ZombieModeRunState run, int runId,
            Func<bool> valid, Action apply, bool worldEffect = false)
        {
            if (owner == null || run == null || valid == null || !valid()) return;
            CharacterMainControl player = CharacterMainControl.Main;
            LevelManager level = LevelManager.Instance;
            bool connected = BossRushCoop.IsConnected, host = BossRushCoop.IsHost;
            string room = BossRushCoop.RunId, scene = BossRushCoop.SceneToken;
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
                pending = owner.StartCoroutine(ExplosionNextFrame(owner, run, record, player, level,
                    valid, apply, connected, host, room, scene, worldEffect && connected && host));
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
            Func<bool> valid, Action apply, bool connected, bool host, string room, string scene,
            bool worldAuthority)
        {
            yield return null;
            try
            {
                while (owner != null && record.CleanupAction != null && valid()
                    && connected == BossRushCoop.IsConnected
                    && (!connected || (host == BossRushCoop.IsHost && room == BossRushCoop.RunId && scene == BossRushCoop.SceneToken))
                    // 世界技能属于房主当前局，房主倒地后仍要命中继续作战的队友。
                    // 个人效果仍绑定原角色，不能借世界权限跨过个人死亡或换角色。
                    && (worldAuthority || (player != null && ReferenceEquals(player, CharacterMainControl.Main)
                        && player.Health != null && !player.Health.IsDead))
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

    }
}
