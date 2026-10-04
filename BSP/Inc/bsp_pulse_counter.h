#ifndef BSP_PULSE_COUNTER_H
#define BSP_PULSE_COUNTER_H

#include <stdint.h>

typedef enum
{
  BSP_PULSE_COUNTER_X1 = 0,
  BSP_PULSE_COUNTER_X2,
  BSP_PULSE_COUNTER_COUNT
} BSP_PULSE_COUNTER_Channel;

/* 初始化X1/PE9和X2/PB7的硬件外部脉冲计数器。 */
int BSP_PULSE_COUNTER_Init(void);

/* 读取指定通道当前的16位硬件计数值。 */
uint16_t BSP_PULSE_COUNTER_Read(BSP_PULSE_COUNTER_Channel channel);

#endif /* BSP_PULSE_COUNTER_H */
