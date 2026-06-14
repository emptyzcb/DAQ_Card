#include "sys.h"

void APP_Init(void)
{
  DataHub_Init();
  PLATFORM_Init();
}

void APP_RunOnce(void)
{
  PLATFORM_Process();
}

void APP_RunLedBlink(void)
{
  for (;;)
  {
    PLATFORM_ProcessLedBlink();
    osDelay(1);
  }
}
