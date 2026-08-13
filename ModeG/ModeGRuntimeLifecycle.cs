using System;

namespace BossRush
{
    internal sealed partial class ModeGRuntimeModule
    {
        private void SubscribePlayerDeath()
        {
            if (_playerDeadSubscribed) return;
            try
            {
                Health.OnDead += HandlePlayerDeadEvent;
                _playerDeadSubscribed = true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeG] [WARNING] 玩家死亡订阅失败: " + e.Message);
            }
        }

        private void UnsubscribePlayerDeath()
        {
            if (!_playerDeadSubscribed) return;
            try { Health.OnDead -= HandlePlayerDeadEvent; } catch { /* no-throw cleanup */ }
            _playerDeadSubscribed = false;
        }

        private void HandlePlayerDeadEvent(Health health, DamageInfo info)
        {
            try
            {
                if (_state == null || _ended) return;
                CharacterMainControl main = CharacterMainControl.Main;
                if (main == null || !ReferenceEquals(health, main.Health)) return;
                ModeGDeathRouting.HandlePlayerDeath(this, health, info);
            }
            catch { /* no-throw event boundary */ }
        }

        private static void DestroyBoss(CharacterMainControl boss)
        {
            try
            {
                if (boss != null && boss.gameObject != null)
                {
                    UnityEngine.Object.Destroy(boss.gameObject);
                }
            }
            catch { /* no-throw cleanup */ }
        }
    }
}
