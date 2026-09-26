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

}
