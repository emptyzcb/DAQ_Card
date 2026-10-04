#include "sys.h"

void PLATFORM_Init(void)
{
  /* Outputs are initialized first and forced to their inactive state. */
  DIGITAL_IO_SERVICE_Init();
  DIGITAL_IO_SERVICE_AllOutputsOff();

  /* Persistent IO rules must be loaded before the rule task starts. */
  IO_CONFIG_Init();

  BSP_CONSOLE_Init();
  RS485_UART_Init();
  CAN_SERVICE_Init();
  AD7606_SERVICE_Init();
  LED_BLINK_Init();
}

void PLATFORM_ProcessLedBlink(void)
{
  LED_BLINK_Process();
}
