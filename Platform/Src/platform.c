#include "platform.h"

#include "bsp_console.h"
#include "bsp_imu660rc.h"
#include "daq_service.h"
#include "led_blink.h"

void PLATFORM_Init(void)
{
  BSP_CONSOLE_Init();
  DAQ_SERVICE_SetSensor(&BSP_IMU660RC_Driver);
  DAQ_SERVICE_Init();
  LED_BLINK_Init();
}

void PLATFORM_Process(void)
{
  DAQ_SERVICE_Process();
}

void PLATFORM_ProcessLedBlink(void)
{
  LED_BLINK_Process();
}
