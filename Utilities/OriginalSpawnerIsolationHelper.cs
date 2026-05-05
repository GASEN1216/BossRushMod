using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    internal sealed class OriginalSpawnerIsolationRecord
    {
        public GameObject GameObject;
        public bool WasActive;
        public bool WasEnabled;
        public bool HadCreatedValue;
        public bool WasCreated;
    }

    internal static class OriginalSpawnerIsolationHelper
    {
        private static readonly BindingFlags InstanceBindingFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static FieldInfo createdField;
        private static bool createdFieldCached;

        internal static int Disable(
            IList<OriginalSpawnerIsolationRecord> records,
            Func<CharacterSpawnerRoot, bool> shouldSkipSpawner)
        {
            if (records == null)
            {
                return 0;
            }

            records.Clear();

            CharacterSpawnerRoot[] spawners = UnityEngine.Object.FindObjectsOfType<CharacterSpawnerRoot>(true);
            if (spawners == null || spawners.Length <= 0)
            {
                return 0;
            }

            FieldInfo field = GetCreatedField();
            Scene activeScene = SceneManager.GetActiveScene();
            int disabled = 0;
            for (int i = 0; i < spawners.Length; i++)
            {
                CharacterSpawnerRoot spawner = spawners[i];
                if (spawner == null || spawner.gameObject == null)
                {
                    continue;
                }

                if (!spawner.gameObject.scene.IsValid() || spawner.gameObject.scene != activeScene)
                {
                    continue;
                }

                if (shouldSkipSpawner != null && shouldSkipSpawner(spawner))
                {
                    continue;
                }

                OriginalSpawnerIsolationRecord record = new OriginalSpawnerIsolationRecord();
                record.GameObject = spawner.gameObject;
                record.WasActive = spawner.gameObject.activeSelf;
                record.WasEnabled = spawner.enabled;
                record.HadCreatedValue = false;
                records.Add(record);

                try
                {
                    if (field != null)
                    {
                        object originalValue = field.GetValue(spawner);
                        if (originalValue is bool)
                        {
                            record.HadCreatedValue = true;
                            record.WasCreated = (bool)originalValue;
                        }
                        field.SetValue(spawner, true);
                    }
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[SpawnerIsolation] [WARNING] set created=true failed: " + e.Message);
                }

                try
                {
                    spawner.enabled = false;
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[SpawnerIsolation] [WARNING] disable component failed: " + e.Message);
                }

                try
                {
                    spawner.gameObject.SetActive(false);
                    disabled++;
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[SpawnerIsolation] [WARNING] set inactive failed: " + e.Message);
                }
            }

            return disabled;
        }

        internal static int Restore(IList<OriginalSpawnerIsolationRecord> records)
        {
            if (records == null)
            {
                return 0;
            }

            int restored = 0;
            for (int i = records.Count - 1; i >= 0; i--)
            {
                OriginalSpawnerIsolationRecord record = records[i];
                if (record == null || record.GameObject == null)
                {
                    continue;
                }

                CharacterSpawnerRoot spawner = record.GameObject.GetComponent<CharacterSpawnerRoot>();
                if (spawner != null)
                {
                    try
                    {
                        FieldInfo field = GetCreatedField();
                        if (field != null && record.HadCreatedValue)
                        {
                            field.SetValue(spawner, record.WasCreated);
                        }
                    }
                    catch (Exception e)
                    {
                        ModBehaviour.DevLog("[SpawnerIsolation] [WARNING] restore created failed: " + e.Message);
                    }

                    try
                    {
                        spawner.enabled = record.WasEnabled;
                    }
                    catch (Exception e)
                    {
                        ModBehaviour.DevLog("[SpawnerIsolation] [WARNING] restore enabled failed: " + e.Message);
                    }
                }

                try
                {
                    record.GameObject.SetActive(record.WasActive);
                    if (record.WasActive)
                    {
                        restored++;
                    }
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[SpawnerIsolation] [WARNING] restore active failed: " + e.Message);
                }
            }

            records.Clear();
            return restored;
        }

        private static FieldInfo GetCreatedField()
        {
            if (!createdFieldCached)
            {
                createdField = typeof(CharacterSpawnerRoot).GetField("created", InstanceBindingFlags);
                createdFieldCached = true;
            }

            return createdField;
        }
    }
}
