using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    internal sealed class OriginalCharacterIsolationRecord
    {
        public GameObject GameObject;
        public bool WasActive;
        public bool WasEnabled;
    }

    internal static class OriginalCharacterIsolationHelper
    {
        internal static int Disable(
            IList<OriginalCharacterIsolationRecord> records,
            Func<CharacterMainControl, bool> shouldSkipCharacter)
        {
            if (records == null)
            {
                return 0;
            }

            records.Clear();

            CharacterMainControl[] characters = UnityEngine.Object.FindObjectsOfType<CharacterMainControl>(true);
            if (characters == null || characters.Length <= 0)
            {
                return 0;
            }

            Scene activeScene = SceneManager.GetActiveScene();
            int disabled = 0;
            for (int i = 0; i < characters.Length; i++)
            {
                CharacterMainControl character = characters[i];
                if (character == null || character.gameObject == null)
                {
                    continue;
                }

                if (!character.gameObject.scene.IsValid() || character.gameObject.scene != activeScene)
                {
                    continue;
                }

                if (shouldSkipCharacter != null && shouldSkipCharacter(character))
                {
                    continue;
                }

                OriginalCharacterIsolationRecord record = new OriginalCharacterIsolationRecord();
                record.GameObject = character.gameObject;
                record.WasActive = character.gameObject.activeSelf;
                record.WasEnabled = character.enabled;
                records.Add(record);

                try
                {
                    character.enabled = false;
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[CharacterIsolation] [WARNING] disable character failed: " + e.Message);
                }

                try
                {
                    character.gameObject.SetActive(false);
                    disabled++;
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[CharacterIsolation] [WARNING] set inactive failed: " + e.Message);
                }
            }

            return disabled;
        }

        internal static int Restore(IList<OriginalCharacterIsolationRecord> records)
        {
            if (records == null)
            {
                return 0;
            }

            int restored = 0;
            for (int i = records.Count - 1; i >= 0; i--)
            {
                OriginalCharacterIsolationRecord record = records[i];
                if (record == null || record.GameObject == null)
                {
                    continue;
                }

                CharacterMainControl character = record.GameObject.GetComponent<CharacterMainControl>();
                if (character != null)
                {
                    try
                    {
                        character.enabled = record.WasEnabled;
                    }
                    catch (Exception e)
                    {
                        ModBehaviour.DevLog("[CharacterIsolation] [WARNING] restore enabled failed: " + e.Message);
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
                    ModBehaviour.DevLog("[CharacterIsolation] [WARNING] restore active failed: " + e.Message);
                }
            }

            records.Clear();
            return restored;
        }
    }
}
