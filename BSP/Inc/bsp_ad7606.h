#ifndef BSP_AD7606_H
#define BSP_AD7606_H

#include <stdint.h>

#include "board_pins.h"

#ifdef __cplusplus
extern "C" {
#endif

#define BSP_AD7606_CHANNEL_COUNT 8U

#define BSP_AD7606_CONVST_GPIO_Port BOARD_AD7606_CONVST_GPIO_Port
#define BSP_AD7606_CONVST_Pin       BOARD_AD7606_CONVST_Pin
#define BSP_AD7606_RESET_GPIO_Port  BOARD_AD7606_RESET_GPIO_Port
#define BSP_AD7606_RESET_Pin        BOARD_AD7606_RESET_Pin
#define BSP_AD7606_RANGE_GPIO_Port  BOARD_AD7606_RANGE_GPIO_Port
#define BSP_AD7606_RANGE_Pin        BOARD_AD7606_RANGE_Pin
#define BSP_AD7606_OS0_GPIO_Port    BOARD_AD7606_OS0_GPIO_Port
#define BSP_AD7606_OS0_Pin          BOARD_AD7606_OS0_Pin
#define BSP_AD7606_OS1_GPIO_Port    BOARD_AD7606_OS1_GPIO_Port
#define BSP_AD7606_OS1_Pin          BOARD_AD7606_OS1_Pin
#define BSP_AD7606_OS2_GPIO_Port    BOARD_AD7606_OS2_GPIO_Port
#define BSP_AD7606_OS2_Pin          BOARD_AD7606_OS2_Pin
#define BSP_AD7606_SCLK_GPIO_Port   BOARD_AD7606_SCLK_GPIO_Port
#define BSP_AD7606_SCLK_Pin         BOARD_AD7606_SCLK_Pin
#define BSP_AD7606_DOUTA_GPIO_Port  BOARD_AD7606_DOUTA_GPIO_Port
#define BSP_AD7606_DOUTA_Pin        BOARD_AD7606_DOUTA_Pin
#define BSP_AD7606_DOUTB_GPIO_Port  BOARD_AD7606_DOUTB_GPIO_Port
#define BSP_AD7606_DOUTB_Pin        BOARD_AD7606_DOUTB_Pin
#define BSP_AD7606_CS_GPIO_Port     BOARD_AD7606_CS_GPIO_Port
#define BSP_AD7606_CS_Pin           BOARD_AD7606_CS_Pin
#define BSP_AD7606_BUSY_GPIO_Port   BOARD_AD7606_BUSY_GPIO_Port
#define BSP_AD7606_BUSY_Pin         BOARD_AD7606_BUSY_Pin

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

typedef enum
{
  BSP_AD7606_HEALTH_NOT_CHECKED = 0,
  BSP_AD7606_HEALTH_OK,
  BSP_AD7606_HEALTH_BUSY_STUCK_HIGH,
  BSP_AD7606_HEALTH_BUSY_DID_NOT_ASSERT,
  BSP_AD7606_HEALTH_BUSY_TIMEOUT,
  BSP_AD7606_HEALTH_DATA_SUSPICIOUS
} BSP_AD7606_HealthResult;

void BSP_AD7606_Init(BSP_AD7606_Range range, BSP_AD7606_Oversampling oversampling);
void BSP_AD7606_Reset(void);
void BSP_AD7606_SetRange(BSP_AD7606_Range range);
void BSP_AD7606_SetOversampling(BSP_AD7606_Oversampling oversampling);
int BSP_AD7606_ReadSample(BSP_AD7606_Sample *sample, uint32_t timeout_ms);
BSP_AD7606_HealthResult BSP_AD7606_CheckHealth(BSP_AD7606_Sample *sample,
                                               uint32_t timeout_ms);

#ifdef __cplusplus
}
#endif

#endif /* BSP_AD7606_H */
