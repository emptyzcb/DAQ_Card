#include "sys.h"

void AppTask_IoLogic(void *argument)
{
  (void)argument;
  IO_LOGIC_ENGINE_Init();

  for (;;)
  {
    DataHubDigitalIoData digital_io;

    DataHub_GetDigitalIo(&digital_io);
    IO_LOGIC_ENGINE_Step(digital_io.input_mask, HAL_GetTick());
    vTaskDelay(pdMS_TO_TICKS(10U));
  }
}
