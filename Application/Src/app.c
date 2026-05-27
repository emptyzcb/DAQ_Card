#include "app.h"

#include "platform.h"

void APP_Init(void)
{
  PLATFORM_Init();
}

void APP_RunOnce(void)
{
  PLATFORM_Process();
}
