#ifndef IO_LOGIC_OUTPUT_H
#define IO_LOGIC_OUTPUT_H

/**
 * @file    io_logic_output.h
 * @brief   联合控制器数字输出动作 API。
 */

/**
 * @brief 数字输出置位/复位。
 * @param channel DO 通道号（1~8）
 * @param level  电平（1 置位，0 复位）
 */
void IO_DO_Set(int channel, int level);

/* 示例：IO_DO_Set(RELAY_1, 1);  IO_DO_Set(TRANSISTOR_1, 0); */

/**
 * @brief 数字输出脉冲。
 * @param channel    DO 通道号（1~8）
 * @param duration_ms 脉冲宽度（1~65535 ms）
 */
void IO_DO_Pulse(int channel, int duration_ms);

/* 示例：IO_DO_Pulse(TRANSISTOR_2, 1000); */

#endif /* IO_LOGIC_OUTPUT_H */
