// ============================================================================
// SteamHelper.cs — Steam 平台辅助工具
// ============================================================================
// 模块说明：
//   通过反射获取 Steam 用户信息，避免对 Steamworks.NET 的硬依赖。
//   供亡魂系统、血猎工事 UI 等多个模块共享使用。
// ============================================================================

using System;
using System.Reflection;
using HarmonyLib;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private static MethodInfo steam_getPersonaNameMethod;
        private static MethodInfo steam_getSteamDisplayMethod;
        private static bool steamPersonaLookupLogged;
        private static bool steamPersonaInvokeLogged;
        private static bool steamDisplayLookupLogged;
        private static bool steamDisplayInvokeLogged;

        /// <summary>
        /// 获取 Steam 人格名（优先 SteamFriends.GetPersonaName，回退 SteamManager.GetSteamDisplay）
        /// </summary>
        internal static string TryGetSteamPersonaName()
        {
            try
            {
                if (steam_getPersonaNameMethod == null)
                {
                    Type steamFriendsType = AccessTools.TypeByName("Steamworks.SteamFriends");
                    if (steamFriendsType != null)
                    {
                        steam_getPersonaNameMethod = steamFriendsType.GetMethod(
                            "GetPersonaName",
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    }

                    if (steam_getPersonaNameMethod == null)
                    {
                        LogSteamHelperWarningOnce(
                            ref steamPersonaLookupLogged,
                            "[SteamHelper] 未找到 SteamFriends.GetPersonaName，尝试回退到 SteamManager.GetSteamDisplay");
                    }
                }

                if (steam_getPersonaNameMethod != null)
                {
                    string name = steam_getPersonaNameMethod.Invoke(null, null) as string;
                    if (!string.IsNullOrEmpty(name))
                    {
                        return name;
                    }
                }
            }
            catch (Exception e)
            {
                LogSteamHelperWarningOnce(
                    ref steamPersonaInvokeLogged,
                    "[SteamHelper] 调用 SteamFriends.GetPersonaName 失败: " + e.Message);
            }

            try
            {
                if (steam_getSteamDisplayMethod == null)
                {
                    Type steamManagerType = AccessTools.TypeByName("SteamManager");
                    if (steamManagerType != null)
                    {
                        steam_getSteamDisplayMethod = steamManagerType.GetMethod(
                            "GetSteamDisplay",
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    }

                    if (steam_getSteamDisplayMethod == null)
                    {
                        LogSteamHelperWarningOnce(
                            ref steamDisplayLookupLogged,
                            "[SteamHelper] 未找到 SteamManager.GetSteamDisplay");
                    }
                }

                if (steam_getSteamDisplayMethod != null)
                {
                    object display = steam_getSteamDisplayMethod.IsStatic
                        ? steam_getSteamDisplayMethod.Invoke(null, null)
                        : null;
                    string displayName = display as string;
                    if (!string.IsNullOrEmpty(displayName))
                    {
                        return displayName;
                    }
                }
            }
            catch (Exception e)
            {
                LogSteamHelperWarningOnce(
                    ref steamDisplayInvokeLogged,
                    "[SteamHelper] 调用 SteamManager.GetSteamDisplay 失败: " + e.Message);
            }

            return null;
        }

        private static void LogSteamHelperWarningOnce(ref bool hasLogged, string message)
        {
            if (hasLogged)
            {
                return;
            }

            hasLogged = true;
            try
            {
                DevLog(message);
            }
            catch { }
        }
    }
}
