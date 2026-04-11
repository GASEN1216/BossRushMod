// ============================================================================
// WishFountainService.cs - 布满了灰尘的星愿许愿台网络服务与内容校验
// ============================================================================
// 模块说明：
//   提供布满了灰尘的星愿许愿台的核心服务：
//   - 文本标准化（全角转半角、空白折叠、换行统一等）
//   - 内容校验（长度、有效字符、重复、链接、敏感词过滤）
//   - 加密配置文件读取（AES-256-CBC）
//   - 通过飞书应用鉴权 + 多维表格 API 写入心愿记录
//   - 复用缓存的 tenant_access_token，并在鉴权失效时自动重试一次
//
// 防污染策略：
//   第一步：StandardizeText — 清洗原始输入
//   第二步：ValidateWishText — 8 条规则逐一校验
//   第三步：发送前限流（全局 30 秒冷却）
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

namespace BossRush
{
    /// <summary>
    /// 布满了灰尘的星愿许愿台网络服务与内容校验
    /// </summary>
    public static class WishFountainService
    {
        // ============================================================================
        // 配置常量
        // ============================================================================

        /// <summary>请求超时（秒）</summary>
        private const int REQUEST_TIMEOUT = 15;

        /// <summary>飞书租户 token 刷新预留秒数</summary>
        private const int TOKEN_REFRESH_BUFFER_SECONDS = 60;

        /// <summary>最小字符数</summary>
        internal const int MIN_CHARS = 20;

        /// <summary>最大字符数</summary>
        internal const int MAX_CHARS = 10000;

        /// <summary>全局发送冷却时间（秒）</summary>
        private const float SEND_COOLDOWN = 30f;

        /// <summary>加密配置文件名</summary>
        private const string CONFIG_FILE_NAME = "wish_config.dat";

        /// <summary>加密配置文件头标识</summary>
        private const string CONFIG_HEADER = "WISH_CFG_V2_FEISHU_APP";

        /// <summary>未配置占位标记</summary>
        private const string UNCONFIGURED_CONFIG_VALUE = "UNCONFIGURED";

        /// <summary>飞书 tenant token 接口</summary>
        private const string FEISHU_TENANT_TOKEN_URL = "https://open.feishu.cn/open-apis/auth/v3/tenant_access_token/internal";

        /// <summary>加密盐值</summary>
        private static readonly byte[] AES_SALT = {
            0x53, 0x74, 0x61, 0x72, 0x57, 0x69, 0x73, 0x68,
            0x46, 0x6F, 0x75, 0x6E, 0x74, 0x61, 0x69, 0x6E
        };

        /// <summary>PBKDF2 迭代次数</summary>
        private const int PBKDF2_ITERATIONS = 10000;

        // ============================================================================
        // 状态字段
        // ============================================================================

        /// <summary>是否正在发送</summary>
        internal static bool IsSending = false;

        /// <summary>上次发送时间（realtimeSinceStartup）</summary>
        private static float lastSendTime = -999f;

        /// <summary>缓存的飞书应用配置</summary>
        private static FeishuAppConfig cachedFeishuConfig = null;

        /// <summary>是否已尝试加载配置</summary>
        private static bool configLoaded = false;

        /// <summary>缓存的 Mod 版本号</summary>
        private static string cachedModVersion = null;

        /// <summary>缓存的 tenant access token</summary>
        private static string cachedTenantAccessToken = null;

        /// <summary>tenant access token 到期时间（UTC）</summary>
        private static DateTime cachedTenantAccessTokenExpiryUtc = DateTime.MinValue;

        [Serializable]
        private sealed class FeishuAppConfig
        {
            public string appId;
            public string appSecret;
            public string appToken;
            public string tableId;

            public bool IsValid()
            {
                return !string.IsNullOrEmpty(appId)
                    && !string.IsNullOrEmpty(appSecret)
                    && !string.IsNullOrEmpty(appToken)
                    && !string.IsNullOrEmpty(tableId);
            }
        }

        // ============================================================================
        // 敏感词黑名单（完整版）
        // ============================================================================

        private static readonly string[] Blacklist = {
            // === 辱骂类（中文）===
            "傻逼", "操你", "去死", "废物", "智障", "脑残", "煞笔", "沙比", "傻比",
            "妈逼", "你妈", "他妈", "尼玛", "你马", "草泥马", "操你妈", "日你",
            "滚蛋", "贱人", "婊子", "狗逼", "狗日", "王八蛋", "混蛋", "杂种",
            "畜生", "白痴", "弱智", "低能", "蠢货", "蠢猪", "猪头",
            "死全家", "全家死", "断子绝孙", "不得好死", "妈的", "他妈的",
            "我操", "卧槽", "牛逼", "妈了个逼", "逼你妈", "屌你", "屁眼",
            "放屁", "狗屎", "吃屎", "人渣", "垃圾人", "下贱", "恶心", "变态",
            "滚出去",

            // === 辱骂类（英文）===
            "fuck", "shit", "bitch", "asshole", "bastard", "cunt", "dick",
            "motherfucker", "nigger", "nigga", "faggot", "retard",
            "slut", "whore", "wanker", "twat", "cock",
            "son of a bitch", "stfu", "gtfo", "kys",

            // === 色情/低俗类 ===
            "做爱", "性交", "约炮", "一夜情", "裸聊", "援交", "卖淫", "嫖娼",
            "小姐服务", "色情", "黄片", "成人视频", "三级片",
            "口交", "肛交", "自慰", "手淫", "高潮", "淫荡", "骚货", "发骚", "露骨",
            "porn", "hentai", "nsfw", "onlyfans",

            // === 广告/引流类 ===
            "代练", "代打", "外挂", "开挂", "辅助软件", "作弊器", "刷钱", "刷币",
            "优惠券", "免费领", "加微信", "私聊", "低价出售", "打折促销",
            "扫码", "二维码", "关注公众号", "抖音号", "快手号", "B站关注",
            "代购", "淘宝", "拼多多", "京东优惠", "点击链接", "注册领取",
            "赌博", "彩票", "下注", "押注", "赢钱", "翻倍", "稳赚",
        };

        /// <summary>联系方式/引流关键词</summary>
        private static readonly string[] ContactPatterns = {
            "微信", "wx:", "加我", "联系",
            "手机", "电话", "tel:", "加群", "私信我",
            "联系方式", "留言",
        };

        /// <summary>需要按完整词匹配的英文关键词</summary>
        private static readonly string[] BlacklistWordPatterns = {
            "damn", "penis", "pussy", "idiot", "moron",
            "bollocks", "piss", "arse", "bloody hell",
            "av", "sex", "nude", "naked", "xxx",
        };

        /// <summary>需要按完整词匹配的联系方式关键词</summary>
        private static readonly string[] ContactWordPatterns = {
            "qq", "wechat", "telegram", "discord", "line",
        };

        // ============================================================================
        // 加密配置文件
        // ============================================================================

        /// <summary>
        /// 加载并解密飞书应用配置（从 Assets/config/wish_config.dat）
        /// </summary>
        public static void LoadFeishuAppConfig()
        {
            if (configLoaded) return;
            configLoaded = true;

            try
            {
                string modDir = GetModDirectory();
                if (string.IsNullOrEmpty(modDir))
                {
                    ModBehaviour.DevLog("[WishFountain] 无法获取 Mod 目录");
                    return;
                }

                string configPath = Path.Combine(modDir, "Assets", "config", CONFIG_FILE_NAME);
                if (!File.Exists(configPath))
                {
                    ModBehaviour.DevLog("[WishFountain] 配置文件不存在: " + configPath);
                    return;
                }

                string[] lines = File.ReadAllLines(configPath);
                if (lines.Length < 2 || lines[0].Trim() != CONFIG_HEADER)
                {
                    ModBehaviour.DevLog("[WishFountain] 配置文件格式无效");
                    return;
                }

                string encryptedBase64 = lines[1].Trim();
                if (string.IsNullOrEmpty(encryptedBase64)
                    || string.Equals(encryptedBase64, UNCONFIGURED_CONFIG_VALUE, StringComparison.Ordinal))
                {
                    ModBehaviour.DevLog("[WishFountain] 飞书应用配置仍为占位状态，请填写真实加密配置");
                    return;
                }

                string decryptedConfig = DecryptString(encryptedBase64);
                if (string.IsNullOrEmpty(decryptedConfig))
                {
                    ModBehaviour.DevLog("[WishFountain] 飞书应用配置解密失败");
                    return;
                }

                cachedFeishuConfig = ParseFeishuAppConfig(decryptedConfig);

                if (cachedFeishuConfig != null && cachedFeishuConfig.IsValid())
                {
                    ModBehaviour.DevLog("[WishFountain] 飞书应用配置加载成功");
                }
                else
                {
                    cachedFeishuConfig = null;
                    ModBehaviour.DevLog("[WishFountain] 飞书应用配置格式无效");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WishFountain] 加载飞书应用配置异常: " + e.Message);
            }
        }

        /// <summary>
        /// 获取当前飞书应用配置（从缓存或配置文件）
        /// </summary>
        private static FeishuAppConfig GetFeishuAppConfig()
        {
            if (!configLoaded) LoadFeishuAppConfig();
            return cachedFeishuConfig;
        }

        /// <summary>
        /// 获取 Mod 目录路径
        /// </summary>
        private static string GetModDirectory()
        {
            try
            {
                string assemblyLocation = typeof(ModBehaviour).Assembly.Location;
                return Path.GetDirectoryName(assemblyLocation);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 获取当前 Mod 版本号
        /// </summary>
        private static string GetModVersion()
        {
            if (!string.IsNullOrEmpty(cachedModVersion))
            {
                return cachedModVersion;
            }

            try
            {
                Assembly assembly = typeof(ModBehaviour).Assembly;

                object[] informationalAttrs = assembly.GetCustomAttributes(
                    typeof(AssemblyInformationalVersionAttribute), false);
                if (informationalAttrs != null && informationalAttrs.Length > 0)
                {
                    string informational = ((AssemblyInformationalVersionAttribute)informationalAttrs[0]).InformationalVersion;
                    if (!string.IsNullOrEmpty(informational))
                    {
                        cachedModVersion = NormalizeVersionText(informational);
                        return cachedModVersion;
                    }
                }

                object[] fileVersionAttrs = assembly.GetCustomAttributes(
                    typeof(AssemblyFileVersionAttribute), false);
                if (fileVersionAttrs != null && fileVersionAttrs.Length > 0)
                {
                    string fileVersion = ((AssemblyFileVersionAttribute)fileVersionAttrs[0]).Version;
                    if (!string.IsNullOrEmpty(fileVersion))
                    {
                        cachedModVersion = NormalizeVersionText(fileVersion);
                        return cachedModVersion;
                    }
                }

                Version version = assembly.GetName().Version;
                if (version != null && version.ToString() != "0.0.0.0")
                {
                    cachedModVersion = NormalizeVersionText(version.ToString());
                    return cachedModVersion;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WishFountain] 获取 Mod 版本失败: " + e.Message);
            }

            string latestWikiVersion = GetLatestWikiChangelogVersion();
            if (!string.IsNullOrEmpty(latestWikiVersion))
            {
                cachedModVersion = latestWikiVersion;
                return cachedModVersion;
            }

            cachedModVersion = "unknown";
            return cachedModVersion;
        }

        private static string NormalizeVersionText(string rawVersion)
        {
            if (string.IsNullOrEmpty(rawVersion))
            {
                return null;
            }

            Match match = Regex.Match(rawVersion, @"\d+\.\d+\.\d+");
            if (match.Success)
            {
                return "v" + match.Value;
            }

            return rawVersion.Trim();
        }

        private static string GetLatestWikiChangelogVersion()
        {
            try
            {
                string modDir = GetModDirectory();
                if (string.IsNullOrEmpty(modDir))
                {
                    return null;
                }

                string wikiContentDir = Path.Combine(modDir, "WikiContent");
                if (!Directory.Exists(wikiContentDir))
                {
                    return null;
                }

                string[] changelogFiles = Directory.GetFiles(
                    wikiContentDir,
                    "changelog__v*.md",
                    SearchOption.AllDirectories);

                Version latestVersion = null;
                for (int i = 0; i < changelogFiles.Length; i++)
                {
                    Version version;
                    if (!TryParseVersionFromWikiChangelogFileName(Path.GetFileName(changelogFiles[i]), out version))
                    {
                        continue;
                    }

                    if (latestVersion == null || version.CompareTo(latestVersion) > 0)
                    {
                        latestVersion = version;
                    }
                }

                if (latestVersion != null)
                {
                    return "v" + latestVersion.ToString(3);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WishFountain] 从 WikiContent 回退版本号失败: " + e.Message);
            }

            return null;
        }

        private static bool TryParseVersionFromWikiChangelogFileName(string fileName, out Version version)
        {
            version = null;
            if (string.IsNullOrEmpty(fileName))
            {
                return false;
            }

            Match match = Regex.Match(fileName, @"changelog__v(\d+)_(\d+)_(\d+)\.md", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return false;
            }

            int major;
            int minor;
            int patch;
            if (!int.TryParse(match.Groups[1].Value, out major)
                || !int.TryParse(match.Groups[2].Value, out minor)
                || !int.TryParse(match.Groups[3].Value, out patch))
            {
                return false;
            }

            version = new Version(major, minor, patch);
            return true;
        }

        /// <summary>
        /// 派生 AES 密钥（PBKDF2）
        /// </summary>
        private static byte[] DeriveKey()
        {
            // 使用 assembly GUID + 固定盐值派生密钥
            string assemblyGuid = "";
            try
            {
                var guidAttrs = typeof(ModBehaviour).Assembly.GetCustomAttributes(
                    typeof(System.Runtime.InteropServices.GuidAttribute), false);
                if (guidAttrs != null && guidAttrs.Length > 0)
                {
                    assemblyGuid = ((System.Runtime.InteropServices.GuidAttribute)guidAttrs[0]).Value;
                }
            }
            catch { }

            if (string.IsNullOrEmpty(assemblyGuid))
            {
                assemblyGuid = "BossRush-StarWish-DefaultKey-2026";
            }

            byte[] passwordBytes = Encoding.UTF8.GetBytes(assemblyGuid);

            using (var deriveBytes = new Rfc2898DeriveBytes(passwordBytes, AES_SALT, PBKDF2_ITERATIONS))
            {
                return deriveBytes.GetBytes(32); // 256-bit key
            }
        }

        /// <summary>
        /// AES 解密字符串
        /// </summary>
        private static string DecryptString(string encryptedBase64)
        {
            try
            {
                byte[] encryptedData = Convert.FromBase64String(encryptedBase64);
                byte[] key = DeriveKey();

                // 前 16 字节是 IV
                if (encryptedData.Length < 17) return null;

                byte[] iv = new byte[16];
                Array.Copy(encryptedData, 0, iv, 0, 16);

                byte[] cipherText = new byte[encryptedData.Length - 16];
                Array.Copy(encryptedData, 16, cipherText, 0, cipherText.Length);

                using (var aes = new RijndaelManaged())
                {
                    aes.KeySize = 256;
                    aes.BlockSize = 128;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;
                    aes.Key = key;
                    aes.IV = iv;

                    using (var decryptor = aes.CreateDecryptor())
                    {
                        byte[] decrypted = decryptor.TransformFinalBlock(cipherText, 0, cipherText.Length);
                        return Encoding.UTF8.GetString(decrypted);
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WishFountain] 解密失败: " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// AES 加密字符串（开发工具）
        /// </summary>
        private static string EncryptConfigString(string plainText)
        {
            try
            {
                byte[] key = DeriveKey();

                using (var aes = new RijndaelManaged())
                {
                    aes.KeySize = 256;
                    aes.BlockSize = 128;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;
                    aes.Key = key;
                    aes.GenerateIV();

                    using (var encryptor = aes.CreateEncryptor())
                    {
                        byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
                        byte[] cipherText = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

                        // IV + CipherText → Base64
                        byte[] result = new byte[aes.IV.Length + cipherText.Length];
                        Array.Copy(aes.IV, 0, result, 0, aes.IV.Length);
                        Array.Copy(cipherText, 0, result, aes.IV.Length, cipherText.Length);

                        return Convert.ToBase64String(result);
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WishFountain] 加密失败: " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// 生成飞书应用配置的加密字符串，供写入 wish_config.dat
        /// </summary>
        public static string EncryptFeishuAppConfig(string appId, string appSecret, string appToken, string tableId)
        {
            StringBuilder sb = SimpleJsonHelper.GetBuilder();
            sb.Append('{');
            SimpleJsonHelper.AppendString(sb, "app_id", appId);
            SimpleJsonHelper.AppendString(sb, "app_secret", appSecret);
            SimpleJsonHelper.AppendString(sb, "app_token", appToken);
            SimpleJsonHelper.AppendString(sb, "table_id", tableId, false);
            sb.Append('}');
            return EncryptConfigString(sb.ToString());
        }

        private static FeishuAppConfig ParseFeishuAppConfig(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            FeishuAppConfig config = new FeishuAppConfig();
            config.appId = SimpleJsonHelper.ExtractString(json, "app_id");
            config.appSecret = SimpleJsonHelper.ExtractString(json, "app_secret");
            config.appToken = SimpleJsonHelper.ExtractString(json, "app_token");
            config.tableId = SimpleJsonHelper.ExtractString(json, "table_id");

            if (!config.IsValid())
            {
                return null;
            }

            return config;
        }

        // ============================================================================
        // 第一步：文本标准化
        // ============================================================================

        /// <summary>
        /// 对原始输入文本进行标准化清洗
        /// </summary>
        /// <param name="raw">原始输入文本</param>
        /// <returns>标准化后的文本</returns>
        public static string StandardizeText(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";

            StringBuilder sb = new StringBuilder(raw.Length);

            // 1. 全角转半角（保留中文标点）
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                // 全角ASCII字符范围 0xFF01~0xFF5E → 半角 0x0021~0x007E
                if (c >= (char)0xFF01 && c <= (char)0xFF5E)
                {
                    // 保留中文常用全角标点
                    if (IsChinesePunctuation(c))
                    {
                        sb.Append(c);
                    }
                    else
                    {
                        sb.Append((char)(c - 0xFEE0)); // 全角→半角
                    }
                }
                else if (c == '\u3000') // 全角空格
                {
                    sb.Append(' ');
                }
                else
                {
                    sb.Append(c);
                }
            }

            string text = sb.ToString();

            // 2. \r\n 和 \r 统一为 \n
            text = text.Replace("\r\n", "\n").Replace("\r", "\n");

            // 3. 连续空白折叠（同一行内的多个空格/制表符→单个空格）
            sb.Length = 0;
            bool prevIsSpace = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\n')
                {
                    sb.Append(c);
                    prevIsSpace = false;
                }
                else if (c == ' ' || c == '\t')
                {
                    if (!prevIsSpace) sb.Append(' ');
                    prevIsSpace = true;
                }
                else
                {
                    sb.Append(c);
                    prevIsSpace = false;
                }
            }
            text = sb.ToString();

            // 4. 连续空行折叠：超过2个\n → 2个\n
            while (text.IndexOf("\n\n\n") >= 0)
            {
                text = text.Replace("\n\n\n", "\n\n");
            }

            // 5. 去除首尾空行和空白
            text = text.Trim();
            while (text.Length > 0 && text[0] == '\n')
            {
                text = text.Substring(1);
            }
            while (text.Length > 0 && text[text.Length - 1] == '\n')
            {
                text = text.Substring(0, text.Length - 1);
            }
            text = text.Trim();

            return text;
        }

        /// <summary>
        /// 检查字符是否为中文常用全角标点（需要保留，不转半角）
        /// </summary>
        private static bool IsChinesePunctuation(char c)
        {
            return c == '，' || c == '。' || c == '；' || c == '：'
                || c == '？' || c == '！' || c == '\u201C' || c == '\u201D'
                || c == '\u2018' || c == '\u2019' || c == '【' || c == '】'
                || c == '（' || c == '）' || c == '—' || c == '…' || c == '、';
        }

        // ============================================================================
        // 第二步：内容校验
        // ============================================================================

        /// <summary>
        /// 校验标准化后的心愿文本
        /// </summary>
        public static bool ValidateWishText(string text, out string errorMsg)
        {
            errorMsg = "";

            // === 规则 1：空白检查 ===
            if (string.IsNullOrEmpty(text) || text.Trim().Length == 0)
            {
                errorMsg = L10n.T("请输入有意义的内容哦~", "Please enter meaningful content~");
                return false;
            }

            // === 规则 2：长度下限 ===
            if (text.Length < MIN_CHARS)
            {
                errorMsg = L10n.T(
                    "⚠ 最少输入 " + MIN_CHARS + " 个字符哦",
                    "⚠ At least " + MIN_CHARS + " characters required");
                return false;
            }

            // === 规则 4：有效文字数 ≥ 5 ===
            int effectiveChars = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsLetterOrDigit(c) || (c >= 0x4E00 && c <= 0x9FFF))
                {
                    effectiveChars++;
                }
            }
            if (effectiveChars < 5)
            {
                errorMsg = L10n.T("请输入有意义的内容哦~", "Please enter meaningful content~");
                return false;
            }

            // === 规则 5：同字符占比 > 70% ===
            if (!CheckCharDiversity(text, out errorMsg)) return false;

            // === 规则 6：重复行检测 ===
            if (!CheckDuplicateLines(text, out errorMsg)) return false;

            // === 规则 7：链接/联系方式/引流检测 ===
            if (!CheckNoContactInfo(text, out errorMsg)) return false;

            // === 规则 8：敏感词/辱骂/广告 ===
            if (!CheckNoProfanity(text, out errorMsg)) return false;

            return true;
        }

        /// <summary>规则 5：检查字符多样性</summary>
        private static bool CheckCharDiversity(string text, out string errorMsg)
        {
            errorMsg = "";
            Dictionary<char, int> charCount = new Dictionary<char, int>();
            int maxCount = 0;
            int contentLen = 0;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == ' ' || c == '\n') continue;
                contentLen++;

                int count;
                charCount.TryGetValue(c, out count);
                count++;
                charCount[c] = count;
                if (count > maxCount) maxCount = count;
            }

            if (contentLen > 0 && (float)maxCount / contentLen > 0.7f)
            {
                errorMsg = L10n.T("请输入有意义的内容哦~", "Please enter meaningful content~");
                return false;
            }

            return true;
        }

        /// <summary>规则 6：检查重复行</summary>
        private static bool CheckDuplicateLines(string text, out string errorMsg)
        {
            errorMsg = "";
            string[] lines = text.Split('\n');

            HashSet<string> uniqueLines = new HashSet<string>();
            int nonEmptyLines = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                string trimLine = lines[i].Trim();
                if (trimLine.Length > 0)
                {
                    nonEmptyLines++;
                    uniqueLines.Add(trimLine);
                }
            }

            if (nonEmptyLines <= 1)
            {
                return true;
            }

            if (nonEmptyLines > 0 && (float)uniqueLines.Count / nonEmptyLines < 0.3f)
            {
                errorMsg = L10n.T("请输入有意义的内容哦~", "Please enter meaningful content~");
                return false;
            }

            return true;
        }

        /// <summary>规则 7：检查链接、联系方式、引流</summary>
        private static bool CheckNoContactInfo(string text, out string errorMsg)
        {
            errorMsg = "";
            string lower = text.ToLowerInvariant();

            // URL 检测
            if (lower.IndexOf("http://") >= 0 || lower.IndexOf("https://") >= 0 || lower.IndexOf("www.") >= 0)
            {
                errorMsg = L10n.T("心愿里不能包含链接或联系方式哦~",
                                  "Wishes cannot contain links or contact info~");
                return false;
            }

            // 二维码引导词
            if (lower.IndexOf("扫码") >= 0 || lower.IndexOf("二维码") >= 0 || lower.IndexOf("qr code") >= 0)
            {
                errorMsg = L10n.T("心愿里不能包含链接或联系方式哦~",
                                  "Wishes cannot contain links or contact info~");
                return false;
            }

            // 联系方式关键词
            for (int i = 0; i < ContactPatterns.Length; i++)
            {
                if (lower.IndexOf(ContactPatterns[i].ToLowerInvariant()) >= 0)
                {
                    errorMsg = L10n.T("心愿里不能包含链接或联系方式哦~",
                                      "Wishes cannot contain links or contact info~");
                    return false;
                }
            }

            for (int i = 0; i < ContactWordPatterns.Length; i++)
            {
                if (ContainsWholeLatinTerm(lower, ContactWordPatterns[i]))
                {
                    errorMsg = L10n.T("心愿里不能包含链接或联系方式哦~",
                                      "Wishes cannot contain links or contact info~");
                    return false;
                }
            }

            // 手机号格式：1开头的11位连续数字
            for (int i = 0; i < text.Length - 10; i++)
            {
                if (text[i] == '1' && char.IsDigit(text[i]))
                {
                    bool allDigit = true;
                    for (int j = 1; j < 11 && i + j < text.Length; j++)
                    {
                        if (!char.IsDigit(text[i + j]))
                        {
                            allDigit = false;
                            break;
                        }
                    }
                    if (allDigit
                        && (i == 0 || !char.IsDigit(text[i - 1]))
                        && (i + 11 >= text.Length || !char.IsDigit(text[i + 11])))
                    {
                        errorMsg = L10n.T("心愿里不能包含链接或联系方式哦~",
                                          "Wishes cannot contain links or contact info~");
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>规则 8：检查敏感词/辱骂/广告</summary>
        private static bool CheckNoProfanity(string text, out string errorMsg)
        {
            errorMsg = "";
            string lower = text.ToLowerInvariant();

            for (int i = 0; i < Blacklist.Length; i++)
            {
                if (lower.IndexOf(Blacklist[i].ToLowerInvariant()) >= 0)
                {
                    errorMsg = L10n.T("内容包含不当用语，请修改后重试",
                                      "Content contains inappropriate language, please revise");
                    return false;
                }
            }

            for (int i = 0; i < BlacklistWordPatterns.Length; i++)
            {
                if (ContainsWholeLatinTerm(lower, BlacklistWordPatterns[i]))
                {
                    errorMsg = L10n.T("内容包含不当用语，请修改后重试",
                                      "Content contains inappropriate language, please revise");
                    return false;
                }
            }

            return true;
        }

        private static bool ContainsWholeLatinTerm(string lowerText, string term)
        {
            return Regex.IsMatch(
                lowerText,
                @"(?<![a-z0-9])" + Regex.Escape(term) + @"(?![a-z0-9])",
                RegexOptions.CultureInvariant);
        }

        // ============================================================================
        // 发送服务
        // ============================================================================

        /// <summary>检查是否在冷却中</summary>
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
