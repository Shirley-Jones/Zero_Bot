#include <windows.h>
#include <stdio.h>
#include <stdint.h> // 需要用到 uint64_t
#include <cstdint>
#include <math.h> 
#include "VMProtectSDK.h"

#define WM_WAKEUP_LUA (WM_USER + 1337)

WNDPROC oWndProc = NULL;
HWND hGameWindow = NULL;

// =======================
//   魔兽 1.12.1 引擎基址
// =======================
typedef void(__fastcall* FrameScript_Execute_t)(const char* text, const char* name, int unused);
FrameScript_Execute_t WoW_DoString = (FrameScript_Execute_t)0x00704CD0;

// =======================
//   数据结构定义
// =======================
#pragma pack(push, 1) // 强制 1 字节对齐，防止 C# 和 C++ 之间出现内存错位
struct Vector3 {
    float x;
    float y;
    float z;
};

// 升级版的共享内存结构：支持多种指令分发
#pragma pack(push, 1)
struct SharedMemoryData {
    int securityToken;    // 动态安全令牌，必须放在最上面第一个！
    int commandType;      // 指令类型：0=空闲, 1=Spell(施法), 2=Move_To(CTM移动), 3=RightClick, 4=AOE, 5=动态Lua
    DWORD myPlayerBase;   // 你的角色基址 (CTM需要)
    int ctmActionId;      // CTM 动作类型 (通常移动是 4)
    uint64_t targetGuid;  // 目标的 GUID (如果是移动到特定目标)
    Vector3 dest;         // 目标 XYZ 坐标
    DWORD targetBase;     // 目标对象的基址 (右键拾取/对话需要)
    int isCompleted;      // 状态：0=执行中, 1=已完成
    char luaPayload[10240];
};
#pragma pack(pop)

HANDLE hMapFile = NULL;
SharedMemoryData* pSharedMem = nullptr;


// 1. 声明底层函数的形态 (利用 __fastcall 伪装 __thiscall)
typedef char(__fastcall* HandleTerrainClick_t)(Vector3* pCoords, void* edx_dummy);

// 2. 绑定你刚才在 IDA 里扒出来的大神级函数地址
HandleTerrainClick_t HandleTerrainClick = (HandleTerrainClick_t)0x006E60F0;

// 3. 终极 AOE 触发器
void DoGroundAOEAction(float targetX, float targetY, float targetZ) {
    // a. 把你要砸的坐标，组装成底层的 Vector3 结构体
    Vector3 dest;
    dest.x = targetX;
    dest.y = targetY;
    dest.z = targetZ;

    // b. 直接 CALL 这个底层函数，把坐标指针传给它！
    // 游戏底层会乖乖地把你给的坐标提取出来，组装成封包发给服务器！
    HandleTerrainClick(&dest, nullptr);
}

// =======================
//   CTM 函数封装
// =======================
typedef char(__fastcall* ClickToMove_t)(DWORD playerPtr, DWORD edx_dummy, int actionId, uint64_t* pGuid, Vector3* pPos, float precision);
ClickToMove_t CTM_Call = (ClickToMove_t)0x00611130;

void DoCTMAction(DWORD myPlayer, int action, uint64_t targetGuid, Vector3 dest) {
    if (!myPlayer) return;

    // 必须用局部变量挂载，确保 a3 (*pGuid) 永远指向合法的内存，防止游戏解引用崩溃
    uint64_t guid_val = targetGuid;

    // 调用 0x00611130，底层逻辑闭环
    CTM_Call(myPlayer, 0, action, &guid_val, &dest, 2.0f);
}

// =======================
//   右键Unit 函数封装
// =======================
typedef void(__fastcall* OnRightClick_t)(DWORD pThis, DWORD edxDummy, int autoLoot);
OnRightClick_t RightClick_Call = (OnRightClick_t)0x0060BEA0;

void DoRightClickAction(DWORD targetBase) {
    if (!targetBase) return;
    // 传 1 代表强制开启 Shift 自动拾取
    RightClick_Call(targetBase, 0, 1);
}

// =======================
//   右键GameObject 函数封装
// =======================
// 1. 声明函数指针：完美对应 IDA 伪代码 int __thiscall sub_5F8660(this, a2)
// pThis 进 ECX, edxDummy 进 EDX(丢弃不用), autoLoot 压入堆栈 (对应伪代码的 a2)
typedef int(__fastcall* CGGameObject_OnRightClick_t)(DWORD pThis, DWORD edxDummy, int autoLoot);

// 2. 绑定真实内存地址
CGGameObject_OnRightClick_t GameObject_RightClick = (CGGameObject_OnRightClick_t)0x005F8660;

// 3. 供 C# 调用的外部接口 (比如你在 switch 里写个 case 7:)
void DoGameObjectRightClick(DWORD targetBase) {
    if (!targetBase) return;

    // 发起致命一击！1 代表 autoLoot
    GameObject_RightClick(targetBase, 0, 1);
}


// =======================
//   法术指向目标封装 (修正版)
// =======================
// 关键修正：必须使用 __fastcall！
// 第一个参数 pGuid 会被编译器自动放进 ECX 寄存器
// 第二个参数 edx_dummy 会被放进 EDX 寄存器（占位废弃不用）
typedef void(__fastcall* SpellTargetGuid_t)(uint64_t* pGuid, void* edx_dummy);

// 绑定 0x006E5B10
SpellTargetGuid_t SpellTargetGuid_Call = (SpellTargetGuid_t)0x006E5B10;

void DoSpellTargetGuidAction(uint64_t targetGuid) {
    if (targetGuid == 0) return;

    // 局部变量存 GUID，保证内存地址合法
    uint64_t guid_val = targetGuid;

    // 发起致命一击！
    // 传入 &guid_val 给 ECX，传入 nullptr 给 EDX 占位！
    SpellTargetGuid_Call(&guid_val, nullptr);
}


// 【多开窗口匹配器】
BOOL CALLBACK EnumWindowsProc(HWND hwnd, LPARAM lParam) {
    DWORD dwProcId = 0;
    GetWindowThreadProcessId(hwnd, &dwProcId);
    if (dwProcId == (DWORD)lParam) {
        char className[256];
        GetClassNameA(hwnd, className, sizeof(className));
        if (strcmp(className, "GxWindowClassD3d") == 0) {
            hGameWindow = hwnd;
            return FALSE;
        }
    }
    return TRUE;
}

// 【挂钩执行者】主线程安全执行
LRESULT CALLBACK hkWndProc(HWND hWnd, UINT uMsg, WPARAM wParam, LPARAM lParam) {
    if (uMsg == WM_WAKEUP_LUA) {
        if (pSharedMem != nullptr) {

            // 🌟 核心拦截机制：核对“开门密码”
            // 如果 C# 传过来的 wParam 和共享内存里咱们自己生成的令牌对不上，直接视为攻击，拒绝执行！
            if (wParam != (WPARAM)pSharedMem->securityToken) {
                return 1; // 假装处理了消息，但直接无视它
            }

            // 密码核对成功，开始执行指令
            if (pSharedMem->commandType > 0) {
                __try 
                {
                    // 根据指令类型分发任务
                    switch (pSharedMem->commandType) 
                    {
                        case 1:
                        {   // Move_To CTM移动指令
                            DoCTMAction(pSharedMem->myPlayerBase, pSharedMem->ctmActionId, pSharedMem->targetGuid, pSharedMem->dest);
                            break;
                        }
                        case 2:
                        {  // RightClick 右键交互指令
                            DoRightClickAction(pSharedMem->targetBase);
                            break;
                        }
                        case 3:
                        {    // RightClick GameObject 右键交互指令
                            DoGameObjectRightClick(pSharedMem->targetBase);
                            break;
                        }
                        case 4:
                        {  // AOE 坐标指令
                            DoGroundAOEAction(pSharedMem->dest.x, pSharedMem->dest.y, pSharedMem->dest.z);
                            break;
                        }
                        // 终极动态 Lua 执行通道 (UTF-8 安全)
                        case 5:
                        {
                            // 此时 pSharedMem->luaPayload 里面已经是 C# 准备好的纯正 UTF-8 字符串
                            WoW_DoString((const char*)pSharedMem->luaPayload, "Bot_DynamicLua", 0);
                            break;
                        }
                        case 6:
                        {
                            DoSpellTargetGuidAction(pSharedMem->targetGuid);
                            break;
                        }
                        // 核心终极修复：统一通道清理 
                        // 不管执行了哪个 case，只要执行完了，立刻抹除指令，让 DLL 归于沉寂！
                        pSharedMem->commandType = 0;
                        pSharedMem->isCompleted = 1;
                    }
                }
                __except (EXCEPTION_EXECUTE_HANDLER) {
                    // 拦截异常，防止客户端崩溃
                }

                // 执行完毕，重置内存状态，但绝不重置令牌！
                pSharedMem->commandType = 0;
                pSharedMem->isCompleted = 1;
            }
        }
        return 1;
    }
    return CallWindowProc(oWndProc, hWnd, uMsg, wParam, lParam);
}

// 【注入自启主线程】
DWORD WINAPI MainThread(LPVOID lpReserved) {
    DWORD currentPid = GetCurrentProcessId();
    char memName[256];
    sprintf_s(memName, sizeof(memName), "WoW_Bot_SharedMem_%lu", currentPid);

    hMapFile = CreateFileMappingA(INVALID_HANDLE_VALUE, NULL, PAGE_READWRITE, 0, sizeof(SharedMemoryData), memName);
    if (hMapFile) {
        pSharedMem = (SharedMemoryData*)MapViewOfFile(hMapFile, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(SharedMemoryData));
        if (pSharedMem) {

            // 🌟 核心防御设置：生成随机令牌
            // 利用 Windows 开机滴答数、当前进程 PID 和一个魔法数字异或混淆，生成独一无二的令牌
            // 因为 C# 可以通过 SharedMemory 读取，所以 C# 知道密码，但瞎发消息的外部程序绝对猜不到
            // 🌟 直接改成 GetTickCount64()，并强转为 int，警告瞬间消失！
            pSharedMem->securityToken = (int)GetTickCount64() ^ currentPid ^ 0x1337BEEF;

            pSharedMem->commandType = 0;
            pSharedMem->isCompleted = 0;
        }
    }

    EnumWindows(EnumWindowsProc, currentPid);

    if (hGameWindow) {
        oWndProc = (WNDPROC)SetWindowLongPtr(hGameWindow, GWLP_WNDPROC, (LONG_PTR)hkWndProc);
    }
    return TRUE;
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID lpReserved) {
    if (ul_reason_for_call == DLL_PROCESS_ATTACH) {
        DisableThreadLibraryCalls(hModule);
        CreateThread(nullptr, 0, MainThread, hModule, 0, nullptr);
    }
    return TRUE;
}
