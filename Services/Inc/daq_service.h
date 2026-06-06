#ifndef DAQ_SERVICE_H
#define DAQ_SERVICE_H

#include <stdint.h>

#include "bsp_sensor.h"

typedef enum
{
  DAQ_SERVICE_STATE_IDLE = 0,
  DAQ_SERVICE_STATE_RUNNING,
  DAQ_SERVICE_STATE_ERROR
} DAQ_SERVICE_State;

typedef enum
{
  DAQ_SERVICE_ERROR_NONE = 0,
  DAQ_SERVICE_ERROR_INVALID_CONFIG,
  DAQ_SERVICE_ERROR_IMU_INIT_FAILED,
  DAQ_SERVICE_ERROR_FRAME_ENCODE_FAILED,
  DAQ_SERVICE_ERROR_CONSOLE_WRITE_FAILED
} DAQ_SERVICE_Error;

typedef enum
{
  DAQ_SERVICE_OUTPUT_TEXT = 0,
  DAQ_SERVICE_OUTPUT_BINARY,
  DAQ_SERVICE_OUTPUT_TEXT_AND_BINARY
} DAQ_SERVICE_OutputMode;

typedef struct
{
  uint32_t sample_period_ms;
  uint32_t sensor_range;
  DAQ_SERVICE_OutputMode output_mode;
} DAQ_SERVICE_Config;

typedef struct
{
  DAQ_SERVICE_State state;
  DAQ_SERVICE_Error last_error;
  uint8_t imu_chip_id;
  uint32_t sample_count;
  uint32_t report_count;
  uint32_t tx_fail_count;
  uint32_t frame_encode_fail_count;
  uint32_t last_sample_tick;
  uint32_t last_tx_tick;
} DAQ_SERVICE_Diagnostics;

void DAQ_SERVICE_SetSensor(const BSP_SensorDriver *drv);
void DAQ_SERVICE_Init(void);
void DAQ_SERVICE_Process(void);
DAQ_SERVICE_State DAQ_SERVICE_GetState(void);
DAQ_SERVICE_Error DAQ_SERVICE_GetLastError(void);
const DAQ_SERVICE_Config *DAQ_SERVICE_GetConfig(void);
int DAQ_SERVICE_SetConfig(const DAQ_SERVICE_Config *config);
void DAQ_SERVICE_GetDiagnostics(DAQ_SERVICE_Diagnostics *diagnostics);

#endif /* DAQ_SERVICE_H */
