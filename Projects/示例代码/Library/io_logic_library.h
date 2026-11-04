#ifndef IO_LOGIC_LIBRARY_H
#define IO_LOGIC_LIBRARY_H

/**
 * @file    io_logic_library.h
 * @brief   联合控制器（IOCF v2）指令库统一入口。
 *
 * 运行模型：
 *   1. 可定义 IO_Mode_0_Callback ~ IO_Mode_15_Callback 作为模式入口；
 *      未定义的模式由 DSL 编译器生成空入口，不是 C 语言 __weak 函数。
 *   2. 主程序 IO_Logic_Main 写 while(1) 则循环执行；不写则执行一次后结束（输出保持）。
 *   3. 模式是否生效完全由主程序决定：外部 DI3~DI6 编码只是信号，主程序未读取则不切换。
 *   4. 上位机将 DSL 源码编译为 v2 字节码镜像，经 0x41 协议下载到板卡 Flash，
 *      掉电保存、免重烧固件。
 *
 * 安全限制：禁止定义 main；禁止调用 HAL_*；禁止直接访问 GPIO/中断/时钟。
 * 条件支持 !、&&、|| 和括号；优先级依次为 !、&&、||，按 C 规则短路求值。
 * 对比较式取反须加括号，例如 !(IO_DI_Level(INPUT_1) == 1)。
 */
#include "io_logic_types.h"
#include "io_logic_input.h"
#include "io_logic_output.h"
#include "io_logic_timer.h"
#include "io_logic_analog.h"

#endif /* IO_LOGIC_LIBRARY_H */
