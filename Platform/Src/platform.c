#include "platform.h"

#include "bsp_console.h"
#include "daq_service.h"

void PLATFORM_Init(void)
{
  BSP_CONSOLE_Init();
  DAQ_SERVICE_Init();
}

void PLATFORM_Process(void)
{
  DAQ_SERVICE_Process();
}
