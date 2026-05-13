// ============================================================================
// WishFountainSendPipeline.cs - 飞书发送链路
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using ItemStatsSystem;
using Saves;

namespace BossRush
{
    public static partial class WishFountainService
    {
        public static bool IsInCooldown()
        {
            return Time.realtimeSinceStartup - lastSendTime < SEND_COOLDOWN;
        }

        /// <summary>获取剩余冷却秒数</summary>
        public static int GetCooldownRemaining()
        {
            float remaining = SEND_COOLDOWN - (Time.realtimeSinceStartup - lastSendTime);
            return remaining > 0 ? Mathf.CeilToInt(remaining) : 0;
        }

        /// <summary>
        /// 获取 tenant access token
        /// </summary>
        private static IEnumerator GetTenantAccessToken(FeishuAppConfig config, Action<string> onSuccess, Action<string> onFailure)
        {
            if (config == null || !config.IsValid())
            {
                if (onFailure != null)
                {
                    onFailure("invalid_config");
                }
                yield break;
            }

            if (!string.IsNullOrEmpty(cachedTenantAccessToken)
                && DateTime.UtcNow < cachedTenantAccessTokenExpiryUtc)
            {
                if (onSuccess != null)
                {
                    onSuccess(cachedTenantAccessToken);
                }
                yield break;
            }

            StringBuilder authBuilder = SimpleJsonHelper.GetBuilder();
            authBuilder.Append('{');
            SimpleJsonHelper.AppendString(authBuilder, "app_id", config.appId);
            SimpleJsonHelper.AppendString(authBuilder, "app_secret", config.appSecret, false);
            authBuilder.Append('}');

            byte[] authBody = Encoding.UTF8.GetBytes(authBuilder.ToString());

            using (UnityWebRequest authRequest = new UnityWebRequest(FEISHU_TENANT_TOKEN_URL, "POST"))
            {
                authRequest.uploadHandler = new UploadHandlerRaw(authBody);
                authRequest.downloadHandler = new DownloadHandlerBuffer();
                authRequest.SetRequestHeader("Content-Type", "application/json");
                authRequest.timeout = REQUEST_TIMEOUT;

                yield return authRequest.SendWebRequest();

                if (HasRequestTransportError(authRequest))
                {
                    if (onFailure != null)
                    {
                        onFailure(authRequest.error ?? "auth_transport_error");
                    }
                    yield break;
                }

                string authResponse = authRequest.downloadHandler != null ? authRequest.downloadHandler.text : "";
                int code = SimpleJsonHelper.ExtractInt(authResponse, "code");
                if (code != 0)
                {
                    if (onFailure != null)
                    {
                        onFailure(GetFeishuErrorMessage(authResponse, "auth_api_error"));
                    }
                    yield break;
                }

                string tenantAccessToken = SimpleJsonHelper.ExtractString(authResponse, "tenant_access_token");
                int expireSeconds = SimpleJsonHelper.ExtractInt(authResponse, "expire");
                if (string.IsNullOrEmpty(tenantAccessToken))
                {
                    if (onFailure != null)
                    {
                        onFailure("missing_tenant_access_token");
                    }
                    yield break;
                }

                cachedTenantAccessToken = tenantAccessToken;
                int cacheSeconds = expireSeconds > TOKEN_REFRESH_BUFFER_SECONDS
                    ? expireSeconds - TOKEN_REFRESH_BUFFER_SECONDS
                    : Mathf.Max(30, expireSeconds);
                cachedTenantAccessTokenExpiryUtc = DateTime.UtcNow.AddSeconds(cacheSeconds);

                if (onSuccess != null)
                {
                    onSuccess(tenantAccessToken);
                }
            }
        }

        private static string BuildFeishuRecordJson(string playerName, string wishText, string sceneName, string language, string modVersion)
        {
            StringBuilder sb = SimpleJsonHelper.GetBuilder();
            sb.Append("{\"fields\":{");
            SimpleJsonHelper.AppendString(sb, "时间", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            SimpleJsonHelper.AppendString(sb, "版本", modVersion);
            SimpleJsonHelper.AppendString(sb, "玩家名", playerName);
            SimpleJsonHelper.AppendString(sb, "心愿内容", wishText);
            SimpleJsonHelper.AppendString(sb, "场景", sceneName);
            SimpleJsonHelper.AppendString(sb, "语言", language, false);
            sb.Append("}}");
            return sb.ToString();
        }

        private static string GetFeishuRecordsUrl(FeishuAppConfig config)
        {
            return "https://open.feishu.cn/open-apis/bitable/v1/apps/"
                + config.appToken + "/tables/" + config.tableId + "/records";
        }

        private static string GetFeishuErrorMessage(string responseText, string fallback)
        {
            string msg = SimpleJsonHelper.ExtractString(responseText, "msg");
            if (!string.IsNullOrEmpty(msg))
            {
                return msg;
            }

            string message = SimpleJsonHelper.ExtractString(responseText, "message");
            if (!string.IsNullOrEmpty(message))
            {
                return message;
            }

            return fallback;
        }

        private static void InvalidateCachedTenantAccessToken()
        {
            cachedTenantAccessToken = null;
            cachedTenantAccessTokenExpiryUtc = DateTime.MinValue;
        }

        private static bool ShouldRetryWithFreshTenantToken(UnityWebRequest request, string responseText)
        {
            long responseCode = 0;
            try
            {
                responseCode = request.responseCode;
            }
            catch { }

            if (responseCode == 401 || responseCode == 403)
            {
                return true;
            }

            string lower = string.IsNullOrEmpty(responseText) ? "" : responseText.ToLowerInvariant();
            return lower.IndexOf("tenant_access_token", StringComparison.Ordinal) >= 0
                || lower.IndexOf("access token", StringComparison.Ordinal) >= 0
                || lower.IndexOf("token expired", StringComparison.Ordinal) >= 0
                || lower.IndexOf("unauthorized", StringComparison.Ordinal) >= 0
                || lower.IndexOf("99991661", StringComparison.Ordinal) >= 0
                || lower.IndexOf("99991663", StringComparison.Ordinal) >= 0;
        }

        private static bool HasRequestTransportError(UnityWebRequest request)
        {
            return request.result != UnityWebRequest.Result.Success;
        }

        /// <summary>
        /// 发送心愿数据到飞书多维表格（协程）
        /// </summary>
        public static IEnumerator SendWish(string wishText, bool isAnonymous,
            Action onSuccess, Action<string> onFailure)
        {
            if (IsSending)
            {
                if (onFailure != null)
                {
                    onFailure(L10n.T("正在发送中，请稍候…", "Sending in progress, please wait…"));
                }
                yield break;
            }

            if (IsInCooldown())
            {
                if (onFailure != null)
                {
                    int remaining = GetCooldownRemaining();
                    onFailure(L10n.T(
                        "请 " + remaining + " 秒后再试",
                        "Please wait " + remaining + " seconds"));
                }
                yield break;
            }

            FeishuAppConfig config = GetFeishuAppConfig();
            if (config == null || !config.IsValid())
            {
                if (onFailure != null)
                {
                    onFailure(L10n.T("星空暂时联系不上，请稍后再试",
                                     "The stars are unreachable, please try again later"));
                }
                ModBehaviour.DevLog("[WishFountain] 飞书应用配置未准备好");
                yield break;
            }

            IsSending = true;

            try
            {
                // 截断超长文本
                if (wishText.Length > MAX_CHARS)
                {
                    wishText = wishText.Substring(0, MAX_CHARS);
                }

                // 获取玩家名
                string playerName;
                if (isAnonymous)
                {
                    playerName = L10n.T("匿名玩家", "Anonymous");
                }
                else
                {
                    playerName = ModBehaviour.TryGetSteamPersonaName();
                    if (string.IsNullOrEmpty(playerName))
                    {
                        playerName = L10n.T("匿名玩家", "Anonymous");
                    }
                }

                // 获取当前场景
                string sceneName = "";
                try
                {
                    sceneName = SceneManager.GetActiveScene().name;
                }
                catch { }

                // 获取语言
                string language = L10n.IsChinese ? "zh-CN" : "en";
                string modVersion = GetModVersion();

                string jsonBody = BuildFeishuRecordJson(playerName, wishText, sceneName, language, modVersion);
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
                string recordsUrl = GetFeishuRecordsUrl(config);

                for (int attempt = 0; attempt < 2; attempt++)
                {
                    string tenantAccessToken = null;
                    string authError = null;
                    yield return GetTenantAccessToken(
                        config,
                        token => tenantAccessToken = token,
                        error => authError = error);

                    if (string.IsNullOrEmpty(tenantAccessToken))
                    {
                        ModBehaviour.DevLog("[WishFountain] 飞书鉴权失败: " + (authError ?? "unknown"));
                        if (onFailure != null)
                        {
                            onFailure(L10n.T("星空暂时联系不上，请稍后再试",
                                             "The stars are unreachable, please try again later"));
                        }
                        yield break;
                    }

                    using (UnityWebRequest request = new UnityWebRequest(recordsUrl, "POST"))
                    {
                        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                        request.downloadHandler = new DownloadHandlerBuffer();
                        request.SetRequestHeader("Authorization", "Bearer " + tenantAccessToken);
                        request.SetRequestHeader("Content-Type", "application/json");
                        request.timeout = REQUEST_TIMEOUT;

                        yield return request.SendWebRequest();

                        string responseText = request.downloadHandler != null ? request.downloadHandler.text : "";
                        if (HasRequestTransportError(request))
                        {
                            if (attempt == 0 && ShouldRetryWithFreshTenantToken(request, responseText))
                            {
                                ModBehaviour.DevLog("[WishFountain] 记录写入疑似 token 失效，清理缓存后重试一次");
                                InvalidateCachedTenantAccessToken();
                                continue;
                            }

                            string error = request.error ?? "Unknown error";
                            if (!string.IsNullOrEmpty(responseText))
                            {
                                error = error + " | " + responseText;
                            }
                            ModBehaviour.DevLog("[WishFountain] 写入飞书失败: " + error);
                            if (onFailure != null)
                            {
                                onFailure(L10n.T("星空暂时联系不上，请稍后再试",
                                                 "The stars are unreachable, please try again later"));
                            }
                            yield break;
                        }

                        int code = SimpleJsonHelper.ExtractInt(responseText, "code");
                        if (code != 0)
                        {
                            if (attempt == 0 && ShouldRetryWithFreshTenantToken(request, responseText))
                            {
                                ModBehaviour.DevLog("[WishFountain] 飞书 API 指示 token 无效，清理缓存后重试一次");
                                InvalidateCachedTenantAccessToken();
                                continue;
                            }

                            ModBehaviour.DevLog("[WishFountain] 飞书 API 返回错误: " + GetFeishuErrorMessage(responseText, "bitable_api_error"));
                            if (onFailure != null)
                            {
                                onFailure(L10n.T("星空暂时联系不上，请稍后再试",
                                                 "The stars are unreachable, please try again later"));
                            }
                            yield break;
                        }

                        lastSendTime = Time.realtimeSinceStartup;
                        ModBehaviour.DevLog("[WishFountain] 心愿发送成功");
                        if (onSuccess != null)
                        {
                            onSuccess();
                        }
                        yield break;
                    }
                }

                if (onFailure != null)
                {
                    onFailure(L10n.T("星空暂时联系不上，请稍后再试",
                                     "The stars are unreachable, please try again later"));
                }
            }
            finally
            {
                IsSending = false;
            }
        }
    }
}
