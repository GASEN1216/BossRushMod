using System;

namespace BossRush
{
    /// <summary>好感度系统的运行时状态与生命周期 owner。</summary>
    internal sealed class AffinityRuntimeModule : BossRushRuntimeModuleBase
    {
        private ModBehaviour _owner;
        private bool _affinityChangedSubscribed;
        private bool _levelUpSubscribed;
        private bool _cleanupCompleted;

        public override string ModuleName { get { return "Affinity"; } }

        public override void OnAwake(ModBehaviour owner)
        {
            _owner = owner;
            if (owner != null)
            {
                owner.AttachAffinityRuntimeModule(this);
            }
        }

        public override void OnDestroy()
        {
            Cleanup();

            ModBehaviour owner = _owner;
            if (owner != null)
            {
                owner.DetachAffinityRuntimeModule(this);
            }
            _owner = null;
        }

        /// <summary>在原 deferred-content hook 的位置初始化，保持 NPC 注册与事件订阅顺序。</summary>
        internal void InitializeAffinitySystem()
        {
            _cleanupCompleted = false;
            try
            {
                AffinityManager.Initialize();

                int affinityNpcCount = NPCModuleRegistry.RegisterAffinityConfigs();
                DevLog("[BossRush] NPC 好感度配置注册完成，数量: " + affinityNpcCount);

                if (!_affinityChangedSubscribed)
                {
                    AffinityManager.OnAffinityChanged += OnAffinityChanged;
                    _affinityChangedSubscribed = true;
                }

                if (!_levelUpSubscribed)
                {
                    AffinityManager.OnLevelUp += OnAffinityLevelUp;
                    _levelUpSubscribed = true;
                }

                DevLog("[BossRush] 好感度系统初始化完成");
            }
            catch (Exception e)
            {
                DevLog("[BossRush] [WARNING] 好感度系统初始化失败: " + e.Message);
            }
        }

        internal void TickAffinityRuntime()
        {
            AffinityManager.UpdateDeferredSave();
        }

        internal void OnAffinitySceneUnload()
        {
            AffinityUIManager.OnSceneUnload();
            AffinityManager.OnSceneUnload();
        }

        /// <summary>可由既有宿主清理槽调用；OnDestroy 再调用时保持幂等。</summary>
        internal void Cleanup()
        {
            if (_cleanupCompleted)
            {
                return;
            }

            try
            {
                if (_affinityChangedSubscribed)
                {
                    AffinityManager.OnAffinityChanged -= OnAffinityChanged;
                    _affinityChangedSubscribed = false;
                }

                if (_levelUpSubscribed)
                {
                    AffinityManager.OnLevelUp -= OnAffinityLevelUp;
                    _levelUpSubscribed = false;
                }

                AffinityManager.Shutdown();
                AffinityManager.ResetStaticCaches();
                AffinityUIManager.Cleanup();
            }
            catch (Exception e)
            {
                DevLog("[BossRush] [WARNING] Affinity runtime cleanup failed: " + e.Message);
            }

            _cleanupCompleted = true;
        }

        private void OnAffinityChanged(string npcId, int oldPoints, int newPoints)
        {
            int delta = newPoints - oldPoints;
            AffinityUIManager.ShowAffinityChange(npcId, delta);

            if (!string.IsNullOrEmpty(npcId) && _owner != null)
            {
                _owner.HandleSpouseFollowAffinityLoss(npcId);
                _owner.RefreshSpouseInteractionOptionsForNpc(npcId);
            }
        }

        private void OnAffinityLevelUp(string npcId, int newLevel)
        {
            AffinityUIManager.ShowLevelUpNotification(npcId, newLevel);
        }

        [System.Diagnostics.Conditional("BOSSRUSH_DEV")]
        private static void DevLog(string message)
        {
            ModBehaviour.DevLog(message);
        }
    }
}
