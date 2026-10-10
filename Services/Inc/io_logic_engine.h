#ifndef IO_LOGIC_ENGINE_H
#define IO_LOGIC_ENGINE_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/*
 * IO 逻辑字节码虚拟机（联合控制器运行时）。
 *
 * 上位机将用户编写的 DSL 代码（16 个模式弱定义回调 + 主程序 while）
 * 编译为 v2 字节码镜像并下载到 Flash。本引擎按 10ms 时基逐条解释执行：
 *   - 主程序：从镜像 body 偏移 0 开始执行，遇到 OP_END 结束；
 *     包含 while 的程序通过条件跳转形成循环，永不触碰 OP_END；
 *   - 16 个模式回调：入口表 + 子程序调用栈，OP_RET 返回；
 *   - 切模式：先关闭全部输出，再切换到目标模式（规格要求）；
 *   - 鉴频：按程序内设置的阈值 + 迟滞状态机计算档位，供条件测试指令使用。
 */

/* 镜像 v2 布局常量（与 io_config_storage 配合） */
#define IO_LOGIC_IMAGE_HEADER_LENGTH   32U
#define IO_LOGIC_MODE_COUNT            16U
#define IO_LOGIC_MODE_ENTRY_TABLE_LEN  (IO_LOGIC_MODE_COUNT * 2U)
#define IO_LOGIC_BODY_OFFSET           (IO_LOGIC_IMAGE_HEADER_LENGTH + IO_LOGIC_MODE_ENTRY_TABLE_LEN)
#define IO_LOGIC_CALL_DEPTH            8U
#define IO_LOGIC_MAX_INSTR_PER_TICK    200U

/* 规格书上限：DI1/DI2 最高 2kHz，超过即超量程 */
#define IO_LOGIC_SPEC_MAX_FREQ_HZ      2000U

/* 运行状态 */
#define IO_LOGIC_RUN_STATE_STOPPED     0U
#define IO_LOGIC_RUN_STATE_RUNNING     1U
#define IO_LOGIC_RUN_STATE_ENDED       2U /* 主程序无 while，执行一次后结束（输出保持） */

/* 状态标志位（寄存器上报） */
#define IO_LOGIC_STATUS_VM_FAULT       0x0001U /* 虚拟机执行错误（已安全停止） */

/* 模拟量输出使能：指令直接驱动 BSP_ANALOG_OUTPUT（DAC1 双通道 0~10V / 0~20mA） */
#define IO_LOGIC_ENABLE_ANALOG_OUTPUT  1U

/**
 * @brief 初始化字节码虚拟机：清零状态、初始化模拟量输出并关闭全部输出。
 */
void IO_LOGIC_ENGINE_Init(void);

/**
 * @brief 10ms 调度步进：从当前程序计数器继续执行字节码。
 * @param input_mask 逻辑输入掩码（由数字 IO 服务层提供）
 * @param now_ms     当前系统毫秒
 */
void IO_LOGIC_ENGINE_Step(uint16_t input_mask, uint32_t now_ms);

/**
 * @brief 停止虚拟机：关闭全部输出、清空运行状态。
 */
void IO_LOGIC_ENGINE_Stop(void);

uint16_t IO_LOGIC_ENGINE_GetOutputMask(void);
uint8_t IO_LOGIC_ENGINE_GetCurrentMode(void);
uint8_t IO_LOGIC_ENGINE_GetRunState(void);
uint16_t IO_LOGIC_ENGINE_GetStatusFlags(void);

/* 鉴频档位查询（寄存器上报），channel：0 = DI1，1 = DI2 */
uint8_t IO_LOGIC_ENGINE_GetFreqClass(uint8_t channel);

/* 模拟量输出状态查询（寄存器上报） */
uint16_t IO_LOGIC_ENGINE_GetAoVoltageMv(void);
uint16_t IO_LOGIC_ENGINE_GetAoCurrentUa(void);

#ifdef __cplusplus
}
#endif

#endif /* IO_LOGIC_ENGINE_H */
