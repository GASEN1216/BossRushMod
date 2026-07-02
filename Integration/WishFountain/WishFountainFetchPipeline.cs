// ============================================================================
// WishFountainFetchPipeline.cs - 许愿台飞书读取与弹幕缓存
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace BossRush
{
    public static partial class WishFountainService
    {
        private const int DANMAKU_POOL_FETCH_SIZE = 500;
        internal const int DANMAKU_DISPLAY_TOTAL = 100;
        private const float DANMAKU_LATEST_RATIO = 0.6f;
        private const string DANMAKU_CACHE_FILE_NAME = "wish_danmaku_cache.txt";
        private const string DANMAKU_CACHE_HEADER = "WISH_DANMAKU_CACHE_V1";

        private sealed class WishRecord
        {
            public string time;
            public string content;
        }

        /// <summary>
        /// 拉取飞书中已保存的心愿记录。
        /// 联网成功时优先使用服务端结果并刷新本地缓存；
        /// 联网失败或飞书配置不可用时，自动回退到最近一次成功缓存的弹幕池。
        /// </summary>
        public static void RequestRecentWishes(Action<List<string>> onSuccess, Action<string> onFailure)
        {
            if (TryReturnRecentDanmakuResult(onSuccess, onFailure))
            {
                return;
            }

            if (onSuccess != null)
            {
                danmakuFetchSuccessWaiters += onSuccess;
            }

            if (onFailure != null)
            {
                danmakuFetchFailureWaiters += onFailure;
            }

            if (danmakuFetchInProgress)
            {
                return;
            }

            ModBehaviour owner = ModBehaviour.Instance;
            if (owner == null)
            {
                CompleteDanmakuFetchFailure("runtime_unavailable");
                return;
            }

            danmakuFetchInProgress = true;
            lastDanmakuFetchAttemptRealtime = Time.realtimeSinceStartup;
            danmakuFetchBackgroundCoroutine = owner.StartCoroutine(FetchRecentWishesQueued());
        }

        public static void CancelRecentWishesRequest(Action<List<string>> onSuccess, Action<string> onFailure)
        {
            if (onSuccess != null)
            {
                danmakuFetchSuccessWaiters -= onSuccess;
            }

            if (onFailure != null)
            {
                danmakuFetchFailureWaiters -= onFailure;
            }
        }

        public static IEnumerator FetchRecentWishes(Action<List<string>> onSuccess, Action<string> onFailure)
        {
            RequestRecentWishes(onSuccess, onFailure);
            yield break;
        }

        private static IEnumerator FetchRecentWishesQueued()
        {
            IEnumerator fetchEnumerator = FetchRecentWishesQueuedCore();
            while (true)
            {
                object currentYield;
                Exception stepError;
                if (!TryAdvanceDanmakuFetchEnumerator(fetchEnumerator, out currentYield, out stepError))
                {
                    if (stepError != null)
                    {
                        ModBehaviour.DevLog("[WishFountain] [WARNING] 弹幕读取异常: " + stepError.Message);
                        CompleteDanmakuFetchFailure("unexpected_exception");
                    }
                    yield break;
                }

                yield return currentYield;
            }
        }

        private static IEnumerator FetchRecentWishesQueuedCore()
        {
            FeishuAppConfig config = GetFeishuAppConfig();
            if (config == null || !config.IsValid())
            {
                List<string> cachedFallback;
                if (TryLoadCachedDanmakuFallback("invalid_config", out cachedFallback))
                {
                    CompleteDanmakuFetchSuccess(cachedFallback);
                    yield break;
                }

                ModBehaviour.DevLog("[WishFountain] 弹幕读取：飞书应用配置未准备好");
                CompleteDanmakuFetchFailure("invalid_config");
                yield break;
            }

            string recordsUrl = BuildFeishuDanmakuRecordsUrl(config);
            List<WishRecord> records = null;
            string fetchFailureReason = "no_data";

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
                    fetchFailureReason = "auth_failed";
                    ModBehaviour.DevLog("[WishFountain] 弹幕读取：飞书鉴权失败 " + (authError ?? "unknown"));
                    break;
                }

                using (UnityWebRequest request = UnityWebRequest.Get(recordsUrl))
                {
                    request.SetRequestHeader("Authorization", "Bearer " + tenantAccessToken);
                    request.timeout = REQUEST_TIMEOUT;

                    yield return request.SendWebRequest();

                    string responseText = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;

                    if (HasRequestTransportError(request))
                    {
                        if (attempt == 0 && ShouldRetryWithFreshTenantToken(request, responseText))
                        {
                            InvalidateCachedTenantAccessToken();
                            continue;
                        }

                        fetchFailureReason = "transport_error";
                        ModBehaviour.DevLog("[WishFountain] 弹幕读取失败: " + (request.error ?? "unknown"));
                        break;
                    }

                    int code = SimpleJsonHelper.ExtractInt(responseText, "code");
                    if (code != 0)
                    {
                        if (attempt == 0 && ShouldRetryWithFreshTenantToken(request, responseText))
                        {
                            InvalidateCachedTenantAccessToken();
                            continue;
                        }

                        fetchFailureReason = "api_error";
                        ModBehaviour.DevLog("[WishFountain] 弹幕读取 API 返回错误: " + GetFeishuErrorMessage(responseText, "list_api_error"));
                        break;
                    }

                    records = new List<WishRecord>();
                    ParseWishRecordsFromResponse(responseText, records);
                    break;
                }
            }

            if (records != null)
            {
                List<string> selected = SelectWishContentsForDanmaku(records);
                if (selected.Count > 0)
                {
                    SaveDanmakuCacheToDisk(selected);
                }
                ModBehaviour.DevLog("[WishFountain] 弹幕读取成功，原始 " + records.Count + " 条，展示 " + selected.Count + " 条");
                CompleteDanmakuFetchSuccess(selected);
                yield break;
            }

            List<string> cachedDanmaku;
            if (TryLoadCachedDanmakuFallback(fetchFailureReason, out cachedDanmaku))
            {
                CompleteDanmakuFetchSuccess(cachedDanmaku);
                yield break;
            }

            CompleteDanmakuFetchFailure(fetchFailureReason);
        }

        private static bool TryAdvanceDanmakuFetchEnumerator(IEnumerator enumerator, out object current, out Exception error)
        {
            current = null;
            error = null;

            try
            {
                if (!enumerator.MoveNext())
                {
                    return false;
                }

                current = enumerator.Current;
                return true;
            }
            catch (Exception e)
            {
                error = e;
                return false;
            }
        }

        private static string BuildFeishuDanmakuRecordsUrl(FeishuAppConfig config)
        {
            string sortJson = "[\"时间 DESC\"]";
            return GetFeishuRecordsUrl(config)
                + "?page_size=" + DANMAKU_POOL_FETCH_SIZE
                + "&sort=" + UnityWebRequest.EscapeURL(sortJson);
        }

        private static bool TryLoadCachedDanmakuFallback(string reason, out List<string> cached)
        {
            cached = TryLoadDanmakuCacheSnapshot();
            if (cached == null || cached.Count <= 0)
            {
                return false;
            }

            ModBehaviour.DevLog("[WishFountain] 弹幕读取回退到本地缓存(" + reason + ")，共 " + cached.Count + " 条");
            return true;
        }

        private static bool TryReturnRecentDanmakuResult(Action<List<string>> onSuccess, Action<string> onFailure)
        {
            float age = Time.realtimeSinceStartup - lastDanmakuFetchAttemptRealtime;
            if (age < 0f || age > DANMAKU_FETCH_TTL_SECONDS)
            {
                return false;
            }

            if (lastDanmakuFetchSucceeded)
            {
                List<string> snapshot = cachedDanmakuFetchSnapshot != null
                    ? new List<string>(cachedDanmakuFetchSnapshot)
                    : new List<string>();
                if (onSuccess != null)
                {
                    onSuccess(snapshot);
                }
                return true;
            }

            return false;
        }

        private static void CompleteDanmakuFetchSuccess(List<string> wishContents)
        {
            danmakuFetchInProgress = false;
            danmakuFetchBackgroundCoroutine = null;
            lastDanmakuFetchSucceeded = true;
            lastDanmakuFetchFailureReason = null;
            cachedDanmakuFetchSnapshot = wishContents != null && wishContents.Count > 0
                ? new List<string>(wishContents)
                : null;

            Action<List<string>> successWaiters = danmakuFetchSuccessWaiters;
            danmakuFetchSuccessWaiters = null;
            danmakuFetchFailureWaiters = null;

            if (successWaiters != null)
            {
                successWaiters(cachedDanmakuFetchSnapshot != null
                    ? new List<string>(cachedDanmakuFetchSnapshot)
                    : new List<string>());
            }
        }

        private static void CompleteDanmakuFetchFailure(string reason)
        {
            danmakuFetchInProgress = false;
            danmakuFetchBackgroundCoroutine = null;
            lastDanmakuFetchSucceeded = false;
            lastDanmakuFetchFailureReason = string.IsNullOrEmpty(reason) ? "unknown" : reason;
            cachedDanmakuFetchSnapshot = null;

            Action<string> failureWaiters = danmakuFetchFailureWaiters;
            danmakuFetchSuccessWaiters = null;
            danmakuFetchFailureWaiters = null;

            if (failureWaiters != null)
            {
                failureWaiters(lastDanmakuFetchFailureReason);
            }
        }

        private static void ResetDanmakuFetchState()
        {
            if (danmakuFetchBackgroundCoroutine != null && ModBehaviour.Instance != null)
            {
                ModBehaviour.Instance.StopCoroutine(danmakuFetchBackgroundCoroutine);
            }

            danmakuFetchBackgroundCoroutine = null;
            danmakuFetchInProgress = false;
            lastDanmakuFetchAttemptRealtime = -999f;
            lastDanmakuFetchSucceeded = false;
            lastDanmakuFetchFailureReason = null;
            cachedDanmakuFetchSnapshot = null;
            danmakuFetchSuccessWaiters = null;
            danmakuFetchFailureWaiters = null;
        }

        internal static List<string> TryLoadDanmakuCacheSnapshot()
        {
            List<string> cached = LoadDanmakuCacheFromDisk();
            return cached != null && cached.Count > 0
                ? new List<string>(cached)
                : null;
        }

        private static string GetDanmakuCacheFilePath()
        {
            try
            {
                string modPath = ModBehaviour.GetModPath();
                if (string.IsNullOrEmpty(modPath))
                {
                    return null;
                }

                string cacheDirectory = Path.Combine(modPath, "Cache");
                Directory.CreateDirectory(cacheDirectory);
                return Path.Combine(cacheDirectory, DANMAKU_CACHE_FILE_NAME);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WishFountain] [WARNING] 弹幕缓存路径创建失败: " + e.Message);
                return null;
            }
        }

        private static void SaveDanmakuCacheToDisk(List<string> wishContents)
        {
            try
            {
                string filePath = GetDanmakuCacheFilePath();
                if (string.IsNullOrEmpty(filePath))
                {
                    return;
                }

                List<string> safeContents = wishContents ?? new List<string>();
                StringBuilder sb = new StringBuilder(64 + safeContents.Count * 32);
                sb.AppendLine(DANMAKU_CACHE_HEADER);

                for (int i = 0; i < safeContents.Count; i++)
                {
                    string content = safeContents[i];
                    if (string.IsNullOrEmpty(content))
                    {
                        continue;
                    }

                    string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(content));
                    sb.AppendLine(encoded);
                }

                File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WishFountain] [WARNING] 弹幕缓存写入失败: " + e.Message);
            }
        }

        private static List<string> LoadDanmakuCacheFromDisk()
        {
            try
            {
                string filePath = GetDanmakuCacheFilePath();
                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                {
                    return null;
                }

                string[] lines = File.ReadAllLines(filePath, Encoding.UTF8);
                if (lines == null || lines.Length <= 0)
                {
                    return null;
                }

                int startIndex = 0;
                if (string.Equals(lines[0], DANMAKU_CACHE_HEADER, StringComparison.Ordinal))
                {
                    startIndex = 1;
                }

                List<string> result = new List<string>(Mathf.Min(lines.Length - startIndex, DANMAKU_DISPLAY_TOTAL));
                for (int i = startIndex; i < lines.Length; i++)
                {
                    if (result.Count >= DANMAKU_DISPLAY_TOTAL)
                    {
                        break;
                    }

                    string encoded = lines[i];
                    if (string.IsNullOrEmpty(encoded))
                    {
                        continue;
                    }

                    try
                    {
                        string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                        if (!string.IsNullOrEmpty(decoded))
                        {
                            result.Add(decoded);
                        }
                    }
                    catch
                    {
                    }
                }

                return result;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WishFountain] [WARNING] 弹幕缓存读取失败: " + e.Message);
                return null;
            }
        }

        private static void ParseWishRecordsFromResponse(string responseText, List<WishRecord> outRecords)
        {
            if (string.IsNullOrEmpty(responseText) || outRecords == null)
            {
                return;
            }

            int itemsKey = responseText.IndexOf("\"items\"", StringComparison.Ordinal);
            if (itemsKey < 0)
            {
                return;
            }

            int arrayStart = responseText.IndexOf('[', itemsKey);
            if (arrayStart < 0)
            {
                return;
            }

            int arrayEnd = FindMatchingArrayEnd(responseText, arrayStart, responseText.Length);
            if (arrayEnd <= arrayStart)
            {
                return;
            }

            SimpleJsonHelper.ForEachObject(responseText, arrayStart, arrayEnd, (json, objStart, objEnd) =>
            {
                string content = ExtractFeishuTextField(json, "心愿内容", objStart, objEnd);
                if (string.IsNullOrEmpty(content))
                {
                    return;
                }

                content = content.Trim();
                if (content.Length == 0)
                {
                    return;
                }

                string time = ExtractFeishuTextField(json, "时间", objStart, objEnd);
                outRecords.Add(new WishRecord
                {
                    time = time ?? string.Empty,
                    content = content
                });
            });
        }

        private static string ExtractFeishuTextField(string json, string fieldName, int objStart, int objEnd)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(fieldName))
            {
                return null;
            }

            string keyPattern = "\"" + fieldName + "\"";
            int keyPos = json.IndexOf(keyPattern, objStart, StringComparison.Ordinal);
            if (keyPos < 0 || keyPos >= objEnd)
            {
                return null;
            }

            int cursor = keyPos + keyPattern.Length;
            while (cursor < objEnd && json[cursor] != ':')
            {
                cursor++;
            }

            cursor++;

            while (cursor < objEnd && char.IsWhiteSpace(json[cursor]))
            {
                cursor++;
            }

            if (cursor >= objEnd)
            {
                return null;
            }

            char valueChar = json[cursor];
            if (valueChar == '"')
            {
                return SimpleJsonHelper.ExtractString(json, fieldName, objStart, objEnd);
            }

            if (valueChar == '[')
            {
                int arrayEnd = FindMatchingArrayEnd(json, cursor, objEnd);
                if (arrayEnd < 0)
                {
                    arrayEnd = objEnd;
                }

                StringBuilder sb = new StringBuilder();
                int searchStart = cursor;
                while (true)
                {
                    int textKeyPos = json.IndexOf("\"text\"", searchStart, StringComparison.Ordinal);
                    if (textKeyPos < 0 || textKeyPos >= arrayEnd)
                    {
                        break;
                    }

                    string segment = SimpleJsonHelper.ExtractString(json, "text", textKeyPos, arrayEnd);
                    if (!string.IsNullOrEmpty(segment))
                    {
                        sb.Append(segment);
                    }

                    searchStart = textKeyPos + 6;
                }

                return sb.Length > 0 ? sb.ToString() : null;
            }

            return null;
        }

        private static int FindMatchingArrayEnd(string json, int openBracketPos, int limit)
        {
            int depth = 0;
            bool inString = false;
            for (int i = openBracketPos; i < limit && i < json.Length; i++)
            {
                char c = json[i];
                if (inString)
                {
                    if (c == '"' && IsUnescapedQuote(json, i, openBracketPos))
                    {
                        inString = false;
                    }
                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                }
                else if (c == '[')
                {
                    depth++;
                }
                else if (c == ']')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        private static bool IsUnescapedQuote(string json, int quotePos, int lowerBound)
        {
            if (string.IsNullOrEmpty(json) || quotePos < 0 || quotePos >= json.Length)
            {
                return false;
            }

            int backslashCount = 0;
            int cursor = quotePos - 1;
            while (cursor >= lowerBound && json[cursor] == '\\')
            {
                backslashCount++;
                cursor--;
            }

            return backslashCount % 2 == 0;
        }

        private static List<string> SelectWishContentsForDanmaku(List<WishRecord> records)
        {
            List<string> result = new List<string>();
            if (records == null || records.Count == 0)
            {
                return result;
            }

            records.Sort((a, b) => string.CompareOrdinal(
                b != null ? b.time : string.Empty,
                a != null ? a.time : string.Empty));

            if (records.Count <= DANMAKU_DISPLAY_TOTAL)
            {
                for (int i = 0; i < records.Count; i++)
                {
                    if (records[i] != null && !string.IsNullOrEmpty(records[i].content))
                    {
                        result.Add(records[i].content);
                    }
                }

                ShuffleInPlace(result);
                return result;
            }

            int latestCount = Mathf.RoundToInt(DANMAKU_DISPLAY_TOTAL * DANMAKU_LATEST_RATIO);
            latestCount = Mathf.Clamp(latestCount, 0, records.Count);

            for (int i = 0; i < latestCount; i++)
            {
                if (records[i] != null && !string.IsNullOrEmpty(records[i].content))
                {
                    result.Add(records[i].content);
                }
            }

            int randomCount = DANMAKU_DISPLAY_TOTAL - latestCount;
            if (randomCount > 0 && records.Count > latestCount)
            {
                List<int> remainingIndices = new List<int>(records.Count - latestCount);
                for (int i = latestCount; i < records.Count; i++)
                {
                    remainingIndices.Add(i);
                }

                int pickable = Mathf.Min(randomCount, remainingIndices.Count);
                for (int i = 0; i < pickable; i++)
                {
                    int swap = UnityEngine.Random.Range(i, remainingIndices.Count);
                    int tmp = remainingIndices[i];
                    remainingIndices[i] = remainingIndices[swap];
                    remainingIndices[swap] = tmp;

                    WishRecord picked = records[remainingIndices[i]];
                    if (picked != null && !string.IsNullOrEmpty(picked.content))
                    {
                        result.Add(picked.content);
                    }
                }
            }

            ShuffleInPlace(result);
            return result;
        }

        private static void ShuffleInPlace(List<string> list)
        {
            if (list == null)
            {
                return;
            }

            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                string tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }
    }
}
