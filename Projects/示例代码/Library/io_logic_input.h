#ifndef IO_LOGIC_INPUT_H
#define IO_LOGIC_INPUT_H

/**
 * @file    io_logic_input.h
 * @brief   联合控制器输入条件 API（用于 if / while 条件）。
 *
 * 条件编译为字节码 TEST 指令；比较右值必须是数字常量。
 */

/**
 * @brief 读取 DI 通道电平。
 * @param channel DI 通道号（1~8）
 * @return 1 有效（低电平有效为 0），0 无效
 * 用法：if (IO_DI_Level(INPUT_1) == 1) { ... }
 */
int IO_DI_Level(int channel);

/**
 * @brief DI 通道上升沿。
 * @param channel DI 通道号（1~8）
 * @return 本 tick 出现上升沿返回非 0
 */
int IO_DI_Rising(int channel);

/**
 * @brief DI 通道下降沿。
 * @param channel DI 通道号（1~8）
 * @return 本 tick 出现下降沿返回非 0
 */
int IO_DI_Falling(int channel);

/**
 * @brief 读取外部编码模式（DI3~DI6，bit0=DI3，稳定 20ms 后认可）。
 * @return 0~15
 */
int IO_GetExternalMode(void);

/**
 * @brief 读取当前运行模式。
 * @return 0~15
 */
int IO_GetCurrentMode(void);

/**
 * @brief 读取 DI1/DI2 实时频率。
 * @param channel 频率通道（1 = DI1，2 = DI2）
 * @return 频率（Hz），0~2000
 * 用法：if (IO_Freq_Hz(INPUT_1) > 100) { ... }
 */
int IO_Freq_Hz(int channel);

/**
 * @brief 读取 DI1/DI2 鉴频档位。
 * @param channel 频率通道（1 = DI1，2 = DI2）
 * @return 档位：0=NONE 1=LOW 2=NORMAL 3=HIGH 4=OVER
 * 用法：if (IO_Freq_Class(INPUT_1) == IO_FREQ_CLASS_NORMAL) { ... }
 */
int IO_Freq_Class(int channel);

/**
 * @brief AD7606 通道数据是否可用于控制：1=最近 500 ms 内采样成功，0=未就绪/失败/超时。
 * @param channel AD7606 通道 1~8；CH1/CH2 同时是两路 4~20 mA 电流输入。
 * 用法：if (IO_AI_Valid(3) == 1 && IO_AI_Mv(3) > 2500) { ... }
 */
int IO_AI_Valid(int channel);

/**
 * @brief 比较 AD7606 通道电压，单位 mV，支持 -10000~10000 与 ==、!=、>、<。
 * 数据无效时，任何数值比较都为假；不要把无效数据当作 0 V。
 */
int IO_AI_Mv(int channel);

/**
 * @brief CH1/CH2 电流输入数据是否有效，判断规则与 IO_AI_Valid 相同。
 * @param channel 电流输入通道 1~2。
 */
int IO_Current_Valid(int channel);

/**
 * @brief 比较电流输入，单位 uA；按板载 6.2 ohm * 25 倍增益换算：uA = mV * 1000 / 155。
 * @param channel 电流输入通道 1~2；支持 0~65535 与 ==、!=、>、<。
 * 用法：if (IO_Current_Valid(1) == 1 && IO_Current_uA(1) > 12000) { ... }
 */
int IO_Current_uA(int channel);

/**
 * @brief RS485 轮询项是否有效：已启用、最近轮询成功且未超时返回 1。
 * @param item 上位机 RS485 列表的行号 1~32；超时为 max(1000 ms, 3 * 周期 + 活动项数 * 250 ms)。
 */
int IO_RS485_Valid(int item);

/**
 * @brief 比较 RS485 轮询项中的原始 16 位寄存器值。
 * @param item 轮询项行号 1~32。
 * @param offset 该项起始寄存器的偏移 0~15，且必须小于该项配置的数量。
 * @return 支持 0~65535 与 ==、!=、>、<；无效/超时时所有数值比较都为假。
 * 用法：if (IO_RS485_Valid(1) == 1 && IO_RS485_Value(1, 0) > 500) { ... }
 */
int IO_RS485_Value(int item, int offset);

#endif /* IO_LOGIC_INPUT_H */
