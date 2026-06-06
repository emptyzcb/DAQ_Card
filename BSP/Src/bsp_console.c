#include "sys.h"

void BSP_CONSOLE_Init(void)
{
  /* USART2 is configured by CubeMX before the scheduler starts. */
}

int BSP_CONSOLE_Write(const uint8_t *data, uint16_t length)
{
  if ((data == 0) || (length == 0U))
  {
    return 0;
  }

  return (HAL_UART_Transmit(&huart2, (uint8_t *)data, length, 100U) == HAL_OK);
}
