#include "app.h"

#include "cmsis_os.h"
#include "platform.h"

void APP_Init(void)
{
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
