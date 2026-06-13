#include "rs485_uart.h"
#include "usart.h"

extern DMA_HandleTypeDef hdma_usart3_rx;

void DMA1_Stream0_IRQHandler(void)
{
  HAL_DMA_IRQHandler(&hdma_usart3_rx);
}

void USART3_IRQHandler(void)
{
  if ((__HAL_UART_GET_FLAG(&huart3, UART_FLAG_IDLE) != RESET) &&
      (__HAL_UART_GET_IT_SOURCE(&huart3, UART_IT_IDLE) != RESET))
  {
    __HAL_UART_CLEAR_IDLEFLAG(&huart3);
    RS485_UART_IdleIRQHandler();
  }

  HAL_UART_IRQHandler(&huart3);
}
