#include "daq_service.h"

#include <stdio.h>

#include "bsp_console.h"
#include "bsp_imu660rc.h"
#include "stm32f4xx_hal.h"

static DAQ_SERVICE_State daq_state;
static uint32_t daq_last_report_tick;

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

void DAQ_SERVICE_Init(void)
{
  const char banner[] = "IMU660RC gyro init...\r\n";

  (void)BSP_CONSOLE_Write((const uint8_t *)banner, (uint16_t)(sizeof(banner) - 1U));

  if (BSP_IMU660RC_GyroInit(BSP_IMU660RC_GYRO_RANGE_2000DPS))
  {
    daq_state = DAQ_SERVICE_STATE_RUNNING;
    daq_last_report_tick = HAL_GetTick();
    (void)BSP_CONSOLE_Write((const uint8_t *)"IMU660RC gyro ready\r\n", 21U);
  }
  else
  {
    char line[48];
    int length;

    daq_state = DAQ_SERVICE_STATE_ERROR;
    length = snprintf(line, sizeof(line), "IMU660RC init failed, id=0x%02X\r\n",
                      BSP_IMU660RC_ReadChipId());
    if (length > 0)
    {
      (void)BSP_CONSOLE_Write((const uint8_t *)line, (uint16_t)length);
    }
  }
}

void DAQ_SERVICE_Process(void)
{
  uint32_t now = HAL_GetTick();

  if (daq_state != DAQ_SERVICE_STATE_RUNNING)
  {
    return;
  }

  if ((now - daq_last_report_tick) >= 100U)
  {
    BSP_IMU660RC_GyroData data;
    char gyro_text[72];
    char line[96];
    int length;

    daq_last_report_tick = now;
    BSP_IMU660RC_GyroRead(&data);

    format_scaled(gyro_text, sizeof(gyro_text), "gyro[dps]",
                  scale_float(data.x_dps, 1000.0f),
                  scale_float(data.y_dps, 1000.0f),
                  scale_float(data.z_dps, 1000.0f),
                  1000U);
    length = snprintf(line, sizeof(line), "%s\r\n", gyro_text);
    if (length > 0)
    {
      (void)BSP_CONSOLE_Write((const uint8_t *)line, (uint16_t)length);
    }
  }
}

DAQ_SERVICE_State DAQ_SERVICE_GetState(void)
{
  return daq_state;
}
