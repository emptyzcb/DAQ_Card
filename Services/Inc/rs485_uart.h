#ifndef RS485_UART_H
#define RS485_UART_H

#include <stdint.h>

#include "usart.h"

#ifdef __cplusplus
extern "C" {
#endif

#define RS485_UART_DMA_RX_BUF_SIZE 10U
#define RS485_UART_RX_RING_SIZE    2048U

void RS485_UART_Init(void);
void RS485_UART_PumpRxFromDma(void);
void RS485_UART_IdleIRQHandler(void);
void RS485_UART_Flush(void);

uint16_t RS485_UART_Available(void);
uint16_t RS485_UART_Read(uint8_t *dst, uint16_t len);
uint16_t RS485_UART_ReadClaim(uint8_t **pptr);
void RS485_UART_ReadCommit(uint16_t len);
uint32_t RS485_UART_RxDropped(void);
uint32_t RS485_UART_RxHalfIrqCount(void);
uint32_t RS485_UART_RxFullIrqCount(void);
uint32_t RS485_UART_RxIdleIrqCount(void);

#ifdef __cplusplus
}
#endif

#endif /* RS485_UART_H */
