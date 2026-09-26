#include "modbus_slave.h"

#include "usart.h"

/* USART1 RX interrupt: push each received byte into the Modbus slave
 * ring buffer. Overrun errors are cleared without touching the buffer. */
void USART1_IRQHandler(void)
{
  if ((USART1->ISR & USART_ISR_RXNE_RXFNE) != 0U)
  {
    uint8_t byte = (uint8_t)(USART1->RDR & 0xFFU);
    MODBUS_SLAVE_OnByteReceived(byte);
  }

  if ((USART1->ISR & USART_ISR_ORE) != 0U)
  {
    USART1->ICR = USART_ICR_ORECF;
  }
}
