#include "sys.h"

void AppTask_Ad7606(void *argument)
{
  (void)argument;

  for (;;)
  {
    AD7606_SERVICE_Process();
    vTaskDelay(pdMS_TO_TICKS(1U));
  }
}
