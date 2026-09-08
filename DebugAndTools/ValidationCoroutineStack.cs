using System;
using System.Collections;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>
    /// F3 自持整条协程调用栈。异常、取消和超时都必须从叶到根 Dispose，
    /// 否则父协程的 finally 不执行，临时物品、ERROR 控制权与替身后端会留在游戏里。
    /// 不依赖 Unity，执行夹具直接链接本文件验证这些退出路径。
    /// </summary>
    internal sealed class ValidationCoroutineStack : IEnumerator, IDisposable
    {
        private readonly List<IEnumerator> _stack = new List<IEnumerator>(8);
        private bool _disposed;
        public object Current { get; private set; }

        internal ValidationCoroutineStack(IEnumerator root)
        {
            if (root == null) throw new ArgumentNullException("root");
            _stack.Add(root);
        }

        public bool MoveNext()
        {
            if (_disposed) return false;
            Current = null;
            // 深层同步子协程也分帧，避免大量 yield break 挤在同一帧；阻止自引用栈。
            for (int steps = 0; steps < 64 && _stack.Count > 0; steps++)
            {
                IEnumerator top = _stack[_stack.Count - 1];
                if (!top.MoveNext())
                {
                    _stack.RemoveAt(_stack.Count - 1);
                    IDisposable completed = top as IDisposable;
                    if (completed != null) completed.Dispose();
                    continue;
                }
                object value = top.Current;
                IEnumerator child = value as IEnumerator;
                if (child == null)
                {
                    Current = value;
                    return true;
                }
                if (_stack.Count >= 128 || _stack.Contains(child))
                    throw new InvalidOperationException("validation_coroutine_cycle_or_depth_limit");
                _stack.Add(child);
            }
            return _stack.Count > 0;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Current = null;
            List<Exception> errors = null;
            for (int i = _stack.Count - 1; i >= 0; i--)
            {
                try
                {
                    IDisposable item = _stack[i] as IDisposable;
                    if (item != null) item.Dispose();
                }
                catch (Exception e)
                {
                    if (errors == null) errors = new List<Exception>();
                    errors.Add(e);
                }
            }
            _stack.Clear();
            if (errors != null) throw new AggregateException("validation_dispose_failed", errors);
        }

        public void Reset() { throw new NotSupportedException(); }
    }
}
