#ifndef BSP_AD7606_H
#define BSP_AD7606_H

#include <stdint.h>

#include "main.h"

#ifdef __cplusplus
extern "C" {
#endif

#define BSP_AD7606_CHANNEL_COUNT 8U

/*
 * Default hardware mapping for STM32H743VITx.
 * Parallel data bus:
 *   DB0..DB15 -> PE0..PE15
 *
 * Control pins:
 *   CONVST_A and CONVST_B may be tied together and driven by CONVST.
 *   CS and RD are controlled separately for parallel read timing.
 */
#define BSP_AD7606_DATA_GPIO_Port GPIOE
#define BSP_AD7606_DATA_MASK      0xFFFFU

#define BSP_AD7606_CONVST_GPIO_Port GPIOD
#define BSP_AD7606_CONVST_Pin       GPIO_PIN_0

#define BSP_AD7606_RD_GPIO_Port GPIOD
#define BSP_AD7606_RD_Pin       GPIO_PIN_1

#define BSP_AD7606_CS_GPIO_Port GPIOD
#define BSP_AD7606_CS_Pin       GPIO_PIN_2

#define BSP_AD7606_RESET_GPIO_Port GPIOD
#define BSP_AD7606_RESET_Pin       GPIO_PIN_3

#define BSP_AD7606_BUSY_GPIO_Port GPIOD
#define BSP_AD7606_BUSY_Pin       GPIO_PIN_4

#define BSP_AD7606_RANGE_GPIO_Port GPIOD
#define BSP_AD7606_RANGE_Pin       GPIO_PIN_5

#define BSP_AD7606_OS0_GPIO_Port GPIOD
#define BSP_AD7606_OS0_Pin       GPIO_PIN_6

#define BSP_AD7606_OS1_GPIO_Port GPIOD
#define BSP_AD7606_OS1_Pin       GPIO_PIN_7

#define BSP_AD7606_OS2_GPIO_Port GPIOB
#define BSP_AD7606_OS2_Pin       GPIO_PIN_0

#define BSP_AD7606_STBY_GPIO_Port GPIOB
#define BSP_AD7606_STBY_Pin       GPIO_PIN_1

typedef enum
{
  BSP_AD7606_RANGE_5V = 5000,
  BSP_AD7606_RANGE_10V = 10000
} BSP_AD7606_Range;

typedef enum
{
  BSP_AD7606_OS_NONE = 0,
  BSP_AD7606_OS_2 = 1,
  BSP_AD7606_OS_4 = 2,
  BSP_AD7606_OS_8 = 3,
  BSP_AD7606_OS_16 = 4,
  BSP_AD7606_OS_32 = 5,
  BSP_AD7606_OS_64 = 6
} BSP_AD7606_Oversampling;

typedef struct
{
  int16_t raw[BSP_AD7606_CHANNEL_COUNT];
  int32_t mv[BSP_AD7606_CHANNEL_COUNT];
  uint32_t timestamp_ms;
} BSP_AD7606_Sample;

void BSP_AD7606_Init(BSP_AD7606_Range range, BSP_AD7606_Oversampling oversampling);
void BSP_AD7606_Reset(void);
void BSP_AD7606_SetRange(BSP_AD7606_Range range);
void BSP_AD7606_SetOversampling(BSP_AD7606_Oversampling oversampling);
int BSP_AD7606_ReadSample(BSP_AD7606_Sample *sample, uint32_t timeout_ms);

#ifdef __cplusplus
}
#endif

#endif /* BSP_AD7606_H */
