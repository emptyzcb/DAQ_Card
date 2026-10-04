#include "sys.h"

void AppTask_DigitalIo(void *argument)
{
  DataHubDigitalIoData io_snapshot = {0};

  (void)argument;

  for (;;)
  {
    /* 由本任务统一采集IO状态，并将完整快照发布给其他任务。 */
    io_snapshot.timestamp_ms = HAL_GetTick();
    io_snapshot.input_mask = DIGITAL_IO_SERVICE_ReadInputs();
    io_snapshot.output_mask = DIGITAL_IO_SERVICE_GetOutputMask();
    DataHub_PublishDigitalIo(&io_snapshot);

    vTaskDelay(pdMS_TO_TICKS(50));
  }
}
