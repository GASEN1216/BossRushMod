using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    internal sealed class RuntimeScope
    {
        private readonly string scopeName;
        private readonly MonoBehaviour coroutineOwner;
        private readonly List<GameObject> objects = new List<GameObject>();
        private readonly List<Coroutine> coroutines = new List<Coroutine>();
        private readonly List<Action> cleanupActions = new List<Action>();

        internal RuntimeScope(string scopeName, MonoBehaviour coroutineOwner)
        {
            this.scopeName = string.IsNullOrEmpty(scopeName) ? "RuntimeScope" : scopeName;
            this.coroutineOwner = coroutineOwner;
        }

        internal void RegisterObject(GameObject obj)
        {
            if (obj != null && !objects.Contains(obj))
            {
                objects.Add(obj);
            }
        }

        internal void RegisterCoroutine(Coroutine coroutine)
        {
            if (coroutine != null && !coroutines.Contains(coroutine))
            {
                coroutines.Add(coroutine);
            }
        }

        internal void RegisterCleanup(Action cleanup)
        {
            if (cleanup != null)
            {
                cleanupActions.Add(cleanup);
            }
        }

        internal void Clear(string reason)
        {
            for (int i = cleanupActions.Count - 1; i >= 0; i--)
            {
                try
                {
                    cleanupActions[i]();
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[" + scopeName + "] cleanup action failed (" + reason + "): " + e.Message);
                }
            }

            cleanupActions.Clear();

            if (coroutineOwner != null)
            {
                for (int i = coroutines.Count - 1; i >= 0; i--)
                {
                    Coroutine coroutine = coroutines[i];
                    if (coroutine == null)
                    {
                        continue;
                    }

                    try
                    {
                        coroutineOwner.StopCoroutine(coroutine);
                    }
                    catch (Exception e)
                    {
                        ModBehaviour.DevLog("[" + scopeName + "] coroutine cleanup failed (" + reason + "): " + e.Message);
                    }
                }
            }

            coroutines.Clear();

            for (int i = objects.Count - 1; i >= 0; i--)
            {
                GameObject obj = objects[i];
                if (obj == null)
                {
                    continue;
                }

                try
                {
                    UnityEngine.Object.Destroy(obj);
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[" + scopeName + "] object cleanup failed (" + reason + "): " + e.Message);
                }
            }

            objects.Clear();
        }
    }
}
