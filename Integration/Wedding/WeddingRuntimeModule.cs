using System.Collections;
using UnityEngine;

namespace BossRush
{
    /// <summary>婚姻与婚礼建筑运行时状态的唯一 owner。</summary>
    internal sealed partial class WeddingRuntimeModule : BossRushRuntimeModuleBase
    {
        private ModBehaviour _owner;
        private bool _builtEventSubscribed;
        private bool _destroyedEventSubscribed;

        public override string ModuleName { get { return "Wedding"; } }

        internal bool HasAssetBundle { get { return weddingAssetBundle != null; } }

        public override void OnAwake(ModBehaviour owner)
        {
            _owner = owner;
        }

        public override void OnDestroy()
        {
            CleanupWeddingBuilding();
            _owner = null;
        }

        internal ModBehaviour Owner { get { return _owner; } }

        private Coroutine StartCoroutine(IEnumerator routine)
        {
            return _owner != null ? _owner.StartCoroutine(routine) : null;
        }

        private void StopCoroutine(Coroutine coroutine)
        {
            if (_owner != null && coroutine != null)
            {
                _owner.StopCoroutine(coroutine);
            }
        }
    }
}
