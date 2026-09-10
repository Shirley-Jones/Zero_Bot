using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using Newtonsoft.Json;
using Zero.Core;

namespace Zero
{
    public partial class Role_Management : Form
    {
        // 传递回主界面的关键信息
        public string SelectedAccount = "";
        public string SelectedScriptName = "";

        private bool _isEditMode = false;
        private string _editAccountName = "";

        // 新增账号时的构造函数
        public Role_Management()
        {
            InitializeComponent();
            _isEditMode = false;
        }

        // 编辑现有账号时的构造函数
        public Role_Management(string accountName, string currentScript)
        {
            InitializeComponent();
            _isEditMode = true;
            _editAccountName = accountName;
            SelectedScriptName = currentScript;
        }

        private void Form2_Load(object sender, EventArgs e)
        {
            // 初始化脚本下拉框
            comboBox_Script.Items.Clear();
            foreach (var scriptTemplate in Main.ScriptLibrary)
            {
                comboBox_Script.Items.Add(new ScriptClient
                {
                    DispName = scriptTemplate.ScriptName,
                    FileName = scriptTemplate.ScriptName
                });
            }

            if (_isEditMode)
            {
                Input_username.Text = _editAccountName;
                Input_username.Enabled = false; // 编辑模式下不允许修改账号名（作为唯一ID）
                LoadConfig(_editAccountName);
            }
            else
            {
                SetDefaultConfig();
            }
        }

        private void LoadConfig(string accountName)
        {
            string filePath = Path.Combine(Application.StartupPath, "CharacterConfigs.json");
            var allConfigs = LoadAllConfigs(filePath);

            if (allConfigs.ContainsKey(accountName))
            {
                var cfg = allConfigs[accountName];

                Input_password.Text = cfg.PassWord;
                Input_player_name.Text = cfg.PlayerName;
                Input_realm_name.Text = cfg.RealmName;

                if (!string.IsNullOrEmpty(cfg.ScriptName))
                {
                    foreach (ScriptClient sc in comboBox_Script.Items)
                    {
                        if (sc.DispName == cfg.ScriptName)
                        {
                            comboBox_Script.SelectedItem = sc;
                            break;
                        }
                    }
                }

                item_whitelist.Text = cfg.Whitelist;
                item_destructionlist.Text = cfg.Destruction;
                exquisite_box.Checked = cfg.KeepExquisite;
                epic_box.Checked = cfg.KeepEpic;
                legend_box.Checked = cfg.KeepLegend;
                team_name.Text = cfg.TeamName;
                mailing_exquisite.Checked = cfg.Mailing_exquisite;
                mailing_epic.Checked = cfg.Mailing_epic;
                mailing_Gold.Checked = cfg.Mailing_Gold;
                mailing_whitelist.Text = cfg.Mailing_whitelist;
                mailing_name.Text = cfg.Mailing_name;
            }
            else
            {
                SetDefaultConfig();
            }
        }

        private void SetDefaultConfig()
        {
            Input_password.Text = "";     //密码
            Input_player_name.Text = "";     //角色名字
            Input_realm_name.Text = "";     //服务器名字
            item_whitelist.Text = "魔粉,传送符文,传送门符文";      //物品白名单
            item_destructionlist.Text = "蛮荒之叶,荆棘幼崽的种子,一本写满评论的《纳特·帕格的钓鱼技巧完全攻略》";     //摧毁名单
            exquisite_box.Checked = true;      //保留蓝装
            epic_box.Checked = true;          //保留紫装 
            legend_box.Checked = true;      //保留橙装  传说
            team_name.Text = "";        //队员名字
            mailing_exquisite.Checked = true;     //邮寄蓝装
            mailing_epic.Checked = true;        //邮寄紫装
            mailing_Gold.Checked = true;         //邮寄金币
            mailing_whitelist.Text = "旅行者的背包";        //邮寄白名单
            mailing_name.Text = "";             //邮寄角色名字
            if (comboBox_Script.Items.Count > 0) comboBox_Script.SelectedIndex = 0;
        }

        private void SaveConfig(string accountName)
        {
            string filePath = Path.Combine(Application.StartupPath, "CharacterConfigs.json");
            var allConfigs = LoadAllConfigs(filePath);

            string currentScript = "";
            if (comboBox_Script.SelectedItem != null)
                currentScript = ((ScriptClient)comboBox_Script.SelectedItem).DispName;

            allConfigs[accountName] = new AccountConfig
            {
                PassWord = Input_password.Text.Trim(),
                PlayerName = Input_player_name.Text.Trim(),
                RealmName = Input_realm_name.Text.Trim(),
                ScriptName = currentScript,
                Whitelist = item_whitelist.Text.Replace("\r", "").Replace("\n", ""),
                Destruction = item_destructionlist.Text.Replace("\r", "").Replace("\n", ""),
                KeepExquisite = exquisite_box.Checked,
                KeepEpic = epic_box.Checked,
                KeepLegend = legend_box.Checked,
                TeamName = team_name.Text.Replace("\r", "").Replace("\n", ""),
                Mailing_exquisite = mailing_exquisite.Checked,
                Mailing_epic = mailing_epic.Checked,
                Mailing_Gold = mailing_Gold.Checked,
                Mailing_whitelist = mailing_whitelist.Text.Replace("\r", "").Replace("\n", ""),
                Mailing_name = mailing_name.Text.Replace("\r", "").Replace("\n", ""),

            };

            SaveAllConfigs(filePath, allConfigs);
        }

        private void btn_Confirm_Click(object sender, EventArgs e)
        {
            string account = Input_username.Text.Trim();
            if (string.IsNullOrEmpty(account)) { MessageBox.Show("账号不能为空！"); return; }
            if (comboBox_Script.SelectedItem == null) { MessageBox.Show("请选择一个脚本！"); return; }

            SelectedAccount = account;
            SelectedScriptName = ((ScriptClient)comboBox_Script.SelectedItem).DispName;

            // 保存到 JSON
            SaveConfig(SelectedAccount);

            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void btn_cancel_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }

        // ================= 使用 Newtonsoft.Json 替代手写字符串拼接 =================
        public static Dictionary<string, AccountConfig> LoadAllConfigs(string filePath)
        {
            if (!File.Exists(filePath)) return new Dictionary<string, AccountConfig>();
            try
            {
                string json = File.ReadAllText(filePath);
                var dict = JsonConvert.DeserializeObject<Dictionary<string, AccountConfig>>(json);
                return dict ?? new Dictionary<string, AccountConfig>();
            }
            catch
            {
                return new Dictionary<string, AccountConfig>();
            }
        }

        private void SaveAllConfigs(string filePath, Dictionary<string, AccountConfig> allConfigs)
        {
            try
            {
                // Formatting.Indented 使 JSON 保存为美观的多行格式
                string json = JsonConvert.SerializeObject(allConfigs, Formatting.Indented);
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存配置失败: {ex.Message}");
            }
        }

        public class ScriptClient
        {
            public string DispName { get; set; }
            public string FileName { get; set; }
            public override string ToString() { return DispName; }
        }
    }
}