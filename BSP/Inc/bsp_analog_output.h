#ifndef BSP_ANALOG_OUTPUT_H
#define BSP_ANALOG_OUTPUT_H

#include <stdint.h>

/* 初始化DAC及板载0~20 mA、0~10 V输出电路，初始输出均为0。 */
void BSP_ANALOG_OUTPUT_Init(void);

/* 设置电流输出，单位为uA；超过20000 uA时按20000 uA限幅。 */
void BSP_ANALOG_OUTPUT_SetCurrentUa(uint16_t current_ua);

/* 设置电压输出，单位为mV；超过10000 mV时按10000 mV限幅。 */
void BSP_ANALOG_OUTPUT_SetVoltageMv(uint16_t voltage_mv);

/* 启动非阻塞电压呼吸波，cycle_ms表示一次完整升降周期。 */
void BSP_ANALOG_OUTPUT_StartVoltageBreath(uint16_t minimum_mv,
                                         uint16_t maximum_mv,
                                         uint16_t cycle_ms,
                                         uint32_t now_ms);

/* 由10 ms IO逻辑任务周期调用，推进正在运行的模拟输出波形。 */
void BSP_ANALOG_OUTPUT_Process(uint32_t now_ms);

/* 停止动态波形并将两路模拟输出归零。 */
void BSP_ANALOG_OUTPUT_AllOff(void);

#endif
