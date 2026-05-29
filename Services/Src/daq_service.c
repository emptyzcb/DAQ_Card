#include "daq_service.h"

#include <stddef.h>
#include <stdio.h>

#include "bsp_console.h"
#include "bsp_imu660rc.h"
#include "stm32f4xx_hal.h"
#include "stream_protocol.h"

static DAQ_SERVICE_Config daq_config = {
  100U,
  BSP_IMU660RC_GYRO_RANGE_2000DPS,
  DAQ_SERVICE_OUTPUT_TEXT
};
static DAQ_SERVICE_Diagnostics daq_diagnostics;
static uint16_t daq_frame_sequence;

static int is_output_text_enabled(void)
{
  return (daq_config.output_mode == DAQ_SERVICE_OUTPUT_TEXT) ||
         (daq_config.output_mode == DAQ_SERVICE_OUTPUT_TEXT_AND_BINARY);
}

static int is_output_binary_enabled(void)
{
  return (daq_config.output_mode == DAQ_SERVICE_OUTPUT_BINARY) ||
         (daq_config.output_mode == DAQ_SERVICE_OUTPUT_TEXT_AND_BINARY);
}

static void set_error(DAQ_SERVICE_Error error)
{
  daq_diagnostics.last_error = error;
}

static int validate_config(const DAQ_SERVICE_Config *config)
{
  if (config == NULL)
  {
    return 0;
  }

  if (config->sample_period_ms == 0U)
  {
    return 0;
  }

  if (config->gyro_range > BSP_IMU660RC_GYRO_RANGE_4000DPS)
  {
    return 0;
  }

  if (config->output_mode > DAQ_SERVICE_OUTPUT_TEXT_AND_BINARY)
  {
    return 0;
  }

  return 1;
}

static int32_t scale_float(float value, float scale)
{
  if (value >= 0.0f)
  {
    return (int32_t)((value * scale) + 0.5f);
  }

  return (int32_t)((value * scale) - 0.5f);
}

static void format_one_scaled(char *buffer, size_t size, int32_t value, uint32_t divisor)
{
  uint32_t magnitude;

  if (value < 0)
  {
    magnitude = (uint32_t)(-value);
    (void)snprintf(buffer, size, "-%lu.%03lu",
                   (unsigned long)(magnitude / divisor),
                   (unsigned long)(magnitude % divisor));
  }
  else
  {
    magnitude = (uint32_t)value;
    (void)snprintf(buffer, size, "%lu.%03lu",
                   (unsigned long)(magnitude / divisor),
                   (unsigned long)(magnitude % divisor));
  }
}

static void format_scaled(char *buffer, size_t size, const char *label,
                          int32_t x, int32_t y, int32_t z, uint32_t divisor)
{
  char x_text[16];
  char y_text[16];
  char z_text[16];

  format_one_scaled(x_text, sizeof(x_text), x, divisor);
  format_one_scaled(y_text, sizeof(y_text), y, divisor);
  format_one_scaled(z_text, sizeof(z_text), z, divisor);
  (void)snprintf(buffer, size, "%s=%s,%s,%s", label, x_text, y_text, z_text);
}

static int write_console(const uint8_t *data, uint16_t length)
{
  if (!BSP_CONSOLE_Write(data, length))
  {
    daq_diagnostics.tx_fail_count++;
    set_error(DAQ_SERVICE_ERROR_CONSOLE_WRITE_FAILED);
    return 0;
  }

  daq_diagnostics.last_tx_tick = HAL_GetTick();
  return 1;
}

static void report_gyro_text(const BSP_IMU660RC_GyroData *data)
{
  char gyro_text[72];
  char line[96];
  int length;

  format_scaled(gyro_text, sizeof(gyro_text), "gyro[dps]",
                scale_float(data->x_dps, 1000.0f),
                scale_float(data->y_dps, 1000.0f),
                scale_float(data->z_dps, 1000.0f),
                1000U);
  length = snprintf(line, sizeof(line), "%s\r\n", gyro_text);
  if (length > 0)
  {
    (void)write_console((const uint8_t *)line, (uint16_t)length);
  }
}

static void report_gyro_binary(const BSP_IMU660RC_GyroData *data, uint32_t timestamp_ms)
{
  STREAM_PROTOCOL_GyroSample sample;
  uint8_t frame[STREAM_PROTOCOL_MAX_FRAME_SIZE];
  uint16_t frame_length;

  sample.x_raw = data->x_raw;
  sample.y_raw = data->y_raw;
  sample.z_raw = data->z_raw;
  sample.x_mdps = scale_float(data->x_dps, 1000.0f);
  sample.y_mdps = scale_float(data->y_dps, 1000.0f);
  sample.z_mdps = scale_float(data->z_dps, 1000.0f);

  frame_length = STREAM_PROTOCOL_EncodeGyro(daq_frame_sequence,
                                            timestamp_ms,
                                            &sample,
                                            frame,
                                            (uint16_t)sizeof(frame));
  if (frame_length == 0U)
  {
    daq_diagnostics.frame_encode_fail_count++;
    set_error(DAQ_SERVICE_ERROR_FRAME_ENCODE_FAILED);
    return;
  }

  (void)write_console(frame, frame_length);
  daq_frame_sequence++;
}

void DAQ_SERVICE_Init(void)
{
  const char banner[] = "IMU660RC gyro init...\r\n";

  daq_diagnostics.state = DAQ_SERVICE_STATE_IDLE;
  daq_diagnostics.last_error = DAQ_SERVICE_ERROR_NONE;
  daq_diagnostics.imu_chip_id = 0U;
  daq_diagnostics.sample_count = 0U;
  daq_diagnostics.report_count = 0U;
  daq_diagnostics.tx_fail_count = 0U;
  daq_diagnostics.frame_encode_fail_count = 0U;
  daq_diagnostics.last_sample_tick = 0U;
  daq_diagnostics.last_tx_tick = 0U;
  daq_frame_sequence = 0U;

  (void)write_console((const uint8_t *)banner, (uint16_t)(sizeof(banner) - 1U));

  if (BSP_IMU660RC_GyroInit(daq_config.gyro_range))
  {
    daq_diagnostics.state = DAQ_SERVICE_STATE_RUNNING;
    daq_diagnostics.last_sample_tick = HAL_GetTick();
    daq_diagnostics.imu_chip_id = BSP_IMU660RC_ReadChipId();
    (void)write_console((const uint8_t *)"IMU660RC gyro ready\r\n", 21U);
  }
  else
  {
    char line[48];
    int length;

    daq_diagnostics.state = DAQ_SERVICE_STATE_ERROR;
    set_error(DAQ_SERVICE_ERROR_IMU_INIT_FAILED);
    daq_diagnostics.imu_chip_id = BSP_IMU660RC_ReadChipId();
    length = snprintf(line, sizeof(line), "IMU660RC init failed, id=0x%02X\r\n",
                      daq_diagnostics.imu_chip_id);
    if (length > 0)
    {
      (void)write_console((const uint8_t *)line, (uint16_t)length);
    }
  }
}

void DAQ_SERVICE_Process(void)
{
  uint32_t now = HAL_GetTick();

  if (daq_diagnostics.state != DAQ_SERVICE_STATE_RUNNING)
  {
    return;
  }

  if ((now - daq_diagnostics.last_sample_tick) >= daq_config.sample_period_ms)
  {
    BSP_IMU660RC_GyroData data;

    daq_diagnostics.last_sample_tick = now;
    daq_diagnostics.sample_count++;
    BSP_IMU660RC_GyroRead(&data);

    if (is_output_text_enabled())
    {
      report_gyro_text(&data);
    }

    if (is_output_binary_enabled())
    {
      report_gyro_binary(&data, now);
    }

    daq_diagnostics.report_count++;
  }
}

DAQ_SERVICE_State DAQ_SERVICE_GetState(void)
{
  return daq_diagnostics.state;
}

DAQ_SERVICE_Error DAQ_SERVICE_GetLastError(void)
{
  return daq_diagnostics.last_error;
}

const DAQ_SERVICE_Config *DAQ_SERVICE_GetConfig(void)
{
  return &daq_config;
}

int DAQ_SERVICE_SetConfig(const DAQ_SERVICE_Config *config)
{
  if (!validate_config(config))
  {
    set_error(DAQ_SERVICE_ERROR_INVALID_CONFIG);
    return 0;
  }

  if ((daq_diagnostics.state == DAQ_SERVICE_STATE_RUNNING) &&
      (config->gyro_range != daq_config.gyro_range))
  {
    set_error(DAQ_SERVICE_ERROR_INVALID_CONFIG);
    return 0;
  }

  daq_config = *config;
  set_error(DAQ_SERVICE_ERROR_NONE);
  return 1;
}

void DAQ_SERVICE_GetDiagnostics(DAQ_SERVICE_Diagnostics *diagnostics)
{
  if (diagnostics == NULL)
  {
    return;
  }

  *diagnostics = daq_diagnostics;
}
