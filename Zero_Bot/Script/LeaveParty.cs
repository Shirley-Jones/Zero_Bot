using Newtonsoft.Json;
using System;
using System.Diagnostics;
using Zero.Core;

namespace Zero.Script
{
    public class LeaveParty : DLLManager // 继承自 DLLManager，确保能直接调用 ExecuteDynamicLua
    {
        public override string ScriptName => "自动离队-队长";

        private IntPtr _hProcess;
        private int _playerBase;
        private string _PlayerName;
        private bool _isTransitioning = false;
        private bool _isFirstStartup = true;
        private bool _wasInWorld = true; // 记录上一帧是否在正常游戏世界中（用于捕获蓝条结束瞬间）
        // 防暂离专属变量
        private Random _antiAfkRandom = new Random();
        // 初始化时，先随机定一个 90 到 150 秒之后的起跳时间
        private DateTime _nextJumpTime = DateTime.Now.AddSeconds(new Random().Next(80, 200));
        private DateTime _lastInviteTime = DateTime.MinValue;
        private DateTime _transitionFailsafe = DateTime.MinValue;
        private DateTime _resetTimer = DateTime.MinValue;

        public LeaveParty()
        {
            BuildTree();
        }

        private Node _rootTree; // 最高指挥官

        private void BuildTree()
        {
            // 🌟 最外层必须是 Selector (选择器)！
            // 它负责在“正常重置”、“死亡重置”和“待机摸鱼”这三个状态中选择一个来执行。
            _rootTree = new Selector(
                new Sequence(
                    // A1: 正常重置信号雷达 (只监听发给自己的信号)
                    new ActionNode(() => {
                        if (IPCManager.IsResetRequestPending(_PlayerName))
                        {
                            Logger.Write($"[{CurrentPlayerName}] 收到专属小退信号！启动 25 秒倒计时...");
                            return NodeState.Success;
                        }
                        return NodeState.Failure;
                    }),

                    new WaitNode(25000),

                    new ActionNode(() => {
                        Logger.Write($"[{CurrentPlayerName}] 25 秒缓冲结束，执行离开队伍操作...");
                        ExecuteDynamicLua("LeaveParty()");
                        return NodeState.Success;
                    }),

                    new WaitNode(2000),

                    new ActionNode(() => {
                        // 回复信号，明确是自己完成了
                        IPCManager.MarkResetComplete(_PlayerName);
                        return NodeState.Success;
                    })
                ),

                new Sequence(
                    // B1: 死亡重置信号雷达 (只监听发给自己的信号)
                    new ActionNode(() => {
                        if (IPCManager.IsDeathResetRequestPending(_PlayerName))
                        {
                            Logger.Write($"[{CurrentPlayerName}] 收到专属死亡重置信号！执行重置...");
                            return NodeState.Success;
                        }
                        return NodeState.Failure;
                    }),

                    new ActionNode(() => {
                        ExecuteDynamicLua("ResetInstances()");
                        return NodeState.Success;
                    }),

                    new WaitNode(2000),

                    new ActionNode(() => {
                        IPCManager.MarkDeathResetComplete(_PlayerName);
                        Logger.Write($"[{CurrentPlayerName}] 死亡重置指令已发送完毕！");
                        return NodeState.Success;
                    })
                ),

                new ActionNode(() => {
                    return NodeState.Running;
                })
            );
        }

        public override string OnTick(IntPtr hProcess, int playerBase)
        {
            // 1. 更新句柄，提前赋给全局变量，供底下的雷达使用
            _hProcess = hProcess;
            _playerBase = playerBase;

            // IsInWorld 绝对物理锁（防刷屏动态拦截版）
            if (!IsInWorld(_hProcess) || _playerBase == 0)
            {
                if (_isTransitioning) _transitionFailsafe = DateTime.Now.AddSeconds(15);
                if (_wasInWorld)
                {
                    Logger.Write($"[{CurrentPlayerName}] [智能检测] 蓝条加载中...");
                    _wasInWorld = false; // 瞬间闭锁，后续几百帧蓝条期间绝不再打印这行日志
                }

                return "蓝条加载中";
            }

            // 捕捉从蓝条出来，落地站稳的第一个瞬间（上升沿触发）
            if (!_wasInWorld)
            {
                Logger.Write($"[{CurrentPlayerName}] [智能检测] 蓝条加载完毕，已自动清空行为树历史记录防错乱！");
                // 在副本.cs 接管后，执行一次这行代码释放锁：
                //ExecuteDynamicLua("ZeroBot_IsEnteringWorld = nil; ZeroBot_HasRequestedLogin = nil; ");
                ExecuteDynamicLua("ZeroBot_IsEnteringWorld = nil; ZeroBot_HasRequestedLogin = nil; StaticPopup_Hide('CAMP');");
                // 屏蔽LUA报错！
                // 💥 强制清除上一局的所有记忆，防止行为树误判再次小退！
                //SuppressLUAerrors();
                _rootTree?.Reset(); // 强行把所有 SmartPathNode 的进度全部归零！
                _wasInWorld = true; // 重新开锁，为下一次进蓝条做准备
            }

            // 缓存名字
            if (CurrentPlayerName == "未知角色" && _hProcess != IntPtr.Zero)
            {
                CurrentPlayerName = GetPlayerName(_hProcess);
            }

            // 【队长号代码】
            AccountConfig config = ConfigManager.GetConfig(this.AccountName) ?? new AccountConfig();
            if (config != null && !string.IsNullOrEmpty(config.TeamName))
            {
                if ((DateTime.Now - _lastInviteTime).TotalSeconds > 10)
                {
                    string teamName = config.TeamName; // 注意：这里的 TeamName 配置的应该是【刷本号】的名字
                    //队长号自己的名字,直接读取配置文件不会出错，直接读内存会导致加载的时候名字异常
                    _PlayerName = config.PlayerName;
                    string finalLua = "";
                    
                    if (IPCManager.IsResetRequestPending(_PlayerName) || IPCManager.IsResetCompleted(_PlayerName))
                    {
                        // 重置期间只拒绝一切邀请（退组逻辑在你IPC那部分已经有了）
                        finalLua = "local s='PARTY_INVITE'; if StaticPopup_Visible(s) then DeclineGroup(); StaticPopup_Hide(s); end";
                    }
                    else
                    {
                        // 队长号逻辑：只接刷本号的邀请，拒绝其他骚扰 -> 如果队伍里没有刷本号就退组
                        // 拾取和移交队长已经由里面的刷本号代劳了，这里什么都不用干！
                        finalLua = $@"
                local t='{teamName}';
                local s='PARTY_INVITE';
                local p=StaticPopup_Visible(s);
                if p then 
                    local txt=getglobal(p..'Text');
                    local str=txt and txt:GetText() or '';
                    if string.find(str, t) then AcceptGroup(); else DeclineGroup(); end;
                    StaticPopup_Hide(s);
                end;
                local m=GetNumPartyMembers();
                if m>0 then 
                    local f=0;
                    for i=1,m do if UnitName('party'..i)==t then f=1; end; end;
                    if f==0 then LeaveParty(); end;
                end;
            ";

                        // 移除多余换行防止某些旧框架解析断层
                        finalLua = finalLua.Replace("\r", "").Replace("\n", " ");
                    }

                    // ⚠️ 全局只调用一次，绝不产生内存覆盖！
                    ExecuteDynamicLua(finalLua);
                    _lastInviteTime = DateTime.Now;
                }
            }

            // 只在脚本刚启动的瞬间执行一次
            if (_isFirstStartup)
            {
                // 🌟 核心：角色成功落地！立即砸碎 Lua 里的进游戏锁，为下一次意外掉线重连做准备！
                ExecuteDynamicLua("ZeroBot_HasRequestedLogin = nil;");
                // 绝对不能马上跳！把第一次跳跃的时间强行推迟到进游戏后的 8 秒钟
                _nextJumpTime = DateTime.Now.AddSeconds(8);
                _isFirstStartup = false;

            }

            // ==========================================
            // 🦘 【防暂离系统】随机跳跃防线 (完全独立)
            // ==========================================
            if (DateTime.Now > _nextJumpTime)
            {
                // 1. 执行你的物理跳跃动作 (模拟按下空格)
                _ = Jump(50);

                // 2. 生成下一次跳跃的随机时间！(这里设定为 80秒 到 200秒 之间随机)
                // 这样平均下来刚好是 2 分钟左右，而且绝对没有规律可循！
                int nextSeconds = _antiAfkRandom.Next(80, 200);
                _nextJumpTime = DateTime.Now.AddSeconds(nextSeconds);

                Logger.Write($"[{CurrentPlayerName}] [防暂离] 角色执行了随机跳跃，下一次将在 {nextSeconds} 秒后！");
            }

            // ==========================================
            // 🌳 将执行权交给行为树最高指挥官
            // ==========================================
            if (_rootTree != null)
            {
                NodeState state = _rootTree.Evaluate();
                if (state == NodeState.Running) return "运行中";
            }

            return "空闲";
        }
    }
}