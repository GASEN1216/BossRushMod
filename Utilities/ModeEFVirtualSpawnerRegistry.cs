// E/F 共用的虚拟 spawner 登记。保留原反射目标、静态访问器和清理时机。
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace BossRush
{
    internal sealed class ModeEFVirtualSpawnerRegistry
    {
        /// <summary>Mode E 专用的虚拟 CharacterSpawnerRoot，用于让 BossLiveMapMod 检测到 Mode E 生成的敌人</summary>
        private CharacterSpawnerRoot modeEVirtualSpawnerRoot = null;

        /// <summary>虚拟 SpawnerRoot 中已登记的 Mode E 敌人，避免重复 AddCreatedCharacter。</summary>
        private readonly HashSet<CharacterMainControl> modeESpawnerRootRegisteredEnemies = new HashSet<CharacterMainControl>();

        private static FieldInfo modeESpawnerRootCreatedCharactersField = null;
        private static PropertyInfo modeESpawnerRootCreatedCharactersProperty = null;
        private static bool modeESpawnerRootCreatedCharactersAccessorCached = false;
        private static bool modeESpawnerRootCreatedCharactersAccessorMissingLogged = false;

        internal void ClearRegisteredEnemies()
        {
            modeESpawnerRootRegisteredEnemies.Clear();
        }

        #region Mode E BossLiveMapMod 集成

        /// <summary>
        /// 获取或创建 Mode E 专用的虚拟 CharacterSpawnerRoot
        /// BossLiveMapMod 通过遍历 CharacterSpawnerRoot.CreatedCharacters 来发现敌人，
        /// Mode E 的敌人通过 modeEHost.SpawnEnemyCore 直接生成，不经过游戏原版 spawner 系统，
        /// 因此需要创建一个虚拟的 CharacterSpawnerRoot 来注册这些敌人
        /// </summary>
        private CharacterSpawnerRoot GetOrCreateModeESpawnerRoot()
        {
            if (modeEVirtualSpawnerRoot != null) return modeEVirtualSpawnerRoot;

            try
            {
                GameObject spawnerObj = new GameObject("ModeE_VirtualSpawnerRoot");
                UnityEngine.Object.DontDestroyOnLoad(spawnerObj);
                modeEVirtualSpawnerRoot = spawnerObj.AddComponent<CharacterSpawnerRoot>();
                // This virtual root is only a registry bridge; keep Update/Init from entering the vanilla spawn pipeline.
                modeEVirtualSpawnerRoot.enabled = false;
                ModBehaviour.DevLog("[ModeE] 创建虚拟 CharacterSpawnerRoot 用于 BossLiveMapMod 集成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeE] [ERROR] 创建虚拟 CharacterSpawnerRoot 失败: " + e.Message);
            }

            return modeEVirtualSpawnerRoot;
        }

        private System.Collections.IList GetModeESpawnerRootCreatedCharactersList()
        {
            if (modeEVirtualSpawnerRoot == null)
            {
                return null;
            }

            try
            {
                if (!modeESpawnerRootCreatedCharactersAccessorCached)
                {
                    const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                    Type rootType = typeof(CharacterSpawnerRoot);

                    modeESpawnerRootCreatedCharactersField =
                        rootType.GetField("CreatedCharacters", flags) ??
                        rootType.GetField("createdCharacters", flags);

                    if (modeESpawnerRootCreatedCharactersField == null)
                    {
                        modeESpawnerRootCreatedCharactersProperty =
                            rootType.GetProperty("CreatedCharacters", flags) ??
                            rootType.GetProperty("createdCharacters", flags);
                    }

                    modeESpawnerRootCreatedCharactersAccessorCached = true;

                    if (modeESpawnerRootCreatedCharactersField == null &&
                        modeESpawnerRootCreatedCharactersProperty == null &&
                        !modeESpawnerRootCreatedCharactersAccessorMissingLogged)
                    {
                        modeESpawnerRootCreatedCharactersAccessorMissingLogged = true;
                        ModBehaviour.DevLog("[ModeE] [WARNING] 未找到虚拟 SpawnerRoot 的 createdCharacters/CreatedCharacters 访问器");
                    }
                }

                if (modeESpawnerRootCreatedCharactersField != null)
                {
                    return modeESpawnerRootCreatedCharactersField.GetValue(modeEVirtualSpawnerRoot) as System.Collections.IList;
                }

                if (modeESpawnerRootCreatedCharactersProperty != null)
                {
                    return modeESpawnerRootCreatedCharactersProperty.GetValue(modeEVirtualSpawnerRoot, null) as System.Collections.IList;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeE] [WARNING] 获取虚拟 SpawnerRoot CreatedCharacters 失败: " + e.Message);
            }

            return null;
        }

        private void RemoveModeEEnemyFromSpawnerRootList(CharacterMainControl character)
        {
            try
            {
                System.Collections.IList list = GetModeESpawnerRootCreatedCharactersList();
                if (list == null)
                {
                    return;
                }

                for (int i = list.Count - 1; i >= 0; i--)
                {
                    object entry = list[i];
                    if (entry == null)
                    {
                        list.RemoveAt(i);
                        continue;
                    }

                    CharacterMainControl existing = entry as CharacterMainControl;
                    if (existing != null && object.ReferenceEquals(existing, character))
                    {
                        list.RemoveAt(i);
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// 从虚拟 CharacterSpawnerRoot 中移除敌人，防止 CreatedCharacters 列表无限膨胀
        /// 通过反射获取 CreatedCharacters 列表（无公开 Remove API）
        /// </summary>
        internal void UnregisterModeEEnemyFromSpawnerRoot(CharacterMainControl character)
        {
            try
            {
                modeESpawnerRootRegisteredEnemies.Remove(character);

                if (modeEVirtualSpawnerRoot == null) return;

                RemoveModeEEnemyFromSpawnerRootList(character);
            }
            catch { }
        }

        /// <summary>
        /// 将 Mode E 生成的敌人注册到虚拟 CharacterSpawnerRoot，
        /// 使 BossLiveMapMod 能通过标准流程检测到这些敌人
        /// </summary>
        internal void RegisterModeEEnemyToSpawnerRoot(CharacterMainControl character)
        {
            try
            {
                if (character == null)
                {
                    return;
                }

                CharacterSpawnerRoot root = GetOrCreateModeESpawnerRoot();
                if (root != null)
                {
                    RemoveModeEEnemyFromSpawnerRootList(character);
                    modeESpawnerRootRegisteredEnemies.Add(character);
                    root.AddCreatedCharacter(character);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeE] [WARNING] RegisterModeEEnemyToSpawnerRoot 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 清理 Mode E 虚拟 CharacterSpawnerRoot
        /// </summary>
        internal void CleanupModeEVirtualSpawnerRoot()
        {
            try
            {
                if (modeEVirtualSpawnerRoot != null)
                {
                    try
                    {
                        System.Collections.IList list = GetModeESpawnerRootCreatedCharactersList();
                        if (list != null)
                        {
                            list.Clear();
                        }
                    }
                    catch { }

                    if (modeEVirtualSpawnerRoot.gameObject != null)
                    {
                        UnityEngine.Object.Destroy(modeEVirtualSpawnerRoot.gameObject);
                    }
                    modeEVirtualSpawnerRoot = null;
                    modeESpawnerRootRegisteredEnemies.Clear();
                    ModBehaviour.DevLog("[ModeE] 已清理虚拟 CharacterSpawnerRoot");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeE] [WARNING] CleanupModeEVirtualSpawnerRoot 失败: " + e.Message);
            }
        }

        #endregion

    }
}
