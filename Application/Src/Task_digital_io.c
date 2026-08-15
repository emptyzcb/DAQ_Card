#include "sys.h"

void Task_digital_io(void *arg)
{
  uint16_t last_input_mask;
  uint32_t last_report_tick;

  (void)arg;

  DIGITAL_IO_SERVICE_Init();
  DIGITAL_IO_SERVICE_AllOutputsOff();

  last_input_mask = DIGITAL_IO_SERVICE_ReadInputs();
  last_report_tick = HAL_GetTick();

  printf("[DIO] init ok, input=0x%02X output=0x%03X\r\n",
         (unsigned int)last_input_mask,
         (unsigned int)DIGITAL_IO_SERVICE_GetOutputMask());

  for (;;)
  {
    uint16_t input_mask = DIGITAL_IO_SERVICE_ReadInputs();
    uint32_t now = HAL_GetTick();

    if ((input_mask != last_input_mask) || ((now - last_report_tick) >= 5000U))
    {
      printf("[DIO] input=0x%02X output=0x%03X\r\n",
             (unsigned int)input_mask,
             (unsigned int)DIGITAL_IO_SERVICE_GetOutputMask());

      last_input_mask = input_mask;
      last_report_tick = now;
    }

    vTaskDelay(pdMS_TO_TICKS(50));
  }
}
