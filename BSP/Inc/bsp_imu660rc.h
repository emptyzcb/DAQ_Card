#ifndef BSP_IMU660RC_H
#define BSP_IMU660RC_H

#include <stdint.h>

#include "bsp_sensor.h"
#include "stm32f4xx_hal.h"

/*
 * IMU660RC gyroscope BSP adapter.
 *
 * Default software-SPI wiring. Change these macros to match the board:
 * SPC/SCL -> SCK, SDI/SDA -> MOSI, SDO/SA0 -> MISO.
 */
#ifndef BSP_IMU660RC_SCK_GPIO_Port
#define BSP_IMU660RC_SCK_GPIO_Port  GPIOC
#define BSP_IMU660RC_SCK_Pin        GPIO_PIN_10
#endif

#ifndef BSP_IMU660RC_MOSI_GPIO_Port
#define BSP_IMU660RC_MOSI_GPIO_Port GPIOC
#define BSP_IMU660RC_MOSI_Pin       GPIO_PIN_12
#endif

#ifndef BSP_IMU660RC_MISO_GPIO_Port
#define BSP_IMU660RC_MISO_GPIO_Port GPIOC
#define BSP_IMU660RC_MISO_Pin       GPIO_PIN_11
#endif

#ifndef BSP_IMU660RC_CS_GPIO_Port
#define BSP_IMU660RC_CS_GPIO_Port   GPIOC
#define BSP_IMU660RC_CS_Pin         GPIO_PIN_9
#endif

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
  float x_dps;
  float y_dps;
  float z_dps;
} BSP_IMU660RC_GyroData;

int BSP_IMU660RC_GyroInit(BSP_IMU660RC_GyroRange range);
void BSP_IMU660RC_GyroRead(BSP_IMU660RC_GyroData *data);
uint8_t BSP_IMU660RC_ReadChipId(void);

extern const BSP_SensorDriver BSP_IMU660RC_Driver;

#endif /* BSP_IMU660RC_H */
