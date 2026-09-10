using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using System.Windows.Forms;

namespace Zero.Core
{
    // ★ 核心修复 1：告诉 VMP 不要混淆这个数据类的结构
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class ItemTemplate
    {
        // ★ 核心修复 2：强制指定 JSON 里的键名，这样就算属性名被混淆了，Json 也能精准对号入座
        [JsonProperty("Item_ID")]
        public int Item_ID { get; set; }

        [JsonProperty("Chinese_Name")]
        public string Chinese_Name { get; set; }

        [JsonProperty("English_Name")]
        public string English_Name { get; set; }

        [JsonProperty("Quality_ID")]
        public int Quality_ID { get; set; }
    }

    public static class ItemDbManager
    {
        public static Dictionary<int, ItemTemplate> IdMap = new Dictionary<int, ItemTemplate>();
        public static Dictionary<string, int> NameMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private static bool _isLoaded = false;

        public static void LoadDatabase()
        {
            if (_isLoaded) return;

            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                // 恢复为内嵌资源方式
                string resourceName = "Zero.resource.item_template.json";

                using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        MessageBox.Show(
                            "未找到物品数据库文件！请确保已将其设置为【嵌入的资源】。",
                            "致命错误",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error
                        );
                        return;
                    }

                    using (StreamReader reader = new StreamReader(stream))
                    {
                        string json = reader.ReadToEnd();

                        // 解析 JSON
                        List<ItemTemplate> items = JsonConvert.DeserializeObject<List<ItemTemplate>>(json);

                        if (items != null)
                        {
                            foreach (var item in items)
                            {
                                // 存入ID映射字典
                                IdMap[item.Item_ID] = item;

                                // 存入名字映射字典
                                if (!string.IsNullOrEmpty(item.Chinese_Name)) NameMap[item.Chinese_Name] = item.Item_ID;
                                if (!string.IsNullOrEmpty(item.English_Name)) NameMap[item.English_Name] = item.Item_ID;
                            }

                            // 此时 Logger 已经是安全的，可以放心调用
                            Logger.Write($"[UI控制台] 物品数据库加载成功，共载入 {IdMap.Count} 条记录。");
                        }
                    }
                }
                _isLoaded = true;
            }
            catch (Exception ex)
            {
                Logger.Write($"[UI控制台] 物品数据库加载失败: " + ex.Message);
                MessageBox.Show(
                    "物品数据库加载失败！\n原因: " + ex.Message,
                    "致命错误",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }

        public static HashSet<int> ParseNamesToIds(string namesStr)
        {
            HashSet<int> ids = new HashSet<int>();
            if (string.IsNullOrWhiteSpace(namesStr)) return ids;

            string[] names = namesStr.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var name in names)
            {
                string cleanName = name.Trim();
                if (NameMap.TryGetValue(cleanName, out int id))
                {
                    ids.Add(id);
                }
            }
            return ids;
        }
    }
}