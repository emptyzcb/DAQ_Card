#include "sys.h"

void APP_Init(void)
{
  PLATFORM_Init();
  DataHub_Init();
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
