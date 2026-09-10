using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Zero.Core;
using static Zero.Core.PreDungeonShieldNode;
using static Zero.Core.PathType;

namespace Zero.Script
{
    public class DireMaulNorth : DLLManager
    {
        public override string ScriptName => "厄运北猎人完美贡品(这个是没有完善的版本不能用！)";

        private IntPtr _hProcess;
        private int _playerBase;
        private int sellIndex = 0;
        private int _mapId;
        private int _lastMapId = 0;
        private int _activeGyRoute = 0;
        private bool _isDead;
        private bool _needGoHome;
        private bool _wasDead = false;
        private bool _isFirstStartup = true;
        private bool _needResetInstance = false;
        private bool _isTransitioning = false;
        private bool _hasReleasedSpirit = false;
        private bool isSellingInitialized = false;
        private bool _hasAttemptedSell = false;
        private bool _wasInWorld = true;
        public static string LastAction = "";
        public static float LastX = 0;
        public static float LastY = 0;
        private DateTime _lastCombatItemUseTime = DateTime.MinValue;
        private DateTime _lastCastTime = DateTime.MinValue;
        private DateTime _lastItemUseTime = DateTime.MinValue;
        private DateTime _lastAcceptTime = DateTime.MinValue;
        private DateTime _hearthstoneTimer = DateTime.MinValue;
        private DateTime _resetTimer = DateTime.MinValue;
        private DateTime WaitTimer = DateTime.MinValue;
        private DateTime _lastDrinkTime = DateTime.MinValue;
        private DateTime _lastFoodTime = DateTime.MinValue;
        private DateTime _lastZijiuPrintTime = DateTime.MinValue;
        private DateTime _transitionFailsafe = DateTime.MinValue;
        private DateTime nextSellTime = DateTime.MinValue;
        private DateTime _lastClassWarnTime = DateTime.MinValue;
        private DateTime _lastLevelWarnTime = DateTime.MinValue;
        private DateTime _releaseSpiritTime = DateTime.MinValue;
        private bool _needStartupPrep = false;
        private DateTime _startupPrepTimer = DateTime.MinValue;
        private DateTime _GodekGateTime = DateTime.MinValue;
        int chestStep = 0;
        DateTime chestTimer = DateTime.MinValue;
        DateTime logTimer = DateTime.MinValue;
        int wpIndex = 0;
        // 在类级别声明你的时间戳变量
        private DateTime _lastSummonTime = DateTime.MinValue;
        // 记录芬古斯上一次距离，用来判断他是在“靠近”还是“远离”
        float lastFengusDist = 9999f;
        // 声明拉怪专用的局部闭包变量
        int pullStep_1 = 0;
        // 在类级别（或节点外部）声明一个状态控制变量，用于记录开火和宝宝状态阶段
        private int _gordokAttackStep = 0;
        DateTime pullTimer_1 = DateTime.MinValue;
        // 声明“猎杀基尔罗格之眼”专用的闭包变量
        int eyeStep_1 = 0;
        DateTime eyeTimer_1 = DateTime.MinValue;
        bool isMelee_1 = false; // 用于标记刚才是不是打了近战
        // 声明三楼游荡的基尔罗格之眼专属状态变量
        int eyeStep_2 = 0;
        DateTime eyeTimer_2 = DateTime.MinValue;
        bool isMelee_2 = false;
        DateTime searchLogTimer_2 = DateTime.MinValue;
        bool isFromDownstairs_2 = false; // 永久标记这只眼睛的“户口”来源
        // 声明假死监控专用的日志节流器
        DateTime slipkikLogTimer = DateTime.MinValue;
        // 在 BuildTree 方法内部声明这两个局部变量，利用闭包特性让 ActionNode 记住跳跃状态
        bool hasAttemptedJump = false;
        DateTime jumpTime = DateTime.MinValue;
        List<InventoryItem> itemsToSell = null;
        private int _pullStrategy = 0;

        // 请在外部声明这两个变量，用于控制走砍节奏
        //private int _dpsStep = 0; 
        private int _dps2Step = 0; 

        private DateTime _actionTimer = DateTime.MinValue;
        // 在类外部或顶部定义这个变量
        private DateTime _lastFacingTime = DateTime.MinValue;
        // ==========================================
        // 大王战：全局状态机枚举与变量
        // ==========================================
        public enum GordokFightState
        {
            Kill_King_Gordok_0,
            Kill_King_Gordok_1,
            Kill_King_Gordok_2,
            Kill_King_Gordok_3,
            Kill_King_Gordok_4,
            Kill_King_Gordok_5,
        }

        private GordokFightState _gordokState = GordokFightState.Kill_King_Gordok_1;
        private DateTime _drinkTimer = DateTime.MinValue;
        private DateTime _moveLogTimer = DateTime.MinValue;




        public DireMaulNorth()
        {
            BuildTree();
        }

        private Node _rootTree; // 最高指挥官
        private Node _farmTree; // 副本内战斗逻辑
        private Node _runToInnTree_Horde;  //跑回旅店 部落路径
        private Node _runToInnTree_Alliance;  //跑回旅店 联盟路径
        private Node _runToInstanceTree_Horde;  //跑回副本 部落路径
        private Node _runToInstanceTree_Alliance;  //跑回副本 联盟路径

        // 进场路径 (跑向宝箱)
        float[][] approachPath = new float[][] {
            new float[] { 383.98f, 202.75f, 11.22f },
            new float[] { 380.44f, 227.24f, 11.20f },
            new float[] { 379.33f, 241.27f, 11.44f },
            new float[] { 380.20f, 258.03f, 11.44f }
        };

        /// <summary>
        /// 创建起飞前置物资自检节点
        /// </summary>
        public Node CreatePreFlightCheckNode()
        {
            return new ActionNode(() =>
            {
                Logger.Write($"[{CurrentPlayerName}] [起飞自检] 正在扫描背包内存，清点核心战略物资...");

                // ==========================================
                // 1. 统计食物和水 (同类相加，总和大于等于 20 即可)
                // ==========================================
                int foodCount = GetItemCount(_hProcess, 8076) + // 魔法甜面包
                                GetItemCount(_hProcess, 8952) + // 烤鹌鹑
                                GetItemCount(_hProcess, 22895); // 魔法肉桂面包

                int waterCount = GetItemCount(_hProcess, 8078) + // 魔法苏打水
                                 GetItemCount(_hProcess, 8766) + // 晨露酒
                                 GetItemCount(_hProcess, 8079);  // 魔法晶水

                // ==========================================
                // 2. 检查完美贡品硬通货 (必须每种保底 >= 1)
                // ==========================================
                int bombCount = GetItemCount(_hProcess, 4398);        // 大型爆盐炸弹
                int lesserInvisCount = GetItemCount(_hProcess, 3823); // 次级隐形药水
                int invisCount = GetItemCount(_hProcess, 9172);       // 隐形药水

                // ==========================================
                // 3. 构建缺漏名单
                // ==========================================
                List<string> missingItems = new List<string>();

                if (foodCount < 20) missingItems.Add($"食物不足20个 (当前:{foodCount})");
                if (waterCount < 20) missingItems.Add($"饮用水不足20个 (当前:{waterCount})");
                if (bombCount < 1) missingItems.Add("缺少 [大型爆盐炸弹]");
                if (lesserInvisCount < 1) missingItems.Add("缺少 [次级隐形药水]");
                if (invisCount < 1) missingItems.Add("缺少 [隐形药水]");

                // ==========================================
                // 4. 终极判决：放行 OR 熔断
                // ==========================================
                if (missingItems.Count > 0)
                {
                    // 只要有一个不达标，打印具体缺什么，并返回 Failure 摧毁行为树的继续执行
                    Logger.Write($"[{CurrentPlayerName}] [战前准备] 物资检查未通过！缺少物资: {string.Join(", ", missingItems)}");
                    Logger.Write($"[{CurrentPlayerName}] [战前准备] 脚本已强制熔断，停止一切后续操作！");

                    return NodeState.Failure;
                }

                // 全部达标，完美放行！
                Logger.Write($"[{CurrentPlayerName}] [战前准备] 物资充沛！(食物:{foodCount} 水:{waterCount} 炸弹:{bombCount} 次级隐形:{lesserInvisCount} 隐形:{invisCount})");
                Logger.Write($"[{CurrentPlayerName}] [战前准备] 引擎点火，大王我来了！");

                return NodeState.Success;
            });
        }

        /// <summary>
        /// 创建【智能动态吃喝】节点 (自动识别背包、自动优先级降级、人宠双层保护)
        /// </summary>
        public Node autoDrinkEatNode()
        {
            // 定义物资优先级矩阵 (ItemID 数组，按从高到低排序)
            // 数组前面的代表最高优先级，后面代表低级备用物资
            int[] foodPriorityList = new int[] { 22895, 8076, 8952 }; // 魔法肉桂面包 > 魔法甜面包 > 烤鹌鹑
            int[] waterPriorityList = new int[] { 8079, 8766, 8078 }; // 魔法晶水 > 晨露酒 > 魔法苏打水

            return new ActionNode(() =>
            {
                float hp = GetHealthPercent(_hProcess, _playerBase);
                float mp = GetManaPercent(_hProcess, _playerBase);

                // 1. 状态全满，亮绿灯安全放行
                if (hp > 99f && mp > 99f) return NodeState.Success;

                // 2. 状态未满，读取当前的吃喝状态和物品 GCD 状态
                GetEatDrinkStatus(_hProcess, _playerBase, out bool isEating, out bool isDrinking);
                bool isItemCooldown = (DateTime.Now - _lastItemUseTime).TotalSeconds < 1.6;

                if (!isItemCooldown)
                {
                    bool usedItem = false;

                    // ==========================================
                    // 🛑 【智能食物分配：动态降级扫描】
                    // ==========================================
                    if (hp <= 99f && !isEating)
                    {
                        int bestFoodIdToUse = 0;

                        // 顺着优先级链条从头往下扫描背包，揪出当前拥有的最高级食物
                        foreach (int itemId in foodPriorityList)
                        {
                            if (GetItemCount(_hProcess, itemId) > 0)
                            {
                                bestFoodIdToUse = itemId;
                                break; // 抓到了最高级的，立刻跳出循环！
                            }
                        }

                        if (bestFoodIdToUse != 0)
                        {
                            string foodName = bestFoodIdToUse == 22895 ? "魔法肉桂面包" :
                                              bestFoodIdToUse == 8076 ? "魔法甜面包" : "烤鹌鹑";

                            Logger.Write($"[{CurrentPlayerName}] [智能吃喝] 血量偏低 ({hp:F1}%)，智能检索背包，正在食用：【{foodName}】(ID: {bestFoodIdToUse})...");

                            UseItemByItemId(_hProcess, bestFoodIdToUse);
                            _lastItemUseTime = DateTime.Now;
                            usedItem = true;
                        }
                        else
                        {
                            // 背包里连一个食物都找不到了！
                            Logger.Write($"[{CurrentPlayerName}] [智能吃喝] 警告：血量不足，但在背包中没有找到任何有效的食物！");
                        }
                    }

                    // ==========================================
                    // 🛑 【智能法力水分配：动态降级扫描】
                    // ==========================================
                    // 注意：!usedItem 判定非常关键，1.12.1 中吃和喝不能在同一毫秒发包，否则必定被服务器吞掉一个
                    if (mp <= 99f && !isDrinking && !usedItem)
                    {
                        int bestWaterIdToUse = 0;

                        // 顺着优先级链条从头往下扫描背包，揪出当前拥有的最高级水
                        foreach (int itemId in waterPriorityList)
                        {
                            if (GetItemCount(_hProcess, itemId) > 0)
                            {
                                bestWaterIdToUse = itemId;
                                break; // 抓到了最高级的，立刻跳出循环！
                            }
                        }

                        if (bestWaterIdToUse != 0)
                        {
                            string waterName = bestWaterIdToUse == 8079 ? "魔法晶水" :
                                               bestWaterIdToUse == 8766 ? "晨露酒" : "魔法苏打水";

                            Logger.Write($"[{CurrentPlayerName}] [智能吃喝] 蓝量偏低 ({mp:F1}%)，智能检索背包，正在饮用：【{waterName}】(ID: {bestWaterIdToUse})...");

                            UseItemByItemId(_hProcess, bestWaterIdToUse);
                            _lastItemUseTime = DateTime.Now;
                        }
                        else
                        {
                            // 背包里一滴水都没了！
                            Logger.Write($"[{CurrentPlayerName}] [智能吃喝] 警告：蓝量不足，但在背包中没有找到任何有效的饮用水！");
                        }
                    }
                }

                // 只要有一项没补满，就让行为树保持 Running 阻塞，角色原地坐地板直至回满
                return NodeState.Running;
            });
        }

        /// <summary>
        /// “秒上 Buff 即放行”的吃喝节点
        /// </summary>
        public Node ApplyEatDrinkBuffsNode()
        {
            int[] foodPriorityList = new int[] { 22895, 8076, 8952 };
            int[] waterPriorityList = new int[] { 8079, 8766, 8078 };

            return new ActionNode(() =>
            {
                float hp = GetHealthPercent(_hProcess, _playerBase);
                float mp = GetManaPercent(_hProcess, _playerBase);

                // 获取当前是否已经拥有吃喝 Buff
                GetEatDrinkStatus(_hProcess, _playerBase, out bool isEating, out bool isDrinking);

                // 核心放行逻辑：
                // 1. 如果血满，或者虽然没满但已经在吃东西了 -> 食物状态 OK
                bool foodOk = (hp > 95f) || isEating;
                // 2. 如果蓝满，或者虽然没满但已经在喝水了 -> 饮水状态 OK
                bool waterOk = (mp > 95f) || isDrinking;

                // 只要食物和饮水状态都达标，立刻绿灯放行！
                if (foodOk && waterOk)
                {
                    return NodeState.Success;
                }

                // ==========================================
                // 以下是使用物品逻辑 (未吃喝且状态不满时才会执行)
                // ==========================================
                bool isItemCooldown = (DateTime.Now - _lastItemUseTime).TotalSeconds < 1.6;
                if (!isItemCooldown)
                {
                    bool usedItem = false;

                    // 补充食物
                    if (!foodOk)
                    {
                        int bestFoodIdToUse = 0;
                        foreach (int itemId in foodPriorityList)
                        {
                            if (GetItemCount(_hProcess, itemId) > 0)
                            {
                                bestFoodIdToUse = itemId;
                                break;
                            }
                        }

                        if (bestFoodIdToUse != 0)
                        {
                            UseItemByItemId(_hProcess, bestFoodIdToUse);
                            _lastItemUseTime = DateTime.Now;
                            usedItem = true;
                        }
                    }

                    // 补充饮水 (利用 usedItem 互斥 GCD)
                    if (!waterOk && !usedItem)
                    {
                        int bestWaterIdToUse = 0;
                        foreach (int itemId in waterPriorityList)
                        {
                            if (GetItemCount(_hProcess, itemId) > 0)
                            {
                                bestWaterIdToUse = itemId;
                                break;
                            }
                        }

                        if (bestWaterIdToUse != 0)
                        {
                            UseItemByItemId(_hProcess, bestWaterIdToUse);
                            _lastItemUseTime = DateTime.Now;
                        }
                    }
                }

                // 物品正在冷却，或者刚发包等待服务器响应 Buff，继续阻塞一帧
                return NodeState.Running;
            });
        }


        /// <summary>
        /// 创建戈多克狼群避让雷达 (监控 P4 -> P5 路段)
        /// </summary>
        public Node CreateWolfRadarNode_P4()
        {
            // ==========================================
            // 戈多克驯狼 (巡逻雷达) 专属闭包变量
            // ==========================================
            float lastDistToP4_Wolf = 9999f;
            bool isApproachingP4_Wolf = true;
            DateTime wolfLogTimer = DateTime.MinValue;
            return new ActionNode(() =>
            {
                // 带头大哥的完全体 GUID
                ulong leaderGuid = 0xF1300032EC049D2A;
                int wolfBase = GetTargetBaseByGuid(_hProcess, leaderGuid);

                // ====================================================
                // 1. 视距兜底：如果基址为 0 (离得极远根本没渲染) 或已死亡，绝对安全！
                // ====================================================
                if (wolfBase == 0 || MemoryAPI.ReadInteger(_hProcess, MemoryAPI.ReadInteger(_hProcess, wolfBase + 0x08) + 0x58) <= 0)
                {
                    if ((DateTime.Now - wolfLogTimer).TotalSeconds > 3.0)
                    {
                        Logger.Write($"[{CurrentPlayerName}] [避让雷达] 巡逻狼带头大哥 (0x{leaderGuid:X}) 不在视野内或已死亡，安全放行！");
                        wolfLogTimer = DateTime.Now;
                    }

                    // 放行前重置状态，为下一次跑本复用做准备
                    lastDistToP4_Wolf = 9999f;
                    isApproachingP4_Wolf = true;
                    return NodeState.Success;
                }

                // 获取老大当前坐标
                float wX = MemoryAPI.ReadFloat(_hProcess, wolfBase + 0x9B8);
                float wY = MemoryAPI.ReadFloat(_hProcess, wolfBase + 0x9BC);
                float wZ = MemoryAPI.ReadFloat(_hProcess, wolfBase + 0x9C0);

                // 危险卡点 P4 坐标
                float p4_X = 652.064f;
                float p4_Y = 497.282f;

                // 计算老大当前距离 P4 的平面距离
                float distToP4 = (float)Math.Sqrt(Math.Pow(wX - p4_X, 2) + Math.Pow(wY - p4_Y, 2));

                // ====================================================
                // 2. 核心算法：过滤坐标抖动，捕捉其真实移动趋向 (来还是去？)
                // ====================================================
                // 只有当实质位移 > 0.5 码时才更新趋势，防止停步发呆时的浮点数抖动误判
                if (Math.Abs(distToP4 - lastDistToP4_Wolf) > 0.5f)
                {
                    isApproachingP4_Wolf = distToP4 < lastDistToP4_Wolf;
                    lastDistToP4_Wolf = distToP4;
                }

                // ====================================================
                // 3. 避让逻辑决策树
                // ====================================================

                // 决策 A：如果它距离 P4 超过 50 码（比如它在后场 P16 散步），不管它怎么走都对我们毫无威胁，直接过！
                if (distToP4 > 50.0f)
                {
                    Logger.Write($"[{CurrentPlayerName}] [战斗系统] 巡逻狼群在后场 (距卡点 {distToP4:F1} 码)，无威胁，放行！");
                    lastDistToP4_Wolf = 9999f;
                    isApproachingP4_Wolf = true;
                    return NodeState.Success;
                }

                // 决策 B：在 50 码内，且正在朝 P4 走来 (或停在原地) -> 极度危险，死等！
                if (isApproachingP4_Wolf)
                {
                    if ((DateTime.Now - wolfLogTimer).TotalSeconds > 2.0)
                    {
                        Logger.Write($"[{CurrentPlayerName}] [战斗系统] 狼群正在靠近危险点 (距P4: {distToP4:F1} 码)！原地隐蔽死等...");
                        wolfLogTimer = DateTime.Now;
                    }
                    StopMovement(_hProcess, _playerBase); // 物理强制手刹，绝对不许往前滑行
                    return NodeState.Running;
                }

                // 决策 C：在 50 码内，但是【正在远离】P4 (说明它已经拐弯往 P5 走了)
                if (!isApproachingP4_Wolf)
                {
                    // 黄金防拖尾保护：虽然它往 P5 走了，但距离 P4 还有点近，背后的 2 只小弟可能会刮到我们
                    if (distToP4 < 15.0f)
                    {
                        if ((DateTime.Now - wolfLogTimer).TotalSeconds > 2.0)
                        {
                            Logger.Write($"[{CurrentPlayerName}] [战斗系统] 狼群已拐弯前往 P5 (距P4: {distToP4:F1} 码)，等其再走远一点...");
                            wolfLogTimer = DateTime.Now;
                        }
                        StopMovement(_hProcess, _playerBase);
                        return NodeState.Running;
                    }
                    else
                    {
                        // 老大已经离开 P4 超过 15 码了，后面的小弟也绝对被带走了！亮绿灯！
                        Logger.Write($"[{CurrentPlayerName}] [战斗系统] 狼群已离开危险点 {distToP4:F1} 码！警报解除，全速发车！");
                        lastDistToP4_Wolf = 9999f;
                        isApproachingP4_Wolf = true;
                        return NodeState.Success;
                    }
                }

                return NodeState.Running;
            });
        }

        /// <summary>
        /// 创建戈多克狼群避让雷达 (监控 P11 -> P12 路段)
        /// </summary>
        public Node CreateWolfRadarNode_P11()
        {
            // 闭包状态变量：绝对隔离，不会和 P4 的雷达冲突
            float lastDistToP11 = 9999f;
            bool isApproachingP11 = true;
            DateTime wolfLogTimer = DateTime.MinValue;

            return new ActionNode(() =>
            {
                // 带头大哥的完全体 GUID
                ulong leaderGuid = 0xF1300032EC049D2A;
                int wolfBase = GetTargetBaseByGuid(_hProcess, leaderGuid);

                // ====================================================
                // 1. 视距兜底：不在视野内或已死亡，绝对安全
                // ====================================================
                if (wolfBase == 0 || MemoryAPI.ReadInteger(_hProcess, MemoryAPI.ReadInteger(_hProcess, wolfBase + 0x08) + 0x58) <= 0)
                {
                    if ((DateTime.Now - wolfLogTimer).TotalSeconds > 3.0)
                    {
                        Logger.Write($"[{CurrentPlayerName}] [战斗系统] 巡逻狼带头大哥不在视野内或已死亡，安全放行！");
                        wolfLogTimer = DateTime.Now;
                    }

                    // 重置状态
                    lastDistToP11 = 9999f;
                    isApproachingP11 = true;
                    return NodeState.Success;
                }

                // 获取老大当前坐标
                float wX = MemoryAPI.ReadFloat(_hProcess, wolfBase + 0x9B8);
                float wY = MemoryAPI.ReadFloat(_hProcess, wolfBase + 0x9BC);
                float wZ = MemoryAPI.ReadFloat(_hProcess, wolfBase + 0x9C0);

                // 危险卡点 P11 坐标
                float p11_X = 716.497f;
                float p11_Y = 506.804f;

                // 计算距离 P11 的平面距离
                float distToP11 = (float)Math.Sqrt(Math.Pow(wX - p11_X, 2) + Math.Pow(wY - p11_Y, 2));

                // ====================================================
                // 2. 核心算法：趋势捕捉 (过滤 0.5 码以下的抖动)
                // ====================================================
                if (Math.Abs(distToP11 - lastDistToP11) > 0.5f)
                {
                    isApproachingP11 = distToP11 < lastDistToP11;
                    lastDistToP11 = distToP11;
                }

                // ====================================================
                // 3. 避让逻辑决策树
                // ====================================================

                // 决策 A：如果它距离 P11 超过 50 码，对我们毫无威胁，直接过！
                if (distToP11 > 50.0f)
                {
                    Logger.Write($"[{CurrentPlayerName}] [战斗系统] 狼群在极远处 (距P11 {distToP11:F1} 码)，无威胁，放行！");
                    lastDistToP11 = 9999f;
                    isApproachingP11 = true;
                    return NodeState.Success;
                }

                // 决策 B：正在朝 P11 走来 -> 死等！
                if (isApproachingP11)
                {
                    if ((DateTime.Now - wolfLogTimer).TotalSeconds > 2.0)
                    {
                        Logger.Write($"[{CurrentPlayerName}] [战斗系统] 狼群正在靠近危险点 P11 (距P11: {distToP11:F1} 码)！原地隐蔽死等...");
                        wolfLogTimer = DateTime.Now;
                    }
                    StopMovement(_hProcess, _playerBase); // 强制手刹
                    return NodeState.Running;
                }

                // 决策 C：【正在远离】P11 (说明它已经过了 P11，正在往 P12 走)
                if (!isApproachingP11)
                {
                    // P11 到 P12 的总距离是 16 码。
                    // 我们要求它离开 P11 至少 14 码以上（此时它基本就踩在 P12 上了），才给绿灯！
                    if (distToP11 < 14.0f)
                    {
                        if ((DateTime.Now - wolfLogTimer).TotalSeconds > 2.0)
                        {
                            Logger.Write($"[{CurrentPlayerName}] [战斗系统] 狼群已转身前往 P12 (距P11: {distToP11:F1} 码)，等其彻底走远...");
                            wolfLogTimer = DateTime.Now;
                        }
                        StopMovement(_hProcess, _playerBase);
                        return NodeState.Running;
                    }
                    else
                    {
                        // 老大已经离开 P11 超过 14 码，马上就到 P12 了，安全！
                        Logger.Write($"[{CurrentPlayerName}] [战斗系统] 狼群已逼近 P12 (离开 P11 {distToP11:F1} 码)！警报解除，全速发车！");
                        lastDistToP11 = 9999f;
                        isApproachingP11 = true;
                        return NodeState.Success;
                    }
                }

                return NodeState.Running;
            });
        }

        /// <summary>
        /// 创建假死节点：猎人假死脱战 (加入宠物存活与战斗状态双向监控)
        /// </summary>
        public Node CreateFeignDeathNode()
        {
            int fdStep = 0;
            DateTime fdTimer = DateTime.MinValue;

            return new ActionNode(() =>
            {
                // ==========================================
                // 阶段 0：立刻刹车，取消攻击并施放假死
                // ==========================================
                if (fdStep == 0)
                {
                    Logger.Write($"[{CurrentPlayerName}] [战斗系统] 已到达安全区！停止所有攻击，准备假死...");

                    // 1. 物理手刹，绝对禁止滑行中放假死
                    StopMovement(_hProcess, _playerBase);

                    // 2. 完美取消宏：断读条/自动射击 -> 丢目标断平A -> 施放假死
                    ExecuteDynamicLua("SpellStopCasting(); ClearTarget(); CastSpellByName('假死');");

                    // 记录动作时间
                    fdTimer = DateTime.Now;
                    fdStep = 1;
                    return NodeState.Running;
                }

                // ==========================================
                // 阶段 1：确认假死 Buff 是否成功挂上
                // ==========================================
                if (fdStep == 1)
                {
                    // 给客户端和服务器 300 毫秒的反应同步时间
                    if ((DateTime.Now - fdTimer).TotalMilliseconds > 300)
                    {
                        // 检查底层是否真的挂上了 5384 光环
                        bool hasFeignDeath = HasPlayerBuff(_hProcess, _playerBase, 5384);

                        if (hasFeignDeath)
                        {
                            Logger.Write($"[{CurrentPlayerName}] [战斗系统] 假死光环 (5384) 确认生效！等待仇恨清空...");
                            fdStep = 2;
                        }
                        else
                        {
                            // 超过 1.5 秒还没挂上 Buff，说明假死没放出来（可能被晕、卡公共CD等）
                            if ((DateTime.Now - fdTimer).TotalSeconds > 1.5)
                            {
                                Logger.Write($"[{CurrentPlayerName}] [战斗系统] 假死光环未生效！重试中...");
                                fdStep = 0; // 退回阶段 0 重新按假死
                            }
                        }
                    }
                    return NodeState.Running;
                }

                // ==========================================
                // 阶段 2：躺地等待脱战 (双线雷达严格 AND 监控)
                // ==========================================
                if (fdStep == 2)
                {
                    // 1. 监控猎人自身
                    bool playerInCombat = Unit_Behavioral_State(_hProcess, _playerBase, 19);

                    // 2. 监控宠物状态
                    int petBase = GetPetBase(_hProcess);
                    bool petDespawned = (petBase == 0);
                    bool petDead = !petDespawned && IsPetDead(_hProcess, petBase);
                    bool petOutOfCombat = !petDespawned && !petDead && !Pet_Behavioral_State(_hProcess, petBase, 19);

                    // 🚨 核心逻辑修复：【必须】猎人脱战 AND (宠物消失 OR 宠物死亡 OR 宠物脱战)
                    if (!playerInCombat && (petDespawned || petDead || petOutOfCombat))
                    {
                        // 延迟 2 秒防服务器数据回摆，确保怪物仇恨列表彻底清空，防止起跳瞬间吃社会仇恨
                        if ((DateTime.Now - fdTimer).TotalSeconds > 2.0)
                        {
                            // 动态拼装放行理由
                            string petReason = petDespawned ? "宠物已消失" :
                                               petDead ? "宠物已阵亡" : "宠物已脱战";

                            Logger.Write($"[{CurrentPlayerName}] [战斗系统] 确认绝对安全 (猎人已脱战 且 {petReason})！假死战术成功，放行！");

                            fdStep = 0;
                            return NodeState.Success;
                        }
                    }
                    else
                    {
                        // 极端情况：躺了 15 秒，依然没有满足双重脱战条件
                        if ((DateTime.Now - fdTimer).TotalSeconds > 15)
                        {
                            Logger.Write($"[{CurrentPlayerName}] [战斗系统] 假死被抵抗或宠物持续引战超时！直接放行交由后续跑尸接管...");
                            fdStep = 0;
                            return NodeState.Success;
                        }
                    }

                    return NodeState.Running;
                }

                return NodeState.Running;
            });
        }

        /// <summary>
        /// 创建大王战专用假死节点：猎人假死清仇恨 (仅监控猎人脱战状态，无视宠物)
        /// </summary>
        public Node CreateGordokFeignDeathNode()
        {
            int fdStep = 0;
            DateTime fdTimer = DateTime.MinValue;
            DateTime combatClearTimer = DateTime.MinValue;

            return new ActionNode(() =>
            {
                // ==========================================
                // 阶段 0：立刻刹车，取消攻击并施放假死
                // ==========================================
                if (fdStep == 0)
                {
                    Logger.Write($"[{CurrentPlayerName}] [战斗系统] 触发战假死战术！停止攻击，准备假死...");

                    // 1. 物理手刹，绝对禁止滑行中放假死
                    StopMovement(_hProcess, _playerBase);

                    // 2. 完美取消宏：断读条/自动射击 -> 丢目标断平A -> 施放假死
                    ExecuteDynamicLua("SpellStopCasting(); ClearTarget(); CastSpellByName('假死');");

                    // 记录动作时间
                    fdTimer = DateTime.Now;
                    fdStep = 1;
                    return NodeState.Running;
                }

                // ==========================================
                // 阶段 1：确认假死 Buff 是否成功挂上
                // ==========================================
                if (fdStep == 1)
                {
                    // 给客户端和服务器 300 毫秒的反应同步时间
                    if ((DateTime.Now - fdTimer).TotalMilliseconds > 300)
                    {
                        // 💡 顺手升级：用统一的 HasUnitAura 检测自身的假死光环 (5384)
                        bool hasFeignDeath = HasUnitAura(_hProcess, _playerBase, 5384);

                        if (hasFeignDeath)
                        {
                            Logger.Write($"[{CurrentPlayerName}] [战斗系统] 假死光环 (5384) 确认生效！等待自身战斗状态解除...");
                            fdStep = 2;
                            combatClearTimer = DateTime.MinValue; // 重置脱战计时器
                        }
                        else
                        {
                            // 超过 1.5 秒还没挂上 Buff，说明假死没放出来（可能卡 GCD，被大王眩晕等）
                            if ((DateTime.Now - fdTimer).TotalSeconds > 1.5)
                            {
                                Logger.Write($"[{CurrentPlayerName}] [战斗系统] 假死光环未生效！尝试重新释放...");
                                fdStep = 0; // 退回阶段 0 重新按假死
                            }
                        }
                    }
                    return NodeState.Running;
                }

                // ==========================================
                // 阶段 2：躺地等待猎人脱战 + 毒蛇钉刺残留监控
                // ==========================================
                if (fdStep == 2)
                {
                    // 1. 监控猎人自身第 19 位战斗标志
                    bool playerInCombat = Unit_Behavioral_State(_hProcess, _playerBase, 19);

                    // 2. 监控大王身上的毒蛇钉刺残留 (全等级 Spell ID 拦截)
                    ulong gordokGuid = 0xF130002CED01F5E9;
                    int bossBase = GetTargetBaseByGuid(_hProcess, gordokGuid);
                    bool King_Gordok_HasSerpentSting = false;

                    if (bossBase != 0)
                    {
                        King_Gordok_HasSerpentSting = HasUnitAura(_hProcess, bossBase, 1978, 13549, 13550, 13551, 13552, 13553, 13554, 13555, 25295);
                    }

                    // 💡 终极双重安检：必须猎人脱战 且 大王身上没有毒蛇钉刺，才允许放行！
                    if (!playerInCombat && !King_Gordok_HasSerpentSting)
                    {
                        // 一旦确认绝对安全，给一个极短的缓冲时间（比如 200ms）确保服务端仇恨列表彻底清空
                        if (combatClearTimer == DateTime.MinValue)
                        {
                            combatClearTimer = DateTime.Now;
                        }
                        else if ((DateTime.Now - combatClearTimer).TotalMilliseconds > 200)
                        {
                            Logger.Write($"[{CurrentPlayerName}] [战斗系统] 猎人成功脱战且大王身上无毒蛇残留！节点安全放行！");
                            fdStep = 0;
                            return NodeState.Success;
                        }
                    }
                    else
                    {
                        // 如果条件不满足，立刻重置缓冲计时器，继续死死趴在地上！
                        combatClearTimer = DateTime.MinValue;

                        // 如果过了 15 秒依然不能起立（要么假死抵抗，要么毒蛇打得太晚一直跳不完）
                        if ((DateTime.Now - fdTimer).TotalSeconds > 15.0)
                        {
                            if (King_Gordok_HasSerpentSting)
                            {
                                Logger.Write($"[{CurrentPlayerName}] [战斗系统] 警告：假死超过15秒，大王身上毒蛇钉刺仍未消失，强制起立防假死超时！");
                            }
                            else
                            {
                                Logger.Write($"[{CurrentPlayerName}] [战斗系统] 致命警告：假死被抵抗或脱战失败！立刻放弃躺地，准备逃生！");
                            }

                            fdStep = 0;
                            return NodeState.Success; // 强制放行交由后续战斗状态机处理
                        }
                    }

                    return NodeState.Running;
                }

                return NodeState.Running;
            });
        }

        /// <summary>
        /// 创建一个阻塞节点：静默等待【假死】技能冷却完毕后才放行
        /// </summary>
        public Node CreateWaitForFeignDeathNode()
        {
            // 利用闭包特性，将计时器封装在函数内部，避免全局变量污染
            DateTime feignDeathWaitTimer = DateTime.MinValue;
            // 标记是否真的进行了等待（用于优化日志，如果不缺 CD 就不打印“冷却完毕”的废话）
            bool hasWaited = false;

            return new ActionNode(() =>
            {
                // 假死技能 ID 为 5384
                bool isCooldown = IsSpellOnCooldown(_hProcess, 5384);

                if (isCooldown)
                {
                    hasWaited = true; // 标记我们确实被卡在这里等过 CD

                    // 每 3 秒打印一次等待日志，防止刷屏
                    if ((DateTime.Now - feignDeathWaitTimer).TotalSeconds > 3.0)
                    {
                        Logger.Write($"[{CurrentPlayerName}] [战斗系统] 假死仍在冷却中...原地静默等待...");
                        feignDeathWaitTimer = DateTime.Now;
                    }

                    // 只要还在冷却，就无限返回 Running 阻塞整棵树，绝对不允许往下走
                    return NodeState.Running;
                }

                // 冷却完毕放行
                if (hasWaited)
                {
                    // 只有真正等过 CD 的，放行时才打印这句话，保持控制台整洁
                    Logger.Write($"[{CurrentPlayerName}] [战斗系统] 假死冷却完毕！准备执行下一步行动！");
                }

                // 重置状态并放行
                feignDeathWaitTimer = DateTime.MinValue;
                hasWaited = false;
                return NodeState.Success;
            });
        }

        /// <summary>
        /// 创建【全自动兽医】节点 (召唤、复活、喂食、喝水、加血、护法)
        /// </summary>
        public Node CreatePetVetNode()
        {
            // 闭包状态变量
            int petStateStep = 0;
            DateTime petProcessTimer = DateTime.MinValue;
            DateTime lastFeedTime = DateTime.MinValue;
            DateTime petLogTimer = DateTime.MinValue; // 护法日志防刷屏计时器
            int feedRetryCount = 0; // 喂食失败重试计数器

            return new ActionNode(() =>
            {
                int petBase = GetPetBase(_hProcess);
                bool isPetDead = IsPetDead(_hProcess, petBase);

                // ==========================================
                // 【阶段 0：智能路由分配中心】
                // ==========================================
                if (petStateStep == 0)
                {
                    // 1. 如果宠物丢失或死亡，优先级最高，立刻抢救
                    if (petBase == 0 || isPetDead)
                    {
                        StopMovement(_hProcess, _playerBase);
                        if (petBase == 0)
                        {
                            Logger.Write($"[{CurrentPlayerName}] [宠物系统] 内存中无实体，施放【召唤宠物】...");
                            ExecuteDynamicLua("CastSpellByName('召唤宠物')");
                            petProcessTimer = DateTime.Now;
                            petStateStep = 1;
                        }
                        else
                        {
                            Logger.Write($"[{CurrentPlayerName}] [宠物系统] 发现尸体，施放【复活宠物】...");
                            ExecuteDynamicLua("CastSpellByName('复活宠物')");
                            petProcessTimer = DateTime.Now;
                            petStateStep = 2;
                        }
                        return NodeState.Running;
                    }

                    // 2. 宠物存活着，进行全方位体检
                    int happiness = GetPetHappiness(_hProcess, petBase);
                    int petDesc = MemoryAPI.ReadInteger(_hProcess, petBase + 0x08);
                    int curHp = MemoryAPI.ReadInteger(_hProcess, petDesc + 0x58);
                    int maxHp = MemoryAPI.ReadInteger(_hProcess, petDesc + 0x70);
                    float hpPercent = maxHp > 0 ? ((float)curHp / maxHp * 100f) : 100f;

                    bool hasFeedBuff = HasPetAura(_hProcess, petBase, 1539);  // 喂食Buff
                    bool hasHealBuff = HasPetAura(_hProcess, petBase, 13544); // 治疗Buff

                    // 路由 A：需要喂食 (未达绿脸，且没在吃东西，且脱离上次喂食10秒)
                    if (happiness <= 666666 && !hasFeedBuff && (DateTime.Now - lastFeedTime).TotalSeconds > 10)
                    {
                        petStateStep = 4;
                        return NodeState.Running;
                    }

                    // 路由 B：需要加血 (血量低于 85%，且身上没有治疗Buff)
                    if (hpPercent <= 85f && !hasHealBuff)
                    {
                        petStateStep = 7; // 切入加血/喝水模块
                        return NodeState.Running;
                    }

                    // 路由 C：正在加血中 (原地护法，防止移动打断通道法术)
                    if (hasHealBuff)
                    {
                        StopMovement(_hProcess, _playerBase);
                        if ((DateTime.Now - petLogTimer).TotalSeconds > 3)
                        {
                            Logger.Write($"[{CurrentPlayerName}] [宠物系统] 正在【治疗宠物】(血量:{hpPercent:F1}%)...");
                            petLogTimer = DateTime.Now;
                        }
                        return NodeState.Running;
                    }

                    // 路由 D：正在吃东西，但还没到绿脸 (原地护法，等快乐值涨上去)
                    if (happiness <= 666666 && hasFeedBuff)
                    {
                        StopMovement(_hProcess, _playerBase);
                        if ((DateTime.Now - petLogTimer).TotalSeconds > 3)
                        {
                            Logger.Write($"[{CurrentPlayerName}] [宠物系统] 宝宝正在进食(快乐值:{happiness})，等涨到绿脸再走...");
                            petLogTimer = DateTime.Now;
                        }
                        return NodeState.Running;
                    }

                    // 3. 所有体检通过 (满血、绿脸、没死)！放行去打工！
                    return NodeState.Success;
                }

                // ==========================================
                // 【阶段 1 ~ 3：复活相关逻辑】(保留原样)
                // ==========================================
                if (petStateStep == 1)
                {
                    if ((DateTime.Now - petProcessTimer).TotalSeconds > 1.5)
                    {
                        petBase = GetPetBase(_hProcess);
                        isPetDead = IsPetDead(_hProcess, petBase);
                        if (petBase != 0 && isPetDead)
                        {
                            ExecuteDynamicLua("CastSpellByName('复活宠物')");
                            petProcessTimer = DateTime.Now;
                            petStateStep = 2;
                        }
                        else petStateStep = 0;
                    }
                    return NodeState.Running;
                }
                if (petStateStep == 2)
                {
                    int currentSpell = MemoryAPI.ReadInteger(_hProcess, _playerBase + 0xC8C);
                    if (currentSpell == 982 || currentSpell == 715) return NodeState.Running;
                    // ====================================================
                    // 🚨 第一道保险：读条刚结束，立刻大喊一声“跟随”！
                    // ====================================================
                    Logger.Write($"[{CurrentPlayerName}] [宠物系统] 召唤/复活施法结束，下达【跟随】指令打断停留AI！");
                    ExecuteDynamicLua("PetFollow()");
                    petProcessTimer = DateTime.Now;
                    petStateStep = 3;
                    return NodeState.Running;
                }
                if (petStateStep == 3)
                {
                    if ((DateTime.Now - petProcessTimer).TotalSeconds > 1.5)
                    {
                        // ====================================================
                        // 🚨 第二道保险：1.5秒缓冲结束，宠物彻底站立，再补发一次“跟随”彻底洗掉残留状态！
                        // ====================================================
                        ExecuteDynamicLua("PetFollow()");
                        petStateStep = 0; // 重置状态机，退回体检路由
                    }
                    return NodeState.Running;
                }

                // ==========================================
                // 【阶段 4 ~ 5：喂食与Buff验证】
                // ==========================================
                if (petStateStep == 4)
                {
                    Logger.Write($"[{CurrentPlayerName}] [宠物系统] 正在投喂【烤鹌鹑】... (第 {feedRetryCount + 1} 次尝试)");
                    StopMovement(_hProcess, _playerBase);
                    string feedLua = @"
                CastSpellByName('喂养宠物');
                for b=0,4 do for s=1,18 do 
                    local l=GetContainerItemLink(b,s); 
                    if l and string.find(l,'烤鹌鹑') then PickupContainerItem(b,s); return; end 
                end end";
                    ExecuteDynamicLua(feedLua.Replace("\r", "").Replace("\n", " "));
                    lastFeedTime = DateTime.Now;
                    petProcessTimer = DateTime.Now;
                    petStateStep = 5;
                    return NodeState.Running;
                }
                if (petStateStep == 5)
                {
                    if ((DateTime.Now - petProcessTimer).TotalSeconds > 1.5)
                    {
                        if (HasPetAura(_hProcess, petBase, 1539))
                        {
                            Logger.Write($"[{CurrentPlayerName}] [宠物系统] 喂食成功！");
                            feedRetryCount = 0;
                            petStateStep = 0; // 重回路由，如果还需要加血，它会自动分配！
                        }
                        else
                        {
                            feedRetryCount++;
                            if (feedRetryCount >= 3)
                            {
                                Logger.Write($"[{CurrentPlayerName}] [致命错误] 连续 3 次喂食失败(可能是无食物)！强制终止脚本！");
                                return NodeState.Failure;
                            }
                            petStateStep = 4; // 重新喂
                        }
                    }
                    return NodeState.Running;
                }

                // ==========================================
                // 【阶段 7：加血与自动喝水判定】
                // ==========================================
                if (petStateStep == 7)
                {
                    // 猎人自身蓝量判定 (PowerType 0 的偏移为 0x5C)
                    int pDesc = MemoryAPI.ReadInteger(_hProcess, _playerBase + 0x08);
                    int myMana = MemoryAPI.ReadInteger(_hProcess, pDesc + 0x5C);

                    if (myMana < 1200)
                    {
                        // 如果在战斗中没蓝，绝不能坐地等死！必须跳过加血，直接切出去战斗！
                        if (Unit_Behavioral_State(_hProcess, _playerBase, 19))
                        {
                            Logger.Write($"[{CurrentPlayerName}] [宠物系统] OOM 没蓝加血但正在挨打！跳过急救，拔刀反击！");
                            petStateStep = 0;
                            return NodeState.Success;
                        }

                        // 没在战斗中，老老实实坐下喝水
                        GetEatDrinkStatus(_hProcess, _playerBase, out bool isEating, out bool isDrinking);
                        if (!isDrinking)
                        {
                            Logger.Write($"[{CurrentPlayerName}] [宠物系统] 蓝量极低({myMana}/1200)，正在饮用魔法水...");
                            StopMovement(_hProcess, _playerBase);
                            //UseItemByItemId(_hProcess, 8079); // 调用你类里的最好水 ID
                            //使用智能吃喝
                            autoDrinkEatNode();
                        }
                        return NodeState.Running; // 只要蓝不够 1200 就一直阻塞，直到喝够
                    }

                    // 蓝量充足，直接搓治疗宠物！
                    Logger.Write($"[{CurrentPlayerName}] [宠物系统] 蓝量充足，正在施放【治疗宠物】...");
                    StopMovement(_hProcess, _playerBase);
                    ExecuteDynamicLua("CastSpellByName('治疗宠物')");
                    petProcessTimer = DateTime.Now;
                    petStateStep = 8;
                    return NodeState.Running;
                }

                // ==========================================
                // 【阶段 8：加血通道验证】
                // ==========================================
                if (petStateStep == 8)
                {
                    // 给网络 1.5 秒确认通道法术建立
                    if ((DateTime.Now - petProcessTimer).TotalSeconds > 1.5)
                    {
                        // 无论是否成功挂上 Buff，都退回阶段 0
                        // 因为如果是假失败（比如被打断），阶段 0 会再次发现血量不满，重新分配回来！
                        petStateStep = 0;
                    }
                    return NodeState.Running;
                }

                return NodeState.Running;
            });
        }

        /// <summary>
        /// 战斗系统节点 3.0：精准背包扫描、CD 剩余时间比对、绝境直接终结
        /// </summary>
        public Node CreateUseInvisibilityPotionNode()
        {
            int invStep = 0;
            DateTime invTimer = DateTime.MinValue;
            DateTime potionLogTimer = DateTime.MinValue;

            // 闭包状态变量：用于记录当前决定要喝哪一种药水
            int targetPotionId = 0;
            int targetBuffId = 0;

            return new ActionNode(() =>
            {
                int lesserPotionId = 3823; // 次级隐形药水
                int normalPotionId = 9172; // 隐形药水
                int lesserBuffId = 3680;   // 次级隐形 Buff
                int normalBuffId = 11392;  // 普通隐形 Buff

                // ==========================================
                // 阶段 0：战术决策阶段 (只执行一次)
                // ==========================================
                if (invStep == 0)
                {
                    // 扫描真实背包，确认有货
                    var inventory = GetAllInventoryItems(_hProcess);
                    bool hasLesser = inventory.Exists(i => i.ItemId == lesserPotionId);
                    bool hasNormal = inventory.Exists(i => i.ItemId == normalPotionId);

                    // 逻辑 3：身上没有药水，直接等死
                    if (!hasLesser && !hasNormal)
                    {
                        Logger.Write($"[{CurrentPlayerName}] [战斗系统] 弹尽粮绝！背包没有任何隐形药水，终结整棵树！");
                        return NodeState.Failure;
                    }

                    // 获取剩余 CD 秒数（如果没有这种药，把 CD 设为极大值，强制淘汰）
                    double cdLesser = hasLesser ? IsSpellOnCooldownRemaining(_hProcess, lesserPotionId) : double.MaxValue;
                    double cdNormal = hasNormal ? IsSpellOnCooldownRemaining(_hProcess, normalPotionId) : double.MaxValue;

                    // 逻辑 4：如果都有，取时间最短的；如果只有一个，自然会取到有的那个
                    if (cdLesser <= cdNormal)
                    {
                        targetPotionId = lesserPotionId;
                        targetBuffId = lesserBuffId;
                    }
                    else
                    {
                        targetPotionId = normalPotionId;
                        targetBuffId = normalBuffId;
                    }

                    double finalTargetCd = (targetPotionId == lesserPotionId) ? cdLesser : cdNormal;

                    // 逻辑 1 & 2：判断这瓶药水的 CD 是否超过了假死的极限时间 (6分钟 = 360秒)
                    if (finalTargetCd > 360.0)
                    {
                        Logger.Write($"[{CurrentPlayerName}] [战斗系统] 绝境！唯一药水的CD高达 {finalTargetCd:F1} 秒 (> 360秒)，假死必定超时，终结整棵树等死！");
                        return NodeState.Failure;
                    }

                    Logger.Write($"[{CurrentPlayerName}] [战斗系统] 锁定目标药水 ID:{targetPotionId}，剩余CD: {finalTargetCd:F1} 秒。开始执行！");
                    invStep = 1; // 决策完毕，进入等待/饮用阶段
                    return NodeState.Running;
                }

                // ==========================================
                // 阶段 1：等待 CD 与饮用阶段
                // ==========================================
                if (invStep == 1)
                {
                    double currentCd = IsSpellOnCooldownRemaining(_hProcess, targetPotionId);

                    if (currentCd > 0)
                    {
                        // 还在 CD，原地躺着等
                        if ((DateTime.Now - potionLogTimer).TotalSeconds > 3.0)
                        {
                            Logger.Write($"[{CurrentPlayerName}] [战斗系统] 药水仍在冷却，距离可使用还剩 {currentCd:F1} 秒...保持假死静默...");
                            potionLogTimer = DateTime.Now;
                        }
                        return NodeState.Running; // 阻塞
                    }
                    else
                    {
                        // CD 转好，直接喝！
                        UseItemByItemId(_hProcess, targetPotionId);
                        Logger.Write($"[{CurrentPlayerName}] [战斗系统] 饮用指令已发送！等待底层 Buff {targetBuffId} 生效...");

                        invTimer = DateTime.Now;
                        invStep = 2; // 进入确认阶段
                        return NodeState.Running;
                    }
                }

                // ==========================================
                // 阶段 2：底层 Buff 确认阶段
                // ==========================================
                if (invStep == 2)
                {
                    if ((DateTime.Now - invTimer).TotalMilliseconds > 300)
                    {
                        // 精准校验我们刚才选定的那瓶药水的 Buff
                        if (HasPlayerBuff(_hProcess, _playerBase, targetBuffId))
                        {
                            Logger.Write($"[{CurrentPlayerName}] [战斗系统] 隐形光环确认生效！起跑！");
                            invStep = 0; // 重置节点状态，以便下次跑本
                            return NodeState.Success; // 放行上楼！
                        }
                        else
                        {
                            // 1.5 秒没挂上，退回尝试重新喝
                            if ((DateTime.Now - invTimer).TotalSeconds > 1.5)
                            {
                                Logger.Write($"[{CurrentPlayerName}] [战斗系统] 光环未生效，尝试重试...");
                                invStep = 1; // 退回喝药阶段
                            }
                        }
                    }
                    return NodeState.Running;
                }

                return NodeState.Running;
            });
        }

        /// <summary>
        /// 创建【确保解散野兽】节点：反复确认宠物基址消失才放行
        /// </summary>
        public Node CreateEnsureDismissPetNode()
        {
            // 闭包状态变量：保证节点复用时不互相干扰
            int dismissStep = 0;
            DateTime dismissTimer = DateTime.MinValue;

            return new ActionNode(() =>
            {
                // 每一帧实时读取最新宠物基址
                int petBase = GetPetBase(_hProcess);

                // ==========================================
                // 阶段 0：首次验证与发起施法
                // ==========================================
                if (dismissStep == 0)
                {
                    // 🚨 置顶验证：如果当前压根就没有宝宝，直接完美放行！
                    if (petBase == 0)
                    {
                        Logger.Write($"[{CurrentPlayerName}] [战斗系统] 宠物已不在内存中，无需解散，直接放行！");
                        dismissStep = 0; // 重置以备复用
                        return NodeState.Success;
                    }

                    Logger.Write($"[{CurrentPlayerName}] [战斗系统] 发现残留宠物实体 (0x{petBase:X})，开始强制执行【解散野兽】...");

                    // 解散野兽需要 5 秒引导，必须物理急刹车死死站住
                    StopMovement(_hProcess, _playerBase);
                    ExecuteDynamicLua("CastSpellByName('解散野兽')");

                    dismissTimer = DateTime.Now;
                    dismissStep = 1;
                    return NodeState.Running;
                }

                // ==========================================
                // 阶段 1：监控读条与极速放行
                // ==========================================
                if (dismissStep == 1)
                {
                    // 🟢 极速放行判定：只要在等待期间基址归零，代表服务器已确认销毁，瞬间放行！
                    if (petBase == 0)
                    {
                        Logger.Write($"[{CurrentPlayerName}] [战斗系统] 解散确认成功！宠物实体已彻底销毁！");

                        dismissStep = 0; // 重置状态
                        return NodeState.Success;
                    }

                    // ⚠️ 超时兜底：如果等了 5.5 秒，宠物基址依然没有变成 0
                    // 说明可能施法被怪物打断、被玩家干扰退条、或是自己手贱动了一下
                    if ((DateTime.Now - dismissTimer).TotalSeconds > 5.5)
                    {
                        Logger.Write($"[{CurrentPlayerName}] [战斗系统] 解散超时/被打断！宠物竟然还在，准备重新解散...");

                        dismissStep = 0; // 打回阶段 0，下一帧重新踩刹车念咒！
                    }

                    return NodeState.Running; // 还在 5.5 秒读条等待期内，保持阻塞
                }

                return NodeState.Running;
            });
        }

        /// <summary>
        /// 创建刷新宠物CD节点：解散并瞬间秒召宠物 (支持多处复用，状态绝对隔离)
        /// </summary>
        public Node CreateRefreshPetCooldownNode()
        {
            // 🚨 核心改动：把状态变量放在这里！
            // 每次调用此方法创建 Node，都会在内存中形成独立的闭包 (Closure)。
            // 即使你在行为树里塞了 10 个这个节点，它们彼此之间的 Step 和 Timer 也绝对不会互相污染！
            int refreshStep = 0;
            DateTime refreshTimer = DateTime.MinValue;

            return new ActionNode(() =>
            {
                int petBase = GetPetBase(_hProcess);

                // ==========================================
                // 阶段 0：发起解散宠物 (5秒读条)
                // ==========================================
                if (refreshStep == 0)
                {
                    // 兜底：如果没宠物，直接跳到召唤阶段
                    if (petBase == 0)
                    {
                        refreshStep = 2;
                        return NodeState.Running;
                    }

                    Logger.Write($"[{CurrentPlayerName}] [战斗系统] 开始解散宠物以刷新 CD...");

                    // 必须死死站住，外部逻辑在进入此节点前应当已执行 StopMovement
                    ExecuteDynamicLua("CastSpellByName('解散野兽')");

                    refreshTimer = DateTime.Now;
                    refreshStep = 1;
                    return NodeState.Running;
                }

                // ==========================================
                // 阶段 1：监控解散是否完成
                // ==========================================
                if (refreshStep == 1)
                {
                    if (petBase == 0)
                    {
                        Logger.Write($"[{CurrentPlayerName}] [战斗系统] 宠物已从内存销毁！移动施放瞬间秒召！");

                        // 召唤是瞬发，不用停步！直接下达 Lua 宏！
                        ExecuteDynamicLua("CastSpellByName('召唤宠物')");

                        refreshTimer = DateTime.Now;
                        refreshStep = 2; // 进入瞬发基址捕获阶段
                        return NodeState.Running;
                    }

                    // 防打断超时：解散宠物 5 秒读条，给 5.5 秒冗余
                    if ((DateTime.Now - refreshTimer).TotalSeconds > 5.5)
                    {
                        Logger.Write($"[{CurrentPlayerName}] [战斗系统] 解散宠物超时(被干扰)，重新执行解散...");
                        refreshStep = 0;
                    }
                    return NodeState.Running;
                }

                // ==========================================
                // 阶段 2：极速捕获新宠物实体
                // ==========================================
                if (refreshStep == 2)
                {
                    // 只要基址刷出来，瞬间放行！
                    if (petBase != 0)
                    {
                        Logger.Write($"[{CurrentPlayerName}] [战斗系统] 瞬发召唤成功 (新基址: 0x{petBase:X})！突进 CD 满血复活！");

                        refreshStep = 0; // 成功后重置该节点的内部状态，以便下一次跑到该节点时能正常触发
                        return NodeState.Success; // 💥 亮绿灯，行为树狂飙起跑！
                    }

                    // 瞬发技能超时：因为不需要读条，只有网络丢包会导致失败。最多给 0.5 秒容错。
                    if ((DateTime.Now - refreshTimer).TotalSeconds > 0.5)
                    {
                        Logger.Write($"[{CurrentPlayerName}] [战斗系统] 瞬发召唤未收到服务器响应，重试...");

                        ExecuteDynamicLua("CastSpellByName('召唤宠物')");
                        refreshTimer = DateTime.Now;
                    }
                    return NodeState.Running;
                }

                return NodeState.Running;
            });
        }


        private void BuildTree()
        {
            //调试代码
            bool isDebug = true;
            if (isDebug)
            {
                _rootTree = new Sequence(

                    new ActionNode(() =>
                    {
                        Logger.Write($"[{CurrentPlayerName}] [战斗系统] 副本逻辑接管，准备开始刷本！");
                        Logger.Write($"[{CurrentPlayerName}] [战斗系统] 正在执行刷本前的准备");
                        return NodeState.Success;
                    }),



                    //拉怪点
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 766.99f, 545.53f, 40.40f, 1.0f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    new WaitNode(100),


                    //已经到达拉怪点

                    new ActionNode(() =>
                    {
                        int petBase = GetPetBase(_hProcess);
                        if (petBase == 0) return NodeState.Failure; // 兜底：宝宝如果没了报错打断
                        ulong King_Gordok_GUID = GetGuidByEntryId(_hProcess, 11501);
                        if (King_Gordok_GUID == 0) return NodeState.Failure; // 兜底：大王如果没了报错打断
                        // 阶段 0：锁目标 A，派宝宝咬
                        if (pullStep_1 == 0)
                        {
                            Logger.Write($"[{CurrentPlayerName}] [战斗系统] 锁定怪物 ({King_Gordok_GUID}) 指派宠物前往攻击...");

                            Select_Target(_hProcess, King_Gordok_GUID);

                            // 第一次绝对安全，直接并攻击
                            ExecuteDynamicLua("PetAttack()");
                            pullTimer_1 = DateTime.Now;
                            pullStep_1 = 1;
                            return NodeState.Running;
                        }

                        // 阶段 1：等待宠物进入战斗状态
                        if (pullStep_1 == 1)
                        {
                            // 15 秒没进战，重置
                            if ((DateTime.Now - pullTimer_1).TotalSeconds > 15)
                            {
                                pullStep_1 = 0;
                                return NodeState.Running;
                            }

                            if (Pet_Behavioral_State(_hProcess, petBase, 19))
                            {
                                Logger.Write($"[{CurrentPlayerName}] [战斗系统] 宠物已成功引到 ({King_Gordok_GUID}) 的仇恨！");

                                ExecuteDynamicLua("PetFollow()");
                                pullTimer_1 = DateTime.Now;
                                pullStep_1 = 2;
                            }
                            return NodeState.Running;
                        }

                        // 阶段 2：动作缓冲放行
                        if (pullStep_1 == 2)
                        {
                            // 留 100 毫秒让攻击动作在服务器生效，然后猎人再开跑
                            if ((DateTime.Now - pullTimer_1).TotalMilliseconds >= 100)
                            {
                                pullStep_1 = 0; // 重置变量，为下次跑本复用做准备
                                return NodeState.Success; // 完美放行，猎人开跑！
                            }
                            return NodeState.Running;
                        }
                        return NodeState.Running;
                    }),

                    new WaitNode(100),

                    //这里要跳一下，否则大概率宠物拉到大王后会直接飞过来
                    new ActionNode(() =>
                    {
                        _ = Jump(50);
                        return NodeState.Success;

                    }),
                    new WaitNode(100),

                    //等待宝宝消失，重新召唤出来
                    new ActionNode(() =>
                    {
                        // 调用现有的方法获取宠物基址
                        int petBase = GetPetBase(_hProcess);

                        if (petBase == 0)
                        {
                            // 宠物确实消失了，结束等待，放行
                            return NodeState.Success;
                        }

                        // 宠物还在，返回 Running 状态
                        // 这会让行为树在下一次 Tick 时继续执行这个节点，起到“阻塞等待”的作用
                        return NodeState.Running;
                    }),

                    new WaitNode(100),

                    new ActionNode(() =>
                    {
                        // 1. 验证基址：获取当前宠物状态
                        int petBase = GetPetBase(_hProcess);

                        // 2. 成功条件：如果宠物基址不等于 0，说明召唤成功，放行
                        if (petBase != 0)
                        {
                            // 重置时间戳，防止影响这棵行为树下次的重复执行
                            _lastSummonTime = DateTime.MinValue;
                            return NodeState.Success;
                        }

                        // 3. 执行召唤与等待逻辑 (此时宠物基址依然为 0)
                        // 判定条件：如果是初始状态 (MinValue)，或者距离上次施法已经过去了 100 毫秒
                        if (_lastSummonTime == DateTime.MinValue || (DateTime.Now - _lastSummonTime).TotalMilliseconds >= 100)
                        {
                            // 执行召唤宠物 Lua
                            ExecuteDynamicLua("CastSpellByName('召唤宠物');");

                            // 刷新最后一次执行的时间戳
                            _lastSummonTime = DateTime.Now;
                        }

                        // 4. 返回 Running 状态，等待下一帧Tick
                        return NodeState.Running;
                    }),

                    new WaitNode(100),



                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 766.58f, 559.50f, 40.40f, 1.0f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    new WaitNode(1000),

                    // 2. 核心改动：定住宝宝，并立刻派它去开大王！
                    new ActionNode(() =>
                    {
                        Logger.Write($"[{CurrentPlayerName}] [战斗系统] 宠物就位！下达攻击指令，猎人准备前往狙击点。");
                        ulong King_Gordok_GUID = GetGuidByEntryId(_hProcess, 11501);
                        if (King_Gordok_GUID == 0) return NodeState.Failure; // 兜底：大王如果没了报错打断
                        // 必须先在内存中锁死大王，再派宝宝
                        Select_Target(_hProcess, King_Gordok_GUID);

                        // 定住宝宝的同时，直接让它去咬大王
                        ExecuteDynamicLua("PetWait(); PetAttack();");

                        // 初始化后续的状态机变量
                        _gordokAttackStep = 0;
                        return NodeState.Success;
                    }),

                    new WaitNode(100),

                    //出来与大王交战

                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 770.68f, 552.91f, 40.40f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 790.10f, 551.91f, 40.80f, 1.0f))
                        {
                            Logger.Write($"[{CurrentPlayerName}] [战斗系统] 猎人已到达狙击点(790.10f, 551.91f, 40.80f)。");
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),


                    //在这里等着大王，到射程范围内奥术射击一下+让宝宝去咬一下立刻执行防御型宏让宝宝回到原位，这里使用宝宝的战斗行为19位来判断宝宝是否进战斗了
                    // ----------------------------------------------------
                    // 猎人已就位，执行 3 阶段交战逻辑
                    // ----------------------------------------------------
                    // 5. 猎人已就位，进入交战与接力监控节点
                    new ActionNode(() =>
                    {
                        ulong King_Gordok_GUID = GetGuidByEntryId(_hProcess, 11501);
                        if (King_Gordok_GUID == 0) return NodeState.Failure; // 兜底：大王如果没了报错打断
                        // 阶段 0：猎人站着等，盯着宝宝是否进战
                        if (_gordokAttackStep == 0)
                        {
                            int petBase = GetPetBase(_hProcess);
                            if (petBase == 0) return NodeState.Failure;

                            // 监控宝宝的第 19 位 (战斗状态)
                            if (Pet_Behavioral_State(_hProcess, petBase, 19))
                            {
                                Logger.Write($"[{CurrentPlayerName}] [战斗系统] 宝宝已摸到大王并进战，立刻执行被动拉回！");

                                // 强制拉回宝宝，大王开始追宝宝
                                ExecuteDynamicLua("PetPassiveMode();");
                                _gordokAttackStep = 1;
                            }

                            // 返回 Running，此时猎人在原地卡帧等待
                            return NodeState.Running;
                        }

                        // 阶段 1：宝宝正在往回跑，大王被拉过来，猎人死盯射程
                        if (_gordokAttackStep == 1)
                        {
                            int targetBase = GetTargetBaseByGuid(_hProcess, King_Gordok_GUID);
                            if (targetBase == 0) return NodeState.Running;

                            float pX = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9B8);
                            float pY = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9BC);
                            float pZ = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9C0);

                            float mX = MemoryAPI.ReadFloat(_hProcess, targetBase + 0x9B8);
                            float mY = MemoryAPI.ReadFloat(_hProcess, targetBase + 0x9BC);
                            float mZ = MemoryAPI.ReadFloat(_hProcess, targetBase + 0x9C0);

                            float dist = (float)Math.Sqrt(Math.Pow(mX - pX, 2) + Math.Pow(mY - pY, 2) + Math.Pow(mZ - pZ, 2));

                            // 大王一进入射程 (预留 1.5 码防卡视野)
                            if (dist <= 39.5f)
                            {
                                Logger.Write($"[{CurrentPlayerName}] [战斗系统] 大王进入射程 ({dist:F1} 码)，奥射接力抢仇恨！");

                                Select_Target(_hProcess, King_Gordok_GUID);
                                //防止观察者套盾  这里打扰乱更稳定
                                ExecuteDynamicLua("CastSpellByName('扰乱射击');");

                                _gordokAttackStep = 0; // 重置变量，本节点彻底完成
                                return NodeState.Success; // 放行！猎人可以立刻往下跑了
                            }

                            return NodeState.Running;
                        }

                        return NodeState.Running;
                    }),

                    // 可选：为了防止 Lua 宏执行与猎人转身起步冲突，给个极短的停顿
                    new WaitNode(100),

                    //射完大王后立即跑下来


                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 802.66f, 545.22f, 28.18f, 1.0f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),



                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 812.41f, 546.32f, 28.41f, 1.0f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 821.50f, 545.94f, 30.63f, 1.0f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 834.41f, 542.53f, 34.26f, 1.0f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 829.89f, 540.44f, 34.26f, 1.0f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 818.25f, 540.47f, 34.26f, 1.0f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 807.66f, 539.19f, 34.26f, 1.0f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 799.02f, 536.72f, 34.26f, 1.0f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 788.90f, 531.58f, 34.26f, 1.0f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 782.08f, 525.15f, 34.26f, 1.0f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    //猎人已就位，接下来就是击杀逻辑代码
                    new WaitNode(100),

                    //“秒上 Buff 即放行”的吃喝节点
                    //ApplyEatDrinkBuffsNode(),
                    //new WaitNode(100),


                    // ----------------------------------------------------
                    // 完美贡品：大王拉锯战主循环节点 
                    // ----------------------------------------------------
                    new Sequence(
                        // ==========================================
                        // 第一道防线：前置安检 (一票否决)
                        // ==========================================
                        // 只有这道防线放在 Sequence 的第一步，宠物死亡的 Failure 才能真正打断战斗！
                        new ActionNode(() =>
                        {
                            int Pet_Base = GetPetBase(_hProcess);
                            if (Pet_Base == 0)
                            {
                                Logger.Write($"[{CurrentPlayerName}] [战斗系统] 致命警告：宠物实体完全丢失！翻车！");
                                return NodeState.Failure; // 直接阻断下方所有逻辑，交由你的 OnTick 或跑尸接管
                            }

                            int Pet_Desc = MemoryAPI.ReadInteger(_hProcess, Pet_Base + 0x08);
                            int Pet_Current_Hp = MemoryAPI.ReadInteger(_hProcess, Pet_Desc + 0x58);

                            if (Pet_Current_Hp <= 0)
                            {
                                Logger.Write($"[{CurrentPlayerName}] [战斗系统] 致命警告：宠物血量归零！翻车！");
                                return NodeState.Failure; // 直接阻断下方所有逻辑
                            }

                            // 宠物活蹦乱跳，安检通过，放行给下方的战斗状态机！
                            return NodeState.Success;
                        }),

                        //状态机开始
                        new CombatLoopNode(
                            new Selector(
                                // ------------------------------------------
                                // 优先级 0：全局胜利监控
                                // ------------------------------------------
                                new ActionNode(() =>
                                {
                                    ulong King_Gordok_GUID = GetGuidByEntryId(_hProcess, 11501);
                                    if (King_Gordok_GUID == 0) return NodeState.Failure; // 兜底：大王如果没了报错打断

                                    int King_Gordok_Base = GetTargetBaseByGuid(_hProcess, King_Gordok_GUID);
                                    if (King_Gordok_Base == 0)
                                    {
                                        Logger.Write($"[{CurrentPlayerName}] [战斗系统] 致命警告：大王在击杀过程中实体(基址)消失了，可能被GM抓到了！结束战斗循环！");
                                        // 这里的 Success 会让 Selector 停止，并向外层的 Sequence 汇报圆满完成！
                                        // 然后行为树就会无缝衔接到下面的“找箱子”节点了。
                                        return NodeState.Success;
                                    }

                                    int King_Gordok_Desc = MemoryAPI.ReadInteger(_hProcess, King_Gordok_Base + 0x08);
                                    int King_Gordok_CurrentHp = MemoryAPI.ReadInteger(_hProcess, King_Gordok_Desc + 0x58);
                                    int King_Gordok_MaxHp = MemoryAPI.ReadInteger(_hProcess, King_Gordok_Desc + 0x70);
                                    //战斗状态
                                    bool King_Gordok_Combat_Status = Unit_Behavioral_State(_hProcess, King_Gordok_Base, 19);

                                    if (King_Gordok_CurrentHp <= 0 && !King_Gordok_Combat_Status)
                                    {
                                        Logger.Write($"[{CurrentPlayerName}] [战斗系统] 戈多克大王已死亡，完美贡品达成！结束战斗循环！");
                                        // 这里的 Success 会让 Selector 停止，并向外层的 Sequence 汇报圆满完成！
                                        // 然后行为树就会无缝衔接到下面的“找箱子”节点了。
                                        return NodeState.Success;
                                    }

                                    // 大王还没死，返回 Failure，让 Selector 往下找战斗步骤
                                    return NodeState.Failure;
                                }),

                                // ==========================================
                                // 状态机 1：前往假死坐标 
                                // ==========================================
                                new Sequence(
                                    new ConditionNode(() => _gordokState == GordokFightState.Kill_King_Gordok_1),

                                    new ActionNode(() =>
                                    {
                                        if (MoveTo(_hProcess, _playerBase, 784.08f, 522.92f, 34.26f, 1.0f))
                                        {
                                            Logger.Write($"[{CurrentPlayerName}] [战斗系统] [状态机 1] 猎人已经到达假死坐标 784.08f, 522.92f, 34.26f ");
                                            StopMovement(_hProcess, _playerBase);
                                            return NodeState.Success; // 到达位置，放行给下一关！
                                        }
                                        return NodeState.Running;
                                    }),

                                    new WaitNode(100),
                                    //清理目标和取消攻击状态
                                    new ActionNode(() =>
                                    {
                                        Logger.Write($"[{CurrentPlayerName}] [战斗系统] [状态机 1] 清理目标和取消攻击状态");
                                        ExecuteDynamicLua("SpellStopCasting(); ClearTarget();");
                                        return NodeState.Success;
                                    }),
                                    new WaitNode(100),

                                    //读取大王和观察者身上buff情况
                                    new ActionNode(() =>
                                    {
                                        bool King_Gordok_HasSerpentSting = false;
                                        bool Observer_Krush_HasSerpentSting = false;

                                        ulong King_Gordok_GUID = GetGuidByEntryId(_hProcess, 11501);
                                        if (King_Gordok_GUID == 0) return NodeState.Failure; // 兜底：大王如果没了报错打断

                                        ulong Observer_Krush_GUID = GetGuidByEntryId(_hProcess, 14324);
                                        if (Observer_Krush_GUID == 0) return NodeState.Failure; // 兜底：观察者如果没了报错打断

                                        //获取大王基址
                                        int King_Gordok_Base = GetTargetBaseByGuid(_hProcess, King_Gordok_GUID);
                                        if (King_Gordok_Base != 0)
                                        {
                                            // 毒蛇钉刺检测
                                            King_Gordok_HasSerpentSting = HasUnitAura(_hProcess, King_Gordok_Base, 1978, 13549, 13550, 13551, 13552, 13553, 13554, 13555, 25295);
                                        }

                                        //获取观察者基址
                                        int Observer_Krush_Base = GetTargetBaseByGuid(_hProcess, Observer_Krush_GUID);
                                        if (Observer_Krush_Base != 0)
                                        {
                                            //蝰蛇钉刺检测
                                            Observer_Krush_HasSerpentSting = HasUnitAura(_hProcess, Observer_Krush_Base, 14280, 14279, 3034);
                                        }

                                        // 核心改动：如果大王身上还有毒蛇残留，或者观察者身上有蝰蛇残留，霸占当前微状态，原地死等，绝对不放行！
                                        if (King_Gordok_HasSerpentSting || Observer_Krush_HasSerpentSting)
                                        {
                                            // 这里可以不打印日志，防止每秒几十次的 Tick 刷屏；也可以加个节拍器打印
                                            return NodeState.Running;
                                        }

                                        // 只有两个人身上都没有钉刺残留，才会执行到这一步！
                                        Logger.Write($"[{CurrentPlayerName}] [战斗系统] [状态机 1] 大王身上没有毒蛇钉刺Debuff，节点放行！");
                                        return NodeState.Success;
                                    }),

                                    new WaitNode(100),

                                    new ActionNode(() =>
                                    {
                                        ulong King_Gordok_GUID = GetGuidByEntryId(_hProcess, 11501);
                                        if (King_Gordok_GUID == 0) return NodeState.Failure; // 兜底：大王如果没了报错打断

                                        ulong Observer_Krush_GUID = GetGuidByEntryId(_hProcess, 14324);
                                        if (Observer_Krush_GUID == 0) return NodeState.Failure; // 兜底：观察者如果没了报错打断

                                        //获取大王基址
                                        int King_Gordok_Base = GetTargetBaseByGuid(_hProcess, King_Gordok_GUID);
                                        if (King_Gordok_Base == 0) return NodeState.Failure;

                                        //获取观察者基址
                                        int Observer_Krush_Base = GetTargetBaseByGuid(_hProcess, Observer_Krush_GUID);
                                        if (Observer_Krush_Base == 0) return NodeState.Failure;

                                        float PlayerX = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9B8);
                                        float PlayerY = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9BC);
                                        float PlayerZ = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9C0);
                                        float King_Gordok_X = MemoryAPI.ReadFloat(_hProcess, King_Gordok_Base + 0x9B8);
                                        float King_Gordok_Y = MemoryAPI.ReadFloat(_hProcess, King_Gordok_Base + 0x9BC);
                                        float King_Gordok_Z = MemoryAPI.ReadFloat(_hProcess, King_Gordok_Base + 0x9C0);
                                        float Observer_Krush_X = MemoryAPI.ReadFloat(_hProcess, Observer_Krush_Base + 0x9B8);
                                        float Observer_Krush_Y = MemoryAPI.ReadFloat(_hProcess, Observer_Krush_Base + 0x9BC);
                                        float Observer_Krush_Z = MemoryAPI.ReadFloat(_hProcess, Observer_Krush_Base + 0x9C0);

                                        // 分别计算大王和观察者距离猎人的距离
                                        float distBossToPlayer = (float)Math.Sqrt(Math.Pow(King_Gordok_X - PlayerX, 2) + Math.Pow(King_Gordok_Y - PlayerY, 2) + Math.Pow(King_Gordok_Z - PlayerZ, 2));
                                        float distObserverToPlayer = (float)Math.Sqrt(Math.Pow(Observer_Krush_X - PlayerX, 2) + Math.Pow(Observer_Krush_Y - PlayerY, 2) + Math.Pow(Observer_Krush_Z - PlayerZ, 2));

                                        // 核心改动：只要大王或观察者其中有一个进入 30 码盲区，立刻放行假死！
                                        if (distBossToPlayer <= 30f || distObserverToPlayer <= 30f)
                                        {
                                            // 动态判断一下到底是谁逼近了，让日志更清晰
                                            string triggerName = distBossToPlayer <= distObserverToPlayer ? "大王" : "观察者";
                                            float triggerDist = distBossToPlayer <= distObserverToPlayer ? distBossToPlayer : distObserverToPlayer;

                                            Logger.Write($"[{CurrentPlayerName}] [战斗系统] [状态机 1] {triggerName} 进入盲区 ({triggerDist:F1} 码)，立即执行假死！");
                                            return NodeState.Success;
                                        }

                                        return NodeState.Running; // 两人都没到 30 码，卡在这里死盯
                                    }),

                                    //假死函数，只看自己，不看宠物
                                    CreateGordokFeignDeathNode(),

                                    new WaitNode(1000),

                                    new ActionNode(() =>
                                    {
                                        ulong Observer_Krush_GUID = GetGuidByEntryId(_hProcess, 14324);
                                        if (Observer_Krush_GUID == 0) return NodeState.Failure; // 兜底：观察者如果没了报错打断

                                        int Observer_Krush_Base = GetTargetBaseByGuid(_hProcess, Observer_Krush_GUID);

                                        if (Observer_Krush_Base == 0)
                                        {
                                            Logger.Write($"[{CurrentPlayerName}] [战斗系统] 致命警告：观察者在击杀过程中实体(基址)消失了，可能被GM抓到了！结束战斗循环！");
                                            // 这里的 Success 会让 Selector 停止，并向外层的 Sequence 汇报圆满完成！
                                            // 然后行为树就会无缝衔接到下面的“找箱子”节点了。
                                            return NodeState.Success;
                                        }

                                        //监控观察者的蓝量，智能选择行为树
                                        int Observer_Krush_Desc = MemoryAPI.ReadInteger(_hProcess, Observer_Krush_Base + 0x08);
                                        int Observer_Krush_CurrentMana = MemoryAPI.ReadInteger(_hProcess, Observer_Krush_Desc + 0x5C);
                                        int Observer_Krush_MaxMana = MemoryAPI.ReadInteger(_hProcess, Observer_Krush_Desc + 0x74);

                                        // 1. 安全计算蓝量百分比 (强转 float 并防除 0 崩溃)
                                        float manaPercent = 0f;
                                        if (Observer_Krush_MaxMana > 0)
                                        {
                                            manaPercent = ((float)Observer_Krush_CurrentMana / Observer_Krush_MaxMana) * 100f;
                                        }

                                        // 2. 核心分流逻辑
                                        if (manaPercent >= 5.0f)
                                        {
                                            // 蓝量大于等于 5%，切入抽蓝与规避期
                                            Logger.Write($"[{CurrentPlayerName}] [战斗系统] 观察者剩余蓝量 {manaPercent:F1}%，执行抽蓝走位策略");
                                            // 只有假死完全成功、脱战了，才会执行到这里！
                                            Logger.Write($"[{CurrentPlayerName}] [战斗系统] [状态机 1] 假死任务执行成功，立刻进入下一个状态机");
                                            _gordokState = GordokFightState.Kill_King_Gordok_2;
                                        }
                                        else
                                        {
                                            // 蓝量小于 5%，观察者已废，切入纯净大王斩杀期
                                            Logger.Write($"[{CurrentPlayerName}] [战斗系统] 观察者已空蓝 ({manaPercent:F1}%)，执行大王斩杀策略");
                                            // 只有假死完全成功、脱战了，才会执行到这里！
                                            Logger.Write($"[{CurrentPlayerName}] [战斗系统] [状态机 1] 假死任务执行成功，立刻进入下一个状态机");
                                            _gordokState = GordokFightState.Kill_King_Gordok_3;
                                        }

                                        return NodeState.Failure;
                                    })
                                ),


                                // ------------------------------------------
                                // 状态机 2：前往输出坐标 + 检测宝宝与大王还有观察者的距离
                                // ------------------------------------------
                                new Sequence(

                                    new ConditionNode(() => _gordokState == GordokFightState.Kill_King_Gordok_2),
                                    new ActionNode(() =>
                                    {
                                        if (MoveTo(_hProcess, _playerBase, 786.25f, 519.74f, 34.26f, 1.0f))
                                        {
                                            return NodeState.Success;
                                        }

                                        return NodeState.Running;
                                    }),

                                    new ActionNode(() =>
                                    {
                                        if (MoveTo(_hProcess, _playerBase, 799.51f, 529.95f, 34.26f, 1.0f))
                                        {
                                            Logger.Write($"[{CurrentPlayerName}] [战斗系统] [状态机 2] 猎人已经到达输出坐标 799.51f, 529.95f, 34.26f");
                                            return NodeState.Success;
                                        }

                                        return NodeState.Running;
                                    }),

                                    new WaitNode(100),

                                    new ActionNode(() =>
                                    {
                                        ulong King_Gordok_GUID = GetGuidByEntryId(_hProcess, 11501);   //大王GUID 
                                        if (King_Gordok_GUID == 0) return NodeState.Failure;

                                        ulong Observer_Krush_GUID = GetGuidByEntryId(_hProcess, 14324);     //观察者GUID
                                        if (Observer_Krush_GUID == 0) return NodeState.Failure;

                                        int King_Gordok_Base = GetTargetBaseByGuid(_hProcess, King_Gordok_GUID);
                                        if (King_Gordok_Base == 0) return NodeState.Failure;

                                        int Observer_Krush_Base = GetTargetBaseByGuid(_hProcess, Observer_Krush_GUID);
                                        if (Observer_Krush_Base == 0) return NodeState.Failure;

                                        int Pet_Base = GetPetBase(_hProcess);
                                        if (Pet_Base == 0) return NodeState.Failure;

                                        // 提取坐标
                                        float Pet_X = MemoryAPI.ReadFloat(_hProcess, Pet_Base + 0x9B8);
                                        float Pet_Y = MemoryAPI.ReadFloat(_hProcess, Pet_Base + 0x9BC);
                                        float Pet_Z = MemoryAPI.ReadFloat(_hProcess, Pet_Base + 0x9C0);

                                        float King_Gordok_X = MemoryAPI.ReadFloat(_hProcess, King_Gordok_Base + 0x9B8);
                                        float King_Gordok_Y = MemoryAPI.ReadFloat(_hProcess, King_Gordok_Base + 0x9BC);
                                        float King_Gordok_Z = MemoryAPI.ReadFloat(_hProcess, King_Gordok_Base + 0x9C0);

                                        float Observer_Krush_X = MemoryAPI.ReadFloat(_hProcess, Observer_Krush_Base + 0x9B8);
                                        float Observer_Krush_Y = MemoryAPI.ReadFloat(_hProcess, Observer_Krush_Base + 0x9BC);
                                        float Observer_Krush_Z = MemoryAPI.ReadFloat(_hProcess, Observer_Krush_Base + 0x9C0);

                                        // 分别计算距离
                                        float Dist_King_Gordok_To_Pet = (float)Math.Sqrt(Math.Pow(King_Gordok_X - Pet_X, 2) + Math.Pow(King_Gordok_Y - Pet_Y, 2) + Math.Pow(King_Gordok_Z - Pet_Z, 2));
                                        float Dist_Observer_Krush_To_Pet = (float)Math.Sqrt(Math.Pow(Observer_Krush_X - Pet_X, 2) + Math.Pow(Observer_Krush_Y - Pet_Y, 2) + Math.Pow(Observer_Krush_Z - Pet_Z, 2));

                                        // ==========================================
                                        // 1. 雷达触发与初始化分流
                                        // ==========================================
                                        // 💡 新增：直接计算大王与观察者之间的物理距离
                                        float Dist_King_To_Observer = (float)Math.Sqrt(Math.Pow(King_Gordok_X - Observer_Krush_X, 2) + Math.Pow(King_Gordok_Y - Observer_Krush_Y, 2) + Math.Pow(King_Gordok_Z - Observer_Krush_Z, 2));

                                        if (_dps2Step == 0)
                                        {
                                            if (_gordokState == GordokFightState.Kill_King_Gordok_2)
                                            {
                                                if (Dist_King_Gordok_To_Pet <= 40f || Dist_Observer_Krush_To_Pet <= 40f)
                                                {
                                                    // 🌟 最高优先级检测：双怪是否重合？(间距 <= 12 码)
                                                    if (Dist_King_To_Observer <= 12f)
                                                    {
                                                        _pullStrategy = 3;
                                                        _dps2Step = 20; // 跨入分支三 (20 起步)
                                                        Select_Target(_hProcess, King_Gordok_GUID); // 先选定大王作为多重射击主目标
                                                        Logger.Write($"[{CurrentPlayerName}] [战斗系统] [状态机 2] 大王与观察者重合 (间距 {Dist_King_To_Observer:F1} 码)！激活【分支三：多重射击暴力接盘】");
                                                    }
                                                    else
                                                    {
                                                        // 如果没重合，再按老规矩判断谁先到
                                                        bool isKingFirst = Dist_King_Gordok_To_Pet <= Dist_Observer_Krush_To_Pet;

                                                        if (isKingFirst)
                                                        {
                                                            _pullStrategy = 1;
                                                            _dps2Step = 1;
                                                            Select_Target(_hProcess, King_Gordok_GUID);
                                                            Logger.Write($"[{CurrentPlayerName}] [战斗系统] 大王先冲过 40 码！激活【分支一：站桩接怪】");
                                                        }
                                                        else
                                                        {
                                                            _pullStrategy = 2;
                                                            _dps2Step = 10;
                                                            Select_Target(_hProcess, Observer_Krush_GUID);
                                                            Logger.Write($"[{CurrentPlayerName}] [战斗系统] 观察者先露头！激活【分支二：躲猫猫卡读条】");
                                                        }
                                                    }
                                                    _actionTimer = DateTime.Now;
                                                }
                                            }
                                            else
                                            {
                                                return NodeState.Success;
                                            }
                                        }

                                        // ==========================================
                                        // 2. 【分支一】：大王先到策略 
                                        // ==========================================
                                        if (_pullStrategy == 1)
                                        {
                                            if (_dps2Step == 1)
                                            {
                                                if ((DateTime.Now - _actionTimer).TotalMilliseconds > 100)
                                                {
                                                    SetFacingByGuid(_playerBase, King_Gordok_GUID);
                                                    _actionTimer = DateTime.Now;
                                                    _dps2Step = 2;
                                                }
                                            }
                                            else if (_dps2Step == 2)
                                            {
                                                // 1. 优先进行 CD 检测（包含了扰乱射击的所有等级）
                                                bool isDistractingShotOnCd = IsSpellOnCooldown(_hProcess, 20736, 14274, 15629, 15630, 15631, 15632);

                                                // 2. 如果检测到已经在冷却中了，说明刚才那一枪绝对结结实实地打出去了！
                                                if (isDistractingShotOnCd)
                                                {
                                                    ExecuteDynamicLua("SpellStopCasting(); ClearTarget();");
                                                    Logger.Write($"[{CurrentPlayerName}] [战斗系统] [状态机 2 分支一 2] 扰乱射击已确认进入冷却！仇恨建立成功。");
                                                    _actionTimer = DateTime.Now;

                                                    // 确认成功，放行到下一步（比如去锁观察者）
                                                    _dps2Step = 3;
                                                }

                                                // 3. 如果没在冷却（比如 GCD 卡了，或者刚切进来），每隔 100 毫秒狂按！
                                                if ((DateTime.Now - _actionTimer).TotalMilliseconds > 100)
                                                {
                                                    ExecuteDynamicLua("CastSpellByName('扰乱射击')");
                                                    // Logger.Write($"[{CurrentPlayerName}] [战斗系统] [状态机 2 分支一 2] 正在尝试施放扰乱射击...");

                                                    // 重置计时器，等下个 100ms 再没进 CD 就继续按
                                                    _actionTimer = DateTime.Now;
                                                }
                                            }

                                            else if (_dps2Step == 3)
                                            {
                                                if ((DateTime.Now - _actionTimer).TotalMilliseconds > 100)
                                                {
                                                    //提前面朝观察者 它会一直持续面对观察者
                                                    SetFacingByGuid(_playerBase, Observer_Krush_GUID);
                                                    _actionTimer = DateTime.Now;
                                                    _dps2Step = 4;
                                                }
                                            }

                                            else if (_dps2Step == 4)
                                            {
                                                // 异步距离栅栏：死等短腿观察者进 指定 码 
                                                if (Dist_Observer_Krush_To_Pet <= 45f)
                                                {
                                                    Select_Target(_hProcess, Observer_Krush_GUID);
                                                    _actionTimer = DateTime.Now;
                                                    _dps2Step = 5;
                                                }
                                            }
                                            else if (_dps2Step == 5)
                                            {
                                                if ((DateTime.Now - _actionTimer).TotalMilliseconds > 100)
                                                {
                                                    // 只打一发蝰蛇钉刺，猎人会自动开始平A
                                                    ExecuteDynamicLua("CastSpellByName('蝰蛇钉刺')");
                                                    Logger.Write($"[{CurrentPlayerName}] [战斗系统] [状态机 2 分支一 5] 蝰蛇钉刺已挂！开启仇恨雷达死盯观察者目标...");

                                                    _actionTimer = DateTime.Now;
                                                    _dps2Step = 6; // 挺进死等仇恨的终极阶段！
                                                }
                                            }

                                            else if (_dps2Step == 6)
                                            {
                                                // 1. 获取猎人自己的 GUID
                                                ulong playerGuid = MemoryAPI.ReadQword(_hProcess, _playerBase + 0x30);

                                                // 2. 获取观察者的当前目标 GUID (通过 Descriptors + 0x40)
                                                int observerDesc = MemoryAPI.ReadInteger(_hProcess, Observer_Krush_Base + 0x08);
                                                ulong observerTargetGuid = MemoryAPI.ReadQword(_hProcess, observerDesc + 0x40);

                                                // 3. 神级判断：观察者真的在看我吗？
                                                if (observerTargetGuid == playerGuid)
                                                {
                                                    Logger.Write($"[{CurrentPlayerName}] [战斗系统] [状态机 2 分支一 6] 仇恨接管成功！大王与观察者目标均为猎人，完美放行！");

                                                    // 大功告成，释放控制权，进入下一个大状态 (比如跑位躲视角的阶段)
                                                    _dps2Step = 7;
                                                }

                                                // 只要观察者没看你（还在看宝宝），就死死卡在这一帧，让猎人的平A自动飞一会儿！
                                                //return NodeState.Running;
                                            }

                                            else if (_dps2Step == 7)
                                            {
                                                if (MoveTo(_hProcess, _playerBase, 804.64f, 531.62f, 34.26f, 1.0f))
                                                {
                                                    Logger.Write($"[{CurrentPlayerName}] [战斗系统] [状态机 2 分支一 7] 猎人已经到达柱子后方，躲避观察者的攻击！");
                                                    _dps2Step = 8;
                                                }
                                            }
                                            else if (_dps2Step == 8)
                                            {
                                                //死盯观察者终极绊马索 (X: 805.379)
                                                if (Observer_Krush_X >= 805.379f)
                                                {
                                                    Logger.Write($"[{CurrentPlayerName}] [战斗系统] [状态机 2 分支一 8] 观察者已踩入绊马索(X:{Observer_Krush_X})！立即开启走位规避！");
                                                    _dps2Step = 9;
                                                }

                                            }

                                            else if (_dps2Step == 9)
                                            {
                                                if ((DateTime.Now - _actionTimer).TotalMilliseconds > 500)
                                                {
                                                    if (MoveTo(_hProcess, _playerBase, 785.79f, 519.73f, 34.26f, 1.0f))
                                                    {
                                                        Logger.Write($"[{CurrentPlayerName}] [战斗系统] 状态机 2 分支一 9] 猎人已经到达重置点坐标 785.79f, 519.73f, 34.26f");
                                                        _dps2Step = 0;
                                                        _pullStrategy = 0;
                                                        //回到假死状态机重新开始
                                                        _gordokState = GordokFightState.Kill_King_Gordok_1;
                                                        return NodeState.Failure;
                                                    }
                                                }
                                                
                                                //return NodeState.Running;
                                            }
                                            return NodeState.Running;
                                        }

                                        // ==========================================
                                        // 3. 【分支二】：观察者先到策略
                                        // ==========================================
                                        else if (_pullStrategy == 2)
                                        {
                                            if (_dps2Step == 10)
                                            {
                                                // 缓冲 100ms，面朝观察者
                                                if ((DateTime.Now - _actionTimer).TotalMilliseconds > 100)
                                                {
                                                    SetFacingByGuid(_playerBase, Observer_Krush_GUID);
                                                    _actionTimer = DateTime.Now;
                                                    _dps2Step = 11;
                                                }
                                            }
                                            else if (_dps2Step == 11)
                                            {
                                                // 第一发：给观察者打扰乱射击(部分牧师职业会给自己套盾，没有扰乱拉不过来仇恨)
                                                // 1. 优先进行 CD 检测（包含了扰乱射击的所有等级）
                                                bool isDistractingShotOnCd = IsSpellOnCooldown(_hProcess, 20736, 14274, 15629, 15630, 15631, 15632);

                                                // 2. 如果检测到已经在冷却中了，说明刚才那一枪绝对结结实实地打出去了！
                                                if (isDistractingShotOnCd)
                                                {
                                                    ExecuteDynamicLua("SpellStopCasting(); ClearTarget();");
                                                    Logger.Write($"[{CurrentPlayerName}] [战斗系统] [状态机 2 分支二 11] 扰乱射击已确认进入冷却！仇恨建立成功。");
                                                    _actionTimer = DateTime.Now;

                                                    // 确认成功，放行到下一步
                                                    _dps2Step = 12;
                                                }

                                                // 3. 如果没在冷却（比如 GCD 卡了，或者刚切进来），每隔 100 毫秒狂按！
                                                if ((DateTime.Now - _actionTimer).TotalMilliseconds > 100)
                                                {
                                                    ExecuteDynamicLua("CastSpellByName('扰乱射击')");
                                                    // Logger.Write($"[{CurrentPlayerName}] [战斗系统] [状态机 2 分支二 2] 正在尝试施放扰乱射击...");

                                                    // 重置计时器，等下个 100ms 再没进 CD 就继续按
                                                    _actionTimer = DateTime.Now;
                                                }
                                            }

                                            else if (_dps2Step == 12)
                                            {
                                                // 1. 获取猎人自己的 GUID
                                                ulong playerGuid = MemoryAPI.ReadQword(_hProcess, _playerBase + 0x30);

                                                // 2. 获取观察者的当前目标 GUID (通过 Descriptors + 0x40)
                                                int observerDesc = MemoryAPI.ReadInteger(_hProcess, Observer_Krush_Base + 0x08);
                                                ulong observerTargetGuid = MemoryAPI.ReadQword(_hProcess, observerDesc + 0x40);

                                                // 3. 神级判断：观察者真的在看我吗？
                                                if (observerTargetGuid == playerGuid)
                                                {
                                                    Logger.Write($"[{CurrentPlayerName}] [战斗系统] [状态机 2 分支二 12] 仇恨接管成功！观察者目标为猎人，完美放行！");

                                                    // 大功告成，释放控制权，进入下一个大状态 (比如跑位躲视角的阶段)
                                                    _dps2Step = 121;
                                                    _actionTimer = DateTime.Now;
                                                }
                                            }

                                            else if (_dps2Step == 121)
                                            {
                                                // 🌟 核心防翻车微操：在死等 1.6秒 GCD 期间，每帧都在雷达死盯大王！
                                                // 如果大王已经逼近到 45 码，绝对不能贪这发蝰蛇！必须留技能 CD 强接大王！
                                                if (Dist_King_Gordok_To_Pet <= 45f)
                                                {
                                                    Logger.Write($"[{CurrentPlayerName}] [战斗系统] [状态机 2 分支二 121] 警告：等GCD期间大王已逼近 ({Dist_King_Gordok_To_Pet:F1} 码)！果断放弃蝰蛇，强切拦截步骤！");

                                                    // 放弃蝰蛇，直接推到下一步，并把目标锁定为大王
                                                    Select_Target(_hProcess, King_Gordok_GUID);
                                                    _actionTimer = DateTime.Now;
                                                    _dps2Step = 13; // 或者是直接推到 131 露头输出点，取决于你大王到场的技能释放顺序
                                                    return NodeState.Running;
                                                }

                                                // 大王还远，安全度过 1.6 秒 GCD，继续打蝰蛇
                                                if ((DateTime.Now - _actionTimer).TotalMilliseconds > 1600)
                                                {
                                                    ExecuteDynamicLua("CastSpellByName('蝰蛇钉刺')");
                                                    Logger.Write($"[{CurrentPlayerName}] [战斗系统] [状态机 2 分支二 121] 安全度过GCD，已经补充一个蝰蛇钉刺！立即放行！");

                                                    _dps2Step = 13;
                                                    _actionTimer = DateTime.Now;
                                                }
                                            }

                                            else if (_dps2Step == 13)
                                            {
                                                // 1. 获取猎人自己的 GUID
                                                ulong playerGuid = MemoryAPI.ReadQword(_hProcess, _playerBase + 0x30);

                                                // 2. 获取大王的当前目标 GUID
                                                int King_Gordok_Desc = MemoryAPI.ReadInteger(_hProcess, King_Gordok_Base + 0x08);
                                                ulong King_Gordok_Desc_Target_Guid = MemoryAPI.ReadQword(_hProcess, King_Gordok_Desc + 0x40);

                                                // 3. 神级判断：大王真的在看我吗？
                                                if (King_Gordok_Desc_Target_Guid == playerGuid)
                                                {
                                                    Logger.Write($"[{CurrentPlayerName}] [战斗系统] [状态机 2 分支二 13] 仇恨接管成功！大王目标为猎人，完美放行！");
                                                    _dps2Step = 131; // 放行
                                                }
                                                else
                                                {
                                                    bool isObserverCasting = IsUnitCasting(_hProcess, Observer_Krush_Base);

                                                    // 🌟 核心修改：应对双怪重合！
                                                    // 如果大王已经逼近到 45 码，说明重合不可避免，直接放弃躲猫猫，准备强接大王！
                                                    if (Dist_King_Gordok_To_Pet <= 45f)
                                                    {
                                                        Logger.Write($"[{CurrentPlayerName}] [战斗系统] [状态机 2 分支二 13] 躲猫猫期间雷达捕捉到大王 ({Dist_King_Gordok_To_Pet:F1} 码)！准备拦截！");
                                                        Select_Target(_hProcess, King_Gordok_GUID);
                                                        _actionTimer = DateTime.Now;
                                                        _dps2Step = 131; // 强行跳出当前微状态
                                                    }
                                                    else
                                                    {
                                                        // 大王还远，安心跟观察者躲猫猫
                                                        if (isObserverCasting)
                                                        {
                                                            // 如果他在读条，往后缩
                                                            if (MoveTo(_hProcess, _playerBase, 806.22f, 531.86f, 34.26f, 1.0f))
                                                            {
                                                                // 💡 防刷屏：不打日志
                                                            }
                                                        }
                                                        else
                                                        {
                                                            // 如果他没读条，露头准备勾引
                                                            if (MoveTo(_hProcess, _playerBase, 799.51f, 529.95f, 34.26f, 1.0f))
                                                            {
                                                                // 💡 防刷屏：只要站在了 799.51，MoveTo 会一直 return true，这里绝对不要打日志！
                                                                // 只要站在露头点，就一直自动平A他
                                                            }
                                                        }
                                                    }
                                                }
                                            }

                                            else if (_dps2Step == 131)
                                            {

                                                if (MoveTo(_hProcess, _playerBase, 799.51f, 529.95f, 34.26f, 1.0f))
                                                {
                                                    Logger.Write($"[{CurrentPlayerName}] [战斗系统] [状态机 2 分支二 131] 猎人已经到达输出坐标 799.51f, 529.95f, 34.26f");
                                                    _dps2Step = 14;
                                                }
                                            }

                                            else if (_dps2Step == 14)
                                            {
                                                // 缓冲 100ms，面朝奔跑过来的大王
                                                if ((DateTime.Now - _actionTimer).TotalMilliseconds > 100)
                                                {
                                                    SetFacingByGuid(_playerBase, King_Gordok_GUID);
                                                    _actionTimer = DateTime.Now;
                                                    _dps2Step = 15;
                                                }
                                            }
                                            else if (_dps2Step == 15)
                                            {

                                                // 1. 优先进行 CD 检测（包含了奥术射击的所有等级）
                                                bool isDistractingShotOnCd = IsSpellOnCooldown(_hProcess, 3044, 14281, 14282, 14283, 14284, 14285, 14286, 14287);

                                                // 2. 如果检测到已经在冷却中了，说明刚才那一枪绝对结结实实地打出去了！
                                                if (isDistractingShotOnCd)
                                                {
                                                    Logger.Write($"[{CurrentPlayerName}] [战斗系统] [状态机 2 分支二 15] 奥术射击已确认进入冷却！仇恨建立成功。");
                                                    _actionTimer = DateTime.Now;

                                                    _dps2Step = 16;
                                                }

                                                // 3. 如果没在冷却（比如 GCD 卡了，或者刚切进来），每隔 100 毫秒狂按！
                                                if ((DateTime.Now - _actionTimer).TotalMilliseconds > 100)
                                                {
                                                    ExecuteDynamicLua("CastSpellByName('奥术射击')");
                                                    // Logger.Write($"[{CurrentPlayerName}] [战斗系统] [状态机 2 分支二 15] 正在尝试施放扰乱射击...");

                                                    // 重置计时器，等下个 100ms 再没进 CD 就继续按
                                                    _actionTimer = DateTime.Now;
                                                }
                                            }
                                            else if (_dps2Step == 16)
                                            {
                                                if ((DateTime.Now - _actionTimer).TotalMilliseconds > 500)
                                                {
                                                    if (MoveTo(_hProcess, _playerBase, 785.79f, 519.73f, 34.26f, 1.0f))
                                                    {
                                                        Logger.Write($"[{CurrentPlayerName}] [战斗系统] 状态机 2 分支二 16] 猎人已经到达重置点坐标 785.79f, 519.73f, 34.26f");
                                                        // 确认成功，放行到下一步（比如去锁观察者）
                                                        // 彻底大功告成，清理状态，放行！
                                                        _dps2Step = 0;
                                                        _pullStrategy = 0;
                                                        _gordokState = GordokFightState.Kill_King_Gordok_1;
                                                        return NodeState.Failure;
                                                    }
                                                }
                                            }
                                            return NodeState.Running;
                                        }

                                        // ==========================================
                                        // 4. 【分支三】：双怪重合，多重+蝰蛇 暴力接盘策略
                                        // ==========================================
                                        else if (_pullStrategy == 3)
                                        {
                                            if (_dps2Step == 20)
                                            {
                                                // 缓冲 100ms，面朝大王
                                                if ((DateTime.Now - _actionTimer).TotalMilliseconds > 100)
                                                {
                                                    SetFacingByGuid(_playerBase, King_Gordok_GUID);
                                                    _actionTimer = DateTime.Now;
                                                    _dps2Step = 21;
                                                }
                                            }
                                            else if (_dps2Step == 21)
                                            {
                                                // 1. 优先进行 CD 检测（多重射击全等级 ID: 2643, 14288, 14289, 14290, 25294）
                                                bool isMultiShotOnCd = IsSpellOnCooldown(_hProcess, 2643, 14288, 14289, 14290, 25294);

                                                if (isMultiShotOnCd)
                                                {
                                                    Logger.Write($"[{CurrentPlayerName}] [战斗系统] [状态机 2 分支三 21] 多重射击已确认打出！双目标仇恨瞬间接管！");

                                                    // 多重打出去了，立刻秒切观察者，准备挂蝰蛇
                                                    Select_Target(_hProcess, Observer_Krush_GUID);
                                                    _actionTimer = DateTime.Now;
                                                    _dps2Step = 22;
                                                }

                                                if ((DateTime.Now - _actionTimer).TotalMilliseconds > 100)
                                                {
                                                    ExecuteDynamicLua("CastSpellByName('多重射击')");
                                                    _actionTimer = DateTime.Now;
                                                }
                                            }
                                            else if (_dps2Step == 22)
                                            {
                                                // 缓冲 100ms，面朝观察者 (通常不需要动，因为他们重合了，但为了保险还是转一下)
                                                if ((DateTime.Now - _actionTimer).TotalMilliseconds > 100)
                                                {
                                                    SetFacingByGuid(_playerBase, Observer_Krush_GUID);
                                                    _actionTimer = DateTime.Now;
                                                    _dps2Step = 23;
                                                }
                                            }
                                            else if (_dps2Step == 23)
                                            {
                                                // 多重射击会占用公共冷却(GCD)，所以这里的间隔我们稍微放宽点，让 GCD 转完
                                                if ((DateTime.Now - _actionTimer).TotalMilliseconds > 1600) // 1.5秒 GCD + 网络延迟
                                                {
                                                    ExecuteDynamicLua("CastSpellByName('蝰蛇钉刺')");
                                                    Logger.Write($"[{CurrentPlayerName}] [战斗系统] [状态机 2 分支三 23] 蝰蛇钉刺已强补！连招结束，立刻开溜！");
                                                    _actionTimer = DateTime.Now;
                                                    _dps2Step = 24; // 技能打完，直接进入跑路阶段
                                                }
                                            }
                                            else if (_dps2Step == 24)
                                            {
                                                // 直奔重置点！
                                                if (MoveTo(_hProcess, _playerBase, 785.79f, 519.73f, 34.26f, 1.0f))
                                                {
                                                    Logger.Write($"[{CurrentPlayerName}] [战斗系统] [状态机 2 分支三 24] 猎人已经到达重置点坐标 785.79f, 519.73f, 34.26f");

                                                    _dps2Step = 0;
                                                    _pullStrategy = 0;

                                                    // 回到假死状态机重新开始
                                                    _gordokState = GordokFightState.Kill_King_Gordok_1;

                                                    // 💡 谨遵教诲：为了行为树的硬重置，这里返回 Failure！
                                                    return NodeState.Failure;
                                                }
                                            }
                                        }
                                        // 没进雷达圈，或者微状态正在跑路/缓冲中，统统返回 Running 牢牢卡住行为树
                                        return NodeState.Running;
                                    })
                                ),

                                // ------------------------------------------
                                // 状态机 3： 开始输出
                                // ------------------------------------------
                                new Sequence(
                                    new ConditionNode(() => _gordokState == GordokFightState.Kill_King_Gordok_3)


                                ),
                                // ------------------------------------------
                                // 状态机 4：
                                // ------------------------------------------
                                new Sequence(
                                    new ConditionNode(() => _gordokState == GordokFightState.Kill_King_Gordok_4)


                                ),
                                // ------------------------------------------
                                // 状态机 5：
                                // ------------------------------------------
                                new Sequence(
                                    new ConditionNode(() => _gordokState == GordokFightState.Kill_King_Gordok_5)


                                )
                            )
                        )

                        //CombatLoopNode end
                    ),








                new WaitNode(500000)    //阻止往下运行
                );
                return;
            }



            //副本内战斗逻辑
            _farmTree = new Sequence(
                new ActionNode(() => {
                    Logger.Write($"[{CurrentPlayerName}] [战斗系统] 副本逻辑接管，准备开始刷本！");
                    Logger.Write($"[{CurrentPlayerName}] [战斗系统] 正在执行刷本前准备...");
                    return NodeState.Success;
                }),
                // 跳跃
                    new ActionNode(() =>
                    {
                        _ = Jump(50);
                        return NodeState.Success;
                    }),
                    //等待一秒，等待角色跳跃稳定
                    new WaitNode(2000),
                    //检查物资情况
                    CreatePreFlightCheckNode(),
                    new WaitNode(100),
                    //智能吃喝
                    autoDrinkEatNode(),
                    //智能吃喝完要跳跃一下
                    new ActionNode(() =>
                    {
                        _ = Jump(50);
                        return NodeState.Success;
                    }),
                    //等待一秒，等待角色跳跃稳定
                    new WaitNode(2000),
                    //检查一下宝宝情况
                    CreatePetVetNode(),

                    new WaitNode(100),
                    //宝宝准备就绪，解散宝宝
                    //智能解散宝宝函数
                    CreateEnsureDismissPetNode(),
                    new WaitNode(100),
                    //所有准备已就绪，放行

                    //移动到等待区
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 259.38f, -26.56f, -2.56f))
                        {
                            Logger.Write($"[{CurrentPlayerName}] [战斗系统] 开始刷本...");
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    // 在起点安全区等待，扫描前方航线是否安全
                    new ActionNode(() =>
                    {
                        // 两个110的GUID（厄运北 戈多克蛮兵）
                        ulong guidA = 0xF130002CB1020E92;
                        ulong guidB = 0xF130002CB103026D;

                        // 将你马上要走的 3 个航点录入雷达阵列 (剔除高度 Z，做 2D 极速运算)
                        float[][] pathNodes = new float[][]
                        {
                            new float[] { 282.60f, -19.34f },
                            new float[] { 310.24f, -7.52f },
                            new float[] { 311.49f, 22.00f }
                        };

                        // 定义内部判定函数：航线智能雷达
                        bool IsMobSafeForPath(ulong guid)
                        {
                            int targetBase = GetTargetBaseByGuid(_hProcess, guid);

                            // 1. 内存中读不到这个怪，说明极远，对航线绝对安全！
                            if (targetBase == 0) return true;

                            // 2. 读取怪物三维与朝向数据
                            float mX = MemoryAPI.ReadFloat(_hProcess, targetBase + 0x9B8);
                            float mY = MemoryAPI.ReadFloat(_hProcess, targetBase + 0x9BC);
                            float mFacing = MemoryAPI.ReadFloat(_hProcess, targetBase + 0x9C4);

                            // 3. 核心：找出怪物距离我们整条预定航线中【最近】的一个危险点
                            float minDist = float.MaxValue;
                            float closestNodeX = 0;
                            float closestNodeY = 0;

                            foreach (var node in pathNodes)
                            {
                                float d = (float)Math.Sqrt(Math.Pow(mX - node[0], 2) + Math.Pow(mY - node[1], 2));
                                if (d < minDist)
                                {
                                    minDist = d;
                                    closestNodeX = node[0];
                                    closestNodeY = node[1];
                                }
                            }

                            // 4. 向量预测：怪物相对于这个【最近航点】的运动趋势
                            float vX = closestNodeX - mX; // 怪物指向航点的向量X
                            float vY = closestNodeY - mY; // 怪物指向航点的向量Y

                            float fX = (float)Math.Cos(mFacing); // 怪物实际面朝方向X
                            float fY = (float)Math.Sin(mFacing); // 怪物实际面朝方向Y

                            // 点乘结果判断朝向
                            float dot = (vX * fX) + (vY * fY);
                            bool isFacingPath = dot > 0; // 大于0说明怪物正朝我们的路线走来

                            // ========== 跑道准入判定 ==========

                            // 【禁行死区】：只要怪物离我们路线上的任意一点小于 20 码，绝对不能起步
                            // 20码比较极限，如果求稳可以改成 25 码
                            if (minDist < 20.0f) return false;

                            // 【黄金尾行/侧绕区】：（20码 ~ 35码之间）
                            // 如果怪物在航点附近，但正在背对航点走远（路让出来了），安全，发车！
                            // 如果它正朝航点走来，说明它马上要堵路，危险，原地憋住！
                            if (minDist >= 20.0f && minDist <= 35.0f)
                            {
                                return !isFacingPath; // !isFacingPath 代表正在远离
                            }

                            // 【绝对安全区】：（大于35码）
                            // 怪物距离我们的路线极其遥远，即使它现在回头，以猎人的移速早就跑过去了。
                            if (minDist > 35.0f) return true;

                            return false; // 兜底防错
                        }

                        // 联合判定：必须两只110【同时】不构成威胁，角色才起步冲刺
                        bool isSafeA = IsMobSafeForPath(guidA);
                        bool isSafeB = IsMobSafeForPath(guidB);

                        if (isSafeA && isSafeB)
                        {
                            Logger.Write($"[{CurrentPlayerName}] [战斗系统] 前方道路已清空！蛮兵正在远离预定航点，启动冲刺！");
                            return NodeState.Success; // 放行，交接给你下面的那三个 MoveTo Node
                        }

                        return NodeState.Running; // 怪物挡路中，继续在 (259, -26) 挂机死等
                    }),

                    //已经避开110
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 282.60f, -19.34f, -2.58f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 310.24f, -7.52f, -3.89f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 311.49f, 22.00f, -3.89f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),


                    // 跳墙节点
                    new ActionNode(() =>
                    {
                        // 获取当前坐标
                        float pX = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9B8);
                        float pY = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9BC);
                        float pZ = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9C0);

                        // 定义关键坐标点
                        float prepX = 311.49f, prepY = 22.00f, prepZ = -3.89f;     // 起跑准备点
                        float jumpX = 313.63f, jumpY = 19.71f;                     // 起跳触发点 (不查Z)
                        float endX = 316.04f, endY = 16.86f, endZ = -0.87f;        // 台子上最终目标

                        // ==========================================
                        // 阶段 1：大功告成判定
                        // ==========================================
                        // 如果 Z 轴大于 -1.5 (说明已经上去了)，并且距离终点不到 2 码，判定为跳跃成功！
                        if (pZ > -1.5f && Math.Sqrt(Math.Pow(pX - endX, 2) + Math.Pow(pY - endY, 2)) < 2.0f)
                        {
                            Logger.Write($"[{CurrentPlayerName}] [战斗系统] 成功登上高台！");
                            hasAttemptedJump = false; // 清空状态，为下次刷本复用做准备
                            return NodeState.Success; // 放行，继续接下来的打本逻辑
                        }

                        // ==========================================
                        // 阶段 2：失败回退重试判定
                        // ==========================================
                        // 如果已经起跳过了，且落地时间已超过 1.5 秒，但 Z 轴还是在下面 (< -2.0)，说明跳墙失败卡住了
                        if (hasAttemptedJump && (DateTime.Now - jumpTime).TotalSeconds > 1.5)
                        {
                            // 开始倒车，退回起跑准备点
                            if (MoveTo(_hProcess, _playerBase, prepX, prepY, prepZ, 0.5f))
                            {
                                // 成功退回起跑点后，踩刹车，重置跳跃状态，准备发起下一次冲刺！
                                StopMovement(_hProcess, _playerBase);
                                hasAttemptedJump = false;
                                Logger.Write($"[{CurrentPlayerName}] [战斗系统] 退回起跑点完毕，准备重新冲刺...");
                            }
                            else
                            {
                                // 还没退到位，持续倒车中
                                Logger.Write($"[{CurrentPlayerName}] [战斗系统] 跳墙失败！正在倒车回起跑点...");
                            }
                            return NodeState.Running;
                        }

                        // ==========================================
                        // 阶段 3：冲刺与起跳核心逻辑
                        // ==========================================
                        if (!hasAttemptedJump)
                        {
                            // 持续发送向台子上冲刺的指令
                            MoveTo(_hProcess, _playerBase, endX, endY, endZ, 0.5f);

                            // 计算当前位置与【起跳触发点】的 2D 距离
                            float distToJump = (float)Math.Sqrt(Math.Pow(pX - jumpX, 2) + Math.Pow(pY - jumpY, 2));

                            // 【核心优化】：只要距离起跳点小于 1.0 码（提前量），立刻起跳！
                            // 绝对不能用精确坐标判断，否则一定会因为帧率漏刷而撞墙
                            if (distToJump <= 1.0f)
                            {
                                Logger.Write($"[{CurrentPlayerName}] [战斗系统] 到达起跳区域，执行动态起跳！");
                                _ = Jump(50); // 调用你封装好的跳跃函数
                                hasAttemptedJump = true;
                                jumpTime = DateTime.Now;
                            }
                        }
                        else if ((DateTime.Now - jumpTime).TotalSeconds <= 1.5)
                        {
                            // 【空中微调补偿】：在空中的 1.5 秒内，疯狂向终点发送 MoveTo 指令。
                            // 防止 CTM(点击移动) 机制因为执行 Jump() 而断开，确保角色在空中有向前的物理动能！
                            MoveTo(_hProcess, _playerBase, endX, endY, endZ, 0.5f);
                        }

                        return NodeState.Running; // 整个动作未完成，阻塞行为树
                    }),

                    //已经上墙，继续第二轮
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 321.65f, 19.93f, -1.29f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 322.09f, 27.58f, -1.29f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 322.10f, 38.38f, -1.29f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 322.23f, 52.85f, -1.29f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 322.21f, 66.78f, -1.29f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 322.26f, 78.42f, -1.29f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 321.24f, 93.00f, -1.29f, 1.0f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 333.88f, 92.61f, -1.29f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    //准备第二轮双拉  进入等待区
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 350.80f, 92.76f, -1.29f))    //安全区，等待110  GUID: 0xF130002CB103026D 、0xF130002CB1020E95
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),


                    //这里等待110，
                    // 第一个110 0xF130002CB103026D  等待它离开我们的必经之路
                    // 第二个110 0xF130002CB1020E95  等待它离开起始坐标
                    // 第二个110 起始坐标 X: 369.056 Y: 179.377 Z: 2.849
                    // 只有两个条件满足才能放行
                    // 在此等待两只 110 让出道路
                    new ActionNode(() =>
                    {
                        // 获取玩家自身坐标
                        float pX = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9B8);
                        float pY = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9BC);

                        ulong guidA = 0xF130002CB103026D; // 第一个 110：要求离开必经之路
                        ulong guidB = 0xF130002CB1020E95; // 第二个 110：要求离开起始坐标
                        ulong guidC = 0xF130002CB40313E2; // 第三个 110：要求离开指定的后段航线

                        bool isSafeA = false;
                        bool isSafeB = false;
                        bool isSafeC = false;

                        // ==========================================
                        // 判定 1：第一个 110 (航线动态雷达)
                        // ==========================================
                        int targetBaseA = GetTargetBaseByGuid(_hProcess, guidA);
                        if (targetBaseA == 0)
                        {
                            isSafeA = true; // 读不到怪，离得很远，绝对安全
                        }
                        else
                        {
                            // 第一个 110 的预定航线
                            float[][] pathNodesA = new float[][]
                            {
                                new float[] { 354.57f, 110.55f },
                                new float[] { 363.11f, 124.18f }
                            };

                            float mX_A = MemoryAPI.ReadFloat(_hProcess, targetBaseA + 0x9B8);
                            float mY_A = MemoryAPI.ReadFloat(_hProcess, targetBaseA + 0x9BC);
                            float mFacing_A = MemoryAPI.ReadFloat(_hProcess, targetBaseA + 0x9C4);

                            float minDistA = float.MaxValue;
                            float closestX_A = 0;
                            float closestY_A = 0;

                            foreach (var node in pathNodesA)
                            {
                                float d = (float)Math.Sqrt(Math.Pow(mX_A - node[0], 2) + Math.Pow(mY_A - node[1], 2));
                                if (d < minDistA)
                                {
                                    minDistA = d;
                                    closestX_A = node[0];
                                    closestY_A = node[1];
                                }
                            }

                            float vX_A = closestX_A - mX_A;
                            float vY_A = closestY_A - mY_A;
                            float fX_A = (float)Math.Cos(mFacing_A);
                            float fY_A = (float)Math.Sin(mFacing_A);
                            float dotA = (vX_A * fX_A) + (vY_A * fY_A);
                            bool isFacingPathA = dotA > 0;

                            if (minDistA < 20.0f) isSafeA = false;
                            else if (minDistA >= 20.0f && minDistA <= 35.0f) isSafeA = !isFacingPathA;
                            else isSafeA = true;
                        }

                        // ==========================================
                        // 判定 2：第二个 110 (锚点距离检测)
                        // ==========================================
                        int targetBaseB = GetTargetBaseByGuid(_hProcess, guidB);
                        if (targetBaseB == 0)
                        {
                            isSafeB = true;
                        }
                        else
                        {
                            float mX_B = MemoryAPI.ReadFloat(_hProcess, targetBaseB + 0x9B8);
                            float mY_B = MemoryAPI.ReadFloat(_hProcess, targetBaseB + 0x9BC);

                            // 第二个 110 的核心起始坐标
                            float startX_B = 369.056f;
                            float startY_B = 179.377f;

                            float distFromStartB = (float)Math.Sqrt(Math.Pow(mX_B - startX_B, 2) + Math.Pow(mY_B - startY_B, 2));

                            if (distFromStartB > 25.0f) isSafeB = true;
                            else isSafeB = false;
                        }

                        // ==========================================
                        // 判定 3：第三个 110 (航线动态雷达)
                        // ==========================================
                        int targetBaseC = GetTargetBaseByGuid(_hProcess, guidC);
                        if (targetBaseC == 0)
                        {
                            isSafeC = true; // 读不到怪，离得很远，绝对安全
                        }
                        else
                        {
                            // 第三个 110 的预定航线 (你的新坐标)
                            float[][] pathNodesC = new float[][]
                            {
                                new float[] { 361.46f, 167.32f },
                                new float[] { 381.90f, 168.28f },
                                new float[] { 384.89f, 185.50f }
                            };

                            float mX_C = MemoryAPI.ReadFloat(_hProcess, targetBaseC + 0x9B8);
                            float mY_C = MemoryAPI.ReadFloat(_hProcess, targetBaseC + 0x9BC);
                            float mFacing_C = MemoryAPI.ReadFloat(_hProcess, targetBaseC + 0x9C4);

                            float minDistC = float.MaxValue;
                            float closestX_C = 0;
                            float closestY_C = 0;

                            foreach (var node in pathNodesC)
                            {
                                float d = (float)Math.Sqrt(Math.Pow(mX_C - node[0], 2) + Math.Pow(mY_C - node[1], 2));
                                if (d < minDistC)
                                {
                                    minDistC = d;
                                    closestX_C = node[0];
                                    closestY_C = node[1];
                                }
                            }

                            float vX_C = closestX_C - mX_C;
                            float vY_C = closestY_C - mY_C;
                            float fX_C = (float)Math.Cos(mFacing_C);
                            float fY_C = (float)Math.Sin(mFacing_C);
                            float dotC = (vX_C * fX_C) + (vY_C * fY_C);
                            bool isFacingPathC = dotC > 0;

                            // 第三个 110 的航线准入判定
                            if (minDistC < 20.0f) isSafeC = false; // 离航线太近，危险
                            else if (minDistC >= 20.0f && minDistC <= 35.0f) isSafeC = !isFacingPathC; // 背对航线安全
                            else isSafeC = true; // 超过 35 码，安全
                        }

                        // ==========================================
                        // 终极联合放行
                        // ==========================================
                        if (isSafeA && isSafeB && isSafeC)
                        {
                            Logger.Write($"[{CurrentPlayerName}] [战斗系统] 道路清空！A已离开航线，B已离开锚点，C已离开后段航线，全速通过！");
                            return NodeState.Success;
                        }

                        // 只要有一个怪挡路，就在当前安全区继续挂机
                        return NodeState.Running;
                    }),

                    //已经避开110，前往第二轮双拉点
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 355.23f, 92.80f, -1.29f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 355.57f, 98.34f, -1.29f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 359.08f, 112.66f, -3.89f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    //召唤宝宝把怪拉走
                    //假如当前宝宝 一定是没有召唤出来的，也要来个检测
                    // 如果没有激活的宠物

                    // 召唤宝宝、复活宝宝、投喂烤鹌鹑... 一行代码搞定所有繁琐工序！
                    CreatePetVetNode(),

                    //让宝宝去攻击GUID: 0xF130002CB1020EAE，不一定要攻击，只要宝宝进战斗引到这波怪就可以，
                    //然后再让宝宝去攻击GUID: 0xF130002CB4020EB7，此时就不用管宝宝了
                    //角色按照流程继续下一步寻路，此时宝宝会因为与猎人距离太远而消失，但是我们已经走到安全位置了
                    //然后进行一次假死，本轮战斗就结束了

                    //开始第二轮双拉
                    new ActionNode(() =>
                    {
                        int petBase = GetPetBase(_hProcess);
                        if (petBase == 0) return NodeState.Failure; // 兜底：宝宝如果没了报错打断

                        // 阶段 0：切被动，锁目标 A，派宝宝咬
                        if (pullStep_1 == 0)
                        {
                            Logger.Write($"[{CurrentPlayerName}] [战斗系统] 锁定第一只 110 (0xF130002CB1020EAE)，指派宠物前往攻击...");

                            Select_Target(_hProcess, 0xF130002CB1020EAE);

                            // 第一次绝对安全，直接切被动并攻击
                            ExecuteDynamicLua("PetAttack()");

                            pullTimer_1 = DateTime.Now;
                            pullStep_1 = 1;
                            return NodeState.Running;
                        }

                        // 阶段 1：等待宠物进入战斗状态
                        if (pullStep_1 == 1)
                        {
                            // 10 秒没进战，重置
                            if ((DateTime.Now - pullTimer_1).TotalSeconds > 10)
                            {
                                pullStep_1 = 0;
                                return NodeState.Running;
                            }

                            if (Pet_Behavioral_State(_hProcess, petBase, 19))
                            {
                                Logger.Write($"[{CurrentPlayerName}] [战斗系统] 宠物已成功引到第一只怪的仇恨！强制召回宠物打断战斗锁定...");

                                // 【核心修复 1】必须先强行打断宠物的攻击锁定，否则后续指令容易被引擎丢弃！
                                ExecuteDynamicLua("PetPassiveMode();");
                                pullTimer_1 = DateTime.Now;
                                pullStep_1 = 2;
                            }
                            return NodeState.Running;
                        }

                        // 阶段 2：等待宠物被动指令生效，切换新目标
                        if (pullStep_1 == 2)
                        {
                            // 给被动指令 100 毫秒的时间生效
                            if ((DateTime.Now - pullTimer_1).TotalMilliseconds >= 100)
                            {
                                Logger.Write($"[{CurrentPlayerName}] [战斗系统] 目标切换至第二只 110 (0xF130002CB4020EB7)...");

                                // 写入新目标 GUID
                                Select_Target(_hProcess, 0xF130002CB4020EB7);

                                pullTimer_1 = DateTime.Now;
                                pullStep_1 = 3;
                            }
                            return NodeState.Running;
                        }

                        // 阶段 3：等待 LUA 引擎同步目标，下达转火指令
                        if (pullStep_1 == 3)
                        {
                            // 【核心修复 2】给客户端 150 毫秒的时间，确保底层 Lua 引擎的 "target" 已刷新
                            if ((DateTime.Now - pullTimer_1).TotalMilliseconds >= 150)
                            {
                                Logger.Write($"[{CurrentPlayerName}] [战斗系统] 目标缓冲完毕，重新发送攻击指令！猎人准备起跑！");

                                ExecuteDynamicLua("PetAttack()");
                                pullTimer_1 = DateTime.Now;
                                pullStep_1 = 4; // 进入收尾放行区
                            }
                            return NodeState.Running;
                        }

                        // 阶段 4：动作缓冲放行
                        if (pullStep_1 == 4)
                        {
                            // 留 100 毫秒让攻击动作在服务器生效，然后猎人再开跑
                            if ((DateTime.Now - pullTimer_1).TotalMilliseconds >= 100)
                            {
                                pullStep_1 = 0; // 重置变量，为下次跑本复用做准备
                                return NodeState.Success; // 完美放行，猎人开跑！
                            }
                            return NodeState.Running;
                        }

                        return NodeState.Running;
                    }),

                    //跑向第三轮假死点
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 368.10f, 127.04f, -3.89f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 375.90f, 141.48f, -0.80f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 375.40f, 151.48f, 3.51f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 379.86f, 174.94f, 2.87f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 379.06f, 187.57f, 7.73f, 0.5f))
                        {
                            StopMovement(_hProcess, _playerBase);
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    new WaitNode(500),
                    //执行假死 + 不起来
                    CreateFeignDeathNode(),

                    //准备第三轮  拿到卫兵芬古斯宝箱开门钥匙
                    // ==========================================
                    // 终极阶段 0 ~ 4：八大恶人黑名单 & 双走廊放行法
                    // ==========================================
                    // 内存级假死 CD 监控集成完毕。
                    // 原有的手动计时器 (standUpTimer_1) 已被彻底剥离，逻辑直通底层内存链表，零延迟、零误差。

                    new ActionNode(() =>
                    {
                        ulong[] threatGuids = new ulong[]
                        {
                            0xF130002CB40313E2, // 大法师
                            0xF1300037F101499B, // 卫兵芬古斯
                            0xF130002CB1031393, // 内圈1
                            0xF130002CB1031395, // 内圈2
                            0xF130002CB103138F, // 外圈1
                            0xF130002CB1020EB3, // 外圈2
                            0xF130002CB1031391, // 外圈3
                            0xF130002CB1020EA5  // 外圈4
                        };

                        bool shouldLog = (DateTime.Now - logTimer).TotalSeconds > 1.0;
                        if (shouldLog) logTimer = DateTime.Now;

                        // 点到线段测距
                        float GetMinDistToPath(float mX, float mY, float[][] path)
                        {
                            float minDist = 9999f;
                            for (int i = 0; i < path.Length - 1; i++)
                            {
                                float x1 = path[i][0], y1 = path[i][1], x2 = path[i + 1][0], y2 = path[i + 1][1];
                                float l2 = (x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1);
                                float dist;
                                if (l2 == 0) dist = (float)Math.Sqrt((mX - x1) * (mX - x1) + (mY - y1) * (mY - y1));
                                else
                                {
                                    float t = Math.Max(0, Math.Min(1, ((mX - x1) * (x2 - x1) + (mY - y1) * (y2 - y1)) / l2));
                                    dist = (float)Math.Sqrt(Math.Pow(mX - (x1 + t * (x2 - x1)), 2) + Math.Pow(mY - (y1 + t * (y2 - y1)), 2));
                                }
                                if (dist < minDist) minDist = dist;
                            }
                            return minDist;
                        }

                        // ==========================================
                        // 步骤 0：南门雷达死等
                        // ==========================================
                        if (chestStep == 0)
                        {
                            bool isApproachSafe = true;

                            foreach (ulong guid in threatGuids)
                            {
                                int tBase = GetTargetBaseByGuid(_hProcess, guid);
                                if (tBase != 0 && MemoryAPI.ReadInteger(_hProcess, MemoryAPI.ReadInteger(_hProcess, tBase + 0x08) + 0x58) > 0)
                                {
                                    float mX = MemoryAPI.ReadFloat(_hProcess, tBase + 0x9B8);
                                    float mY = MemoryAPI.ReadFloat(_hProcess, tBase + 0x9BC);

                                    // 🚨 特判 1：大法师 (防踩起点)
                                    if (guid == 0xF130002CB40313E2)
                                    {
                                        float distToStart = (float)Math.Sqrt(Math.Pow(mX - 379.06f, 2) + Math.Pow(mY - 187.57f, 2));
                                        if (distToStart < 25.0f)
                                        {
                                            isApproachSafe = false;
                                            if (shouldLog) Logger.Write($"[{CurrentPlayerName}] [战斗系统] 大法师封锁起点 (距 {distToStart:F1} 码)，严禁起立！");
                                        }
                                    }
                                    // 🚨 特判 2：卫兵芬古斯 (智能矢量预判！)
                                    else if (guid == 0xF1300037F101499B)
                                    {
                                        // 宝箱中心坐标
                                        float chestX = 380.20f, chestY = 258.03f;
                                        float distToChest = (float)Math.Sqrt(Math.Pow(mX - chestX, 2) + Math.Pow(mY - chestY, 2));

                                        bool isApproachingChest = distToChest < lastFengusDist - 0.2f;
                                        lastFengusDist = distToChest;

                                        // 芬古斯的 WP1 和 WP9 离宝箱差不多 65 码。
                                        // 如果他在 70 码内，且正在靠近宝箱，说明他进入了“冲向宝箱”的航线，绝对不能放行！
                                        if (isApproachingChest && distToChest < 70.0f)
                                        {
                                            isApproachSafe = false;
                                            if (shouldLog) Logger.Write($"[{CurrentPlayerName}] [战斗系统] 卫兵芬古斯正朝宝箱走来 (距宝箱 {distToChest:F1} 码)！绝对封锁！");
                                        }
                                        // 如果他虽然离宝箱很近，但是“正在远离”，说明他刚开完箱子转身，安全！
                                    }
                                    // 🚨 通用判定：其他小怪
                                    else
                                    {
                                        float distToPath = GetMinDistToPath(mX, mY, approachPath);
                                        if (distToPath < 20.0f)
                                        {
                                            isApproachSafe = false;
                                            if (shouldLog) Logger.Write($"[{CurrentPlayerName}] [战斗系统] 内/外圈巡逻怪封锁路线 (距路线 {distToPath:F1} 码)");
                                        }
                                    }
                                }
                            }

                            if (isApproachSafe)
                            {
                                Logger.Write($"[{CurrentPlayerName}] [战斗系统] 进场航道极其干净！大卫兵芬古斯不在威胁圈，起立冲锋！");
                                wpIndex = 0;
                                chestStep = 1;
                            }
                            return NodeState.Running;
                        }

                        // ==========================================
                        // 步骤 1：冲向宝箱
                        // ==========================================
                        if (chestStep == 1)
                        {
                            var target = approachPath[wpIndex];
                            if (MoveTo(_hProcess, _playerBase, target[0], target[1], target[2]))
                            {
                                wpIndex++;
                                if (wpIndex >= approachPath.Length)
                                {
                                    StopMovement(_hProcess, _playerBase);
                                    chestStep = 2;
                                }
                            }
                            return NodeState.Running;
                        }

                        // ==========================================
                        // 步骤 2：开宝箱，等待读条完毕，节点完美收官！
                        // ==========================================
                        if (chestStep == 2)
                        {
                            uint boxBase = GetGameObjectBaseByEntry(_hProcess, 179516);
                            if (boxBase != 0)
                            {
                                RightClickGameObject(boxBase);
                                chestTimer = DateTime.Now;
                                chestStep = 3;
                            }
                            else return NodeState.Success;
                            return NodeState.Running;
                        }

                        if (chestStep == 3)
                        {
                            // 读条 5.5 秒保护
                            if ((DateTime.Now - chestTimer).TotalSeconds < 5.5) return NodeState.Running;

                            Logger.Write($"[{CurrentPlayerName}] [战斗系统] 宝箱钥匙已入手！准备启动隐形跑路程序！");

                            // 重要：重置状态，以防跑尸重新执行
                            chestStep = 0;

                            // 返回 Success，让 SequenceNode 推进到下一个喝药节点！
                            return NodeState.Success;
                        }

                        return NodeState.Running;
                    }),

                    new WaitNode(100),
                    // 2. 原地装死等药水 CD -> 喝隐形药水 -> 确认身上挂上隐身 Buff
                    CreateUseInvisibilityPotionNode(),
                    new WaitNode(100),

                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 382.42f, 273.89f, 12.23f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 383.82f, 298.87f, 11.20f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 383.76f, 314.08f, 11.22f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 385.04f, 348.04f, 2.85f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 385.48f, 371.83f, -0.73f, 0.5f))
                        {
                            StopMovement(_hProcess, _playerBase);
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    new WaitNode(1000),
                    // 戈多克庭院大门 ID 177219
                    new ActionNode(() =>
                    {
                        // 1. 获取门的动态基址
                        uint doorBase = GetGameObjectBaseByEntry(_hProcess, 177219);

                        if (doorBase != 0)
                        {
                            // 2. 状态探测：门是否已经开启？
                            if (IsGameObjectOpened(_hProcess, doorBase))
                            {
                                Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [{CurrentPlayerName}] [门禁系统] 门禁系统验证通过！戈多克庭院大门已开启，直接进入！");
                                return NodeState.Success; // 只有这里返回 Success，行为树才会走向下一步
                            }

                            // 3. 冷却探测：如果当前时间还没超过设定的 _GodekGateTime，说明正在读条或等待服务器判定
                            if (DateTime.Now < _GodekGateTime)
                            {
                                return NodeState.Running; // 什么都不做，静静等待
                            }

                            // 4. 门没开，且不在冷却中 -> 执行炸门动作！
                            ulong doorGuid = MemoryAPI.ReadQword(_hProcess, (int)(doorBase + 0x30)); // 精准读取 GUID

                            RightClickGameObject(doorBase);   //右键GameObject

                            // 5. 设定 5.5 秒的冷却期 (施法读条时间 + 服务器判定延迟)
                            _GodekGateTime = DateTime.Now.AddSeconds(5.5);

                            Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [{CurrentPlayerName}] [门禁系统] 成功向戈多克庭院大门 (0x{doorBase:X}) 发送了 1 次右键 Call！");

                            return NodeState.Running; // 返回 Running，让行为树下一帧继续来检查门开了没
                        }

                        // 连基址都没找到，说明距离太远，保持 Running 让前面的寻路逻辑继续靠近
                        Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [{CurrentPlayerName}] [门禁系统] 没找到戈多克庭院大门，请确认你在戈多克庭院大门 3 码以内");
                        return NodeState.Running;
                    }),

                    new WaitNode(100),

                    //使用召唤宝宝函数节点
                    CreatePetVetNode(),

                    //给DLL 100毫秒反应时间
                    new WaitNode(100),

                    //调整宝宝为被动型
                    new ActionNode(() =>
                    {
                        ExecuteDynamicLua("PetPassiveMode()");
                        return NodeState.Success;

                    }),

                    //给DLL 100毫秒反应时间
                    new WaitNode(100),

                    //智能吃喝
                    autoDrinkEatNode(),

                    new WaitNode(100),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 384.24f, 392.18f, -1.68f, 0.5f))
                        {
                            StopMovement(_hProcess, _playerBase);
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    //扫描"游荡的基尔罗格之眼" 坐标 0xF13000383204956A  移动速度很快
                    //一但到达射程范围8-41码内，施放奥术射击+命令宝宝攻击
                    //如果在8码内就施放近战攻击(猛禽一击)+命令宝宝攻击

                    new ActionNode(() =>
                    {
                        ulong eyeGuid = 0xF13000383204956A; // 游荡的基尔罗格之眼

                        // ==========================================
                        // 阶段 0：雷达全图扫视，静默等待进圈
                        // ==========================================
                        if (eyeStep_1 == 0)
                        {
                            int eyeBase = GetTargetBaseByGuid(_hProcess, eyeGuid);
                            if (eyeBase == 0) return NodeState.Running;

                            // 读取猎人与眼睛的坐标
                            float pX = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9B8);
                            float pY = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9BC);
                            float pZ = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9C0);
                            float mX = MemoryAPI.ReadFloat(_hProcess, eyeBase + 0x9B8);
                            float mY = MemoryAPI.ReadFloat(_hProcess, eyeBase + 0x9BC);
                            float mZ = MemoryAPI.ReadFloat(_hProcess, eyeBase + 0x9C0);

                            float dist = (float)Math.Sqrt(Math.Pow(mX - pX, 2) + Math.Pow(mY - pY, 2) + Math.Pow(mZ - pZ, 2));

                            if (dist <= 41.0f)
                            {
                                Logger.Write($"[{CurrentPlayerName}] [战斗系统] 警告：基尔罗格之眼进入检测范围！(距离 {dist:F1} 码)，目标已锁定！");
                                Select_Target(_hProcess, eyeGuid);

                                eyeTimer_1 = DateTime.Now;
                                eyeStep_1 = 1;
                            }
                            return NodeState.Running;
                        }

                        // ==========================================
                        // 阶段 1：锁定缓冲、追击与开火判定
                        // ==========================================
                        if (eyeStep_1 == 1)
                        {
                            if ((DateTime.Now - eyeTimer_1).TotalMilliseconds >= 150)
                            {
                                int eyeBase = GetTargetBaseByGuid(_hProcess, eyeGuid);
                                if (eyeBase == 0) { eyeStep_1 = 0; return NodeState.Running; }

                                float pX = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9B8);
                                float pY = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9BC);
                                float pZ = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9C0); // 拿玩家的 Z 轴，很关键
                                float mX = MemoryAPI.ReadFloat(_hProcess, eyeBase + 0x9B8);
                                float mY = MemoryAPI.ReadFloat(_hProcess, eyeBase + 0x9BC);

                                float dist = (float)Math.Sqrt(Math.Pow(mX - pX, 2) + Math.Pow(mY - pY, 2));

                                if (dist >= 8.0f && dist <= 41.0f)
                                {
                                    Logger.Write($"[{CurrentPlayerName}] [战斗系统] 距离 {dist:F1} 码，符合远程交战条件！发射奥术射击！");
                                    ExecuteDynamicLua("CastSpellByName('奥术射击'); PetAttack();");
                                    isMelee_1 = false;
                                }
                                else if (dist < 8.0f)
                                {
                                    Logger.Write($"[{CurrentPlayerName}] [战斗系统] 距离 {dist:F1} 码，进入盲区！全速追击并施放猛禽一击！");
                                    // 【核心优化】：MoveTo 追击。注意 Z 轴必须用 pZ(猎人所在的地面高度)
                                    // 绝对不能用 mZ(眼睛的高度)，否则底层 CTM 会想往天上走导致寻路失效死机！
                                    MoveTo(_hProcess, _playerBase, mX, mY, pZ, 1.5f);
                                    ExecuteDynamicLua("CastSpellByName('猛禽一击'); PetAttack();");
                                    isMelee_1 = true;
                                }
                                else
                                {
                                    eyeStep_1 = 0;
                                    return NodeState.Running;
                                }

                                eyeTimer_1 = DateTime.Now;
                                eyeStep_1 = 2;
                            }
                            return NodeState.Running;
                        }

                        // ==========================================
                        // 阶段 2：底层冷却验证 & 近战死缠烂打
                        // ==========================================
                        if (eyeStep_1 == 2)
                        {
                            if ((DateTime.Now - eyeTimer_1).TotalMilliseconds >= 400)
                            {
                                int eyeBase = GetTargetBaseByGuid(_hProcess, eyeGuid);

                                // 1. 如果眼睛被秒杀了
                                if (eyeBase == 0 || MemoryAPI.ReadInteger(_hProcess, MemoryAPI.ReadInteger(_hProcess, eyeBase + 0x08) + 0x58) <= 0)
                                {
                                    Logger.Write($"[{CurrentPlayerName}] [战斗系统] 目标被秒杀！继续前进。");
                                    StopMovement(_hProcess, _playerBase); // 刹车，防止猎人往最后一次追击的坐标滑行
                                    eyeStep_1 = 0;
                                    return NodeState.Success;
                                }

                                // 2. 如果没死，分远程和近战处理
                                if (!isMelee_1)
                                {
                                    if (IsSpellOnCooldown(_hProcess, 3044))
                                    {
                                        Logger.Write($"[{CurrentPlayerName}] [战斗系统] 确认开火！奥术射击已进入CD。交由自动射击和宝宝收尾！");
                                        eyeStep_1 = 3;
                                    }
                                    else
                                    {
                                        Logger.Write($"[{CurrentPlayerName}] [战斗系统] 警告：卡视野或距离异常，技能释放失败！重新尝试锁定开火...");
                                        eyeTimer_1 = DateTime.Now;
                                        eyeStep_1 = 1;
                                    }
                                }
                                else
                                {
                                    // 【核心优化】：近战如果没砍死，直接退回阶段 1，它会疯狂刷新眼睛的实时坐标并跟上去砍！
                                    Logger.Write($"[{CurrentPlayerName}] [战斗系统] 警告：近战未击杀！继续贴脸死缠烂打！");
                                    eyeTimer_1 = DateTime.Now;
                                    eyeStep_1 = 1;
                                }
                            }
                            return NodeState.Running;
                        }

                        // ==========================================
                        // 阶段 3：站桩等待目标摧毁
                        // ==========================================
                        if (eyeStep_1 == 3)
                        {
                            int eyeBase = GetTargetBaseByGuid(_hProcess, eyeGuid);

                            if (eyeBase == 0 || MemoryAPI.ReadInteger(_hProcess, MemoryAPI.ReadInteger(_hProcess, eyeBase + 0x08) + 0x58) <= 0)
                            {
                                Logger.Write($"[{CurrentPlayerName}] [战斗系统] 目标已彻底摧毁！放行！");
                                StopMovement(_hProcess, _playerBase); // 刹车
                                eyeStep_1 = 0;
                                return NodeState.Success;
                            }

                            return NodeState.Running;
                        }

                        return NodeState.Running;
                    }),

                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 384.24f, 392.18f, -1.68f, 0.5f))
                        {
                            StopMovement(_hProcess, _playerBase);
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    new WaitNode(100),
                    //使用解散宝宝函数
                    CreateEnsureDismissPetNode(),

                    new WaitNode(100),

                    //使用召唤宝宝函数节点
                    CreatePetVetNode(),

                    //给DLL 100毫秒反应时间
                    new WaitNode(100),

                    //调整宝宝为被动型
                    new ActionNode(() =>
                    {
                        ExecuteDynamicLua("PetPassiveMode()");
                        return NodeState.Success;

                    }),

                    //给DLL 100毫秒反应时间
                    new WaitNode(100),

                    //原地定住宝宝
                    new ActionNode(() =>
                    {
                        ExecuteDynamicLua("PetWait()");
                        return NodeState.Success;

                    }),

                    //给DLL 100毫秒反应时间
                    new WaitNode(100),

                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 387.28f, 412.43f, -1.68f))
                        {
                            //StopMovement(_hProcess, _playerBase);
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),


                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 390.33f, 427.03f, -3.90f, 0.5f))
                        {
                            StopMovement(_hProcess, _playerBase);
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    //已经到达拉怪点

                    new ActionNode(() =>
                    {
                        int petBase = GetPetBase(_hProcess);
                        if (petBase == 0) return NodeState.Failure; // 兜底：宝宝如果没了报错打断

                        // 阶段 0：锁目标 A，派宝宝咬
                        if (pullStep_1 == 0)
                        {
                            Logger.Write($"[{CurrentPlayerName}] [战斗系统] 锁定怪物 (0xF130002CBA049D1C) 指派宠物前往攻击...");

                            Select_Target(_hProcess, 0xF130002CBA049D1C);

                            // 第一次绝对安全，直接攻击
                            ExecuteDynamicLua("PetAttack()");

                            pullTimer_1 = DateTime.Now;
                            pullStep_1 = 1;
                            return NodeState.Running;
                        }

                        // 阶段 1：等待宠物进入战斗状态
                        if (pullStep_1 == 1)
                        {
                            // 15 秒没进战，重置
                            if ((DateTime.Now - pullTimer_1).TotalSeconds > 15)
                            {
                                pullStep_1 = 0;
                                return NodeState.Running;
                            }

                            if (Pet_Behavioral_State(_hProcess, petBase, 19))
                            {
                                Logger.Write($"[{CurrentPlayerName}] [战斗系统] 宠物已成功引到 (0xF130002CBA049D1C) 的仇恨！猎人准备起跑！");
                                ExecuteDynamicLua("PetPassiveMode()");
                                pullTimer_1 = DateTime.Now;
                                pullStep_1 = 2;
                            }
                            return NodeState.Running;
                        }

                        // 阶段 4：动作缓冲放行
                        if (pullStep_1 == 2)
                        {
                            // 留 100 毫秒让攻击动作在服务器生效，然后猎人再开跑
                            if ((DateTime.Now - pullTimer_1).TotalMilliseconds >= 100)
                            {
                                pullStep_1 = 0; // 重置变量，为下次跑本复用做准备
                                return NodeState.Success; // 完美放行，猎人开跑！
                            }
                            return NodeState.Running;
                        }
                        return NodeState.Running;
                    }),

                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 393.90f, 448.21f, -7.23f))
                        {
                            //StopMovement(_hProcess, _playerBase);
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 395.60f, 471.90f, -7.23f))
                        {
                            //StopMovement(_hProcess, _playerBase);
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 395.18f, 484.41f, -7.58f))
                        {
                            //StopMovement(_hProcess, _playerBase);
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 397.97f, 497.91f, -11.63f))
                        {
                            //StopMovement(_hProcess, _playerBase);
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 402.44f, 505.99f, -12.79f))
                        {
                            //StopMovement(_hProcess, _playerBase);
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 406.82f, 506.61f, -12.78f, 0.5f))
                        {
                            StopMovement(_hProcess, _playerBase);
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    //假死函数
                    CreateFeignDeathNode(),

                    //使用召唤宝宝函数节点
                    CreatePetVetNode(),

                    //给DLL 100毫秒反应时间
                    new WaitNode(100),

                    //调整宝宝为被动型
                    new ActionNode(() =>
                    {
                        ExecuteDynamicLua("PetPassiveMode()");
                        return NodeState.Success;

                    }),

                    //给DLL 100毫秒反应时间
                    new WaitNode(100),

                    //原地定住宝宝
                    new ActionNode(() =>
                    {
                        ExecuteDynamicLua("PetWait()");
                        return NodeState.Success;

                    }),

                    //给DLL 100毫秒反应时间
                    new WaitNode(100),

                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 407.00f, 513.90f, -12.79f))
                        {
                            //StopMovement(_hProcess, _playerBase);
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    //等待假死冷却
                    CreateWaitForFeignDeathNode(),

                    //已经到达拉怪点

                    new ActionNode(() =>
                    {
                        int petBase = GetPetBase(_hProcess);
                        if (petBase == 0) return NodeState.Failure; // 兜底：宝宝如果没了报错打断

                        // 阶段 0：锁目标 A，派宝宝咬
                        if (pullStep_1 == 0)
                        {
                            Logger.Write($"[{CurrentPlayerName}] [战斗系统] 锁定怪物 (0xF130002CBA049D1D) 指派宠物前往攻击...");

                            Select_Target(_hProcess, 0xF130002CBA049D1D);

                            // 第一次绝对安全，直接并攻击
                            ExecuteDynamicLua("PetAttack()");
                            pullTimer_1 = DateTime.Now;
                            pullStep_1 = 1;
                            return NodeState.Running;
                        }

                        // 阶段 1：等待宠物进入战斗状态
                        if (pullStep_1 == 1)
                        {
                            // 15 秒没进战，重置
                            if ((DateTime.Now - pullTimer_1).TotalSeconds > 15)
                            {
                                pullStep_1 = 0;
                                return NodeState.Running;
                            }

                            if (Pet_Behavioral_State(_hProcess, petBase, 19))
                            {
                                Logger.Write($"[{CurrentPlayerName}] [战斗系统] 宠物已成功引到 (0xF130002CBA049D1D) 的仇恨！猎人准备起跑！");

                                ExecuteDynamicLua("PetPassiveMode()");
                                pullTimer_1 = DateTime.Now;
                                pullStep_1 = 2;
                            }
                            return NodeState.Running;
                        }

                        // 阶段 2：动作缓冲放行
                        if (pullStep_1 == 2)
                        {
                            // 留 100 毫秒让攻击动作在服务器生效，然后猎人再开跑
                            if ((DateTime.Now - pullTimer_1).TotalMilliseconds >= 100)
                            {
                                pullStep_1 = 0; // 重置变量，为下次跑本复用做准备
                                return NodeState.Success; // 完美放行，猎人开跑！
                            }
                            return NodeState.Running;
                        }
                        return NodeState.Running;
                    }),



                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 408.55f, 522.57f, -14.02f))
                        {
                            //StopMovement(_hProcess, _playerBase);
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 416.90f, 533.85f, -18.34f))
                        {
                            //StopMovement(_hProcess, _playerBase);
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 424.88f, 540.94f, -18.34f))
                        {
                            //StopMovement(_hProcess, _playerBase);
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 442.37f, 541.04f, -20.43f))
                        {
                            //StopMovement(_hProcess, _playerBase);
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 457.47f, 542.86f, -23.90f, 0.5f))
                        {
                            StopMovement(_hProcess, _playerBase);
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    //假死函数
                    CreateFeignDeathNode(),

                    //使用召唤宝宝函数节点
                    CreatePetVetNode(),

                    //给DLL 100毫秒反应时间
                    new WaitNode(100),

                    //调整宝宝为被动型
                    new ActionNode(() =>
                    {
                        ExecuteDynamicLua("PetPassiveMode()");
                        return NodeState.Success;

                    }),

                    //给DLL 100毫秒反应时间
                    new WaitNode(100),

                    //原地定住宝宝
                    new ActionNode(() =>
                    {
                        ExecuteDynamicLua("PetWait()");
                        return NodeState.Success;

                    }),

                    //给DLL 100毫秒反应时间
                    new WaitNode(100),

                    //等待假死冷却
                    CreateWaitForFeignDeathNode(),

                    //已经到达拉怪点

                    new ActionNode(() =>
                    {
                        int petBase = GetPetBase(_hProcess);
                        if (petBase == 0) return NodeState.Failure; // 兜底：宝宝如果没了报错打断

                        // 阶段 0：锁目标 A，派宝宝咬
                        if (pullStep_1 == 0)
                        {
                            Logger.Write($"[{CurrentPlayerName}] [战斗系统] 锁定怪物 (0xF1300033680496AC) 指派宠物前往攻击...");

                            Select_Target(_hProcess, 0xF1300033680496AC);

                            // 第一次绝对安全，直接并攻击
                            ExecuteDynamicLua("PetAttack()");
                            pullTimer_1 = DateTime.Now;
                            pullStep_1 = 1;
                            return NodeState.Running;
                        }

                        // 阶段 1：等待宠物进入战斗状态
                        if (pullStep_1 == 1)
                        {
                            // 15 秒没进战，重置
                            if ((DateTime.Now - pullTimer_1).TotalSeconds > 15)
                            {
                                pullStep_1 = 0;
                                return NodeState.Running;
                            }

                            if (Pet_Behavioral_State(_hProcess, petBase, 19))
                            {
                                Logger.Write($"[{CurrentPlayerName}] [战斗系统] 宠物已成功引到 (0xF1300033680496AC) 的仇恨！猎人准备起跑！");

                                ExecuteDynamicLua("PetPassiveMode()");
                                pullTimer_1 = DateTime.Now;
                                pullStep_1 = 2;
                            }
                            return NodeState.Running;
                        }

                        // 阶段 2：动作缓冲放行
                        if (pullStep_1 == 2)
                        {
                            // 留 100 毫秒让攻击动作在服务器生效，然后猎人再开跑
                            if ((DateTime.Now - pullTimer_1).TotalMilliseconds >= 100)
                            {
                                pullStep_1 = 0; // 重置变量，为下次跑本复用做准备
                                return NodeState.Success; // 完美放行，猎人开跑！
                            }
                            return NodeState.Running;
                        }
                        return NodeState.Running;
                    }),



                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 469.72f, 545.27f, -25.27f))
                        {
                            //StopMovement(_hProcess, _playerBase);
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 480.06f, 552.04f, -25.38f))
                        {
                            //StopMovement(_hProcess, _playerBase);
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 500.12f, 563.29f, -25.40f))
                        {
                            //StopMovement(_hProcess, _playerBase);
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 515.03f, 563.99f, -25.40f))
                        {
                            //StopMovement(_hProcess, _playerBase);
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 527.23f, 564.47f, -25.40f, 0.5f))
                        {
                            StopMovement(_hProcess, _playerBase);
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    //假死函数
                    CreateFeignDeathNode(),

                    new ActionNode(() =>
                    {
                        //跳跃起来，让假死进入cd状态
                        _ = Jump(50);
                        return NodeState.Success;
                    }),

                    new WaitNode(100),

                    //等待假死冷却
                    CreateWaitForFeignDeathNode(),

                    // ==========================================
                    // 卫兵斯里基克 (Slip'kik) 全航线贴地扫描雷达
                    // ==========================================
                    new ActionNode(() =>
                    {
                        ulong slipkikGuid = 0xF1300037F301499A;
                        int tBase = GetTargetBaseByGuid(_hProcess, slipkikGuid);

                        // 如果内存里根本没读到斯里基克，说明他走得极其遥远（出了视野），绝对安全！
                        if (tBase == 0)
                        {
                            Logger.Write($"[{CurrentPlayerName}] [斯里基克雷达] 视野清空，未发现Boss踪影，绿灯起飞！");
                            return NodeState.Success;
                        }
                        // 读取斯里基克的实时血量
                        int mdes = MemoryAPI.ReadInteger(_hProcess, tBase + 0x08);
                        float mcurrHp = MemoryAPI.ReadFloat(_hProcess, mdes + 0x58);
                        if (mcurrHp == 0)
                        {
                            Logger.Write($"[{CurrentPlayerName}] [斯里基克雷达] 已经死亡，直接放行！");
                            return NodeState.Success;
                        }

                        // 读取斯里基克的实时 XY 坐标
                        float mX = MemoryAPI.ReadFloat(_hProcess, tBase + 0x9B8);
                        float mY = MemoryAPI.ReadFloat(_hProcess, tBase + 0x9BC);

                        // 录入你即将执行的整条长廊战术路线
                        float[][] hunterPath = new float[][]
                        {
                            new float[] { 527.23f, 564.47f }, // 安全区起跑
                            new float[] { 526.50f, 556.90f },
                            new float[] { 544.40f, 557.17f },
                            new float[] { 544.40f, 577.78f },
                            new float[] { 540.84f, 578.59f }, // 冰冻陷阱点
                            new float[] { 520.33f, 580.17f },
                            new float[] { 505.98f, 580.68f }, // 震荡射击点
                            new float[] { 495.63f, 581.52f },
                            new float[] { 486.02f, 584.07f }  // 最终假死点
                        };

                        float minDist = float.MaxValue;

                        // 遍历航线上的每一个点，计算 Boss 离你整个计划路径的“最近距离”
                        foreach (var pt in hunterPath)
                        {
                            float dist = (float)Math.Sqrt(Math.Pow(mX - pt[0], 2) + Math.Pow(mY - pt[1], 2));
                            if (dist < minDist)
                            {
                                minDist = dist;
                            }
                        }

                        // 安全警戒距离设定为 30 码 (因为你要边跑边放技能，留足安全冗余)
                        if (minDist < 30.0f)
                        {
                            // 2秒打印一次日志，防刷屏
                            if ((DateTime.Now - slipkikLogTimer).TotalSeconds > 2.0)
                            {
                                Logger.Write($"[{CurrentPlayerName}] [斯里基克雷达] 警告：Boss离航线太近 (最短距离 {minDist:F1} 码 < 30)，原地隐蔽等待...");
                                slipkikLogTimer = DateTime.Now;
                            }
                            return NodeState.Running; // 阻塞整棵树，死死卡住不让走！
                        }

                        Logger.Write($"[{CurrentPlayerName}] [斯里基克雷达] 长廊安全 (Boss距离整条航线 {minDist:F1} 码)！绿灯，开始战术突穿！");
                        return NodeState.Success; // 放行，开始执行你下面写的那些华丽的操作！
                    }),

                    //短暂安全区 ，眼睛刷新之前都是安全的
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 527.23f, 564.47f, -25.40f, 0.5f))
                        {
                            StopMovement(_hProcess, _playerBase);
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 526.50f, 556.90f, -25.40f))
                        {
                            //StopMovement(_hProcess, _playerBase);
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 544.40f, 557.17f, -25.40f))
                        {
                            //StopMovement(_hProcess, _playerBase);
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 544.40f, 577.78f, -25.40f))
                        {
                            //StopMovement(_hProcess, _playerBase);
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    //到达此处后立即原地施放 冰冻陷阱 不需要停稳 冻住0xF130002CBA049D1F  速度要快，否则要挨打
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 540.84f, 578.59f, -25.40f))
                        {
                            //StopMovement(_hProcess, _playerBase);
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    new WaitNode(100),

                    new ActionNode(() =>
                    {
                        ExecuteDynamicLua("CastSpellByName('冰冻陷阱')");
                        return NodeState.Success;
                    }),

                    new WaitNode(100),

                    //立即后退

                    //这里对 0xF130002CBA049D1E 施放震荡射击 否则要挨打
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 545.51f, 578.38f, -25.40f))
                        {
                            //StopMovement(_hProcess, _playerBase);
                            SetFacing(_hProcess, _playerBase, 2.517f);
                            Select_Target(_hProcess, 0xF130002CBA049D1E);
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    new WaitNode(1500),
                    //这里改完朝向不能在同一个ActionNode写施放技能，这样会放不出来
                    new ActionNode(() =>
                    {
                        ExecuteDynamicLua("CastSpellByName('驱散射击')");
                        return NodeState.Success;
                    }),

                    new WaitNode(100),

                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 520.33f, 580.17f, -25.40f))
                        {
                            //StopMovement(_hProcess, _playerBase);
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),



                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 505.98f, 580.68f, -25.40f))
                        {
                            //StopMovement(_hProcess, _playerBase);
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    //然后继续前进

                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 495.63f, 581.52f, -25.40f))
                        {
                            //StopMovement(_hProcess, _playerBase);
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 486.02f, 584.07f, -25.40f, 0.5f))
                        {
                            StopMovement(_hProcess, _playerBase);
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    //到达此处施放假死 躺在地上不要起来
                    CreateFeignDeathNode(),

                    //此时是躺地上假死状态，不要起来，根据CD情况使用隐形药水或次级隐形药水
                    //隐形药水和次级隐形药水他们共cd就两分钟
                    //比如说当前使用了隐形药水，此时隐形药水是10分钟CD，但是次级隐形药水就只有2分钟CD
                    //即使两瓶药水都处于CD状态，躺地上最多两分钟，假死有六分钟时间，完全来得及
                    //检查物品CD状态,请使用函数 IsItemOnCooldown，参数是hProcess和ItemId，返回bool值
                    //次级隐形药水ID3823，隐形药水ID9172
                    //使用物品的函数是 UseItemByItemId ，参数是hProcess和ItemId，
                    //隐形药水光环BUFF ID是 11392
                    //次级隐形药水光环BUFF ID是 3680
                    CreateUseInvisibilityPotionNode(),

                    //已经使用了隐形药水，马上开始跑路
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 481.48f, 584.41f, -25.40f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 482.88f, 587.21f, -25.41f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 494.73f, 586.74f, -20.24f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 511.29f, 586.83f, -12.89f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 532.63f, 586.84f, -4.75f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 551.57f, 578.23f, -4.75f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 567.35f, 570.06f, -4.75f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 584.01f, 561.31f, -4.75f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 584.05f, 549.51f, 2.14f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 588.84f, 545.28f, 4.63f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new WaitNode(100),
                    //在这里把宝宝召唤出来，这时候宝宝一定是存活的！不要用封装函数
                    new ActionNode(() =>
                    {
                        ExecuteDynamicLua("CastSpellByName('召唤宠物')");
                        return NodeState.Success;
                    }),
                    new WaitNode(100),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 587.46f, 539.11f, 6.77f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    // 杀掉 游荡的基尔罗格之眼 0xF13000383204956B
                    new ActionNode(() =>
                    {
                        ulong eyeGuid = 0xF13000383204956B;
                        int eyeBase = GetTargetBaseByGuid(_hProcess, eyeGuid);

                        // ====================================================
                        // 🚨 终极置顶防御：目标消失(彻底刷没) OR 血量归零(变成尸体)
                        // ====================================================
                        if (eyeBase == 0 || MemoryAPI.ReadInteger(_hProcess, MemoryAPI.ReadInteger(_hProcess, eyeBase + 0x08) + 0x58) <= 0)
                        {
                            // 如果是在交战中 (eyeStep_2 > 0) 发现它没了，说明被我们干碎了！
                            if (eyeStep_2 > 0)
                            {
                                Logger.Write($"[{CurrentPlayerName}] [战斗系统] 确认目标已被彻底摧毁！紧急刹车，解除警报！");
                                StopMovement(_hProcess, _playerBase);
                            }
                            // 如果一开始就没看到 (eyeStep_2 == 0)，说明没刷或者不用打
                            else
                            {
                                if ((DateTime.Now - searchLogTimer_2).TotalSeconds > 3.0)
                                {
                                    Logger.Write($"[{CurrentPlayerName}] [战斗系统] 视野内未发现三楼眼睛，安全放行...");
                                    searchLogTimer_2 = DateTime.Now;
                                }
                            }

                            eyeStep_2 = 0; // 彻底重置状态
                            return NodeState.Success;
                        }

                        // ==========================================
                        // 阶段 0：雷达扫视与一波流战术分配
                        // ==========================================
                        if (eyeStep_2 == 0)
                        {
                            // (注：这里原本的 if (eyeBase == 0) 判断已被上面的置顶防御完美接管，彻底删掉！)

                            float pX = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9B8);
                            float pY = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9BC);
                            float pZ = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9C0);
                            float mX = MemoryAPI.ReadFloat(_hProcess, eyeBase + 0x9B8);
                            float mY = MemoryAPI.ReadFloat(_hProcess, eyeBase + 0x9BC);
                            float mZ = MemoryAPI.ReadFloat(_hProcess, eyeBase + 0x9C0);

                            // 判定来源
                            float distToDangerPack = (float)Math.Sqrt(Math.Pow(mX - 580.365f, 2) + Math.Pow(mY - 565.465f, 2) + Math.Pow(mZ - (-4.755f), 2));
                            if (distToDangerPack < 25.0f) isFromDownstairs_2 = true;

                            float dist = (float)Math.Sqrt(Math.Pow(mX - pX, 2) + Math.Pow(mY - pY, 2) + Math.Pow(mZ - pZ, 2));

                            if (dist > 41.0f)
                            {
                                isFromDownstairs_2 = false;
                                if ((DateTime.Now - searchLogTimer_2).TotalSeconds > 3.0)
                                {
                                    Logger.Write($"[{CurrentPlayerName}] [战斗系统] 眼睛距离 {dist:F1} 码，射程之外，原地等待...");
                                    searchLogTimer_2 = DateTime.Now;
                                }
                                return NodeState.Running;
                            }

                            Select_Target(_hProcess, eyeGuid);

                            // 【战术 1】：楼下上来的，必须等贴脸触发战斗
                            if (isFromDownstairs_2)
                            {
                                if (Unit_Behavioral_State(_hProcess, _playerBase, 19))
                                {
                                    Logger.Write($"[{CurrentPlayerName}] [战斗系统] 楼下眼睛贴脸触发战斗！强制改朝向并起跳同步近战面向！");

                                    float deltaX = mX - pX;
                                    float deltaY = mY - pY;
                                    float facingAngle = (float)Math.Atan2(deltaY, deltaX);
                                    if (facingAngle < 0) facingAngle += (float)(Math.PI * 2);

                                    SetFacing(_hProcess, _playerBase, facingAngle);
                                    _ = Jump(50);

                                    isMelee_2 = true;
                                    eyeTimer_2 = DateTime.Now;
                                    eyeStep_2 = 1;
                                }
                                else
                                {
                                    if ((DateTime.Now - searchLogTimer_2).TotalSeconds > 3.0)
                                    {
                                        Logger.Write($"[{CurrentPlayerName}] [战斗系统] 楼下眼睛靠近中...静默待命，放它上来触发战斗...");
                                        searchLogTimer_2 = DateTime.Now;
                                    }
                                }
                                return NodeState.Running;
                            }
                            else
                            {
                                // 【战术 2】：楼上下来的，绝对射击
                                if (dist > 35.0f)
                                {
                                    if ((DateTime.Now - searchLogTimer_2).TotalSeconds > 2.0)
                                    {
                                        Logger.Write($"[{CurrentPlayerName}] [战斗系统] 楼上眼睛靠近中 (当前距离 {dist:F1} 码)，等待进入 35 码绝对开火线...");
                                        searchLogTimer_2 = DateTime.Now;
                                    }
                                    return NodeState.Running;
                                }

                                Logger.Write($"[{CurrentPlayerName}] [战斗系统] 楼上眼睛进入 35 码绝对射程！改朝向并起跳同步远程面向！");

                                float deltaX = mX - pX;
                                float deltaY = mY - pY;
                                float facingAngle = (float)Math.Atan2(deltaY, deltaX);
                                if (facingAngle < 0) facingAngle += (float)(Math.PI * 2);

                                SetFacing(_hProcess, _playerBase, facingAngle);
                                _ = Jump(50);

                                isMelee_2 = false;
                                eyeTimer_2 = DateTime.Now;
                                eyeStep_2 = 1;
                                return NodeState.Running;
                            }
                        }

                        // ==========================================
                        // 阶段 1：跳跃面向同步缓冲与单次开火/拔刀
                        // ==========================================
                        if (eyeStep_2 == 1)
                        {
                            if ((DateTime.Now - eyeTimer_2).TotalMilliseconds >= 200)
                            {
                                if (isMelee_2)
                                {
                                    Logger.Write($"[{CurrentPlayerName}] [战斗系统] 近战面向同步完毕！挂上【猛禽一击】并激活近战白字，贴脸肉搏！");

                                    float mX = MemoryAPI.ReadFloat(_hProcess, eyeBase + 0x9B8);
                                    float mY = MemoryAPI.ReadFloat(_hProcess, eyeBase + 0x9BC);
                                    float pZ = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9C0);

                                    MoveTo(_hProcess, _playerBase, mX, mY, pZ, 1.0f);
                                    ExecuteDynamicLua("CastSpellByName('猛禽一击'); PetAttack(); ");

                                    eyeTimer_2 = DateTime.Now;
                                    eyeStep_2 = 2;
                                }
                                else
                                {
                                    Logger.Write($"[{CurrentPlayerName}] [战斗系统] 远程面向同步完毕！发射【奥术射击】，开启【自动射击】收尾！");
                                    ExecuteDynamicLua("CastSpellByName('奥术射击'); PetAttack();");

                                    eyeTimer_2 = DateTime.Now;
                                    eyeStep_2 = 3;
                                }
                            }
                            return NodeState.Running;
                        }

                        // ==========================================
                        // 阶段 3：远程动作起手缓冲
                        // ==========================================
                        if (eyeStep_2 == 3)
                        {
                            if ((DateTime.Now - eyeTimer_2).TotalMilliseconds >= 100)
                            {
                                eyeTimer_2 = DateTime.Now;
                                eyeStep_2 = 2;
                            }
                            return NodeState.Running;
                        }

                        // ==========================================
                        // 阶段 2：死等目标死亡 (绝对不会再报错跳原点了)
                        // ==========================================
                        if (eyeStep_2 == 2)
                        {
                            if (isMelee_2 && (DateTime.Now - eyeTimer_2).TotalSeconds > 1.0)
                            {
                                float mX = MemoryAPI.ReadFloat(_hProcess, eyeBase + 0x9B8);
                                float mY = MemoryAPI.ReadFloat(_hProcess, eyeBase + 0x9BC);
                                float pZ = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9C0);

                                MoveTo(_hProcess, _playerBase, mX, mY, pZ, 1.0f);
                                eyeTimer_2 = DateTime.Now;
                            }

                            if (!isMelee_2)
                            {
                                float pX = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9B8);
                                float pY = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9BC);
                                float mX = MemoryAPI.ReadFloat(_hProcess, eyeBase + 0x9B8);
                                float mY = MemoryAPI.ReadFloat(_hProcess, eyeBase + 0x9BC);
                                float dist = (float)Math.Sqrt(Math.Pow(mX - pX, 2) + Math.Pow(mY - pY, 2));

                                if (dist <= 10.0f)
                                {
                                    Logger.Write($"[{CurrentPlayerName}] [战斗系统] 眼睛冲进盲区！紧急切近战拔刀！");
                                    float pZ = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9C0);
                                    MoveTo(_hProcess, _playerBase, mX, mY, pZ, 1.0f);
                                    ExecuteDynamicLua("CastSpellByName('猛禽一击'); ");
                                    isMelee_2 = true;
                                    eyeTimer_2 = DateTime.Now;
                                }
                            }

                            return NodeState.Running;
                        }

                        return NodeState.Running;
                    }),
                    new WaitNode(100),

                    new ActionNode(() =>
                    {
                        StopMovement(_hProcess, _playerBase);
                        return NodeState.Success;
                    }),

                    new WaitNode(100),

                    //让宝宝跟随猎人
                    new ActionNode(() =>
                    {
                        ExecuteDynamicLua("PetFollow()");
                        return NodeState.Success;
                    }),
                    new WaitNode(500),
                    // 解散并重新召唤宝宝
                    CreateRefreshPetCooldownNode(),

                    new WaitNode(1000),
                    //杀掉 游荡的基尔罗格之眼 0xF13000383204956B后移动到这个坐标
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 592.04f, 546.70f, 3.79f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new WaitNode(1000),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 592.17f, 544.09f, 5.33f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    //等待2秒 让宝宝回到猎人身边
                    new WaitNode(2000),
                    //这里把宝宝定住
                    new ActionNode(() =>
                    {
                        ExecuteDynamicLua("PetWait()");
                        return NodeState.Success;
                    }),

                    new WaitNode(100),

                    //继续往前走
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 577.79f, 533.68f, 8.33f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 566.44f, 532.92f, 14.42f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 555.78f, 533.18f, 20.14f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    //走到这里后施放一下冰霜陷阱
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 539.55f, 534.43f, 27.92f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    //给DLL 100毫秒缓冲时间，不给的话技能放不出来
                    new WaitNode(100),

                    //这里施放 冰霜陷阱，给我们开门争取更多的时间
                    new ActionNode(() =>
                    {
                        ExecuteDynamicLua("CastSpellByName('冰霜陷阱')");
                        return NodeState.Success;
                    }),
                    //给DLL 100毫秒缓冲时间，不给的话技能放不出来
                    new WaitNode(100),

                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 539.68f, 526.48f, 27.92f, 1.0f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 527.79f, 526.78f, 27.92f, 1.0f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    // 已经到达拉怪点
                    new ActionNode(() =>
                    {
                        int petBase = GetPetBase(_hProcess);
                        if (petBase == 0) return NodeState.Failure; // 兜底：宝宝如果没了报错打断

                        ulong targetGuid = 0xF130002CBA049D23;

                        // ==========================================
                        // 阶段 0：锁目标，派宝宝咬
                        // ==========================================
                        if (pullStep_1 == 0)
                        {
                            Logger.Write($"[{CurrentPlayerName}] [战斗系统] 锁定怪物 (0x{targetGuid:X}) 指派宠物越过怪堆强行拉怪...");

                            Select_Target(_hProcess, targetGuid);

                            // 第一次绝对安全，直接攻击
                            ExecuteDynamicLua("PetAttack()");

                            pullTimer_1 = DateTime.Now;
                            pullStep_1 = 1;
                            return NodeState.Running;
                        }

                        // ==========================================
                        // 阶段 1：监控【目标怪物】是否进战（神级改动）
                        // ==========================================
                        if (pullStep_1 == 1)
                        {
                            // 15 秒没拉到，说明宝宝死在半路了或者卡住了，重置
                            if ((DateTime.Now - pullTimer_1).TotalSeconds > 15)
                            {
                                Logger.Write($"[{CurrentPlayerName}] [战斗系统] 15秒拉怪超时！目标未进战，宝宝可能已阵亡或卡顿，重试...");
                                pullStep_1 = 0;
                                return NodeState.Running;
                            }

                            // 获取目标怪物的基址
                            int targetMobBase = GetTargetBaseByGuid(_hProcess, targetGuid);

                            if (targetMobBase != 0)
                            {
                                // 🚨 核心战术：复用你的状态函数，直接查目标怪！
                                // 只要目标怪的 bit 19 亮了，说明宝宝已经成功冲破防线摸到它了（或者靠得足够近引发了社会仇恨）！
                                bool isTargetInCombat = Unit_Behavioral_State(_hProcess, targetMobBase, 19);

                                if (isTargetInCombat)
                                {
                                    Logger.Write($"[{CurrentPlayerName}] [战斗系统] 目标怪物 (0x{targetGuid:X}) 已进入战斗状态！仇恨建立成功，立刻召回宠物并起跑！");

                                    ExecuteDynamicLua("PetPassiveMode()");
                                    pullTimer_1 = DateTime.Now;
                                    pullStep_1 = 2; // 进入缓冲放行阶段
                                }
                            }

                            return NodeState.Running; // 目标还没进战，让子弹飞一会儿
                        }

                        // ==========================================
                        // 阶段 2：动作缓冲放行
                        // ==========================================
                        if (pullStep_1 == 2)
                        {
                            // 留 100 毫秒让“召回”动作在服务器生效，然后猎人再开跑
                            if ((DateTime.Now - pullTimer_1).TotalMilliseconds >= 100)
                            {
                                pullStep_1 = 0; // 重置变量，为下次跑本复用做准备
                                return NodeState.Success; // 完美放行，猎人开跑！
                            }
                            return NodeState.Running;
                        }

                        return NodeState.Running;
                    }),

                    //前往开门点

                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 512.56f, 524.99f, 27.92f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 501.33f, 525.08f, 27.92f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 494.15f, 524.53f, 28.58f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    //已经到达开门点
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 491.54f, 516.66f, 29.46f, 0.5f))
                        {
                            StopMovement(_hProcess, _playerBase);
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    new WaitNode(100),
                    //要实现完美贡品 这里使用 [大型爆盐炸弹] 开门

                    //将拿起的大型爆盐炸弹应用于戈多克内门
                    // 戈多克内门 ID 177217
                    new ActionNode(() =>
                    {
                        // 1. 获取门的动态基址（请确认你的方法名是这个）
                        uint doorBase = GetGameObjectBaseByEntry(_hProcess, 177217);

                        if (doorBase != 0)
                        {
                            // 2. 状态探测：门是否已经开启？
                            if (IsGameObjectOpened(_hProcess, doorBase))
                            {
                                Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [{CurrentPlayerName}] [门禁系统] 门禁系统验证通过！戈多克内门已开启，直接进去！");
                                return NodeState.Success; // 只有这里返回 Success，行为树才会走向下一步
                            }

                            // 3. 冷却探测：如果当前时间还没超过设定的 _GodekGateTime，说明正在读条或等待服务器判定
                            if (DateTime.Now < _GodekGateTime)
                            {
                                return NodeState.Running; // 什么都不做，静静等待
                            }

                            // 4. 门没开，且不在冷却中 -> 执行炸门动作！
                            ulong doorGuid = MemoryAPI.ReadQword(_hProcess, (int)(doorBase + 0x30)); // 精准读取 GUID

                            // 【核心连招】拿起炸弹 -> 触发蓝手 -> 抛向目标
                            UseItemByItemId(_hProcess, 4398);
                            System.Threading.Thread.Sleep(150); // 极短休眠，确保内存 UI 状态已切换为法术寻的
                            ExecuteSpellTargetGuid(doorGuid);

                            // 5. 设定 5.5 秒的冷却期 (施法读条时间 + 服务器判定延迟)
                            _GodekGateTime = DateTime.Now.AddSeconds(5.5);

                            Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [{CurrentPlayerName}] [门禁系统] 已向大门 (0x{doorGuid:X}) 投掷炸弹，等待 5.5 秒...");

                            return NodeState.Running; // 返回 Running，让行为树下一帧继续来检查门开了没
                        }

                        // 连基址都没找到，说明距离太远，保持 Running 让前面的寻路逻辑继续靠近
                        Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [{CurrentPlayerName}] [门禁系统] 没找到戈多克内门，请确认你在戈多克内门 3 码以内");
                        return NodeState.Running;
                    }),

                    //门已开，继续前进
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 491.42f, 506.14f, 29.46f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 491.55f, 497.61f, 29.46f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 488.86f, 480.06f, 29.46f, 0.5f))
                        {
                            StopMovement(_hProcess, _playerBase);
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new WaitNode(100),
                    //此处假死 等待宝宝消失
                    CreateFeignDeathNode(),
                    new WaitNode(100),
                    //宝宝全能函数
                    CreatePetVetNode(),
                    new WaitNode(100),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 496.43f, 480.87f, 29.46f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 507.07f, 480.37f, 29.46f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 520.64f, 477.85f, 29.46f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 531.81f, 476.72f, 29.46f, 0.5f))
                        {
                            StopMovement(_hProcess, _playerBase);
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new WaitNode(100),
                    // 在此处依旧使用卡虚空的方案拉怪，更稳妥，宝宝也不容易噶
                    // 解散并重新召唤宝宝
                    CreateRefreshPetCooldownNode(),
                    new WaitNode(100),
                    // 立刻定住宝宝
                    new ActionNode(() =>
                    {
                        ExecuteDynamicLua("PetWait()");
                        return NodeState.Success;
                    }),

                    new WaitNode(100),
                    //等待假死冷却后才能走
                    CreateWaitForFeignDeathNode(),

                    new WaitNode(100),
                    new ActionNode(() =>
                    {
                        int petBase = GetPetBase(_hProcess);
                        if (petBase == 0) return NodeState.Failure; // 兜底：宝宝如果没了报错打断

                        // 阶段 0：锁目标 A，派宝宝咬
                        if (pullStep_1 == 0)
                        {
                            Logger.Write($"[{CurrentPlayerName}] [战斗系统] 锁定怪物 (0xF130002CBA049D28 ) 指派宠物前往攻击...");

                            Select_Target(_hProcess, 0xF130002CBA049D28);

                            // 第一次绝对安全，直接并攻击
                            ExecuteDynamicLua("PetAttack()");

                            pullTimer_1 = DateTime.Now;
                            pullStep_1 = 1;
                            return NodeState.Running;
                        }

                        // 阶段 1：等待宠物进入战斗状态
                        if (pullStep_1 == 1)
                        {
                            // 15 秒没进战，重置
                            if ((DateTime.Now - pullTimer_1).TotalSeconds > 15)
                            {
                                pullStep_1 = 0;
                                return NodeState.Running;
                            }

                            if (Pet_Behavioral_State(_hProcess, petBase, 19))
                            {
                                Logger.Write($"[{CurrentPlayerName}] [战斗系统] 宠物已成功引到 (0xF130002CBA049D28 ) 的仇恨！猎人准备起跑！");

                                ExecuteDynamicLua("PetPassiveMode()");
                                pullTimer_1 = DateTime.Now;
                                pullStep_1 = 2;
                            }
                            return NodeState.Running;
                        }

                        // 阶段 2：动作缓冲放行
                        if (pullStep_1 == 2)
                        {
                            // 留 100 毫秒让攻击动作在服务器生效，然后猎人再开跑
                            if ((DateTime.Now - pullTimer_1).TotalMilliseconds >= 100)
                            {
                                pullStep_1 = 0; // 重置变量，为下次跑本复用做准备
                                return NodeState.Success; // 完美放行，猎人开跑！
                            }
                            return NodeState.Running;
                        }
                        return NodeState.Running;
                    }),
                    new WaitNode(100),


                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 548.15f, 473.63f, 29.46f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 562.14f, 473.00f, 29.46f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 574.93f, 472.60f, 29.46f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 589.13f, 471.89f, 29.46f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 599.30f, 470.79f, 29.46f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 607.19f, 466.22f, 29.47f, 0.5f))
                        {
                            StopMovement(_hProcess, _playerBase);
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    new WaitNode(100),
                    //此处假死 等待宝宝消失
                    CreateFeignDeathNode(),

                    //使用召唤宝宝函数节点
                    CreatePetVetNode(),

                    //给DLL 100毫秒反应时间
                    new WaitNode(100),

                    // 立刻定住宝宝
                    new ActionNode(() =>
                    {
                        ExecuteDynamicLua("PetWait()");
                        return NodeState.Success;
                    }),


                    new WaitNode(100),
                    //等待假死冷却后才能走
                    CreateWaitForFeignDeathNode(),


                    new WaitNode(100),
                    //监控戈多克驯狼 P4寻路
                    CreateWolfRadarNode_P4(),

                    new WaitNode(100),
                    //拉走克罗卡斯
                    new ActionNode(() =>
                    {
                        int petBase = GetPetBase(_hProcess);
                        if (petBase == 0) return NodeState.Failure; // 兜底：宝宝如果没了报错打断

                        // 阶段 0：锁目标 A，派宝宝咬
                        if (pullStep_1 == 0)
                        {
                            Logger.Write($"[{CurrentPlayerName}] [战斗系统] 锁定怪物 (0xF1300037F500DE70) 指派宠物前往攻击...");

                            Select_Target(_hProcess, 0xF1300037F500DE70);

                            // 第一次绝对安全，直接并攻击
                            ExecuteDynamicLua("PetAttack()");
                            pullTimer_1 = DateTime.Now;
                            pullStep_1 = 1;
                            return NodeState.Running;
                        }

                        // 阶段 1：等待宠物进入战斗状态
                        if (pullStep_1 == 1)
                        {
                            // 15 秒没进战，重置
                            if ((DateTime.Now - pullTimer_1).TotalSeconds > 15)
                            {
                                pullStep_1 = 0;
                                return NodeState.Running;
                            }

                            if (Pet_Behavioral_State(_hProcess, petBase, 19))
                            {
                                Logger.Write($"[{CurrentPlayerName}] [战斗系统] 宠物已成功引到 (0xF1300037F500DE70) 的仇恨！猎人准备起跑！");

                                ExecuteDynamicLua("PetPassiveMode()");
                                pullTimer_1 = DateTime.Now;
                                pullStep_1 = 2;
                            }
                            return NodeState.Running;
                        }

                        // 阶段 2：动作缓冲放行
                        if (pullStep_1 == 2)
                        {
                            // 留 100 毫秒让攻击动作在服务器生效，然后猎人再开跑
                            if ((DateTime.Now - pullTimer_1).TotalMilliseconds >= 100)
                            {
                                pullStep_1 = 0; // 重置变量，为下次跑本复用做准备
                                return NodeState.Success; // 完美放行，猎人开跑！
                            }
                            return NodeState.Running;
                        }
                        return NodeState.Running;
                    }),
                    new WaitNode(100),


                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 623.52f, 473.68f, 29.46f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 635.25f, 484.55f, 29.46f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 642.36f, 492.68f, 29.47f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 637.70f, 516.53f, 29.47f, 0.5f))
                        {
                            StopMovement(_hProcess, _playerBase);
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new WaitNode(100),
                    //此处假死 等待宝宝消失
                    CreateFeignDeathNode(),

                    new WaitNode(1000),

                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 634.71f, 521.91f, 29.46f, 0.5f))
                        {
                            StopMovement(_hProcess, _playerBase);
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new WaitNode(1000),
                    //使用召唤宝宝函数节点
                    CreatePetVetNode(),

                    //给DLL 100毫秒反应时间
                    new WaitNode(100),

                    // 立刻定住宝宝
                    new ActionNode(() =>
                    {
                        ExecuteDynamicLua("PetWait()");
                        return NodeState.Success;
                    }),


                    new WaitNode(100),

                    //等待假死冷却后才能走
                    CreateWaitForFeignDeathNode(),


                    new WaitNode(100),

                    //监控戈多克驯狼 P11寻路
                    CreateWolfRadarNode_P11(),

                    new WaitNode(100),

                     new ActionNode(() =>
                     {
                         if (MoveTo(_hProcess, _playerBase, 644.31f, 513.93f, 29.46f))
                         {
                             return NodeState.Success;
                         }
                         return NodeState.Running;
                     }),
                     new ActionNode(() =>
                     {
                         if (MoveTo(_hProcess, _playerBase, 649.71f, 507.98f, 29.46f))
                         {
                             return NodeState.Success;
                         }
                         return NodeState.Running;
                     }),

                    new WaitNode(100),
                    new ActionNode(() =>
                    {
                        int petBase = GetPetBase(_hProcess);
                        if (petBase == 0) return NodeState.Failure; // 兜底：宝宝如果没了报错打断

                        // 阶段 0：锁目标 A，派宝宝咬
                        if (pullStep_1 == 0)
                        {
                            Logger.Write($"[{CurrentPlayerName}] [战斗系统] 锁定怪物 (0xF130002CB5020EE1) 指派宠物前往攻击...");

                            Select_Target(_hProcess, 0xF130002CB5020EE1);

                            // 第一次绝对安全，直接并攻击
                            ExecuteDynamicLua("PetAttack()");

                            pullTimer_1 = DateTime.Now;
                            pullStep_1 = 1;
                            return NodeState.Running;
                        }

                        // 阶段 1：等待宠物进入战斗状态
                        if (pullStep_1 == 1)
                        {
                            // 15 秒没进战，重置
                            if ((DateTime.Now - pullTimer_1).TotalSeconds > 15)
                            {
                                pullStep_1 = 0;
                                return NodeState.Running;
                            }

                            if (Pet_Behavioral_State(_hProcess, petBase, 19))
                            {
                                Logger.Write($"[{CurrentPlayerName}] [战斗系统] 宠物已成功引到 (0xF130002CB5020EE1) 的仇恨！猎人准备起跑！");

                                ExecuteDynamicLua("PetPassiveMode()");
                                pullTimer_1 = DateTime.Now;
                                pullStep_1 = 2;
                            }
                            return NodeState.Running;
                        }

                        // 阶段 2：动作缓冲放行
                        if (pullStep_1 == 2)
                        {
                            // 留 100 毫秒让攻击动作在服务器生效，然后猎人再开跑
                            if ((DateTime.Now - pullTimer_1).TotalMilliseconds >= 100)
                            {
                                pullStep_1 = 0; // 重置变量，为下次跑本复用做准备
                                return NodeState.Success; // 完美放行，猎人开跑！
                            }
                            return NodeState.Running;
                        }
                        return NodeState.Running;
                    }),
                    new WaitNode(100),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 661.31f, 507.65f, 29.46f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 673.66f, 507.12f, 29.46f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 690.92f, 510.12f, 28.50f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 710.44f, 519.16f, 28.18f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 734.87f, 523.87f, 28.18f, 0.5f))
                        {
                            StopMovement(_hProcess, _playerBase);
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    //此处假死 等待宝宝消失
                    CreateFeignDeathNode(),
                    new WaitNode(100),

                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 734.87f, 523.87f, 28.18f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 754.06f, 525.80f, 28.18f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    //这里已经见到大王了

                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 775.50f, 533.80f, 28.18f, 1.0f))
                        {
                            StopMovement(_hProcess, _playerBase);
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    new WaitNode(100),

                    CreatePetVetNode(),

                    new WaitNode(100),


                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 814.25f, 548.31f, 28.92f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),

                    new WaitNode(500),

                     //定住宝宝
                     new ActionNode(() =>
                     {
                         ExecuteDynamicLua("PetWait()");
                         return NodeState.Success;
                     }),

                    new WaitNode(100),

                    //继续走

                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 828.81f, 546.85f, 32.65f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 847.76f, 540.32f, 34.26f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 868.89f, 527.45f, 34.26f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 883.87f, 515.01f, 36.76f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 900.23f, 505.26f, 40.41f, 1.0f))   //拐弯处
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),


                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 891.59f, 521.40f, 40.98f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 883.84f, 533.65f, 40.99f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 863.14f, 551.65f, 40.98f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 837.77f, 560.92f, 40.98f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 810.76f, 561.04f, 40.98f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, 783.80f, 552.50f, 40.98f))
                        {
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),








                    //暂时卡死行为树，击杀大王逻辑还没写
                    new WaitNode(9999999)

                    
            );

            // ========================================================
            // 联盟航点坐标   _Alliance
            // ========================================================
            List<Waypoint> routeToHome_Alliance = new List<Waypoint> {
                new Waypoint { X = -4366.23f, Y = 3301.95f, Z = 13.56f, Type = Precise },
                new Waypoint { X = -4370.53f, Y = 3301.16f, Z = 13.57f, Type = Precise },
                new Waypoint { X = -4374.01f, Y = 3299.84f, Z = 13.57f, Type = Precise },
                new Waypoint { X = -4374.33f, Y = 3295.36f, Z = 13.56f, Type = Precise },
                new Waypoint { X = -4374.16f, Y = 3289.35f, Z = 13.56f, Type = Precise },
                new Waypoint { X = -4373.79f, Y = 3282.51f, Z = 13.57f, Type = Precise },
                new Waypoint { X = -4373.61f, Y = 3270.70f, Z = 13.57f, Type = Precise },
                new Waypoint { X = -4373.25f, Y = 3260.52f, Z = 13.26f, Type = Precise },
            };

            List<Waypoint> routeToShore_Alliance = new List<Waypoint>
            {
                new Waypoint { X = -4370.87f, Y = 3242.14f, Z = 12.57f, Type = Rough },
                new Waypoint { X = -4368.48f, Y = 3209.60f, Z = 13.82f, Type = Rough },
                new Waypoint { X = -4364.20f, Y = 3160.64f, Z = 12.06f, Type = Rough },
                new Waypoint { X = -4349.29f, Y = 3104.69f, Z = 0.30f, Type = Rough },
            };

            List<Waypoint> routeToDock_Alliance = new List<Waypoint>
            {
                new Waypoint { X = -4361.96f, Y = 2993.85f, Z = -0.97f, Type = Rough },
                new Waypoint { X = -4368.85f, Y = 2898.33f, Z = -1.04f, Type = Rough },
                new Waypoint { X = -4370.36f, Y = 2782.57f, Z = -1.09f, Type = Rough },
                new Waypoint { X = -4350.96f, Y = 2613.24f, Z = -1.00f, Type = Rough },
                new Waypoint { X = -4333.98f, Y = 2436.23f, Z = -1.17f, Type = Rough },
                new Waypoint { X = -4325.17f, Y = 2355.73f, Z = 0.50f, Type = Rough },
            };

            List<Waypoint> routeToInstanceDoor_Alliance = new List<Waypoint>
            {
                new Waypoint { X = -4328.31f, Y = 2319.69f, Z = 10.38f, Type = Rough },
                new Waypoint { X = -4395.00f, Y = 2243.01f, Z = 6.02f, Type = Rough },
                new Waypoint { X = -4432.30f, Y = 2154.08f, Z = 20.92f, Type = Rough },
                new Waypoint { X = -4447.47f, Y = 2088.23f, Z = 41.91f, Type = Rough },
                new Waypoint { X = -4457.74f, Y = 2054.63f, Z = 45.46f, Type = Rough },
                new Waypoint { X = -4493.03f, Y = 2043.68f, Z = 49.94f, Type = Rough },
                new Waypoint { X = -4544.82f, Y = 2032.73f, Z = 45.11f, Type = Rough },
                new Waypoint { X = -4571.64f, Y = 2024.01f, Z = 46.60f, Type = Rough },
                new Waypoint { X = -4597.96f, Y = 2007.93f, Z = 52.82f, Type = Rough },
                new Waypoint { X = -4619.65f, Y = 1989.20f, Z = 59.14f, Type = Rough },
                new Waypoint { X = -4650.67f, Y = 1962.21f, Z = 66.75f, Type = Rough },
                new Waypoint { X = -4660.20f, Y = 1959.11f, Z = 68.28f, Type = Rough },
                new Waypoint { X = -4669.02f, Y = 1952.96f, Z = 69.73f, Type = Rough },
                new Waypoint { X = -4676.52f, Y = 1946.12f, Z = 71.32f, Type = Rough },
                new Waypoint { X = -4678.75f, Y = 1937.83f, Z = 72.81f, Type = Rough },
                new Waypoint { X = -4677.96f, Y = 1928.53f, Z = 74.79f, Type = Rough },
                new Waypoint { X = -4675.72f, Y = 1918.15f, Z = 78.07f, Type = Rough },
                new Waypoint { X = -4671.42f, Y = 1900.63f, Z = 79.97f, Type = Rough },
                new Waypoint { X = -4669.68f, Y = 1886.74f, Z = 81.04f, Type = Rough },
                new Waypoint { X = -4669.41f, Y = 1872.53f, Z = 83.80f, Type = Rough },
                new Waypoint { X = -4670.80f, Y = 1855.52f, Z = 87.04f, Type = Rough },
                new Waypoint { X = -4682.26f, Y = 1827.39f, Z = 90.22f, Type = Rough },
                new Waypoint { X = -4698.42f, Y = 1798.57f, Z = 91.53f, Type = Rough },
                new Waypoint { X = -4711.48f, Y = 1768.94f, Z = 92.22f, Type = Rough },
                new Waypoint { X = -4726.73f, Y = 1738.46f, Z = 93.24f, Type = Rough },
                new Waypoint { X = -4736.04f, Y = 1715.57f, Z = 94.30f, Type = Rough },
                new Waypoint { X = -4742.27f, Y = 1692.11f, Z = 92.31f, Type = Rough },
                new Waypoint { X = -4744.22f, Y = 1653.66f, Z = 88.64f, Type = Rough },
                new Waypoint { X = -4745.25f, Y = 1602.47f, Z = 83.73f, Type = Rough },
                new Waypoint { X = -4745.57f, Y = 1567.03f, Z = 85.30f, Type = Rough },
                new Waypoint { X = -4747.04f, Y = 1528.11f, Z = 88.55f, Type = Rough },
                new Waypoint { X = -4722.78f, Y = 1407.56f, Z = 88.78f, Type = Rough },
                new Waypoint { X = -4755.79f, Y = 1460.56f, Z = 92.87f, Type = Rough },
                new Waypoint { X = -4779.91f, Y = 1442.93f, Z = 90.50f, Type = Rough },
                new Waypoint { X = -4814.19f, Y = 1408.53f, Z = 83.10f, Type = Rough },
                new Waypoint { X = -4835.87f, Y = 1381.90f, Z = 80.12f, Type = Rough },
                new Waypoint { X = -4841.61f, Y = 1347.82f, Z = 80.46f, Type = Rough },
                new Waypoint { X = -4811.14f, Y = 1322.21f, Z = 84.51f, Type = Rough },
                new Waypoint { X = -4764.35f, Y = 1309.88f, Z = 89.82f, Type = Rough },
                new Waypoint { X = -4639.63f, Y = 1333.08f, Z = 97.67f, Type = Rough },
                new Waypoint { X = -4588.10f, Y = 1330.21f, Z = 105.91f, Type = Rough },
                new Waypoint { X = -4561.39f, Y = 1299.77f, Z = 117.74f, Type = Precise },
                new Waypoint { X = -4551.33f, Y = 1304.45f, Z = 123.92f, Type = Precise },
                new Waypoint { X = -4542.19f, Y = 1305.52f, Z = 126.41f, Type = Precise },
                new Waypoint { X = -4516.79f, Y = 1311.82f, Z = 123.84f, Type = Precise },
                new Waypoint { X = -4482.22f, Y = 1314.97f, Z = 123.79f, Type = Rough },
                new Waypoint { X = -4470.36f, Y = 1330.66f, Z = 124.31f, Type = Rough },
                new Waypoint { X = -4447.79f, Y = 1333.60f, Z = 126.00f, Type = Rough },
                new Waypoint { X = -4423.21f, Y = 1346.29f, Z = 131.42f, Type = Rough },
                new Waypoint { X = -4404.95f, Y = 1346.42f, Z = 139.88f, Type = Rough },
                new Waypoint { X = -4380.19f, Y = 1346.21f, Z = 151.55f, Type = Rough },
            };

            List<Waypoint> route_GY_Feathermoon_Alliance = new List<Waypoint>
            {
                new Waypoint { X = -4574.11f, Y = 3228.86f, Z = 8.96f, Type = Rough },
                new Waypoint { X = -4531.37f, Y = 3234.14f, Z = 8.90f, Type = Rough },
                new Waypoint { X = -4493.90f, Y = 3243.67f, Z = 10.79f, Type = Rough },
                new Waypoint { X = -4462.92f, Y = 3254.90f, Z = 14.73f, Type = Rough },
                new Waypoint { X = -4417.06f, Y = 3238.20f, Z = 12.72f, Type = Rough },
                new Waypoint { X = -4397.40f, Y = 3232.88f, Z = 12.02f, Type = Rough },
                new Waypoint { X = -4367.63f, Y = 3228.98f, Z = 12.78f, Type = Rough },
            };

            List<Waypoint> route_GY_TwinColossals_Alliance = new List<Waypoint> {
                new Waypoint { X = -4579.64f, Y = 1637.26f, Z = 94.83f, Type = Rough },
                new Waypoint { X = -4568.14f, Y = 1647.87f, Z = 98.44f, Type = Rough },
                new Waypoint { X = -4568.12f, Y = 1664.26f, Z = 103.47f, Type = Rough },
                new Waypoint { X = -4585.39f, Y = 1674.01f, Z = 110.23f, Type = Rough },
                new Waypoint { X = -4602.29f, Y = 1680.76f, Z = 115.27f, Type = Rough },
                new Waypoint { X = -4604.18f, Y = 1690.01f, Z = 115.49f, Type = Rough },
                new Waypoint { X = -4606.17f, Y = 1704.64f, Z = 114.79f, Type = Rough },
                new Waypoint { X = -4623.01f, Y = 1733.58f, Z = 101.44f, Type = Rough },
                new Waypoint { X = -4631.04f, Y = 1756.41f, Z = 95.79f, Type = Rough },
                new Waypoint { X = -4636.79f, Y = 1772.79f, Z = 97.41f, Type = Rough },
                new Waypoint { X = -4640.32f, Y = 1808.85f, Z = 94.59f, Type = Rough },
                new Waypoint { X = -4630.26f, Y = 1856.09f, Z = 92.38f, Type = Rough },
                new Waypoint { X = -4632.03f, Y = 1872.54f, Z = 95.20f, Type = Rough },
                new Waypoint { X = -4639.74f, Y = 1890.65f, Z = 92.30f, Type = Rough },
                new Waypoint { X = -4669.58f, Y = 1894.30f, Z = 80.27f, Type = Rough },
            };

            List<Waypoint> route_GY_Mojache_Alliance = new List<Waypoint> {
                new Waypoint { X = -4449.76f, Y = 388.63f, Z = 52.87f, Type = Rough },
                new Waypoint { X = -4464.85f, Y = 408.23f, Z = 54.61f, Type = Rough },
                new Waypoint { X = -4496.88f, Y = 408.42f, Z = 49.59f, Type = Rough },
                new Waypoint { X = -4573.61f, Y = 423.14f, Z = 41.51f, Type = Rough },
                new Waypoint { X = -4603.37f, Y = 448.68f, Z = 42.89f, Type = Rough },
                new Waypoint { X = -4622.56f, Y = 504.38f, Z = 37.39f, Type = Rough },
                new Waypoint { X = -4642.61f, Y = 563.83f, Z = 39.79f, Type = Rough },
                new Waypoint { X = -4654.49f, Y = 610.89f, Z = 49.30f, Type = Rough },
                new Waypoint { X = -4670.78f, Y = 668.07f, Z = 63.15f, Type = Rough },
                new Waypoint { X = -4681.91f, Y = 723.97f, Z = 76.58f, Type = Rough },
                new Waypoint { X = -4670.80f, Y = 752.47f, Z = 82.78f, Type = Rough },
                new Waypoint { X = -4660.36f, Y = 771.37f, Z = 83.83f, Type = Rough },
                new Waypoint { X = -4648.81f, Y = 829.83f, Z = 82.65f, Type = Rough },
                new Waypoint { X = -4643.68f, Y = 865.25f, Z = 85.39f, Type = Rough },
                new Waypoint { X = -4660.60f, Y = 886.45f, Z = 86.91f, Type = Rough },
                new Waypoint { X = -4693.47f, Y = 926.51f, Z = 95.56f, Type = Rough },
                new Waypoint { X = -4691.15f, Y = 945.15f, Z = 98.84f, Type = Rough },
                new Waypoint { X = -4691.92f, Y = 969.01f, Z = 99.64f, Type = Rough },
                new Waypoint { X = -4718.80f, Y = 1011.86f, Z = 108.63f, Type = Rough },
                new Waypoint { X = -4694.92f, Y = 1035.84f, Z = 113.32f, Type = Rough },
                new Waypoint { X = -4680.00f, Y = 1038.54f, Z = 120.00f, Type = Rough },
                new Waypoint { X = -4663.62f, Y = 1068.80f, Z = 109.39f, Type = Rough },
                new Waypoint { X = -4645.12f, Y = 1090.71f, Z = 92.09f, Type = Rough },
                new Waypoint { X = -4632.21f, Y = 1103.47f, Z = 96.25f, Type = Rough },
                new Waypoint { X = -4641.95f, Y = 1116.38f, Z = 85.76f, Type = Rough },
                new Waypoint { X = -4650.97f, Y = 1127.90f, Z = 85.76f, Type = Rough },
                new Waypoint { X = -4670.76f, Y = 1158.40f, Z = 88.31f, Type = Rough },
                new Waypoint { X = -4666.44f, Y = 1200.08f, Z = 94.79f, Type = Rough },
                new Waypoint { X = -4638.11f, Y = 1243.96f, Z = 101.48f, Type = Rough },
                new Waypoint { X = -4618.39f, Y = 1280.24f, Z = 105.84f, Type = Rough },
                new Waypoint { X = -4581.36f, Y = 1308.35f, Z = 109.25f, Type = Rough },
                new Waypoint { X = -4559.00f, Y = 1325.68f, Z = 113.62f, Type = Rough },
                new Waypoint { X = -4520.99f, Y = 1331.07f, Z = 117.87f, Type = Rough },
                new Waypoint { X = -4465.49f, Y = 1332.13f, Z = 124.91f, Type = Rough },
                new Waypoint { X = -4405.75f, Y = 1332.50f, Z = 139.56f, Type = Rough },
                new Waypoint { X = -4369.93f, Y = 1332.98f, Z = 156.44f, Type = Rough },
                new Waypoint { X = -4353.27f, Y = 1333.62f, Z = 159.23f, Type = Rough },
            };

            // ========================================================
            // 部落前往厄运的航点坐标  _Horde
            // ========================================================
            List<Waypoint> routeToHome_Horde = new List<Waypoint> {
                new Waypoint { X = -4484.95f, Y = 233.53f, Z = 48.40f, Type = Precise },
                new Waypoint { X = -4472.29f, Y = 240.17f, Z = 47.34f, Type = Precise },
                new Waypoint { X = -4467.55f, Y = 232.28f, Z = 47.34f, Type = Precise },
                new Waypoint { X = -4450.55f, Y = 242.04f, Z = 39.11f, Type = Precise },
                new Waypoint { X = -4453.34f, Y = 246.70f, Z = 39.11f, Type = Precise },
                new Waypoint { X = -4442.87f, Y = 251.40f, Z = 39.11f, Type = Precise },
            };

            List<Waypoint> routeToInstanceDoor_Horde = new List<Waypoint>
            {
                new Waypoint { X = -4432.91f, Y = 258.55f, Z = 37.97f, Type = Precise },
                new Waypoint { X = -4418.82f, Y = 243.25f, Z = 30.78f, Type = Precise },
                new Waypoint { X = -4403.49f, Y = 229.91f, Z = 25.64f, Type = Precise },
                new Waypoint { X = -4398.39f, Y = 238.98f, Z = 25.47f, Type = Precise },
                new Waypoint { X = -4402.43f, Y = 262.17f, Z = 25.27f, Type = Precise },
                new Waypoint { X = -4405.85f, Y = 272.49f, Z = 25.15f, Type = Precise },
                new Waypoint { X = -4426.07f, Y = 277.45f, Z = 27.52f, Type = Precise },
                new Waypoint { X = -4448.31f, Y = 291.72f, Z = 34.13f, Type = Precise },
                new Waypoint { X = -4463.92f, Y = 302.38f, Z = 38.73f, Type = Precise },
                new Waypoint { X = -4478.32f, Y = 309.88f, Z = 39.68f, Type = Rough },
                new Waypoint { X = -4493.83f, Y = 316.58f, Z = 38.10f, Type = Rough },
                new Waypoint { X = -4513.27f, Y = 324.09f, Z = 35.64f, Type = Rough },
                new Waypoint { X = -4522.81f, Y = 329.02f, Z = 33.96f, Type = Rough },
                new Waypoint { X = -4532.67f, Y = 336.16f, Z = 33.20f, Type = Rough },
                new Waypoint { X = -4545.27f, Y = 345.14f, Z = 31.98f, Type = Rough },
                new Waypoint { X = -4556.50f, Y = 353.97f, Z = 31.94f, Type = Rough },
                new Waypoint { X = -4569.78f, Y = 364.22f, Z = 33.19f, Type = Rough },
                new Waypoint { X = -4586.85f, Y = 379.50f, Z = 34.52f, Type = Rough },
                new Waypoint { X = -4612.06f, Y = 404.62f, Z = 35.72f, Type = Rough },
                new Waypoint { X = -4619.28f, Y = 438.82f, Z = 36.57f, Type = Rough },
                new Waypoint { X = -4626.48f, Y = 510.28f, Z = 37.39f, Type = Rough },
                new Waypoint { X = -4642.24f, Y = 560.30f, Z = 38.83f, Type = Rough },
                new Waypoint { X = -4664.06f, Y = 635.87f, Z = 55.08f, Type = Rough },
                new Waypoint { X = -4680.55f, Y = 704.76f, Z = 72.54f, Type = Rough },
                new Waypoint { X = -4683.45f, Y = 730.35f, Z = 77.79f, Type = Rough },
                new Waypoint { X = -4658.85f, Y = 771.41f, Z = 83.78f, Type = Rough },
                new Waypoint { X = -4646.59f, Y = 846.71f, Z = 82.47f, Type = Rough },
                new Waypoint { X = -4641.81f, Y = 864.97f, Z = 85.43f, Type = Rough },
                new Waypoint { X = -4676.54f, Y = 905.89f, Z = 87.43f, Type = Rough },
                new Waypoint { X = -4692.45f, Y = 921.38f, Z = 93.88f, Type = Rough },
                new Waypoint { X = -4695.45f, Y = 929.31f, Z = 96.72f, Type = Rough },
                new Waypoint { X = -4688.00f, Y = 961.47f, Z = 98.87f, Type = Rough },
                new Waypoint { X = -4709.67f, Y = 995.14f, Z = 104.24f, Type = Rough },
                new Waypoint { X = -4714.20f, Y = 1013.99f, Z = 109.10f, Type = Rough },
                new Waypoint { X = -4736.84f, Y = 1012.81f, Z = 109.40f, Type = Rough },
                new Waypoint { X = -4779.08f, Y = 1028.71f, Z = 113.19f, Type = Rough },
                new Waypoint { X = -4853.15f, Y = 1063.25f, Z = 91.92f, Type = Rough },
                new Waypoint { X = -4839.31f, Y = 1105.29f, Z = 90.65f, Type = Rough },
                new Waypoint { X = -4815.32f, Y = 1141.81f, Z = 90.67f, Type = Rough },
                new Waypoint { X = -4835.49f, Y = 1228.20f, Z = 84.86f, Type = Rough },
                new Waypoint { X = -4857.62f, Y = 1281.24f, Z = 82.11f, Type = Rough },
                new Waypoint { X = -4827.80f, Y = 1301.71f, Z = 83.37f, Type = Rough },
                new Waypoint { X = -4764.35f, Y = 1309.88f, Z = 89.82f, Type = Rough },
                new Waypoint { X = -4639.63f, Y = 1333.08f, Z = 97.67f, Type = Rough },
                new Waypoint { X = -4588.10f, Y = 1330.21f, Z = 105.91f, Type = Rough },
                new Waypoint { X = -4561.39f, Y = 1299.77f, Z = 117.74f, Type = Precise },
                new Waypoint { X = -4551.33f, Y = 1304.45f, Z = 123.92f, Type = Precise },
                new Waypoint { X = -4542.19f, Y = 1305.52f, Z = 126.41f, Type = Precise },
                new Waypoint { X = -4516.79f, Y = 1311.82f, Z = 123.84f, Type = Precise },
                new Waypoint { X = -4482.22f, Y = 1314.97f, Z = 123.79f, Type = Rough },
                new Waypoint { X = -4470.36f, Y = 1330.66f, Z = 124.31f, Type = Rough },
                new Waypoint { X = -4447.79f, Y = 1333.60f, Z = 126.00f, Type = Rough },
                new Waypoint { X = -4423.21f, Y = 1346.29f, Z = 131.42f, Type = Rough },
                new Waypoint { X = -4404.95f, Y = 1346.42f, Z = 139.88f, Type = Rough },
                new Waypoint { X = -4380.19f, Y = 1346.21f, Z = 151.55f, Type = Rough },
            };

            List<Waypoint> route_GY_TwinColossals_Horde = new List<Waypoint> {
                new Waypoint { X = -4566.47f, Y = 1618.61f, Z = 95.40f, Type = Rough },
                new Waypoint { X = -4562.23f, Y = 1589.08f, Z = 101.48f, Type = Rough },
                new Waypoint { X = -4555.82f, Y = 1552.45f, Z = 103.58f, Type = Rough },
                new Waypoint { X = -4539.62f, Y = 1495.03f, Z = 101.72f, Type = Rough },
                new Waypoint { X = -4554.84f, Y = 1452.41f, Z = 102.82f, Type = Rough },
                new Waypoint { X = -4564.44f, Y = 1418.47f, Z = 103.54f, Type = Rough },
                new Waypoint { X = -4572.15f, Y = 1384.34f, Z = 107.89f, Type = Rough },
                new Waypoint { X = -4573.70f, Y = 1360.08f, Z = 107.47f, Type = Rough },
                new Waypoint { X = -4572.17f, Y = 1336.48f, Z = 109.70f, Type = Rough },
            };

            List<Waypoint> route_GY_Mojache_Horde = new List<Waypoint> {
                new Waypoint { X = -4441.58f, Y = 356.69f, Z = 51.21f, Type = Rough },
                new Waypoint { X = -4448.94f, Y = 346.19f, Z = 51.02f, Type = Rough },
                new Waypoint { X = -4456.46f, Y = 333.80f, Z = 50.19f, Type = Rough },
                new Waypoint { X = -4460.53f, Y = 323.80f, Z = 50.52f, Type = Rough },
                new Waypoint { X = -4467.74f, Y = 307.09f, Z = 39.08f, Type = Rough },
            };

            // ========================================================
            //                    公共坐标区域  
            // ========================================================

            // 【厄运北跑尸专线 部落联盟通用】（从双塔山墓地，一路直达副本门口的撞门点之前）   
            List<Waypoint> route_CorpseRun_Instance_North = new List<Waypoint>
            {
                new Waypoint { X = -4564.94f, Y = 1620.16f, Z = 95.46f, Type = Rough },
                new Waypoint { X = -4548.61f, Y = 1594.45f, Z = 101.39f, Type = Rough },
                new Waypoint { X = -4522.95f, Y = 1566.42f, Z = 109.52f, Type = Rough },
                new Waypoint { X = -4510.02f, Y = 1554.83f, Z = 116.04f, Type = Rough },
                new Waypoint { X = -4498.93f, Y = 1549.88f, Z = 124.33f, Type = Rough },
                new Waypoint { X = -4466.53f, Y = 1540.86f, Z = 126.70f, Type = Rough },
                new Waypoint { X = -4454.35f, Y = 1538.35f, Z = 128.14f, Type = Rough },
                new Waypoint { X = -4430.32f, Y = 1538.12f, Z = 126.92f, Type = Precise },
                new Waypoint { X = -4426.54f, Y = 1533.60f, Z = 127.93f, Type = Precise },
                new Waypoint { X = -4422.81f, Y = 1528.71f, Z = 128.53f, Type = Precise },
                new Waypoint { X = -4420.91f, Y = 1526.13f, Z = 128.69f, Type = Precise },
                new Waypoint { X = -4418.10f, Y = 1523.09f, Z = 128.16f, Type = Precise },
                new Waypoint { X = -4409.55f, Y = 1527.06f, Z = 133.07f, Type = Precise },
                new Waypoint { X = -4400.52f, Y = 1527.30f, Z = 137.99f, Type = Precise },
                new Waypoint { X = -4398.24f, Y = 1520.35f, Z = 143.86f, Type = Precise },
                new Waypoint { X = -4398.04f, Y = 1516.23f, Z = 148.24f, Type = Precise },
                new Waypoint { X = -4394.38f, Y = 1514.15f, Z = 150.61f, Type = Precise },
                new Waypoint { X = -4380.73f, Y = 1506.34f, Z = 150.61f, Type = Rough },
                new Waypoint { X = -4357.44f, Y = 1439.77f, Z = 150.61f, Type = Rough },
                new Waypoint { X = -4350.54f, Y = 1384.29f, Z = 153.91f, Type = Rough },
                new Waypoint { X = -4345.36f, Y = 1348.85f, Z = 159.24f, Type = Rough },
                new Waypoint { X = -4311.44f, Y = 1330.46f, Z = 159.24f, Type = Rough },
                new Waypoint { X = -4242.24f, Y = 1326.86f, Z = 161.21f, Type = Rough },
                new Waypoint { X = -4192.48f, Y = 1303.87f, Z = 161.21f, Type = Rough },
                new Waypoint { X = -4180.40f, Y = 1238.10f, Z = 161.21f, Type = Rough },
                new Waypoint { X = -4163.51f, Y = 1155.27f, Z = 161.21f, Type = Rough },
                new Waypoint { X = -4083.80f, Y = 1129.01f, Z = 161.21f, Type = Rough },
                new Waypoint { X = -4039.61f, Y = 1119.26f, Z = 159.74f, Type = Rough },
                new Waypoint { X = -4019.36f, Y = 1073.83f, Z = 159.80f, Type = Rough },
                new Waypoint { X = -3982.84f, Y = 1066.04f, Z = 161.05f, Type = Rough },
                new Waypoint { X = -3981.59f, Y = 1114.37f, Z = 161.02f, Type = Rough },
                new Waypoint { X = -3960.82f, Y = 1125.51f, Z = 161.05f, Type = Rough },
                new Waypoint { X = -3931.51f, Y = 1136.37f, Z = 149.28f, Type = Rough },
                new Waypoint { X = -3909.14f, Y = 1141.86f, Z = 149.31f, Type = Rough },
                new Waypoint { X = -3883.04f, Y = 1145.17f, Z = 154.38f, Type = Rough },
                new Waypoint { X = -3859.43f, Y = 1147.79f, Z = 154.79f, Type = Rough },
                new Waypoint { X = -3837.41f, Y = 1148.43f, Z = 154.79f, Type = Rough },
                new Waypoint { X = -3803.36f, Y = 1152.25f, Z = 154.79f, Type = Rough },
                new Waypoint { X = -3774.49f, Y = 1172.86f, Z = 154.79f, Type = Rough },
                new Waypoint { X = -3742.13f, Y = 1171.06f, Z = 154.79f, Type = Rough },
                new Waypoint { X = -3713.19f, Y = 1159.59f, Z = 154.79f, Type = Rough },
                new Waypoint { X = -3677.08f, Y = 1146.56f, Z = 154.79f, Type = Rough },
                new Waypoint { X = -3652.33f, Y = 1141.23f, Z = 154.79f, Type = Rough },
                new Waypoint { X = -3633.57f, Y = 1137.20f, Z = 154.79f, Type = Rough },
                new Waypoint { X = -3606.41f, Y = 1130.94f, Z = 150.87f, Type = Rough },
                new Waypoint { X = -3569.75f, Y = 1123.56f, Z = 149.55f, Type = Rough },
                new Waypoint { X = -3553.75f, Y = 1122.03f, Z = 157.23f, Type = Rough },
                new Waypoint { X = -3538.55f, Y = 1119.81f, Z = 161.03f, Type = Rough },
                new Waypoint { X = -3523.85f, Y = 1116.52f, Z = 161.02f, Type = Rough },
                new Waypoint { X = -3520.54f, Y = 1108.97f, Z = 161.03f, Type = Rough },
                new Waypoint { X = -3520.05f, Y = 1100.18f, Z = 161.03f, Type = Rough },
                new Waypoint { X = -3519.70f, Y = 1090.95f, Z = 161.06f, Type = Rough },
                new Waypoint { X = -3520.17f, Y = 1082.16f, Z = 161.12f, Type = Rough },   //厄运北门外
                new Waypoint { X = -3520.27f, Y = 1071.72f, Z = 162.59f, Type = Rough },    //进本
            };

            // 【厄运之槌大门口楼梯处进入枢纽中心专线  部落联盟通用】
            List<Waypoint> routeToInstance = new List<Waypoint> {
                new Waypoint { X = -4366.18f, Y = 1344.59f, Z = 158.16f, Type = Rough },
                new Waypoint { X = -4337.05f, Y = 1341.57f, Z = 159.23f, Type = Rough },
                new Waypoint { X = -4312.72f, Y = 1331.71f, Z = 159.23f, Type = Rough },
                new Waypoint { X = -4274.70f, Y = 1330.55f, Z = 161.21f, Type = Rough },
                new Waypoint { X = -4243.98f, Y = 1335.14f, Z = 161.21f, Type = Rough },
                new Waypoint { X = -4217.23f, Y = 1329.56f, Z = 161.21f, Type = Rough },
                new Waypoint { X = -4206.56f, Y = 1316.92f, Z = 161.21f, Type = Precise },
                new Waypoint { X = -4202.33f, Y = 1299.73f, Z = 161.21f, Type = Precise },
                new Waypoint { X = -4202.50f, Y = 1271.96f, Z = 161.21f, Type = Rough },
                new Waypoint { X = -4195.35f, Y = 1244.66f, Z = 161.21f, Type = Precise },
                new Waypoint { X = -4190.74f, Y = 1235.48f, Z = 161.21f, Type = Precise },
                new Waypoint { X = -4161.27f, Y = 1205.48f, Z = 161.21f, Type = Rough },
                new Waypoint { X = -4159.73f, Y = 1179.37f, Z = 161.21f, Type = Rough },
                new Waypoint { X = -4159.85f, Y = 1150.49f, Z = 161.21f, Type = Precise },
                new Waypoint { X = -4144.37f, Y = 1147.82f, Z = 161.21f, Type = Precise },
                new Waypoint { X = -4135.77f, Y = 1137.80f, Z = 161.21f, Type = Rough },
                new Waypoint { X = -4122.64f, Y = 1129.54f, Z = 161.21f, Type = Rough },
                new Waypoint { X = -4107.54f, Y = 1125.09f, Z = 161.21f, Type = Rough },
                new Waypoint { X = -4082.86f, Y = 1126.27f, Z = 161.21f, Type = Rough },
                new Waypoint { X = -4064.85f, Y = 1122.92f, Z = 161.21f, Type = Precise },
                new Waypoint { X = -4049.42f, Y = 1104.25f, Z = 159.79f, Type = Precise },
                new Waypoint { X = -4045.08f, Y = 1094.40f, Z = 159.77f, Type = Precise },
                new Waypoint { X = -4031.06f, Y = 1083.25f, Z = 159.71f, Type = Precise },
                new Waypoint { X = -4009.42f, Y = 1074.32f, Z = 161.09f, Type = Precise },
                new Waypoint { X = -4002.35f, Y = 1069.56f, Z = 161.09f, Type = Precise },
                new Waypoint { X = -3983.84f, Y = 1068.40f, Z = 161.05f, Type = Precise },
                new Waypoint { X = -3979.99f, Y = 1127.21f, Z = 161.03f, Type = Rough },
                new Waypoint { X = -3960.07f, Y = 1127.97f, Z = 161.05f, Type = Rough },
                new Waypoint { X = -3927.11f, Y = 1126.52f, Z = 148.91f, Type = Rough },
                new Waypoint { X = -3900.66f, Y = 1126.59f, Z = 150.39f, Type = Rough },
                new Waypoint { X = -3877.80f, Y = 1124.38f, Z = 154.79f, Type = Rough },  //厄运内部中心点 枢纽位置
            };

            // 【厄运之槌枢纽到厄运北专线  部落联盟通用】     
            List<Waypoint> routeToInstance_North = new List<Waypoint>
            {
                new Waypoint { X = -3839.18f, Y = 1153.30f, Z = 154.79f, Type = Rough },
                new Waypoint { X = -3813.24f, Y = 1155.45f, Z = 154.79f, Type = Rough },
                new Waypoint { X = -3813.00f, Y = 1183.89f, Z = 155.50f, Type = Rough },
                new Waypoint { X = -3778.83f, Y = 1189.71f, Z = 155.50f, Type = Rough },
                new Waypoint { X = -3733.10f, Y = 1189.09f, Z = 155.50f, Type = Rough },
                new Waypoint { X = -3717.57f, Y = 1188.57f, Z = 155.50f, Type = Rough },
                new Waypoint { X = -3692.20f, Y = 1183.80f, Z = 155.50f, Type = Rough },
                new Waypoint { X = -3664.85f, Y = 1172.64f, Z = 150.71f, Type = Rough },
                new Waypoint { X = -3650.99f, Y = 1181.76f, Z = 150.23f, Type = Rough },
                new Waypoint { X = -3620.98f, Y = 1179.45f, Z = 151.62f, Type = Rough },
                new Waypoint { X = -3596.12f, Y = 1180.67f, Z = 152.11f, Type = Rough },
                new Waypoint { X = -3597.77f, Y = 1158.33f, Z = 150.71f, Type = Rough },
                new Waypoint { X = -3570.23f, Y = 1136.77f, Z = 149.47f, Type = Rough },
                new Waypoint { X = -3554.89f, Y = 1129.94f, Z = 156.63f, Type = Rough },
                new Waypoint { X = -3537.78f, Y = 1124.56f, Z = 161.03f, Type = Rough },
                new Waypoint { X = -3525.10f, Y = 1118.64f, Z = 161.02f, Type = Rough },
                new Waypoint { X = -3521.77f, Y = 1107.31f, Z = 161.03f, Type = Rough },    //在这里预留假死/开门
            };

            // 厄运副本门口到厄运枢纽和厄运北路线  双阵营通用  正向！
            Node Enter_Dire_Maul_Branch = new Sequence(
                // 【第一段】：如果还在外围走廊，就跑去枢纽中心！
                new Selector(
                    new Sequence(
                        new ConditionNode(() => {
                            float x = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9B8);
                            float y = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9BC);
                            return x > -4400f && x < -3900f && y < 1350f;
                        }),
                        new ActionNode(() => {
                            Logger.Write($"[{CurrentPlayerName}] [寻路系统] 开始执行 去厄运枢纽路线");
                            return NodeState.Success;
                        }),
                        new SmartPathNode(this, () => _hProcess, () => _playerBase, routeToInstance, false, false)
                    ),
                    // 如果已经过了走廊了（条件不满足），直接返回 Success 放行给下一步！
                    new ActionNode(() => { return NodeState.Success; })
                ),

                // 【第二段】：从枢纽直奔厄运北！
                new ActionNode(() => {
                    Logger.Write($"[{CurrentPlayerName}] [寻路系统] 正常从枢纽进入或断点重启，前往厄运北...");
                    return NodeState.Success;
                }),

                //new PreDungeonShieldNode(this, () => _hProcess, () => _playerBase),  // 如果猎人需要上猎豹/骑马，逻辑可以走此Node
                new SmartPathNode(this, () => _hProcess, () => _playerBase, routeToInstance_North, false, false),

                // ==========================================
                // 【猎人专属：厄运北假死开门逻辑预留节点】
                // ==========================================
                new ActionNode(() => {
                    // 判断是否已经跑到了北门门禁前 (大约 X: -3521, Y: 1107)
                    if (GetDistanceToCoords(_hProcess, _playerBase, -3521.77f, 1107.31f, 161.03f) < 15.0f)
                    {
                        Logger.Write($"[{CurrentPlayerName}] [门禁系统] 已到达厄运北大门，准备执行假死/开门进本逻辑...");
                        StopMovement(_hProcess, _playerBase);
                        //ExecuteDynamicLua("CastSpellByName('假死')");
                    }
                    return NodeState.Success;
                }),
                //施放假死
                CreateFeignDeathNode(),
                new WaitNode(1000),

                new ActionNode(() => {
                    _ = Jump(50);
                    return NodeState.Success;
                    
                }),
                new WaitNode(500),
                //前往开门坐标
                new ActionNode(() => {
                    if (MoveTo(_hProcess, _playerBase, -3520.17f, 1099.43f, 161.03f))
                    {
                        StopMovement(_hProcess, _playerBase);
                        return NodeState.Success;
                    }
                    return NodeState.Running;
                }),

                new WaitNode(100),

                new ActionNode(() =>
                {
                    uint torchBase = GetGameObjectBaseByEntry(_hProcess, 177192);  // 厄运西大门火把开门拉杆ID
                    if (torchBase != 0)
                    {
                        RightClickGameObject(torchBase);   // 右键GameObject
                        Logger.Write($"[{CurrentPlayerName}] [门禁系统] 成功向厄运北大门 (0x{torchBase:X}) 发送了 1 次右键 Call！");
                        return NodeState.Success;
                    }
                    Logger.Write($"[{CurrentPlayerName}] [门禁系统] 没找到厄运北大门，请确认你在厄运北大门 3 码以内");
                    return NodeState.Running; // 没找到必须卡住，不然会撞门！
                }),

                new WaitNode(5500), // 给门打开留点时间


                new ActionNode(() => {
                    if (MoveTo(_hProcess, _playerBase, -3519.70f, 1090.95f, 161.06f))
                    {
                        return NodeState.Success;
                    }
                    return NodeState.Running;
                }),

                new ActionNode(() => {
                    if (MoveTo(_hProcess, _playerBase, -3520.17f, 1082.16f, 161.12f))    //本外
                    {
                        return NodeState.Success;
                    }
                    return NodeState.Running;
                }),

                new ActionNode(() => {
                    if (MoveTo(_hProcess, _playerBase, -3520.27f, 1071.72f, 162.59f))    //进本
                    {
                        return NodeState.Success;
                    }
                    return NodeState.Running;
                }),

                // ==========================================
                // 【防爆本专属死磕循环】
                // ==========================================
                new ActionNode(() => {
                    Logger.Write($"[{CurrentPlayerName}] [防爆本系统] 撞门 3 秒后仍在野外，确诊为爆本！启动退拽死磕模式...");
                    return NodeState.Success;
                }),

                // 倒车到门外航点安全距离 (-3525.10, 1118.64)
                new ActionNode(() => {
                    if (MoveTo(_hProcess, _playerBase, -3520.17f, 1082.16f, 161.12f))
                    {
                        return NodeState.Success;
                    }
                    return NodeState.Running;
                }),

                new WaitNode(100),

                // 再次向北大门蓝条推土机冲刺！
                new ActionNode(() => {
                    Logger.Write($"[{CurrentPlayerName}] [防爆本系统] 倒车缓冲完毕，再次尝试冲刺撞北大门！");
                    MoveToOneShot(_playerBase, -3520.27f, 1071.72f, 162.59f);
                    return NodeState.Success;
                }),

                new WaitNode(5000)
            );

            // 厄运出本总控：枢纽/北门 到 厄运外部野外大门 (双阵营通用)  反向
            Node Export_Dire_Maul_Branch = new Sequence(

                // 0. 前提条件：必须还在厄运内部区域
                new ConditionNode(() => {
                    float x = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9B8);
                    float y = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9BC);
                    return x > -4360f && y < 1350f;
                }),

                // ==========================================
                // 阶段一：支线收口 (如果在北门走廊，先强制退回枢纽！)
                // ==========================================
                new Selector(
                    // 场景：人在【厄运北】支线走廊 (特征：北通道的X基本都大于 -3850)
                    new Sequence(
                        new ConditionNode(() => {
                            float myX = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9B8);
                            return myX > -3850f;
                        }),
                        new ActionNode(() => {
                            Logger.Write($"[{CurrentPlayerName}] [寻路系统] 位于厄运北走廊，准备退回枢纽...");
                            return NodeState.Success;
                        }),

                        // 启动北线退回路线 -> 直达枢纽中心！(参数6 isReverse 传入 true)
                        new SmartPathNode(this, () => _hProcess, () => _playerBase, routeToInstance_North, false, true)
                    ),

                    // 兜底放行！如果不大于-3850，说明已经在【枢纽中心】或【外走廊主干道】上了！
                    new ActionNode(() => NodeState.Success)
                ),

                // ==========================================
                // 阶段二：干道集体冲刺 (从枢纽一路跑出野外大门)
                // ==========================================
                new ActionNode(() => {
                    Logger.Write($"[{CurrentPlayerName}] [寻路系统] 厄运主干道：正从枢纽向野外大门跑去...");
                    return NodeState.Success;
                }),

                // 启动 routeToInstance 反转路线 (终点在外围营地大门)
                new SmartPathNode(this, () => _hProcess, () => _playerBase, routeToInstance, false, true)
            );


            // ========================================================
            //             【跑尸专用】：全境贯通超级国道
            // ========================================================

            // 1. 联盟基础主干道 (旅店 -> 枢纽)
            List<Waypoint> route_MasterCorpseRoad_Alliance = new List<Waypoint>();
            route_MasterCorpseRoad_Alliance.AddRange(routeToHome_Alliance);
            route_MasterCorpseRoad_Alliance.AddRange(routeToShore_Alliance);
            route_MasterCorpseRoad_Alliance.AddRange(routeToDock_Alliance);
            route_MasterCorpseRoad_Alliance.AddRange(routeToInstanceDoor_Alliance);
            route_MasterCorpseRoad_Alliance.AddRange(routeToInstance);

            // 2. 部落基础主干道 (旅店 -> 枢纽)
            List<Waypoint> route_MasterCorpseRoad_Horde = new List<Waypoint>();
            route_MasterCorpseRoad_Horde.AddRange(routeToHome_Horde);
            route_MasterCorpseRoad_Horde.AddRange(routeToInstanceDoor_Horde);
            route_MasterCorpseRoad_Horde.AddRange(routeToInstance);

            // 【联盟 - 厄运北专线】
            List<Waypoint> route_MasterCorpseRoad_Alliance_North = new List<Waypoint>(route_MasterCorpseRoad_Alliance);
            route_MasterCorpseRoad_Alliance_North.AddRange(routeToInstance_North);

            // 【部落 - 厄运北专线】
            List<Waypoint> route_MasterCorpseRoad_Horde_North = new List<Waypoint>(route_MasterCorpseRoad_Horde);
            route_MasterCorpseRoad_Horde_North.AddRange(routeToInstance_North);


            // ========================================================
            // 部落去副本上班状态机（正向行驶）
            // ========================================================
            _runToInstanceTree_Horde = new Selector(

                new Sequence(
                    new ConditionNode(() => {
                        float x = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9B8);
                        float y = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9BC);
                        return x < -4430f && y < 260f;
                    }),
                    new ActionNode(() => {
                        Logger.Write($"[{CurrentPlayerName}] [寻路系统] 开始执行 部落从旅店前往营地大门口路线");
                        return NodeState.Success;
                    }),
                    new SmartPathNode(this, () => _hProcess, () => _playerBase, routeToHome_Horde, false, false),

                    new ActionNode(() => {
                        if (MoveTo(_hProcess, _playerBase, -4432.91f, 258.55f, 37.97f, 0.5f))
                        {
                            StopMovement(_hProcess, _playerBase);
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new WaitNode(100),
                    new ActionNode(() =>
                    {
                        // 可以在此补充猎人雄鹰/猎豹守护
                        // CastSpell(12);
                        return NodeState.Success;
                    }),
                    new WaitNode(500),
                    new ActionNode(() =>
                    {
                        _ = Use_Mount(50);
                        return NodeState.Success;
                    }),
                    new WaitNode(3500),

                    new ActionNode(() => {
                        if (MoveTo(_hProcess, _playerBase, -4418.82f, 243.25f, 30.78f, 1.0f))
                        {
                            StopMovement(_hProcess, _playerBase);
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    })
                ),

                new Sequence(
                    new ConditionNode(() => {
                        float x = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9B8);
                        float y = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9BC);
                        bool inInn = x < -4430f && y < 260f;
                        bool isAtStagingArea = (x > -4400f && y < 1350f);
                        return !inInn && x < -4350f && !isAtStagingArea;
                    }),
                    new ActionNode(() => {
                        Logger.Write($"[{CurrentPlayerName}] [寻路系统] 开始执行 部落去副本门口路线...");
                        return NodeState.Success;
                    }),
                    new SmartPathNode(this, () => _hProcess, () => _playerBase, routeToInstanceDoor_Horde, false, false),
                    new ActionNode(() => {
                        StopMovement(_hProcess, _playerBase);
                        return NodeState.Success;
                    }),
                    new PreDungeonShieldNode(this, () => _hProcess, () => _playerBase)

                ),
                Enter_Dire_Maul_Branch

            );

            // ========================================================
            // 部落回城清包状态机（原路返回）
            // ========================================================
            _runToInnTree_Horde = new Selector(

                Export_Dire_Maul_Branch,

                new Sequence(
                    new ConditionNode(() => {
                        float x = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9B8);
                        float y = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9BC);
                        bool inInn = x < -4430f && y < 260f;
                        return x <= -4360f && !inInn;
                    }),
                    new ActionNode(() => {
                        Logger.Write($"[{CurrentPlayerName}] [寻路系统] 开始执行 部落从副本门口回营地路线...");
                        return NodeState.Success;
                    }),

                    new SmartPathNode(this, () => _hProcess, () => _playerBase, routeToInstanceDoor_Horde, false, true),
                    new ActionNode(() => { StopMovement(_hProcess, _playerBase); return NodeState.Success; }),
                    new ActionNode(() => {
                        if (MoveTo(_hProcess, _playerBase, -4442.87f, 251.40f, 39.11f, 1.0f)) { StopMovement(_hProcess, _playerBase); return NodeState.Success; }
                        return NodeState.Running;
                    })
                ),

                new Sequence(
                    new ConditionNode(() => {
                        float x = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9B8);
                        float y = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9BC);
                        return x < -4430f && y < 260f;
                    }),
                    new ActionNode(() => {
                        Logger.Write($"[{CurrentPlayerName}] [寻路系统] 开始执行 部落旅店门口去售卖NPC路线...");
                        return NodeState.Success;
                    }),

                    new SmartPathNode(this, () => _hProcess, () => _playerBase, routeToHome_Horde, false, true),

                    new ActionNode(() => { StopMovement(_hProcess, _playerBase); return NodeState.Success; }),
                    new WaitNode(500),
                    new DestroyGarbageNode(this, () => _hProcess, () => _playerBase),
                    new WaitNode(500),

                    new ActionNode(() =>
                    {
                        int targetBase = GetTargetBaseByGuid(_hProcess, 0xF13000254C00C39C);  // 卡温德
                        if (targetBase != 0)
                        {
                            RightClickUnit((uint)targetBase);
                            Logger.Write($"[{CurrentPlayerName}] [商人系统] 成功隔空右键商人（卡温德），准备清理背包！");
                        }
                        return NodeState.Success;
                    }),
                    new WaitNode(500),

                    // 售卖垃圾节点 
                    new ActionNode(() =>
                    {
                        if (!isSellingInitialized)
                        {
                            // 🌟 核心修改：用基类保存的 AccountName 去 JSON 里精准拉取配置！
                            AccountConfig currentConfig = ConfigManager.GetConfig(this.AccountName) ?? new AccountConfig();
                            HashSet<int> whitelistIds = ItemDbManager.ParseNamesToIds(currentConfig.Whitelist);
                            List<InventoryItem> allItems = GetAllInventoryItems(_hProcess);

                            itemsToSell = allItems.FindAll(item =>
                            {
                                if (item.IsSoulbound) return false;
                                if (BuiltInWhitelist.Contains(item.ItemId)) return false;
                                if (whitelistIds.Contains(item.ItemId)) return false;
                                if (ItemDbManager.IdMap.TryGetValue(item.ItemId, out var template))
                                {
                                    int quality = template.Quality_ID;
                                    if (quality == 3 && currentConfig.KeepExquisite) return false;
                                    if (quality == 4 && currentConfig.KeepEpic) return false;
                                    if (quality >= 5) return false;
                                }
                                return true;
                            });

                            Logger.Write($"[{CurrentPlayerName}] [背包系统] 背包扫描完毕。总物品: {allItems.Count} | 待售卖: {itemsToSell.Count}");

                            sellIndex = 0;
                            isSellingInitialized = true;
                            nextSellTime = DateTime.Now;

                            if (itemsToSell.Count == 0)
                            {
                                isSellingInitialized = false;
                                _needGoHome = false;
                                _hasAttemptedSell = true;
                                ExecuteDynamicLua("RepairAllItems()");
                                new WaitNode(500);
                                ExecuteDynamicLua("CloseMerchant()");
                                Logger.Write($"[{CurrentPlayerName}] [背包系统] 您的背包很干净，装备已修满，准备重返战场！");
                                return NodeState.Success;
                            }
                        }

                        if (sellIndex >= itemsToSell.Count)
                        {
                            isSellingInitialized = false;
                            _needGoHome = false;
                            _hasAttemptedSell = true;
                            ExecuteDynamicLua("RepairAllItems()");
                            new WaitNode(500);
                            ExecuteDynamicLua("CloseMerchant()");
                            Logger.Write($"[{CurrentPlayerName}] [背包系统] 垃圾清理完毕，装备已修满，准备重返战场！");
                            return NodeState.Success;
                        }

                        if (DateTime.Now < nextSellTime) return NodeState.Running;

                        var currentItem = itemsToSell[sellIndex];
                        ExecuteDynamicLua($"UseContainerItem({currentItem.BagId}, {currentItem.SlotId})");
                        //RightClickBagItem(currentItem.BagId, currentItem.SlotId);
                        sellIndex++;
                        nextSellTime = DateTime.Now.AddMilliseconds(300);
                        return NodeState.Running;
                    })
                )
            );


            // ========================================================
            // 联盟去副本上班状态机（正向行驶）
            // ========================================================
            _runToInstanceTree_Alliance = new Selector(

                new Sequence(
                    new ConditionNode(() => MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9BC) >= 3260f),
                    new ActionNode(() => {
                        Logger.Write($"[{CurrentPlayerName}] [寻路系统] 开始执行 联盟从旅店前往门口上马点");
                        return NodeState.Success;
                    }),
                    new SmartPathNode(this, () => _hProcess, () => _playerBase, routeToHome_Alliance, false, false),
                    new ActionNode(() => {
                        if (MoveTo(_hProcess, _playerBase, -4374.14f, 3255.03f, 12.94f, 0.5f))
                        {
                            StopMovement(_hProcess, _playerBase);
                            return NodeState.Success;
                        }
                        return NodeState.Running;
                    }),
                    new WaitNode(100),
                    new ActionNode(() =>
                    {
                        // 可以在此补充猎人雄鹰/猎豹守护
                        // CastSpell(12);
                        return NodeState.Success;
                    }),
                    new WaitNode(500),
                    new ActionNode(() =>
                    {
                        _ = Use_Mount(50);
                        return NodeState.Success;
                    }),
                    new WaitNode(3500)
                ),

                new Sequence(
                    new ConditionNode(() => {
                        float y = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9BC);
                        return y >= 2340f && y < 3260f;
                    }),

                    new ActionNode(() => {
                        Logger.Write($"[{CurrentPlayerName}] [寻路系统] 开始执行 联盟从旅店门口跑向海边下水点路线");
                        return NodeState.Success;
                    }),
                    new SmartPathNode(this, () => _hProcess, () => _playerBase, routeToShore_Alliance, false, false)
                ),

                new Sequence(
                    new ConditionNode(() => {
                        float y = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9BC);
                        return y >= 2340f && y < 3260f;
                    }),

                    new ActionNode(() => {
                        Logger.Write($"[{CurrentPlayerName}] [寻路系统] 开始执行 联盟从海边下水点到对面码头路线");
                        return NodeState.Success;
                    }),
                    new SmartPathNode(this, () => _hProcess, () => _playerBase, routeToDock_Alliance, false, false),
                    new ActionNode(() => {
                        StopMovement(_hProcess, _playerBase);
                        return NodeState.Success;
                    }),

                    new WaitNode(500),
                    new ActionNode(() =>
                    {
                        _ = Use_Mount(50);
                        return NodeState.Success;
                    }),
                    new WaitNode(3500)
                ),

                new Sequence(
                    new ConditionNode(() => {
                        float x = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9B8);
                        float y = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9BC);
                        bool isAtStagingArea = (x > -4360f && y < 1350f);
                        return y < 2340f && !isAtStagingArea;
                    }),

                    new ActionNode(() => {
                        Logger.Write($"[{CurrentPlayerName}] [寻路系统] 开始执行 联盟去副本门口路线");
                        return NodeState.Success;
                    }),
                    new SmartPathNode(this, () => _hProcess, () => _playerBase, routeToInstanceDoor_Alliance, false, false),
                    new ActionNode(() => {
                        StopMovement(_hProcess, _playerBase);
                        return NodeState.Success;
                    }),
                    new PreDungeonShieldNode(this, () => _hProcess, () => _playerBase)
                ),

                Enter_Dire_Maul_Branch

            );

            // ========================================================
            // 联盟回城清包状态机（原路返回）
            // ========================================================
            _runToInnTree_Alliance = new Selector(

                Export_Dire_Maul_Branch,

                new Sequence(
                    new ConditionNode(() => {
                        float x = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9B8);
                        float y = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9BC);
                        bool isAtStagingArea = (x > -4360f && y < 1350f);
                        return y < 2340f && !isAtStagingArea;
                    }),
                    new SmartPathNode(this, () => _hProcess, () => _playerBase, routeToInstanceDoor_Alliance, false, true),
                    new ActionNode(() => { StopMovement(_hProcess, _playerBase); return NodeState.Success; }),
                    new WaitNode(500),
                    new ActionNode(() =>
                    {
                        if (MoveTo(_hProcess, _playerBase, -4333.98f, 2436.23f, -1.17f, 1.0f)) return NodeState.Success;
                        return NodeState.Running;
                    })
                ),

                new Sequence(
                    new ConditionNode(() =>
                    {
                        float y = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9BC);
                        return y >= 2340f && y < 3260f;
                    }),

                    new ActionNode(() => {
                        Logger.Write($"[{CurrentPlayerName}] [寻路系统] 开始执行 联盟从码头游到营地下水点路线");
                        return NodeState.Success;
                    }),
                    new SmartPathNode(this, () => _hProcess, () => _playerBase, routeToDock_Alliance, false, true),
                    new ActionNode(() => {
                        StopMovement(_hProcess, _playerBase);
                        return NodeState.Success;
                    }),

                    new WaitNode(500),
                    new ActionNode(() =>
                    {
                        _ = Use_Mount(50);
                        return NodeState.Success;
                    }),
                    new WaitNode(3500)
                ),

                new Sequence(
                    new ConditionNode(() =>
                    {
                        float y = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9BC);
                        return y >= 2340f && y < 3260f;
                    }),

                    new ActionNode(() => {
                        Logger.Write($"[{CurrentPlayerName}] [寻路系统] 开始执行 联盟从下水点游到营地路线");
                        return NodeState.Success;
                    }),
                    new SmartPathNode(this, () => _hProcess, () => _playerBase, routeToShore_Alliance, false, true)
                ),

                new Sequence(
                    new ConditionNode(() => MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9BC) >= 3260f),
                    new ActionNode(() => {
                        Logger.Write($"[{CurrentPlayerName}] [寻路系统] 开始执行 联盟旅店门口去售卖NPC路线...");
                        return NodeState.Success;
                    }),
                    new SmartPathNode(this, () => _hProcess, () => _playerBase, routeToHome_Alliance, false, true),
                    new ActionNode(() => { StopMovement(_hProcess, _playerBase); return NodeState.Success; }),
                    new WaitNode(500),
                    new DestroyGarbageNode(this, () => _hProcess, () => _playerBase),
                    new WaitNode(500),
                    new ActionNode(() =>
                    {
                        int targetBase = GetTargetBaseByGuid(_hProcess, 0xF13000283500C38A);  // 杜希雅·霜月
                        if (targetBase != 0)
                        {
                            RightClickUnit((uint)targetBase);
                            Logger.Write($"[{CurrentPlayerName}] [商人系统] 成功隔空右键商人（杜希雅·霜月），准备清理背包！");
                        }
                        return NodeState.Success;

                    }),
                    new WaitNode(500),

                    // 售卖垃圾节点
                    new ActionNode(() =>
                    {
                        if (!isSellingInitialized)
                        {
                            // 🌟 核心修改：用基类保存的 AccountName 去 JSON 里精准拉取配置！
                            AccountConfig currentConfig = ConfigManager.GetConfig(this.AccountName) ?? new AccountConfig();
                            HashSet<int> whitelistIds = ItemDbManager.ParseNamesToIds(currentConfig.Whitelist);
                            List<InventoryItem> allItems = GetAllInventoryItems(_hProcess);

                            itemsToSell = allItems.FindAll(item =>
                            {
                                if (item.IsSoulbound) return false;
                                if (BuiltInWhitelist.Contains(item.ItemId)) return false;
                                if (whitelistIds.Contains(item.ItemId)) return false;
                                if (ItemDbManager.IdMap.TryGetValue(item.ItemId, out var template))
                                {
                                    int quality = template.Quality_ID;
                                    if (quality == 3 && currentConfig.KeepExquisite) return false;
                                    if (quality == 4 && currentConfig.KeepEpic) return false;
                                    if (quality >= 5) return false;
                                }
                                return true;
                            });

                            Logger.Write($"[{CurrentPlayerName}] [背包系统] 背包扫描完毕。总物品: {allItems.Count} | 待售卖: {itemsToSell.Count}");

                            sellIndex = 0;
                            isSellingInitialized = true;
                            nextSellTime = DateTime.Now;

                            if (itemsToSell.Count == 0)
                            {
                                isSellingInitialized = false;
                                _needGoHome = false;
                                _hasAttemptedSell = true;
                                ExecuteDynamicLua("RepairAllItems()");
                                new WaitNode(500);
                                ExecuteDynamicLua("CloseMerchant()");
                                Logger.Write($"[{CurrentPlayerName}] [背包系统] 您的背包很干净，装备已修满，准备重返战场！");
                                return NodeState.Success;
                            }
                        }

                        if (sellIndex >= itemsToSell.Count)
                        {
                            isSellingInitialized = false;
                            _needGoHome = false;
                            _hasAttemptedSell = true;
                            ExecuteDynamicLua("RepairAllItems()");
                            new WaitNode(500);
                            ExecuteDynamicLua("CloseMerchant()");
                            Logger.Write($"[{CurrentPlayerName}] [背包系统] 垃圾清理完毕，装备已修满，准备重返战场！");
                            return NodeState.Success;
                        }

                        if (DateTime.Now < nextSellTime) return NodeState.Running;

                        var currentItem = itemsToSell[sellIndex];
                        //RightClickBagItem(currentItem.BagId, currentItem.SlotId);
                        ExecuteDynamicLua($"UseContainerItem({currentItem.BagId}, {currentItem.SlotId})");
                        sellIndex++;
                        nextSellTime = DateTime.Now.AddMilliseconds(300);
                        return NodeState.Running;
                    })
                )
            );

            // 野外跑尸与副本跑尸状态机
            Node corpseRunBranch = new Sequence(
                new ConditionNode(() => _isDead == true),
                new Selector(
                    new Sequence(
                        new ConditionNode(() => !_hasReleasedSpirit),
                        new ActionNode(() => {
                            StopMovement(_hProcess, _playerBase);
                            Logger.Write($"[{CurrentPlayerName}] [野外跑尸] 确认死亡，已发送释放灵魂指令！正在等待服务器传送...");
                            ExecuteDynamicLua("RepopMe()");     //释放灵魂
                            _hasReleasedSpirit = true;
                            _releaseSpiritTime = DateTime.Now;
                            return NodeState.Success;
                        })
                    ),

                    // 【副本死亡专线】
                    new Sequence(
                        new ConditionNode(() => {
                            if ((DateTime.Now - _releaseSpiritTime).TotalSeconds < 5) return false;
                            return IsCorpseInInstance(_hProcess, -3908.03f, 1130.0f) && _hasReleasedSpirit && HasGhostDebuff(_hProcess, _playerBase);
                        }),
                        new Selector(
                            // 1. 已跨入副本 (MapID=429)，复活！
                            new Sequence(
                                new ConditionNode(() => _mapId == 429),
                                new ActionNode(() => {
                                    StopMovement(_hProcess, _playerBase);
                                    Logger.Write($"[{CurrentPlayerName}] [副本复活] 已跨入副本区域！已复活！");
                                    ExecuteDynamicLua("RetrieveCorpse()");
                                    return NodeState.Success;
                                }),
                                new WaitNode(3000)
                            ),
                            // 2. 在门外 10 码内
                            new Sequence(
                                new ConditionNode(() => _mapId == 1 && GetDistanceToCoords(_hProcess, _playerBase, -3732.33f, 934.51f, 161.73f) < 10.0f),
                                new ActionNode(() => {
                                    if (MoveTo(_hProcess, _playerBase, -3741.32f, 934.54f, 160.99f, 0.5f))
                                    {
                                        StopMovement(_hProcess, _playerBase); return NodeState.Success;
                                    }
                                    return NodeState.Running;
                                }),
                                new WaitNode(1500),
                                new ActionNode(() => {
                                    MoveToOneShot(_playerBase, -3732.33f, 934.51f, 161.73f);
                                    return NodeState.Success;
                                }),
                                new WaitNode(5000)
                            ),
                            // 3. 没到门口，无脑跑厄运北专属路线！
                            new Sequence(
                                new ConditionNode(() => _mapId == 1),
                                new SmartPathNode(this, () => _hProcess, () => _playerBase, route_CorpseRun_Instance_North, true, false)
                            )
                        )
                    ),

                    // 【野外死亡专属逻辑】
                    new Sequence(
                        new ConditionNode(() => {
                            if ((DateTime.Now - _releaseSpiritTime).TotalSeconds < 5) return false;
                            return !IsCorpseInInstance(_hProcess, -3908.03f, 1130.0f) && _hasReleasedSpirit && HasGhostDebuff(_hProcess, _playerBase);
                        }),
                        new Selector(
                            // 1. 到达骨头堆复活！
                            new Sequence(
                                new ConditionNode(() => GetDistanceToCorpse(_hProcess, _playerBase) <= 10.0f),
                                new ActionNode(() => {
                                    StopMovement(_hProcess, _playerBase);
                                    Logger.Write($"[{CurrentPlayerName}] [野外跑尸] 到达骨头堆，尝试复活...");
                                    ExecuteDynamicLua("RetrieveCorpse()");
                                    return NodeState.Success;
                                }),
                                new WaitNode(3000)
                            ),

                            // 2. 尸体12码内盲扑
                            new Sequence(
                                new ConditionNode(() => {
                                    float cx = MemoryAPI.ReadFloat(_hProcess, ModuleBaseAddress + 0x74E284);
                                    if (cx == 0) return false;
                                    return GetDistanceToCorpse(_hProcess, _playerBase) <= 12.0f;
                                }),
                                new ActionNode(() => {
                                    float cx = MemoryAPI.ReadFloat(_hProcess, ModuleBaseAddress + 0x74E284);
                                    float cy = MemoryAPI.ReadFloat(_hProcess, ModuleBaseAddress + 0x74E288);
                                    float cz = MemoryAPI.ReadFloat(_hProcess, ModuleBaseAddress + 0x74E28C);

                                    if (cx == 0 && cy == 0) return NodeState.Failure;

                                    Logger.Write($"[{CurrentPlayerName}] [跑尸系统] 正在向真实尸体盲扑: X={cx}, Y={cy}");
                                    if (MoveTo(_hProcess, _playerBase, cx, cy, cz, 0.5f)) return NodeState.Success;
                                    return NodeState.Running;
                                })
                            ),

                            // 3. 墓地接驳线 —— 双阵营智能分流
                            new Selector(
                                // ================= 部落接驳线分流 =================
                                new Sequence(
                                    new ConditionNode(() => IsHordePlayer(false, false, _hProcess, _playerBase)),
                                    new Selector(
                                        new Sequence(
                                            new ConditionNode(() => {
                                                if (_activeGyRoute == -1) return false;
                                                if (_activeGyRoute == 4) return true;
                                                if (GetDistanceToCoords(_hProcess, _playerBase, -4439.97f, 370.15f, 51.36f) < 50.0f) { _activeGyRoute = 4; return true; }    //莫沙彻墓地
                                                return false;
                                            }),
                                            new ActionNode(() => {
                                                Logger.Write($"[{CurrentPlayerName}] [跑尸系统] 灵魂在莫沙彻墓地降生，启动专属接驳线！");
                                                return NodeState.Success;
                                            }),
                                            new SmartPathNode(this, () => _hProcess, () => _playerBase, route_GY_Mojache_Horde, true, false),
                                            new ActionNode(() => { _activeGyRoute = -1; return NodeState.Success; })
                                        ),
                                        new Sequence(
                                            new ConditionNode(() => {
                                                if (_activeGyRoute == -1) return false;
                                                if (_activeGyRoute == 5) return true;
                                                if (GetDistanceToCoords(_hProcess, _playerBase, -4590.41f, 1632.08f, 93.97f) < 50.0f) { _activeGyRoute = 5; return true; }    //双塔山墓地
                                                return false;
                                            }),
                                            new ActionNode(() => {
                                                Logger.Write($"[{CurrentPlayerName}] [跑尸系统] 灵魂在双塔山墓地降生，启动专属接驳线！");
                                                return NodeState.Success;
                                            }),
                                            new SmartPathNode(this, () => _hProcess, () => _playerBase, route_GY_TwinColossals_Horde, true, false),
                                            new ActionNode(() => { _activeGyRoute = -1; return NodeState.Success; })
                                        )
                                    )
                                ),

                                // ================= 联盟接驳线分流 =================
                                new Sequence(
                                    new ConditionNode(() => !IsHordePlayer(false, false, _hProcess, _playerBase)),
                                    new Selector(
                                        new Sequence(
                                            new ConditionNode(() => {
                                                if (_activeGyRoute == -1) return false;
                                                if (_activeGyRoute == 1) return true;
                                                if (GetDistanceToCoords(_hProcess, _playerBase, -4596.40f, 3229.43f, 8.99f) < 50.0f) { _activeGyRoute = 1; return true; }    //羽月要塞墓地
                                                return false;
                                            }),
                                            new ActionNode(() => {
                                                Logger.Write($"[{CurrentPlayerName}] [跑尸系统] 灵魂在羽月要塞墓地降生，启动专属接驳线！");
                                                return NodeState.Success;
                                            }),
                                            new SmartPathNode(this, () => _hProcess, () => _playerBase, route_GY_Feathermoon_Alliance, true, false),
                                            new ActionNode(() => { _activeGyRoute = -1; return NodeState.Success; })
                                        ),
                                        new Sequence(
                                            new ConditionNode(() => {
                                                if (_activeGyRoute == -1) return false;
                                                if (_activeGyRoute == 2) return true;
                                                if (GetDistanceToCoords(_hProcess, _playerBase, -4590.41f, 1632.08f, 93.97f) < 50.0f) { _activeGyRoute = 2; return true; }    //双塔山墓地
                                                return false;
                                            }),
                                            new ActionNode(() => {
                                                Logger.Write($"[{CurrentPlayerName}] [跑尸系统] 灵魂在双塔山墓地降生，启动专属接驳线！");
                                                return NodeState.Success;
                                            }),
                                            new SmartPathNode(this, () => _hProcess, () => _playerBase, route_GY_TwinColossals_Alliance, true, false),
                                            new ActionNode(() => { _activeGyRoute = -1; return NodeState.Success; })
                                        ),
                                        new Sequence(
                                            new ConditionNode(() => {
                                                if (_activeGyRoute == -1) return false;
                                                if (_activeGyRoute == 3) return true;
                                                if (GetDistanceToCoords(_hProcess, _playerBase, -4439.97f, 370.15f, 51.36f) < 50.0f) { _activeGyRoute = 3; return true; }    //莫沙彻墓地
                                                return false;
                                            }),
                                            new ActionNode(() => {
                                                Logger.Write($"[{CurrentPlayerName}] [跑尸系统] 灵魂在莫沙彻墓地降生，启动专属接驳线！");
                                                return NodeState.Success;
                                            }),
                                            new SmartPathNode(this, () => _hProcess, () => _playerBase, route_GY_Mojache_Alliance, true, false),
                                            new ActionNode(() => { _activeGyRoute = -1; return NodeState.Success; })
                                        )
                                    )
                                )
                            ),

                            // 4. 野外主干道智能寻路 (开启倒车雷达) —— 单一北线动态并网
                            new Selector(
                                // ================== 部落雷达路线 ==================
                                new Sequence(
                                    // 场景 A：纯野外死亡，使用原版基础主干道
                                    new ConditionNode(() => IsHordePlayer(false, false, _hProcess, _playerBase) && !IsCorpseInDireMaulArea(_hProcess)),
                                    new ActionNode(() => {
                                        Logger.Write($"[{CurrentPlayerName}] [跑尸系统] 侦测到野外死亡，锁定 部落->常规主干道！");
                                        return NodeState.Success;
                                    }),
                                    new SmartPathNode(this, () => _hProcess, () => _playerBase, route_MasterCorpseRoad_Horde, true, false)
                                ),
                                new Sequence(
                                    // 部落跑尸 + 尸体在厄运区域内
                                    new ConditionNode(() => IsHordePlayer(false, false, _hProcess, _playerBase) && IsCorpseInDireMaulArea(_hProcess)),
                                    new ActionNode(() => {
                                        Logger.Write($"[{CurrentPlayerName}] [跑尸系统] 锁定 部落->厄运北 动态干线！");
                                        return NodeState.Success;
                                    }),
                                    new SmartPathNode(this, () => _hProcess, () => _playerBase, route_MasterCorpseRoad_Horde_North, true, false)
                                ),

                                // ================== 联盟雷达路线 ==================
                                new Sequence(
                                    // 场景 A：纯野外死亡，使用原版基础主干道
                                    new ConditionNode(() => !IsHordePlayer(false, false, _hProcess, _playerBase) && !IsCorpseInDireMaulArea(_hProcess)),
                                    new ActionNode(() => {
                                        Logger.Write($"[{CurrentPlayerName}] [跑尸系统] 侦测到野外死亡，锁定 联盟->常规主干道！");
                                        return NodeState.Success;
                                    }),
                                    new SmartPathNode(this, () => _hProcess, () => _playerBase, route_MasterCorpseRoad_Alliance, true, false)
                                ),
                                new Sequence(
                                    // 联盟跑尸 + 尸体在厄运区域内
                                    new ConditionNode(() => !IsHordePlayer(false, false, _hProcess, _playerBase) && IsCorpseInDireMaulArea(_hProcess)),
                                    new ActionNode(() => {
                                        Logger.Write($"[{CurrentPlayerName}] [跑尸系统] 锁定 联盟->厄运北 动态干线！");
                                        return NodeState.Success;
                                    }),
                                    new SmartPathNode(this, () => _hProcess, () => _playerBase, route_MasterCorpseRoad_Alliance_North, true, false)
                                )
                            )
                        )
                    )
                )
            );

            _rootTree = new Selector(
                // 优先级 1：只要是死人，先跑尸复活！
                corpseRunBranch,

                // 优先级 2：冷启动野外最高优先级防猝死上马套盾分支
                new Sequence(
                    new ConditionNode(() =>
                    {
                        if (!_needStartupPrep) return false;
                        if (_isDead) return false;
                        if (_mapId == 429) return false;

                        if (Unit_Behavioral_State(_hProcess, _playerBase, 19)) return false;

                        if (!IsHordePlayer(false, false, _hProcess, _playerBase))
                        {
                            float startY = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9BC);
                            if (startY >= 2340f && startY < 3260f)
                            {
                                _needStartupPrep = false;
                                Logger.Write($"[{CurrentPlayerName}] [冷启动保护] 角色在海域中上线，直接走路。");
                                return false;
                            }
                        }

                        return true;
                    }),

                    new ActionNode(() =>
                    {
                        Logger.Write($"[{CurrentPlayerName}] [冷启动保护] 触发野外上线保护！重踩物理手刹，准备上马...");
                        StopMovement(_hProcess, _playerBase);
                        return NodeState.Success;
                    }),

                    new WaitNode(100),

                    new ActionNode(() =>
                    {
                         _ = Use_Mount(50);
                         new WaitNode(3500);
                         return NodeState.Success;
                    }),
                    

                    new ActionNode(() =>
                    {
                        _needStartupPrep = false;
                        Logger.Write($"[{CurrentPlayerName}] [冷启动保护] 首次野外尝试完毕！正式把控制权移交给主寻路模块！");
                        return NodeState.Success;
                    })
                ),

                // 优先级 3：自救回城路线
                new Sequence(
                    new ConditionNode(() => _isDead == false && _needGoHome == true && _mapId == 1),
                    new Selector(
                        new Sequence(new ConditionNode(() => IsHordePlayer(false, false, _hProcess, _playerBase)), _runToInnTree_Horde),
                        new Sequence(new ConditionNode(() => !IsHordePlayer(false, false, _hProcess, _playerBase)), _runToInnTree_Alliance)
                    )
                ),

                // 优先级 4：包空着，在野外/旅店，刚修完装备要出门打工
                new Sequence(
                    new ConditionNode(() => _isDead == false && _needGoHome == false && _mapId == 1 && MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9B8) < -1000f),
                    new Selector(
                        new Sequence(new ConditionNode(() => IsHordePlayer(false, false, _hProcess, _playerBase)), _runToInstanceTree_Horde),
                        new Sequence(new ConditionNode(() => !IsHordePlayer(false, false, _hProcess, _playerBase)), _runToInstanceTree_Alliance)
                    )
                ),

                // 优先级 5：终极目标 -> 在副本内无条件刷花
                new Sequence(
                    new ConditionNode(() => _isDead == false && (_mapId == 429 || MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9B8) > -1000f)),
                    _farmTree
                )
            );
        }


        public override string OnTick(IntPtr hProcess, int playerBase)
        {
            _hProcess = hProcess;
            _playerBase = playerBase;

            if (!IsInWorld(_hProcess) || _playerBase == 0)
            {
                if (_isTransitioning) _transitionFailsafe = DateTime.Now.AddSeconds(15);
                if (_wasInWorld)
                {
                    Logger.Write($"[{CurrentPlayerName}] [智能检测] 蓝条加载中...");
                    _wasInWorld = false;
                }

                return "蓝条加载中";
            }

            if (!_wasInWorld)
            {
                Logger.Write($"[{CurrentPlayerName}] [智能检测] 蓝条加载完毕，已自动清空行为树历史记录防错乱！");
                // 在副本.cs 接管后，执行一次这行代码释放锁：
                ExecuteDynamicLua("ZeroBot_IsEnteringWorld = nil; ZeroBot_HasRequestedLogin = nil; StaticPopup_Hide('CAMP'); ");
                //SuppressLUAerrors();
                _rootTree?.Reset();
                _wasInWorld = true;
            }

            if (CurrentPlayerName == "未知角色" && _hProcess != IntPtr.Zero)
            {
                CurrentPlayerName = GetPlayerName(_hProcess);
            }
            
            // 【猎人专属 等级与职业安全锁】
            if (playerBase != 0)
            {
                int currentClassId = GetPlayerClass(_hProcess, _playerBase);
                int currentLevel = GetPlayerLevel(_hProcess, _playerBase);

                // 猎人的 Class ID = 3
                if (currentClassId > 0 && currentClassId != 3)
                {
                    if ((DateTime.Now - _lastClassWarnTime).TotalSeconds >= 5)
                    {
                        Logger.Write($"[{CurrentPlayerName}] [严重警告] 当前脚本为猎人专属！检测到非法职业ID: {currentClassId}，脚本逻辑已强制锁定！");
                        _lastClassWarnTime = DateTime.Now;
                    }
                    return "请使用猎人职业";
                }

                if (currentLevel > 0 && currentLevel < 59)
                {
                    if ((DateTime.Now - _lastLevelWarnTime).TotalSeconds >= 5)
                    {
                        Logger.Write($"[{CurrentPlayerName}] [严重警告] 当前脚本要求等级 >= 59 级！当前等级: {currentLevel}，脚本逻辑已强制锁定！");
                        _lastLevelWarnTime = DateTime.Now;
                    }
                    return "等级不足，需59级";
                }
            }

            
            int descriptors = MemoryAPI.ReadInteger(_hProcess, _playerBase + 0x08);
            int currentHp = MemoryAPI.ReadInteger(_hProcess, descriptors + 0x58);
            int maxHp = MemoryAPI.ReadInteger(_hProcess, descriptors + 0x70);
            _mapId = MemoryAPI.ReadInteger(_hProcess, ModuleBaseAddress + 0x46A2CC);


            if (_isFirstStartup)
            {
                if (currentHp <= 1 && maxHp > 0)
                {
                    Logger.Write($"[{CurrentPlayerName}] [冷启动检测] 发现角色处于死亡或灵魂状态，强制启动跑尸保护！");
                    _isDead = true;
                    if (currentHp == 1) _hasReleasedSpirit = true;
                }

                float startX = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9B8);
                float startY = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9BC);

                bool isAllianceInInn = !IsHordePlayer(false, false, _hProcess, _playerBase) && _mapId == 1 && startY >= 3260f;
                bool isHordeInInn = IsHordePlayer(false, false, _hProcess, _playerBase) && _mapId == 1 && startX < -4430f && startY < 260f;

                if (!_isDead && (isAllianceInInn || isHordeInInn))
                {
                    Logger.Write($"[{CurrentPlayerName}] [冷启动检测] 发现角色在旅店内部启动，强制触发一轮清包与补给流程！");
                    _needGoHome = true;
                }
                else if (!_isDead)
                {
                    Logger.Write($"[{CurrentPlayerName}] [冷启动检测] 角色在野外或副本中启动，继续当前任务，不强制回城。");

                    if (_mapId != 429)
                    {
                        float currentX = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9B8);
                        float currentY = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9BC);

                        // 厄运北大门
                        double distToNorthDoor = Math.Sqrt(Math.Pow(currentX - (-3520f), 2) + Math.Pow(currentY - 1082f, 2));

                        if (distToNorthDoor <= 40.0)
                        {
                            _needStartupPrep = false;
                            Logger.Write($"[{CurrentPlayerName}] [冷启动检测] 侦测到角色已在副本北大门口上线！直接走进去，拒绝神经病式的原地读条上马！");
                        }
                        else
                        {
                            _needStartupPrep = true;
                        }
                    }
                }
                _isFirstStartup = false;
            }

            if (_lastMapId != 0 && _mapId != 0 && _mapId != _lastMapId)
            {
                if (_lastMapId == 429 && _mapId == 1)
                {
                    if (_isDead || _hasReleasedSpirit)
                    {
                        Logger.Write($"[{CurrentPlayerName}] [过图检测] 侦测到角色【死亡跨出】副本，已在墓地降生，即将呼叫队长重置！");
                        _needResetInstance = true;
                        _resetTimer = DateTime.Now.AddSeconds(4);
                    }
                    else
                    {
                        Logger.Write($"[{CurrentPlayerName}] [过图检测] 侦测到角色活着离开副本(走出门或小退重上)，已跳过死亡重置逻辑");
                    }
                    _rootTree?.Reset();
                }
                else if (_lastMapId == 1 && _mapId == 429)
                {
                    Logger.Write($"[{CurrentPlayerName}] [过图检测] 侦测到角色进入副本");
                    _hasAttemptedSell = false;
                    StopMovement(hProcess, playerBase);
                    _rootTree?.Reset();
                }
            }
            if (_mapId != 0) _lastMapId = _mapId;

            // 【强制重置执行区】
            if (_needResetInstance)
            {
                if (DateTime.Now >= _resetTimer)
                {
                    ExecuteDynamicLua("ResetInstances()");
                    _needResetInstance = false;
                    _resetTimer = DateTime.Now.AddSeconds(1);
                    Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [副本重置] 已经发送重置副本指令");
                    return "重置副本";
                }
                return "等待重置";
            }
            else if (DateTime.Now < _resetTimer)
            {
                return "重置宏执行缓冲中...";
            }

            bool previousDeadState = _isDead;

            if (currentHp > 1)
            {
                _isDead = false;
            }
            else
            {
                _isDead = (currentHp <= 0) || (_hasReleasedSpirit && currentHp <= 1) || HasGhostDebuff(_hProcess, _playerBase);
                if (_hasReleasedSpirit && !_isDead && _mapId == 1 && GetDistanceToCorpse(_hProcess, _playerBase) > 20.0f)
                {
                    _isDead = true;
                }
            }

            if (previousDeadState != _isDead)
            {
                string status = _isDead ? "死亡/灵魂" : "存活";
                Debug.WriteLine($"\n[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{CurrentPlayerName}] [生命体征监控] 状态发生反转! 当前状态: 【{status}】 | 血量: {currentHp}/{maxHp} | 释放灵魂: {_hasReleasedSpirit}");

                if (_isDead && !previousDeadState)
                {
                    Logger.Write($"[{CurrentPlayerName}] [全局中断] 确认阵亡！立即清空所有指令与行为树缓存状态！");
                    StopMovement(_hProcess, _playerBase);
                    _hasReleasedSpirit = false;
                    _activeGyRoute = 0;
                    _needGoHome = false;
                    _rootTree?.Reset();
                }
            }

            if (_wasDead && !_isDead)
            {
                float revX = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9B8);
                float revY = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9BC);

                Debug.WriteLine($"\n=======================================================");
                Logger.Write($"[{CurrentPlayerName}] [重生鉴定] 玩家复活！");
                Logger.Write($"[{CurrentPlayerName}] [重生鉴定] 唤醒坐标: X={revX}, Y={revY}");
                Logger.Write($"[{CurrentPlayerName}] [重生鉴定] 背包满载标志 (_needGoHome): {_needGoHome}");
                Logger.Write($"[{CurrentPlayerName}] [重生鉴定] 正在强行抹除 CTM 移动残留与行为树历史...");

                StopMovement(_hProcess, _playerBase);
                _hasReleasedSpirit = false;
                _activeGyRoute = 0;
                WaitTimer = DateTime.MinValue;
                _rootTree?.Reset();

                Logger.Write($"[{CurrentPlayerName}] [重生鉴定] 系统重置完毕，下一帧将由最高指挥官重新分配任务！");
                Debug.WriteLine($"=======================================================\n");
            }
            _wasDead = _isDead;

            if (DateTime.Now < _hearthstoneTimer)
            {
                double elapsedSeconds = 11.0 - (_hearthstoneTimer - DateTime.Now).TotalSeconds;
                if (elapsedSeconds > 1.5 && elapsedSeconds < 9.5)
                {
                    int currentCastingSpellId = MemoryAPI.ReadInteger(_hProcess, _playerBase + 0xC8C);
                    if (currentCastingSpellId != 8690)
                    {
                        Logger.Write($"[{CurrentPlayerName}] [背包系统] 侦测到炉石读条被打断！(当前施法ID: {currentCastingSpellId}) 立即重试...");
                        StopMovement(_hProcess, _playerBase);
                        _hearthstoneTimer = DateTime.MinValue;
                    }
                }
                return "正在炉石";
            }

            if (!_isDead && !_needGoHome && !_hasAttemptedSell && CheckIfInventoryFull(_hProcess, _playerBase))
            {
                Logger.Write($"[{CurrentPlayerName}] [背包系统] 侦测到背包空位极低（剩余<=5格），正式启动回城机制！");
                _needGoHome = true;
            }

            if (!_isDead && _needGoHome)
            {
                bool hasHearthstone = GetItemCount(_hProcess, 6948) > 0;
                bool isHsReady = hasHearthstone && !IsSpellOnCooldown(_hProcess, 6948);
                bool isInCombat = Unit_Behavioral_State(_hProcess, _playerBase, 19);

                float currentX = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9B8);
                float currentY = MemoryAPI.ReadFloat(_hProcess, _playerBase + 0x9BC);
                bool isAlreadyInInn = false;

                if (_mapId == 1)
                {
                    if (IsHordePlayer(false, false, _hProcess, _playerBase)) isAlreadyInInn = (currentX < -4430f && currentY < 260f);
                    else isAlreadyInInn = (currentY >= 3260f);
                }

                if (isHsReady && !isAlreadyInInn)
                {
                    if (_mapId == 429 && isInCombat)
                    {
                        if ((DateTime.Now - _lastZijiuPrintTime).TotalSeconds >= 5)
                        {
                            Logger.Write($"[{CurrentPlayerName}] [背包系统] 炉石已就绪，但当前处于战斗状态！暂不打断，请冷静把这波怪打完");
                            _lastZijiuPrintTime = DateTime.Now;
                        }
                    }
                    else
                    {
                        Logger.Write($"[{CurrentPlayerName}] [背包系统] 脱战安全期或野外，满足炉石条件！紧急打断，开搓炉石！");
                        StopMovement(_hProcess, _playerBase);
                        UseItemByItemId(_hProcess, 6948);
                        _hearthstoneTimer = DateTime.Now.AddSeconds(11);
                        _rootTree?.Reset();
                        return "正在炉石";
                    }
                }
            }

            if (_rootTree != null)
            {
                NodeState state = _rootTree.Evaluate();
                if (state == NodeState.Running) return "运行中";
            }

            return "空闲";
        }
    }
}