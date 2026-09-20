#ifndef RS485_UART_H
#define RS485_UART_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#define RS485_UART_FRAME_MAX_SIZE   256U
#define RS485_UART_FRAME_QUEUE_SIZE 4U

void RS485_UART_Init(void);
int RS485_UART_TryReceiveFrame(uint8_t *dst, uint16_t capacity, uint16_t *length);
int RS485_UART_Send(const uint8_t *data, uint16_t length, uint32_t timeout_ms);
uint32_t RS485_UART_RxDropped(void);

/* Communication test LED state: off=no frame, on=valid frame, blink=bad frame. */
void RS485_UART_DiagnosticMarkFrame(uint8_t valid);
void RS485_UART_DiagnosticProcess(void);

#ifdef __cplusplus
}
#endif

#endif /* RS485_UART_H */
