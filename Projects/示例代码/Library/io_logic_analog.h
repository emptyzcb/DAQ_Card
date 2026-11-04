#ifndef IO_LOGIC_ANALOG_H
#define IO_LOGIC_ANALOG_H

/**
 * @file    io_logic_analog.h
 * @brief   联合控制器模拟量输出动作 API。
 *
 * 测试工程以状态记录 stub 实现（寄存器 0x005A/0x005B 可读）；
 * 正式工程对接 BSP 后直接驱动硬件。
 */

/**
 * @brief 模拟量电压输出。
 * @param millivolts 电压（0~10000 mV，即 0~10 V）
 */
void IO_AO_SetVoltage(int millivolts);

/**
 * @brief 模拟量电流输出。
 * @param microamps 电流（0~20000 µA，即 0~20 mA）
 */
void IO_AO_SetCurrent(int microamps);

/**
 * @brief 电压呼吸（在上下限之间周期变化）。
 * @param minimum_mv  下限（0~10000 mV）
 * @param maximum_mv  上限（0~10000 mV，须 >= 下限）
 * @param period_ms   完整周期（20~65535 ms）
 */
void IO_AO_Breath(int minimum_mv, int maximum_mv, int period_ms);

/** 电流呼吸：在上下限之间平滑往返，单位uA，完整周期20~65535ms。 */
void IO_AO_BreathCurrent(int minimum_ua, int maximum_ua, int period_ms);

#endif /* IO_LOGIC_ANALOG_H */
