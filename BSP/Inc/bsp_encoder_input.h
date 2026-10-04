#ifndef BSP_ENCODER_INPUT_H
#define BSP_ENCODER_INPUT_H

#include <stdint.h>

typedef struct
{
  int32_t position;
  int32_t index_position;
  int8_t direction;
  uint8_t ab_state;
  uint32_t index_count;
  uint32_t invalid_transition_count;
} BSP_ENCODER_INPUT_Snapshot;

/* 初始化X6/PB0(A)、X7/PB1(B)和X8/PE8(Z)的编码器输入中断。 */
void BSP_ENCODER_INPUT_Init(void);

/* 由EXTI中断入口调用，不得在普通任务中主动调用。 */
void BSP_ENCODER_INPUT_HandleExti(uint16_t gpio_pin);

/* 原子复制当前编码器计数和诊断状态。 */
void BSP_ENCODER_INPUT_GetSnapshot(BSP_ENCODER_INPUT_Snapshot *snapshot);

/* 将当前位置设置为0，不清除Z相和非法跳变计数。 */
void BSP_ENCODER_INPUT_ResetPosition(void);

#endif /* BSP_ENCODER_INPUT_H */
