#ifndef BSP_SENSOR_H
#define BSP_SENSOR_H

#include <stdint.h>

typedef struct
{
  int16_t x_raw;
  int16_t y_raw;
  int16_t z_raw;
  float x_conv;
  float y_conv;
  float z_conv;
} BSP_SensorData;

typedef int  (*BSP_SensorInitFn)(uint32_t range);
typedef void (*BSP_SensorReadFn)(BSP_SensorData *data);
typedef uint8_t (*BSP_SensorReadIdFn)(void);
typedef int  (*BSP_SensorValidateRangeFn)(uint32_t range);

typedef struct
{
  const char               *name;
  BSP_SensorInitFn          Init;
  BSP_SensorReadFn          Read;
  BSP_SensorReadIdFn        ReadChipId;
  BSP_SensorValidateRangeFn ValidateRange;
  uint32_t                  default_range;
} BSP_SensorDriver;

#endif /* BSP_SENSOR_H */
