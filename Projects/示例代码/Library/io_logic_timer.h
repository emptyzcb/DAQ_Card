#ifndef IO_LOGIC_TIMER_H
#define IO_LOGIC_TIMER_H

/**
 * @file    io_logic_timer.h
 * @brief   联合控制器模式/延时/调度 API。
 */

/**
 * @brief 阻塞延时（以 10ms 时基推进）。
 * @param duration_ms 延时（1~65535 ms）
 */
void IO_Delay(int duration_ms);

/**
 * @brief 切换当前模式。
 * @param mode 模式号（0~15）。切换时固件先关闭全部输出再启动目标模式。
 * 特殊写法：IO_SetMode(IO_GetExternalMode()) 直接切到外部编码模式。
 */
void IO_SetMode(int mode);

/**
 * @brief 调用当前模式的回调（未定义模式由编译器生成空入口）。
 */
void IO_RunCurrentMode(void);

/**
 * @brief 模式回调声明（用户按需实现；不是 C __weak 定义）。
 * 编译器按函数名识别：IO_Mode_0_Callback ~ IO_Mode_15_Callback。
 */
void IO_Mode_0_Callback(void);
void IO_Mode_1_Callback(void);
void IO_Mode_2_Callback(void);
void IO_Mode_3_Callback(void);
void IO_Mode_4_Callback(void);
void IO_Mode_5_Callback(void);
void IO_Mode_6_Callback(void);
void IO_Mode_7_Callback(void);
void IO_Mode_8_Callback(void);
void IO_Mode_9_Callback(void);
void IO_Mode_10_Callback(void);
void IO_Mode_11_Callback(void);
void IO_Mode_12_Callback(void);
void IO_Mode_13_Callback(void);
void IO_Mode_14_Callback(void);
void IO_Mode_15_Callback(void);

/**
 * @brief 主程序入口（必须存在）。
 * 写 while(1) 则循环执行；不写则执行一次后结束（输出保持）。
 */
void IO_Logic_Main(void);

#endif /* IO_LOGIC_TIMER_H */
