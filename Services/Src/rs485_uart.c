#include "rs485_uart.h"

#include <string.h>

#include "main.h"
#include "usart.h"

typedef struct
{
  volatile uint8_t head;
  volatile uint8_t tail;
  volatile uint16_t length[RS485_UART_FRAME_QUEUE_SIZE];
  uint8_t data[RS485_UART_FRAME_QUEUE_SIZE][RS485_UART_FRAME_MAX_SIZE];
  volatile uint32_t dropped;
} RS485_UART_FrameQueue;

static RS485_UART_FrameQueue rs485_rx_queue;
static uint8_t rs485_rx_work_buffer[RS485_UART_FRAME_MAX_SIZE];

/*
 * Link statistics. rx_frames/tx_frames are updated by the application task,
 * bad_frames by the Modbus validation path, dropped by the RX interrupt.
 * Volatile guarantees coherent reads from any task context.
 */
static volatile uint32_t rs485_stat_rx_frames;
static volatile uint32_t rs485_stat_bad_frames;
static volatile uint32_t rs485_stat_tx_frames;

#if RS485_COMM_LED_DIAGNOSTIC
typedef enum
{
  RS485_DIAG_NO_FRAME = 0U,
  RS485_DIAG_VALID_FRAME,
  RS485_DIAG_INVALID_FRAME
} RS485_DiagnosticState;

static volatile RS485_DiagnosticState rs485_diag_state = RS485_DIAG_NO_FRAME;
static volatile uint32_t rs485_diag_last_frame_tick;
#define RS485_DIAG_NO_DATA_TIMEOUT_MS 1500U
#define RS485_DIAG_BLINK_INTERVAL_MS  200U
#endif /* RS485_COMM_LED_DIAGNOSTIC */

static void rs485_set_direction(GPIO_PinState state)
{
  HAL_GPIO_WritePin(RS485_DIR_GPIO_Port, RS485_DIR_Pin, state);
}

static uint8_t rs485_queue_next(uint8_t index)
{
  index++;
  if (index >= RS485_UART_FRAME_QUEUE_SIZE)
  {
    index = 0U;
  }

  return index;
}

void RS485_UART_Init(void)
{
  memset(&rs485_rx_queue, 0, sizeof(rs485_rx_queue));
  rs485_stat_rx_frames = 0U;
  rs485_stat_bad_frames = 0U;
  rs485_stat_tx_frames = 0U;
  rs485_set_direction(RS485_DIR_RX);

  if (HAL_UARTEx_ReceiveToIdle_IT(&huart1,
                                  rs485_rx_work_buffer,
                                  RS485_UART_FRAME_MAX_SIZE) != HAL_OK)
  {
    Error_Handler();
  }
}

int RS485_UART_TryReceiveFrame(uint8_t *dst, uint16_t capacity, uint16_t *length)
{
  uint8_t head;
  uint16_t frame_length;

  if ((dst == 0) || (length == 0))
  {
    return -1;
  }

  head = rs485_rx_queue.head;
  if (head == rs485_rx_queue.tail)
  {
    return 0;
  }

  frame_length = rs485_rx_queue.length[head];
  if (capacity < frame_length)
  {
    return -1;
  }

  memcpy(dst, rs485_rx_queue.data[head], frame_length);
  *length = frame_length;
  rs485_rx_queue.head = rs485_queue_next(head);
  rs485_stat_rx_frames++;
  return 1;
}

int RS485_UART_Send(const uint8_t *data, uint16_t length, uint32_t timeout_ms)
{
  HAL_StatusTypeDef status;

  if ((data == 0) || (length == 0U))
  {
    return 0;
  }

  /* Keep the verified hardware direction sequence unchanged. */
  rs485_set_direction(RS485_DIR_TX);
  status = HAL_UART_Transmit(&huart1, (uint8_t *)data, length, timeout_ms);
  rs485_set_direction(RS485_DIR_RX);

  if (status == HAL_OK)
  {
    rs485_stat_tx_frames++;
    return 1;
  }

  return 0;
}

uint32_t RS485_UART_RxDropped(void)
{
  return rs485_rx_queue.dropped;
}

void RS485_UART_GetStats(RS485_UART_Stats *stats)
{
  if (stats == 0)
  {
    return;
  }

  stats->rx_frames = rs485_stat_rx_frames;
  stats->bad_frames = rs485_stat_bad_frames;
  stats->dropped = rs485_rx_queue.dropped;
  stats->tx_frames = rs485_stat_tx_frames;
}

void RS485_UART_DiagnosticMarkFrame(uint8_t valid)
{
  if (valid == 0U)
  {
    rs485_stat_bad_frames++;
  }

#if RS485_COMM_LED_DIAGNOSTIC
  rs485_diag_last_frame_tick = HAL_GetTick();
  rs485_diag_state = (valid != 0U) ? RS485_DIAG_VALID_FRAME : RS485_DIAG_INVALID_FRAME;
#else
  (void)valid;
#endif /* RS485_COMM_LED_DIAGNOSTIC */
}

void RS485_UART_DiagnosticProcess(void)
{
#if RS485_COMM_LED_DIAGNOSTIC
  uint32_t now = HAL_GetTick();

  /* No frame within the watchdog window: return the LED to the idle state. */
  if ((rs485_diag_last_frame_tick == 0U) ||
      ((now - rs485_diag_last_frame_tick) >= RS485_DIAG_NO_DATA_TIMEOUT_MS))
  {
    rs485_diag_state = RS485_DIAG_NO_FRAME;
  }

  if (rs485_diag_state == RS485_DIAG_VALID_FRAME)
  {
    HAL_GPIO_WritePin(PA0_LED_GPIO_Port, PA0_LED_Pin, PA0_LED_ON);
  }
  else if (rs485_diag_state == RS485_DIAG_INVALID_FRAME)
  {
    GPIO_PinState state = (((now / RS485_DIAG_BLINK_INTERVAL_MS) & 0x01U) != 0U)
                              ? PA0_LED_ON
                              : PA0_LED_OFF;
    HAL_GPIO_WritePin(PA0_LED_GPIO_Port, PA0_LED_Pin, state);
  }
  else
  {
    HAL_GPIO_WritePin(PA0_LED_GPIO_Port, PA0_LED_Pin, PA0_LED_OFF);
  }
#endif /* RS485_COMM_LED_DIAGNOSTIC */
}

void HAL_UARTEx_RxEventCallback(UART_HandleTypeDef *huart, uint16_t size)
{
  uint8_t tail;
  uint8_t next_tail;

  if (huart != &huart1)
  {
    return;
  }

  if ((size > 0U) && (size <= RS485_UART_FRAME_MAX_SIZE))
  {
    tail = rs485_rx_queue.tail;
    next_tail = rs485_queue_next(tail);

    if (next_tail == rs485_rx_queue.head)
    {
      rs485_rx_queue.dropped++;
    }
    else
    {
      memcpy(rs485_rx_queue.data[tail], rs485_rx_work_buffer, size);
      rs485_rx_queue.length[tail] = size;
      __DMB();
      rs485_rx_queue.tail = next_tail;
    }
  }

  if (HAL_UARTEx_ReceiveToIdle_IT(&huart1,
                                  rs485_rx_work_buffer,
                                  RS485_UART_FRAME_MAX_SIZE) != HAL_OK)
  {
    rs485_rx_queue.dropped++;
  }
}

void HAL_UART_ErrorCallback(UART_HandleTypeDef *huart)
{
  if (huart != &huart1)
  {
    return;
  }

  (void)HAL_UARTEx_ReceiveToIdle_IT(&huart1,
                                    rs485_rx_work_buffer,
                                    RS485_UART_FRAME_MAX_SIZE);
}
