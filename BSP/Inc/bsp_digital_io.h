#ifndef BSP_DIGITAL_IO_H
#define BSP_DIGITAL_IO_H

#include <stdint.h>

#include "board_pins.h"

#ifndef BSP_DIGITAL_IO_INPUT_ACTIVE_LEVEL
#define BSP_DIGITAL_IO_INPUT_ACTIVE_LEVEL GPIO_PIN_RESET
#endif

#ifndef BSP_DIGITAL_IO_OUTPUT_ACTIVE_LEVEL
#define BSP_DIGITAL_IO_OUTPUT_ACTIVE_LEVEL GPIO_PIN_SET
#endif

typedef enum
{
  BSP_DIGITAL_IO_INPUT_1 = 0,
  BSP_DIGITAL_IO_INPUT_2,
  BSP_DIGITAL_IO_INPUT_3,
  BSP_DIGITAL_IO_INPUT_4,
  BSP_DIGITAL_IO_INPUT_5,
  BSP_DIGITAL_IO_INPUT_6,
  BSP_DIGITAL_IO_INPUT_7,
  BSP_DIGITAL_IO_INPUT_8,
  BSP_DIGITAL_IO_INPUT_COUNT
} BSP_DIGITAL_IO_Input;

typedef enum
{
  BSP_DIGITAL_IO_OUTPUT_RELAY_1 = 0,
  BSP_DIGITAL_IO_OUTPUT_RELAY_2,
  BSP_DIGITAL_IO_OUTPUT_RELAY_3,
  BSP_DIGITAL_IO_OUTPUT_RELAY_4,
  BSP_DIGITAL_IO_OUTPUT_TRANSISTOR_1,
  BSP_DIGITAL_IO_OUTPUT_TRANSISTOR_2,
  BSP_DIGITAL_IO_OUTPUT_TRANSISTOR_3,
  BSP_DIGITAL_IO_OUTPUT_TRANSISTOR_4,
  BSP_DIGITAL_IO_OUTPUT_COUNT
} BSP_DIGITAL_IO_Output;

/* 初始化8路数字输入和8路数字输出，所有输出默认关闭。 */
void BSP_DIGITAL_IO_Init(void);

/* 读取指定输入引脚的原始高低电平，不进行有效电平转换。 */
GPIO_PinState BSP_DIGITAL_IO_ReadInputRaw(BSP_DIGITAL_IO_Input input);

/* 读取指定输入的逻辑状态：1=有效，0=无效或参数非法。 */
int BSP_DIGITAL_IO_ReadInputActive(BSP_DIGITAL_IO_Input input);

/* 读取8路输入位掩码：bit0~bit7对应INPUT_1~INPUT_8，1=有效。 */
uint16_t BSP_DIGITAL_IO_ReadInputMask(void);

/* 设置单路输出的逻辑状态：active非0=打开，0=关闭。 */
void BSP_DIGITAL_IO_SetOutput(BSP_DIGITAL_IO_Output output, int active);

/* 获取单路输出的逻辑状态：1=打开，0=关闭或参数非法。 */
int BSP_DIGITAL_IO_GetOutput(BSP_DIGITAL_IO_Output output);

/* 设置8路输出位掩码：bit0~3=继电器1~4，bit4~7=晶体管1~4，1=打开。 */
void BSP_DIGITAL_IO_SetOutputMask(uint16_t active_mask);

/* 获取8路输出的逻辑状态位掩码，位定义与SetOutputMask一致。 */
uint16_t BSP_DIGITAL_IO_GetOutputMask(void);

/* 关闭全部4路继电器和4路晶体管输出。 */
void BSP_DIGITAL_IO_AllOutputsOff(void);

#endif /* BSP_DIGITAL_IO_H */
