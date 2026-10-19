#include "sys.h"

void APP_Init(void)
{
  DataHub_Init();
  PLATFORM_Init();
}

void APP_RunLedBlink(void)
{
  uint32_t last_report_tick = HAL_GetTick();

  for (;;)
  {
    uint32_t now = HAL_GetTick();

    PLATFORM_ProcessLedBlink();
    if ((uint32_t)(now - last_report_tick) >= 5000U)
    {
      last_report_tick = now;
      printf("[SYS] alive FW=%s\r\n", APP_FIRMWARE_VERSION);
    }
    osDelay(1);
  }
}
