using System.Collections;
using UnityEngine;

namespace BossRush
{
    /// <summary>套装运行模块与原宿主入口之间的兼容桥。</summary>
    internal partial class SetBonusRuntimeModule
    {
        private ModBehaviour _owner;
        private bool destroyed;
        private readonly System.Collections.Generic.HashSet<OwnedSetBonusCoroutine> ownedCoroutines =
            new System.Collections.Generic.HashSet<OwnedSetBonusCoroutine>();

        private sealed class OwnedSetBonusCoroutine : IEnumerator, System.IDisposable
        {
            private readonly SetBonusRuntimeModule owner;
            private readonly IEnumerator routine;
            private bool disposed;
            internal Coroutine Handle;

            internal OwnedSetBonusCoroutine(SetBonusRuntimeModule owner, IEnumerator routine)
            {
                this.owner = owner;
                this.routine = routine;
            }

            public object Current { get { return routine.Current; } }
            public bool MoveNext()
            {
                if (disposed) return false;
                try
                {
                    if (routine.MoveNext()) return true;
                }
                catch
                {
                    Dispose();
                    throw;
                }
                Dispose();
                return false;
            }
            public void Reset() { throw new System.NotSupportedException(); }
            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                owner.ownedCoroutines.Remove(this);
                System.IDisposable disposable = routine as System.IDisposable;
                if (disposable != null) disposable.Dispose();
            }
        }

        public override string ModuleName { get { return "SetBonus"; } }

        public override void OnAwake(ModBehaviour owner)
        {
            _owner = owner;
            destroyed = false;
        }

        public override void OnDestroy()
        {
            if (destroyed) return;
            destroyed = true;
            try
            {
                try { UnregisterDragonSetEvents(); }
                finally { UnregisterSetBonusEvents(); }
            }
            finally
            {
                StopOwnedSetBonusCoroutines();
                CancelDragonDash();
                ResetSetBonusReflectionCaches();
                _owner = null;
            }
        }

        private Coroutine StartSetBonusCoroutine(IEnumerator routine)
        {
            if (_owner == null || routine == null || destroyed) return null;
            OwnedSetBonusCoroutine tracked = new OwnedSetBonusCoroutine(this, routine);
            ownedCoroutines.Add(tracked);
            try { tracked.Handle = _owner.StartCoroutine(tracked); }
            catch { tracked.Dispose(); throw; }
            return tracked.Handle;
        }

        private void StopSetBonusCoroutine(Coroutine coroutine)
        {
            if (coroutine == null) return;
            OwnedSetBonusCoroutine tracked = null;
            foreach (OwnedSetBonusCoroutine candidate in ownedCoroutines)
            {
                if (candidate.Handle == coroutine) { tracked = candidate; break; }
            }
            try
            {
                if (_owner != null) _owner.StopCoroutine(coroutine);
            }
            finally
            {
                if (tracked != null) tracked.Dispose();
            }
        }

        private void StopOwnedSetBonusCoroutines()
        {
            OwnedSetBonusCoroutine[] pending = new OwnedSetBonusCoroutine[ownedCoroutines.Count];
            ownedCoroutines.CopyTo(pending);
            foreach (OwnedSetBonusCoroutine tracked in pending)
            {
                try
                {
                    if (_owner != null && tracked.Handle != null) _owner.StopCoroutine(tracked.Handle);
                }
                catch (System.Exception e) { DevLog("[SetBonus] Stop coroutine failed: " + e.Message); }
                finally { tracked.Dispose(); }
            }
        }

        [System.Diagnostics.Conditional("BOSSRUSH_DEV")]
        private static void DevLog(string message)
        {
            ModBehaviour.DevLog(message);
        }
    }

}
