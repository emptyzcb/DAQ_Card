#ifndef RS485_UART_H
#define RS485_UART_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#define RS485_UART_FRAME_MAX_SIZE   256U
#define RS485_UART_FRAME_QUEUE_SIZE 4U

/**
 * @brief RS485 link statistics, readable by the Modbus register map.
 *
 * All counters are cumulative since RS485_UART_Init() and wrap at 32 bits.
 * They describe the physical link health as seen by the application layer.
 */
typedef struct
{
  uint32_t rx_frames;  /*!< Frames dequeued by the application for processing. */
  uint32_t bad_frames; /*!< Frames rejected by Modbus validation (CRC/address/length). */
  uint32_t dropped;    /*!< Frames discarded on RX queue overflow or re-arm failure. */
  uint32_t tx_frames;  /*!< Frames successfully transmitted. */
} RS485_UART_Stats;

void RS485_UART_Init(void);
int RS485_UART_TryReceiveFrame(uint8_t *dst, uint16_t capacity, uint16_t *length);
int RS485_UART_Send(const uint8_t *data, uint16_t length, uint32_t timeout_ms);
uint32_t RS485_UART_RxDropped(void);

/**
 * @brief Copy the current RS485 link statistics.
 * @param stats Destination buffer; ignored when NULL.
 * @note Callable from any task; counters are updated from interrupt context
 *       and read through volatile-qualified storage.
 */
void RS485_UART_GetStats(RS485_UART_Stats *stats);

/**
 * @brief Mark the validity of the most recently received frame.
 * @param valid Non-zero for a frame that passed Modbus validation.
 *
 * Invalid frames are counted as bad_frames. When RS485_COMM_LED_DIAGNOSTIC
 * is enabled, this also drives the PA0 indicator state machine.
 */
void RS485_UART_DiagnosticMarkFrame(uint8_t valid);

/**
 * @brief Update the PA0 communication diagnostic LED.
 *
 * Should be called periodically from the Modbus task. Behaviour:
 * OFF when no frame arrived within the timeout window, ON for a valid
 * frame, BLINK for an invalid frame. Compiled out unless
 * RS485_COMM_LED_DIAGNOSTIC is defined to 1U in main.h.
 */
void RS485_UART_DiagnosticProcess(void);

#ifdef __cplusplus
}
#endif

#endif /* RS485_UART_H */
