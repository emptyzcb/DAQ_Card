#include "sys.h"

void PLATFORM_Init(void)
{
  BSP_CONSOLE_Init();
  RS485_UART_Init();
  CAN_SERVICE_Init();
  AD7606_SERVICE_Init();
  DAQ_SERVICE_SetSensor(&BSP_IMU660RC_Driver);
  DAQ_SERVICE_Init();
  LED_BLINK_Init();
}

void PLATFORM_Process(void)
{
  AD7606_SERVICE_Process();
  DAQ_SERVICE_Process();
}

void PLATFORM_ProcessLedBlink(void)
{
  LED_BLINK_Process();
}
