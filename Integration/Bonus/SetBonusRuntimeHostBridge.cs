using System.Collections;
using UnityEngine;

namespace BossRush
{
    /// <summary>套装运行模块与原宿主入口之间的兼容桥。</summary>
    internal partial class SetBonusRuntimeModule
    {
        private ModBehaviour _owner;

        public override string ModuleName { get { return "SetBonus"; } }

        public override void OnAwake(ModBehaviour owner)
        {
            _owner = owner;
        }

        public override void OnDestroy()
        {
            _owner = null;
        }

        private Coroutine StartSetBonusCoroutine(IEnumerator routine)
        {
            return _owner != null && routine != null ? _owner.StartCoroutine(routine) : null;
        }

        private void StopSetBonusCoroutine(Coroutine coroutine)
        {
            if (_owner != null && coroutine != null)
            {
                _owner.StopCoroutine(coroutine);
            }
        }

        [System.Diagnostics.Conditional("BOSSRUSH_DEV")]
        private static void DevLog(string message)
        {
            ModBehaviour.DevLog(message);
        }
    }

    public partial class ModBehaviour
    {
        private void RegisterDragonSetEvents()
        {
            if (setBonusRuntime != null)
            {
                setBonusRuntime.RegisterDragonSetEvents();
            }
        }

        private void UnregisterDragonSetEvents()
        {
            if (setBonusRuntime != null)
            {
                setBonusRuntime.UnregisterDragonSetEvents();
            }
        }

        private void RegisterSetBonusEvents()
        {
            if (setBonusRuntime != null)
            {
                setBonusRuntime.RegisterSetBonusEvents();
            }
        }

        private void UnregisterSetBonusEvents()
        {
            if (setBonusRuntime != null)
            {
                setBonusRuntime.UnregisterSetBonusEvents();
            }
        }

        private void UpdateDragonDash()
        {
            if (setBonusRuntime != null)
            {
                setBonusRuntime.UpdateDragonDash();
            }
        }

        internal bool HasSetBonusElementHealing
        {
            get { return setBonusRuntime != null && setBonusRuntime.HasSetBonusElementHealing; }
        }

        internal bool IsDragonDashEnabledForRuntime
        {
            get { return config != null && config.enableDragonDash; }
        }
    }
}
