#include "sys.h"

void Task_io_logic(void *arg)
{
  (void)arg;
  IO_LOGIC_ENGINE_Init();

  for (;;)
  {
    uint16_t input_mask = DIGITAL_IO_SERVICE_ReadInputs();
    IO_LOGIC_ENGINE_Step(input_mask, HAL_GetTick());
    vTaskDelay(pdMS_TO_TICKS(10U));
  }
}
