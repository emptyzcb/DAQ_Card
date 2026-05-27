#include "bsp_console.h"

#include "usart.h"

void BSP_CONSOLE_Init(void)
{
  /* USART1 is configured by CubeMX before the scheduler starts. */
}

int BSP_CONSOLE_Write(const uint8_t *data, uint16_t length)
{
  if ((data == 0) || (length == 0U))
  {
    return 0;
  }

  return (HAL_UART_Transmit(&huart1, (uint8_t *)data, length, 100U) == HAL_OK);
}
