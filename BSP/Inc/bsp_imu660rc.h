#ifndef BSP_IMU660RC_H
#define BSP_IMU660RC_H

#include <stdint.h>

#include "bsp_sensor.h"
#include "stm32h7xx_hal.h"

/*
 * BMI270 BSP adapter.
 *
 * Hardware SPI wiring:
 *   SCK  -> PA5
 *   MISO -> PA6
 *   MOSI -> PA7
 *   CS   -> PD9
 */
#ifndef BSP_IMU660RC_SCK_GPIO_Port
#define BSP_IMU660RC_SCK_GPIO_Port  GPIOA
#define BSP_IMU660RC_SCK_Pin        GPIO_PIN_5
#endif

#ifndef BSP_IMU660RC_MOSI_GPIO_Port
#define BSP_IMU660RC_MOSI_GPIO_Port GPIOA
#define BSP_IMU660RC_MOSI_Pin       GPIO_PIN_7
#endif

#ifndef BSP_IMU660RC_MISO_GPIO_Port
#define BSP_IMU660RC_MISO_GPIO_Port GPIOA
#define BSP_IMU660RC_MISO_Pin       GPIO_PIN_6
#endif

#ifndef BSP_IMU660RC_CS_GPIO_Port
#define BSP_IMU660RC_CS_GPIO_Port   GPIOD
#define BSP_IMU660RC_CS_Pin         GPIO_PIN_9
#endif

typedef enum
{
  BSP_IMU660RC_ACCEL_RANGE_2G = 0,
  BSP_IMU660RC_ACCEL_RANGE_4G,
  BSP_IMU660RC_ACCEL_RANGE_8G,
  BSP_IMU660RC_ACCEL_RANGE_16G
} BSP_IMU660RC_AccelRange;

typedef enum
{
  BSP_IMU660RC_GYRO_RANGE_125DPS = 0,
  BSP_IMU660RC_GYRO_RANGE_250DPS,
  BSP_IMU660RC_GYRO_RANGE_500DPS,
  BSP_IMU660RC_GYRO_RANGE_1000DPS,
  BSP_IMU660RC_GYRO_RANGE_2000DPS,
  BSP_IMU660RC_GYRO_RANGE_4000DPS
} BSP_IMU660RC_GyroRange;

typedef struct
{
  int16_t x_raw;
  int16_t y_raw;
  int16_t z_raw;
  float x_g;
  float y_g;
  float z_g;
  float x_mps2;
  float y_mps2;
  float z_mps2;
} BSP_IMU660RC_AccelData;

typedef struct
{
  int16_t x_raw;
  int16_t y_raw;
  int16_t z_raw;
  float x_dps;
  float y_dps;
  float z_dps;
} BSP_IMU660RC_GyroData;

typedef struct
{
  BSP_IMU660RC_AccelData accel;
  BSP_IMU660RC_GyroData gyro;
} BSP_IMU660RC_6AxisData;

int BSP_IMU660RC_Init(BSP_IMU660RC_AccelRange accel_range,
                      BSP_IMU660RC_GyroRange gyro_range);
int BSP_IMU660RC_AccelInit(BSP_IMU660RC_AccelRange range);
void BSP_IMU660RC_AccelRead(BSP_IMU660RC_AccelData *data);
int BSP_IMU660RC_GyroInit(BSP_IMU660RC_GyroRange range);
void BSP_IMU660RC_GyroRead(BSP_IMU660RC_GyroData *data);
int BSP_IMU660RC_Read6Axis(BSP_IMU660RC_6AxisData *data);
uint8_t BSP_IMU660RC_ReadChipId(void);
void BSP_IMU660RC_PrintDebug(void);
void BSP_IMU660RC_PrintSpiProbe(void);

extern const BSP_SensorDriver BSP_IMU660RC_Driver;

#endif /* BSP_IMU660RC_H */
