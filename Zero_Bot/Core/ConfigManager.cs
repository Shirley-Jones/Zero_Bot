using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Zero.Core
{
    // 将配置类名改为 AccountConfig 更贴合当前逻辑
    public class AccountConfig
    {
        public string PassWord { get; set; } = "";
        public string PlayerName { get; set; } = "";
        public string RealmName { get; set; } = "";
        public string ScriptName { get; set; } = "";
        public string Whitelist { get; set; } = "魔粉,传送符文,传送门符文";
        public string Destruction { get; set; } = "蛮荒之叶,荆棘幼崽的种子,一本写满评论的《纳特·帕格的钓鱼技巧完全攻略》";
        public bool KeepExquisite { get; set; } = true;
        public bool KeepEpic { get; set; } = true;
        public bool KeepLegend { get; set; } = true;
        public string TeamName { get; set; } = "";
        public bool Mailing_exquisite { get; set; } = true;     //邮寄蓝装
        public bool Mailing_epic { get; set; } = true;          //邮寄紫色
        public bool Mailing_Gold { get; set; } = true;          //邮寄金币
        public string Mailing_whitelist { get; set; } = "旅行者的背包";     //邮寄白名单
        public string Mailing_name { get; set; } = "";          //邮寄角色名字
    }

    public static class ConfigManager
    {
        /// <summary>
        /// 即时读取配置文件。主键已改为 Account(账号)
        /// </summary>
        public static AccountConfig GetConfig(string accountName)
        {
            try
            {
                string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CharacterConfigs.json");

                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    var allConfigs = JsonConvert.DeserializeObject<Dictionary<string, AccountConfig>>(json);

                    if (allConfigs != null && !string.IsNullOrEmpty(accountName) && allConfigs.TryGetValue(accountName, out var config))
                    {
                        return config;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Write($"[UI控制台] 读取配置文件失败: {ex.Message}");
            }

            return new AccountConfig();
        }


        /// <summary>
        /// 是否开启调试日志写文件（默认关，安全）
        /// </summary>
        public static bool DebugLogEnabled { get; private set; } = false;

        private static readonly string SettingsPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Settings.txt");

        /// <summary>
        /// 从 Settings.txt 读取 DebugLog 开关（程序启动 / 配置变更后调用）
        /// 格式示例：DebugLog="True"
        /// </summary>
        public static void LoadDebugLogSetting()
        {
            DebugLogEnabled = false; // 读不到默认关

            try
            {
                if (!File.Exists(SettingsPath))
                    return;

                var debugKv = File.ReadAllLines(SettingsPath)
                    .Select(line => line.Trim())
                    .Where(line => !string.IsNullOrEmpty(line) && line.Contains("="))
                    .Select(line =>
                    {
                        int eq = line.IndexOf('=');
                        string key = line.Substring(0, eq).Trim();
                        string val = line.Substring(eq + 1).Trim().Trim('"');
                        return new { Key = key, Value = val };
                    })
                    .FirstOrDefault(x => x.Key.Equals("DebugLog", StringComparison.OrdinalIgnoreCase));

                if (debugKv != null && bool.TryParse(debugKv.Value, out bool result))
                    DebugLogEnabled = result;
            }
            catch
            {
                // 配置读坏也不崩
            }
        }

        /// <summary>
        /// 不读盘，直接同步内存（UI 保存后热生效用，最快）
        /// </summary>
        public static void ApplyDebugLogState(bool enabled)
        {
            DebugLogEnabled = enabled;
        }

    }
}