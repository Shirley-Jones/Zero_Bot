using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Zero.Script;
using Zero.Core;
using System.Linq;
using System.Reflection;

namespace Zero
{
    public partial class Main : Form
    {
        // ========================== 仅保留 DLL 注入需要的底层 API ==========================
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        public const int GWL_STYLE = -16;
        public const int WS_CAPTION = 0x00C00000;      // 标题栏
        public const int WS_THICKFRAME = 0x00040000;   // 调整大小的边框

        public class BotContext
        {
            public bool IsRunning = false;
            public int ModuleBaseAddress = 0;
            public DLLManager CurrentScript = null; // 挂载全自动化状态机实例
        }

        Dictionary<int, BotContext> RunningBots = new Dictionary<int, BotContext>();

        // 🌟 新增：全局异步任务打断锁
        private bool _cancelStartAll = false;

        // 全局 C# 副本脚本类注册中心
        public static List<DLLManager> ScriptLibrary = new List<DLLManager>
        {
            new DireMaulEast(),
            new LeaveParty(),
            new AutoFishing()
            //new DireMaulEast_Hunter()
        };

        // 3. 重写 WndProc 方法，拦截并处理 Windows 系统消息
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Program.WM_SHOWME)
            {
                WakeUpWindow();
            }
            base.WndProc(ref m);
        }

        // 4. 执行唤醒窗口的具体逻辑
        private void WakeUpWindow()
        {
            if (this.WindowState == FormWindowState.Minimized)
            {
                this.WindowState = FormWindowState.Normal;
            }
            this.TopMost = true;   // 瞬间置顶
            this.Activate();       // 激活并赋予焦点
            this.TopMost = false;  // 解除永久置顶，恢复正常窗体行为
        }

        Dictionary<int, string> RACE_DICT = new Dictionary<int, string>() {
            //适配Turtle WoW种族
            {1, "人类"}, {2, "兽人"}, {3, "矮人"}, {4, "暗夜精灵"}, {5, "亡灵"}, {6, "牛头人"}, {7, "侏儒"}, {8, "巨魔"}, {9, "地精"}, {10, "高等精灵"}
        };
        Dictionary<int, string> CLASS_DICT = new Dictionary<int, string>() {
            {1, "战士"}, {2, "圣骑士"}, {3, "猎人"}, {4, "潜行者"}, {5, "牧师"}, {7, "萨满祭司"}, {8, "法师"}, {9, "术士"}, {11, "德鲁伊"}
        };

        // DLL 注入辅助 (64位主程序调用版 - 高级安全模式)
        private bool InjectDLL(int pid, string dllPath)
        {
            if (!File.Exists(dllPath))
            {
                MessageBox.Show("系统找不到指定的 DLL 文件！请重新下载本程序！", "致命错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            string injectorPath = Path.Combine(Application.StartupPath, "Zero_Injector.exe");
            if (!File.Exists(injectorPath))
            {
                MessageBox.Show("系统找不到指定的 Zero_Injector.exe 文件！请重新下载本程序！", "致命错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            try
            {
                string dynamicToken = "ZeroBot_Auth_" + Guid.NewGuid().ToString();

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = injectorPath,
                    Arguments = "",
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardInput = true
                };

                using (Process p = Process.Start(psi))
                {
                    if (p != null)
                    {
                        p.StandardInput.WriteLine($"{dynamicToken}|{pid}|{dllPath}");
                        p.StandardInput.Flush();
                        p.WaitForExit();
                        return p.ExitCode == 0;
                    }
                    return false;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("调用 32 位注入器失败：" + ex.Message, "致命错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        // 启动前改写 config.wtf（服务器名 + 图形/窗口参数）
        private void UpdateRealmNameInConfigWtf(string exePath, string targetRealmName)
        {
            if (string.IsNullOrEmpty(exePath))
                return;

            try
            {
                string wowDir = Path.GetDirectoryName(exePath);
                if (string.IsNullOrEmpty(wowDir)) return;

                string wtfDir = Path.Combine(wowDir, "WTF");
                string configPath = Path.Combine(wtfDir, "config.wtf");

                if (!Directory.Exists(wtfDir))
                    Directory.CreateDirectory(wtfDir);

                List<string> lines = File.Exists(configPath)
                    ? new List<string>(File.ReadAllLines(configPath))
                    : new List<string>();

                // —— 服务器名（原有逻辑，有效才写）——
                if (!string.IsNullOrEmpty(targetRealmName) && targetRealmName != "未知服务器")
                {
                    SetWtfValue(lines, "realmName", targetRealmName);
                }

                // —— 新增的配置（有则改，无则追加）——
                SetWtfValue(lines, "gxColorBits", "24");     //位色
                SetWtfValue(lines, "gxDepthBits", "24");     //位色深
                SetWtfValue(lines, "gxResolution", "800x600");     //分辨率
                SetWtfValue(lines, "gxRefresh", "60");      //刷新率
                SetWtfValue(lines, "gxWindow", "1");      // 窗口化
                SetWtfValue(lines, "gxMaximize", "0");      //最大化
                SetWtfValue(lines, "useUiScale", "0");      //使用UI缩放


                // 1.12.1 客户端只认 ASCII 行，统一写成 SET key "value" 格式
                // 原版客户端自己回写也是这种带引号格式，完全兼容
                File.WriteAllLines(configPath, lines);
            }
            catch (Exception ex)
            {
                Logger.Write($"[UI控制台] 修改 config.wtf 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 在 wtf 行集合中：找到 SET key xxx 就原地替换，找不到就追加到末尾。
        /// 忽略大小写，兼容行首空格，不误伤注释行。
        /// 统一输出格式：SET key "value"
        /// </summary>
        private static void SetWtfValue(List<string> lines, string key, string value)
        {
            // 匹配：行首(允许空白) -> SET -> 空白 -> key(单词边界) 
            var regex = new System.Text.RegularExpressions.Regex(
                @"^\s*SET\s+" + System.Text.RegularExpressions.Regex.Escape(key) + @"\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            for (int i = 0; i < lines.Count; i++)
            {
                if (regex.IsMatch(lines[i]))
                {
                    lines[i] = $"SET {key} \"{value}\"";
                    return;
                }
            }

            // 没找到，追加
            lines.Add($"SET {key} \"{value}\"");
        }


        // 崩溃自动守护/无缝重启逻辑
        private async Task AutoRestartCrashedGameAsync(DataGridViewRow row)
        {
            // 防抖锁：如果已经在重启中了，直接跳过，防止多开爆炸
            if (row.Cells[10].Value?.ToString() == "正在断线重连") return;

            row.Cells[10].Value = "正在断线重连";
            //row.DefaultCellStyle.BackColor = System.Drawing.Color.Orange;

            string accountName = row.Cells[1].Value.ToString();
            string playerName = row.Cells[2].Value.ToString();
            string targetRealm = row.Cells[3].Value.ToString();
            string scriptName = row.Cells[9].Value.ToString();

            if (string.IsNullOrEmpty(Client_path.Text) || !File.Exists(Client_path.Text))
            {
                row.Cells[10].Value = "客户端路径错误，重启失败";
                //row.DefaultCellStyle.BackColor = System.Drawing.Color.Red;
                return;
            }

            try
            {
                // 1. 启动前修改 config.wtf 锁定服务器
                UpdateRealmNameInConfigWtf(Client_path.Text, targetRealm);

                // 2. 拉起新进程
                Process newGame = Process.Start(Client_path.Text);
                int newPid = newGame.Id;

                // 立刻更新 UI 上的 PID，这一步非常关键！
                row.Cells[0].Value = newPid;

                // 3. 异步等待 2.5 秒，给客户端加载界面的时间
                await Task.Delay(2500);

                // 判断这 2.5 秒内玩家有没有手动点“停止”或“删除”
                if (row.DataGridView == null || row.Cells[10].Value?.ToString() != "正在断线重连")
                {
                    try { newGame.Kill(); } catch { } // 如果玩家反悔了，直接把新开的杀掉
                    return;
                }

                // 打入 CTM 防断触内存补丁
                MemoryAPI.FixCTMMouseShake(newPid, newGame.MainModule.BaseAddress.ToInt32());

                // 4. 重新注入 DLL
                string dllPath = Path.Combine(Application.StartupPath, "Zero_Core.dll");
                if (!InjectDLL(newPid, dllPath))
                {
                    row.Cells[10].Value = "断线重连注入失败";
                    //row.DefaultCellStyle.BackColor = System.Drawing.Color.Red;
                    return;
                }

                // 5. 重新组装并激活脚本大脑
                var scriptTemplate = ScriptLibrary.Find(s => s.ScriptName == scriptName);
                if (scriptTemplate != null)
                {
                    var newScript = (DLLManager)Activator.CreateInstance(scriptTemplate.GetType());
                    newScript.PID = newPid;
                    newScript.MainForm = this;
                    newScript.ModuleBaseAddress = newGame.MainModule.BaseAddress.ToInt32();
                    newScript.AccountName = accountName;
                    newScript.PlayerName = playerName;

                    if (!RunningBots.ContainsKey(newPid)) RunningBots[newPid] = new BotContext();
                    RunningBots[newPid].CurrentScript = newScript;
                    RunningBots[newPid].IsRunning = true;
                    RunningBots[newPid].ModuleBaseAddress = newGame.MainModule.BaseAddress.ToInt32();
                }

                // 6. 重置排版并恢复 UI 状态
                //AutoArrangeWindows();
                row.Cells[10].Value = "重连成功";
                //row.DefaultCellStyle.BackColor = System.Drawing.Color.Empty;
            }
            catch (Exception ex)
            {
                row.Cells[10].Value = "重连异常: " + ex.Message;
                //row.DefaultCellStyle.BackColor = System.Drawing.Color.Red;
            }
        }


        private void btn_Add_Click(object sender, EventArgs e)
        {
            Role_Management f2 = new Role_Management();

            if (f2.ShowDialog() == DialogResult.OK)
            {
                var config = ConfigManager.GetConfig(f2.SelectedAccount);

                // 🌟 修复：总共 15 列，索引必须一一对应
                dataGridView1.Rows.Add(
                    0, // 0: PID
                    f2.SelectedAccount, // 1: 账号
                    config.PlayerName, // 2: 角色名
                    config.RealmName, // 3: 服务器名
                    "-", // 4: 等级
                    "-", // 5: 职业
                    "-", // 6: 血
                    "-", // 7: 蓝
                    "-", // 8: 金
                    f2.SelectedScriptName, // 9: 脚本名称
                    "空闲", // 10: 状态
                    "开始", "停止", "编辑", "删除" // 11-14: 按钮
                );
            }
        }

        // DataGridView 内置操作盘点击
        private async void dataGridView1_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;

            string colName = dataGridView1.Columns[e.ColumnIndex].HeaderText;
            int pid = Convert.ToInt32(dataGridView1.Rows[e.RowIndex].Cells[0].Value);
            string accountName = dataGridView1.Rows[e.RowIndex].Cells[1].Value.ToString();
            string PlayerName = dataGridView1.Rows[e.RowIndex].Cells[2].Value.ToString();

            // 🌟 修复 Bug 1：脚本名称因为列增加，现在移到了第 9 列 (Cells[9])
            string scriptName = dataGridView1.Rows[e.RowIndex].Cells[9].Value.ToString();

            if (colName == "删除")
            {
                if (RunningBots.ContainsKey(pid) && RunningBots[pid].IsRunning)
                {
                    MessageBox.Show("请先停止脚本再删除", "致命错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                if (RunningBots.ContainsKey(pid))
                {
                    if (RunningBots[pid].CurrentScript != null) RunningBots[pid].CurrentScript.Dispose();
                    RunningBots.Remove(pid);
                }

                // 🌟 修复 Bug 2：在 UI 上删除的同时，一并从 JSON 文件中抹除记录
                string jsonPath = Path.Combine(Application.StartupPath, "CharacterConfigs.json");
                var allConfigs = Role_Management.LoadAllConfigs(jsonPath);
                if (allConfigs.ContainsKey(accountName))
                {
                    allConfigs.Remove(accountName);
                    string json = JsonConvert.SerializeObject(allConfigs, Formatting.Indented);
                    File.WriteAllText(jsonPath, json);
                }

                dataGridView1.Rows.RemoveAt(e.RowIndex);
            }
            else if (colName == "编辑")
            {
                Role_Management f2 = new Role_Management(accountName, scriptName);

                if (f2.ShowDialog() == DialogResult.OK)
                {
                    dataGridView1.Rows[e.RowIndex].Cells[9].Value = f2.SelectedScriptName; // 第 9 列是脚本名称
                    var newConfig = ConfigManager.GetConfig(f2.SelectedAccount);
                    dataGridView1.Rows[e.RowIndex].Cells[2].Value = newConfig.PlayerName;
                    dataGridView1.Rows[e.RowIndex].Cells[3].Value = newConfig.RealmName;
                }
            }
            else if (colName == "开始")
            {
                dataGridView1.Rows[e.RowIndex].Cells[10].Value = "准备中";
                var scriptTemplate = ScriptLibrary.Find(s => s.ScriptName == scriptName);
                if (scriptTemplate == null)
                {
                    MessageBox.Show("未在注册库中定位到脚本类,请重新下载本程序！", "致命错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                try
                {
                    // 💥 新增防死尸逻辑：不管 PID 是多少，先确认它是不是还活着，死了就清 0！
                    if (pid != 0) { try { var cp = Process.GetProcessById(pid); if (cp.HasExited) pid = 0; } catch { pid = 0; } }

                    if (pid == 0)
                    {
                        if (string.IsNullOrEmpty(Client_path.Text) || !File.Exists(Client_path.Text))
                        {
                            MessageBox.Show("请先选择正确的 Wow.exe 客户端路径！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }

                        // 在启动前，读取当前行的服务器名，并强制写入 config.wtf
                        string targetRealm = dataGridView1.Rows[e.RowIndex].Cells[3].Value.ToString();
                        UpdateRealmNameInConfigWtf(Client_path.Text, targetRealm);


                        Process newGame = Process.Start(Client_path.Text);
                        pid = newGame.Id;
                        dataGridView1.Rows[e.RowIndex].Cells[0].Value = pid;
                        dataGridView1.Rows[e.RowIndex].DefaultCellStyle.BackColor = System.Drawing.Color.Empty;

                        //  1. 异步等待客户端完全跑到登录界面，这期间 C# 界面绝对不卡！
                        await Task.Delay(2500);

                        // 打入 CTM 防断触内存补丁
                        int baseAddress = newGame.MainModule.BaseAddress.ToInt32();
                        MemoryAPI.FixCTMMouseShake(pid, baseAddress);
                        // 2. 趁着游戏在登录界面发呆，赶紧排版！绝对不会卡死客户端
                        //AutoArrangeWindows();
                        //await Task.Delay(500); // 留出 0.5 秒给 DirectX 引擎重绘画面

                        // 3. 排版完之后再注入 DLL
                        string dllPath = Path.Combine(Application.StartupPath, "Zero_Core.dll");
                        if (!InjectDLL(pid, dllPath))
                        {
                            MessageBox.Show("DLL 注入失败！请务必右键以【管理员身份运行】", "致命错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return;
                        }
                    }

                    // 4. 一切就绪，接通挂机引擎（这会激活 AutoLoginTick 开始输账号密码）
                    Process p = Process.GetProcessById(pid);
                    //if (!RunningBots.ContainsKey(pid)) RunningBots[pid] = new BotContext();
                    if (!RunningBots.ContainsKey(pid))
                    {
                        RunningBots[pid] = new BotContext();
                    }
                    else if (RunningBots[pid].CurrentScript != null)
                    {
                        // 修复 3：如果在没死透的情况下重新点开始，强制清理老旧实例防撞车！
                        RunningBots[pid].CurrentScript.Dispose();
                        RunningBots[pid].CurrentScript = null;
                    }
                    var newScript = (DLLManager)Activator.CreateInstance(scriptTemplate.GetType());
                    newScript.PID = pid;
                    newScript.MainForm = this;
                    newScript.ModuleBaseAddress = p.MainModule.BaseAddress.ToInt32();
                    newScript.AccountName = accountName;
                    newScript.PlayerName = PlayerName;

                    RunningBots[pid].CurrentScript = newScript;
                    RunningBots[pid].IsRunning = true;
                    RunningBots[pid].ModuleBaseAddress = p.MainModule.BaseAddress.ToInt32();

                    dataGridView1.Rows[e.RowIndex].Cells[10].Value = "运行中";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"启动进程失败: {ex.Message}", "致命错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            else if (colName == "停止")
            {
                if (RunningBots.ContainsKey(pid))
                {
                    RunningBots[pid].IsRunning = false;
                    dataGridView1.Rows[e.RowIndex].Cells[10].Value = "强行停止";
                    if (RunningBots[pid].CurrentScript != null)
                    {
                        // 💥 修复 1：释放旧的脚本实例，解开共享内存占用锁！
                        RunningBots[pid].CurrentScript.Dispose();
                        RunningBots[pid].CurrentScript = null;
                    }
                }
            }
        }

        // 🌟 修复：处理死亡进程的统一逻辑
        private void HandleDeadProcess(DataGridViewRow row, int oldPid)
        {
            bool wasRunning = false;
            if (RunningBots.ContainsKey(oldPid))
            {
                wasRunning = RunningBots[oldPid].IsRunning;
                if (RunningBots[oldPid].CurrentScript != null)
                {
                    RunningBots[oldPid].CurrentScript.Dispose();
                }
                RunningBots.Remove(oldPid);
            }

            if (wasRunning)
            {
                _ = AutoRestartCrashedGameAsync(row);
            }
            else
            {
                //row.DefaultCellStyle.BackColor = System.Drawing.Color.Red;
                row.Cells[10].Value = "已关闭";

                // 💥 核心修复 2：游戏彻底关了，必须把 PID 清零，下次点开始才会起新号！
                row.Cells[0].Value = 0;
            }
        }

        private void UpdateTimer_Tick(object sender, EventArgs e)
        {
            foreach (DataGridViewRow row in dataGridView1.Rows)
            {
                if (row.IsNewRow || row.Cells[0].Value == null) continue;
                int pid = Convert.ToInt32(row.Cells[0].Value);
                if (pid == 0) continue; // 如果还没启动游戏，直接跳过内存读取

                IntPtr hProcess = MemoryAPI.OpenProcess(MemoryAPI.PROCESS_ALL_ACCESS, false, pid);
                if (hProcess == IntPtr.Zero)
                {
                    //row.DefaultCellStyle.BackColor = System.Drawing.Color.Red;
                    continue;
                }

                try
                {
                    Process p;
                    int ModuleBaseAddress = 0;

                    try
                    {
                        p = Process.GetProcessById(pid);
                        if (p.HasExited)
                        {
                            // 🌟 核心拦截 1：进程安全退出了
                            HandleDeadProcess(row, pid);
                            continue;
                        }

                        //row.DefaultCellStyle.BackColor = System.Drawing.Color.Empty;
                        ModuleBaseAddress = p.MainModule.BaseAddress.ToInt32();
                    }
                    catch (ArgumentException)
                    {
                        // 进程直接蒸发了（闪退/被强杀）
                        HandleDeadProcess(row, pid);
                        continue;
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    // ==========================================
                    // 游戏版本安全校验 
                    // ==========================================
                    // 读取 WoW.exe + 0x437BFC 的子版本号字符串，截取少量长度并去除空格
                    string buildVersion = MemoryAPI.ReadString(hProcess, ModuleBaseAddress + 0x437BFC, 16).Trim();

                    // 如果既不是WoW 1.12.1 (5875) 也是不 Turtle WoW 1.18.1 (7272)
                    if (buildVersion != "5875" && buildVersion != "7272")
                    {
                        row.Cells[10].Value = $"不支持的版本 [{buildVersion}]";
                        //row.DefaultCellStyle.BackColor = System.Drawing.Color.DarkOrange; // UI 变色预警

                        // 如果此时脚本还在运行，强制挂起，防止内存基址偏移导致外挂或游戏崩溃
                        if (RunningBots.ContainsKey(pid) && RunningBots[pid].IsRunning)
                        {
                            RunningBots[pid].IsRunning = false;
                        }

                        continue; // 彻底阻断，不往下执行对象管理器的任何读取
                    }
                    // ==========================================

                    int objMgrPtr = 0x00B41414;
                    int objMgr = MemoryAPI.ReadInteger(hProcess, objMgrPtr);
                    if (objMgr == 0 || MemoryAPI.ReadInteger(hProcess, objMgr + 0xAC) == 0)
                    {
                        if (RunningBots.ContainsKey(pid) && RunningBots[pid].IsRunning)
                        {
                            BotContext ctx = RunningBots[pid];
                            if (ctx.CurrentScript != null)
                            {
                                // 如果副本脚本举起了令牌，全局服务立刻停手，假装没看见！
                                if (ctx.CurrentScript.PauseGlobalAutoLogin)
                                {
                                    row.Cells[10].Value = "等待小退重置";
                                    continue;
                                }

                                string accountName = row.Cells[1].Value.ToString();
                                var config = ConfigManager.GetConfig(accountName);

                                //自动登录
                                string status = ctx.CurrentScript.AutoLoginTick(hProcess, accountName, config.PassWord, config.RealmName, config.PlayerName);
                                row.Cells[10].Value = status;

                                if (status.Contains("登录失败"))
                                {
                                    ctx.IsRunning = false;
                                    //row.DefaultCellStyle.BackColor = System.Drawing.Color.Yellow;
                                }
                            }
                        }
                        continue;
                    }

                    ulong playerGuid = MemoryAPI.ReadQword(hProcess, objMgr + 0xC0);
                    int currentObj = MemoryAPI.ReadInteger(hProcess, objMgr + 0xAC);
                    int playerBase = 0;

                    while (currentObj != 0)
                    {
                        ulong thisGuid = MemoryAPI.ReadQword(hProcess, currentObj + 0x30);
                        if (thisGuid == playerGuid) { playerBase = currentObj; break; }
                        currentObj = MemoryAPI.ReadInteger(hProcess, currentObj + 0x3C);
                    }

                    if (playerBase != 0)
                    {
                        string accountName = row.Cells[1].Value?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(accountName))
                        {
                            var config = ConfigManager.GetConfig(accountName);
                            row.Cells[2].Value = string.IsNullOrEmpty(config.PlayerName) ? "等待读取" : config.PlayerName;
                            row.Cells[3].Value = string.IsNullOrEmpty(config.RealmName) ? "未知服务器" : config.RealmName;
                        }

                        int descriptors = MemoryAPI.ReadInteger(hProcess, playerBase + 0x08);

                        // 🌟 修复 Bug 3：向后顺延索引，防止数据覆盖
                        row.Cells[4].Value = $"{MemoryAPI.ReadInteger(hProcess, descriptors + 0x88).ToString()} 级";
                        byte[] byte_0 = MemoryAPI.ReadBytes(hProcess, descriptors + 0x90, 4);
                        row.Cells[5].Value = $"{(RACE_DICT.ContainsKey(byte_0[0]) ? RACE_DICT[byte_0[0]] : "-")}{(CLASS_DICT.ContainsKey(byte_0[1]) ? CLASS_DICT[byte_0[1]] : "-")}";
                        row.Cells[6].Value = $"{MemoryAPI.ReadInteger(hProcess, descriptors + 0x58)}/{MemoryAPI.ReadInteger(hProcess, descriptors + 0x70)}";
                        row.Cells[7].Value = $"{MemoryAPI.ReadInteger(hProcess, descriptors + 0x5C)}/{MemoryAPI.ReadInteger(hProcess, descriptors + 0x74)}";
                        row.Cells[8].Value = $"{MemoryAPI.ReadInteger(hProcess, descriptors + 0x1260) / 10000} 金";

                        if (RunningBots.ContainsKey(pid) && RunningBots[pid].IsRunning)
                        {
                            BotContext ctx = RunningBots[pid];
                            if (ctx.CurrentScript != null)
                            {
                                string scriptStatus = ctx.CurrentScript.OnTick(hProcess, playerBase);
                                row.Cells[10].Value = scriptStatus; // 🌟 状态输出在第 10 列
                            }
                        }
                    }
                }
                finally
                {
                    MemoryAPI.CloseHandle(hProcess);
                }
            }
        }

        public Main()
        {
            InitializeComponent();
            //加载标题
            this.Text = $"Zero Release X64 编译时间: {BuildInfo.BuildDate}";
            dataGridView1.AllowUserToResizeColumns = false;
            dataGridView1.AllowUserToResizeRows = false;
            dataGridView1.DefaultCellStyle.SelectionBackColor = dataGridView1.DefaultCellStyle.BackColor;
            dataGridView1.DefaultCellStyle.SelectionForeColor = dataGridView1.DefaultCellStyle.ForeColor;
            dataGridView1.ScrollBars = ScrollBars.None;

            dataGridView1.MouseWheel += DataGridView1_MouseWheel;
            this.dataGridView1.CellContentClick += new DataGridViewCellEventHandler(this.dataGridView1_CellContentClick);
            foreach (DataGridViewColumn column in dataGridView1.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;

            this.Load += Main_Load;

            // 🌟 新增需求 4：在主程序窗口即将关闭时，清理它名下的魔兽世界进程
            this.FormClosing += Main_FormClosing;
        }

        // 🌟 新增需求 4 的核心实现：关闭时清退列表中的 WoW
        private void Main_FormClosing(object sender, FormClosingEventArgs e)
        {
            foreach (DataGridViewRow row in dataGridView1.Rows)
            {
                if (row.IsNewRow || row.Cells[0].Value == null) continue;

                int pid = Convert.ToInt32(row.Cells[0].Value);
                if (pid > 0)
                {
                    try
                    {
                        // 根据列表里记录的PID精准击杀，不杀其他的
                        Process p = Process.GetProcessById(pid);
                        if (p != null && !p.HasExited)
                        {
                            p.Kill();
                        }
                    }
                    catch { } // 如果进程已经被关掉，忽略异常
                }
            }
        }




        private string SettingsPath => Path.Combine(Application.StartupPath, "Settings.txt");

        /// <summary>
        /// 通用：只改指定 key 的值，其余行原样保留
        /// 文件不存在自动创建
        /// </summary>
        private void UpdateSetting(string key, string value)
        {
            // 读原文件（不存在就空列表）
            List<string> lines;
            if (File.Exists(SettingsPath))
                lines = File.ReadAllLines(SettingsPath).ToList();
            else
                lines = new List<string>();

            string target = $"{key}=\"{value}\"";
            bool found = false;

            // 找 key 那一行替换（忽略大小写）
            for (int i = 0; i < lines.Count; i++)
            {
                string raw = lines[i].Trim();
                if (string.IsNullOrEmpty(raw)) continue;

                int eq = raw.IndexOf('=');
                if (eq > 0)
                {
                    string k = raw.Substring(0, eq).Trim();
                    if (k.Equals(key, StringComparison.OrdinalIgnoreCase))
                    {
                        lines[i] = target;
                        found = true;
                        break;
                    }
                }
            }

            // 没找到就追加
            if (!found)
                lines.Add(target);

            // 回写（原文件里注释、空行、其他 key 全部保留）
            File.WriteAllLines(SettingsPath, lines);
        }

        private void SaveSettings()
        {
            // 只更新自己关心的两行，其他内容原封不动
            UpdateSetting("Client_path", Client_path.Text);
            UpdateSetting("DebugLog", debug_log_box.Checked.ToString());
        }

        private bool _isLoading = false;
        private void LoadSettings()
        {
            _isLoading = true; // 挂起事件

            if (!File.Exists(SettingsPath))
            {
                _isLoading = false;
                return;
            }

            var dict = File.ReadAllLines(SettingsPath)
                .Where(line => !string.IsNullOrWhiteSpace(line) && line.Contains('='))
                .Select(line =>
                {
                    int eq = line.IndexOf('=');
                    string key = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim().Trim('"');
                    return new { key, val };
                })
                .ToDictionary(x => x.key, x => x.val, StringComparer.OrdinalIgnoreCase);

            // 客户端路径
            if (dict.TryGetValue("Client_path", out string clientPath) && !string.IsNullOrEmpty(clientPath))
            {
                Client_path.Text = clientPath;
            }

            // Debug 状态：以文件为准
            bool debugState = false;
            if (dict.TryGetValue("DebugLog", out string debugVal))
                debugState = bool.TryParse(debugVal, out bool b) && b;

            ConfigManager.ApplyDebugLogState(debugState); // ✅ 内存同步（热更新源）
            debug_log_box.Checked = debugState;           // ✅ UI 同步（不触发保存）

            _isLoading = false;
        }

        private void Main_Load(object sender, EventArgs e)
        {
            LoadSettings();

            string jsonPath = Path.Combine(Application.StartupPath, "CharacterConfigs.json");
            var allConfigs = Role_Management.LoadAllConfigs(jsonPath);

            foreach (var kvp in allConfigs)
            {
                string accountName = kvp.Key;
                var config = kvp.Value;

                // 🌟 修复：总共 15 列，索引一一对应
                dataGridView1.Rows.Add(
                    0, // 0: PID
                    accountName, // 1: 账号
                    string.IsNullOrEmpty(config.PlayerName) ? "等待读取" : config.PlayerName, // 2: 角色
                    string.IsNullOrEmpty(config.RealmName) ? "未知服务器" : config.RealmName, // 3: 服务器
                    "-", "-", "-", "-", "-", // 4,5,6,7,8 (等级 职业 血 蓝 金)
                    config.ScriptName,  // 9: 脚本名称
                    "空闲", // 10: 状态
                    "开始", "停止", "编辑", "删除" // 11-14: 按钮
                );
            }
        }

        private void Select_client_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Filter = "WoW Executable (wow.exe)|wow.exe|All Files (*.*)|*.*";
                ofd.Title = "请选择 1.12.1 5875 客户端的 Wow.exe";

                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    Client_path.Text = ofd.FileName;
                    SaveSettings();   // ← 这里改成统一保存
                }
            }
        }

        private void DataGridView1_MouseWheel(object sender, MouseEventArgs e)
        {
            try
            {
                if (dataGridView1.RowCount == 0) return;
                int currentIndex = dataGridView1.FirstDisplayedScrollingRowIndex;
                if (currentIndex < 0) return;
                int scrollLines = 1;

                int availableHeight = dataGridView1.ClientSize.Height;
                if (dataGridView1.ColumnHeadersVisible)
                {
                    availableHeight -= dataGridView1.ColumnHeadersHeight;
                }

                double exactCapacity = (double)availableHeight / dataGridView1.RowTemplate.Height;
                int visibleCapacity = (int)Math.Round(exactCapacity, MidpointRounding.AwayFromZero);
                int maxIndex = Math.Max(0, dataGridView1.RowCount - visibleCapacity);

                if (e.Delta > 0)
                {
                    currentIndex -= scrollLines;
                    if (currentIndex < 0) currentIndex = 0;
                }
                else
                {
                    currentIndex += scrollLines;
                    if (currentIndex > maxIndex) currentIndex = maxIndex;
                }
                dataGridView1.FirstDisplayedScrollingRowIndex = currentIndex;
            }
            catch { }
        }

        // 🌟 新增：独立的自动排列窗口方法，供多处调用
        private void AutoArrangeWindows()
        {
            int visualWidth = 360;   //宽
            int visualHeight = 300;  //高
            int offsetLeft = 7;
            int offsetRight = 7;
            int offsetBottom = 7;
            int offsetTop = 0;

            int screenWidth = Screen.PrimaryScreen.WorkingArea.Width;
            int cols = screenWidth / visualWidth;
            if (cols == 0) cols = 1;

            int index = 0;
            foreach (DataGridViewRow row in dataGridView1.Rows)
            {
                if (row.IsNewRow || row.Cells[0].Value == null) continue;
                int pid = Convert.ToInt32(row.Cells[0].Value);
                if (pid == 0) continue; // 没启动的跳过

                try
                {
                    Process p = Process.GetProcessById(pid);
                    if (p != null && p.MainWindowHandle != IntPtr.Zero)
                    {
                        IntPtr hWnd = p.MainWindowHandle;
                        int baseX = (index % cols) * visualWidth;
                        int baseY = (index / cols) * visualHeight;
                        int finalX = baseX - offsetLeft;
                        int finalY = baseY - offsetTop;
                        int finalWidth = visualWidth + offsetLeft + offsetRight;
                        int finalHeight = visualHeight + offsetTop + offsetBottom;
                        MoveWindow(hWnd, finalX, finalY, finalWidth, finalHeight, true);
                        index++;
                    }
                }
                catch { }
            }
        }

        // 点击原版按钮，直接调用上面的方法
        private void adjust_the_window_Click(object sender, EventArgs e)
        {
            AutoArrangeWindows();
        }

        private async void Start_all_processes_Click(object sender, EventArgs e)
        {
            _cancelStartAll = false; // 启动前重置电闸
            List<int> newlyOpenedPids = new List<int>();

            // 【阶段 1】
            foreach (DataGridViewRow row in dataGridView1.Rows)
            {
                row.Cells[10].Value = "准备中";
                if (_cancelStartAll) return; // 🌟 只要电闸被拉下，立刻终止排队

                if (row.IsNewRow || row.Cells[0].Value == null) continue;
                int pid = Convert.ToInt32(row.Cells[0].Value);
                string scriptName = row.Cells[9].Value.ToString();

                if (RunningBots.ContainsKey(pid) && RunningBots[pid].IsRunning) continue;
                var scriptTemplate = ScriptLibrary.Find(s => s.ScriptName == scriptName);
                if (scriptTemplate == null) continue;

                // 💥 双重保险：就算 PID 不是 0，也摸一下脉，万一进程死了，立刻清零！
                if (pid != 0) { try { var cp = Process.GetProcessById(pid); if (cp.HasExited) pid = 0; } catch { pid = 0; } }

                if (pid == 0)
                {
                    if (string.IsNullOrEmpty(Client_path.Text) || !File.Exists(Client_path.Text))
                    {
                        MessageBox.Show("请先选择正确的 Wow.exe 客户端路径！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        continue;
                    }

                    string targetRealm = row.Cells[3].Value.ToString();
                    UpdateRealmNameInConfigWtf(Client_path.Text, targetRealm);

                    Process newGame = Process.Start(Client_path.Text);
                    pid = newGame.Id;
                    row.Cells[0].Value = pid;
                    //row.DefaultCellStyle.BackColor = System.Drawing.Color.Empty;

                    newlyOpenedPids.Add(pid);

                    // 等待期间如果点了停止全部，直接返回！
                    await Task.Delay(2500);
                    if (_cancelStartAll) return;
                }
            }

            // 【阶段 2】排版
            if (newlyOpenedPids.Count > 0)
            {
                //AutoArrangeWindows();
                //await Task.Delay(500);
                if (_cancelStartAll) return;
            }

            // 【阶段 3】注入和激活
            foreach (DataGridViewRow row in dataGridView1.Rows)
            {
                if (_cancelStartAll) return; // 🌟 最后的电闸检查

                if (row.IsNewRow || row.Cells[0].Value == null) continue;
                int pid = Convert.ToInt32(row.Cells[0].Value);
                string scriptName = row.Cells[9].Value.ToString();
                string accountName = row.Cells[1].Value.ToString();
                string PlayerName = row.Cells[2].Value.ToString();

                if (RunningBots.ContainsKey(pid) && RunningBots[pid].IsRunning) continue;
                var scriptTemplate = ScriptLibrary.Find(s => s.ScriptName == scriptName);
                if (scriptTemplate == null) continue;

                try
                {
                    if (newlyOpenedPids.Contains(pid))
                    {
                        // 在 DLL 注入前打入 CTM 内存补丁
                        Process newP = Process.GetProcessById(pid);
                        MemoryAPI.FixCTMMouseShake(pid, newP.MainModule.BaseAddress.ToInt32());
                        string dllPath = Path.Combine(Application.StartupPath, "Zero_Core.dll");
                        if (!InjectDLL(pid, dllPath)) continue;
                    }

                    Process p = Process.GetProcessById(pid);
                    //if (!RunningBots.ContainsKey(pid)) RunningBots[pid] = new BotContext();
                    if (!RunningBots.ContainsKey(pid))
                    {
                        RunningBots[pid] = new BotContext();
                    }
                    else if (RunningBots[pid].CurrentScript != null)
                    {
                        // 💥 修复 4：批量重启时的共享内存防撞车锁！
                        RunningBots[pid].CurrentScript.Dispose();
                        RunningBots[pid].CurrentScript = null;
                    }
                    var newScript = (DLLManager)Activator.CreateInstance(scriptTemplate.GetType());
                    newScript.PID = pid;
                    newScript.MainForm = this;
                    newScript.ModuleBaseAddress = p.MainModule.BaseAddress.ToInt32();
                    newScript.AccountName = accountName;
                    newScript.PlayerName = PlayerName;

                    RunningBots[pid].CurrentScript = newScript;
                    RunningBots[pid].IsRunning = true;
                    RunningBots[pid].ModuleBaseAddress = p.MainModule.BaseAddress.ToInt32();

                    row.Cells[10].Value = "排队登录中";

                    if (newlyOpenedPids.Contains(pid))
                    {
                        await Task.Delay(2500);
                        if (_cancelStartAll) return;
                    }
                }
                catch { }
            }
        }

        private void Stop_all_processes_Click(object sender, EventArgs e)
        {
            _cancelStartAll = true; // 拉下总电闸（上一轮加的，保持不变）

            foreach (DataGridViewRow row in dataGridView1.Rows)
            {
                row.Cells[10].Value = "强制停止";
                if (row.IsNewRow || row.Cells[0].Value == null) continue;
                int pid = Convert.ToInt32(row.Cells[0].Value);
                if (RunningBots.ContainsKey(pid))
                {
                    RunningBots[pid].IsRunning = false;
                    row.Cells[10].Value = "强制停止";

                    if (RunningBots[pid].CurrentScript != null)
                    {
                        
                        // 修复 2：同步彻底释放底层 IPC 资源！
                        RunningBots[pid].CurrentScript.Dispose();
                        RunningBots[pid].CurrentScript = null;
                    }
                }
            }
        }

        //用于随时调用停止所有机器人
        private void _Stop_all_processes()
        {
            _cancelStartAll = true; // 拉下总电闸（上一轮加的，保持不变）

            foreach (DataGridViewRow row in dataGridView1.Rows)
            {
                row.Cells[10].Value = "强制停止";
                if (row.IsNewRow || row.Cells[0].Value == null) continue;
                int pid = Convert.ToInt32(row.Cells[0].Value);
                if (RunningBots.ContainsKey(pid))
                {
                    RunningBots[pid].IsRunning = false;
                    row.Cells[10].Value = "强制停止";

                    if (RunningBots[pid].CurrentScript != null)
                    {
                        // 修复 2：同步彻底释放底层 IPC 资源！
                        RunningBots[pid].CurrentScript.Dispose();
                        RunningBots[pid].CurrentScript = null;
                    }
                }
            }
        }

        private void Server_Announcement_Click(object sender, EventArgs e)
        {
        }

        private void Clear_list_Click(object sender, EventArgs e)
        {
            // 1. 遍历并释放所有正在运行的脚本底层资源（防止内存泄漏）
            foreach (var kvp in RunningBots)
            {
                if (kvp.Value.CurrentScript != null)
                {
                    kvp.Value.CurrentScript.Dispose();
                }
            }

            // 2. 清空后台字典记录
            RunningBots.Clear();

            // 3. 清空前端 UI 列表
            dataGridView1.Rows.Clear();

            // 🌟 修复点：同时彻底清空本地 JSON 配置文件
            try
            {
                string jsonPath = Path.Combine(Application.StartupPath, "CharacterConfigs.json");
                // 直接写入一个空的 JSON 字典结构，代表没有任何账号了
                File.WriteAllText(jsonPath, "{ }", Encoding.UTF8);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"清空本地配置文件失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void debug_log_box_CheckedChanged(object sender, EventArgs e)
        {
            if (_isLoading) return; // 启动时跳过，避免覆盖

            SaveSettings(); // 写 Settings.txt（Client_path + DebugLog 一起写）
            ConfigManager.ApplyDebugLogState(debug_log_box.Checked); // 内存立刻生效
        }

    }
}