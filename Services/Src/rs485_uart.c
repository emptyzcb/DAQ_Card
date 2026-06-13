#include "rs485_uart.h"

#include <string.h>

#define RS485_UART_RX_RING_MASK (RS485_UART_RX_RING_SIZE - 1U)

#if ((RS485_UART_RX_RING_SIZE & RS485_UART_RX_RING_MASK) != 0U)
#error "RS485_UART_RX_RING_SIZE must be a power of 2"
#endif

typedef struct
{
  volatile uint16_t head;
  volatile uint16_t tail;
  uint8_t buffer[RS485_UART_RX_RING_SIZE];
} RS485_UART_RingBuffer;

static RS485_UART_RingBuffer rs485_rx_ring;
#if defined(__CC_ARM)
__align(32) static uint8_t rs485_dma_rx_buf[RS485_UART_DMA_RX_BUF_SIZE] __attribute__((at(0x24070000)));
#elif defined(__ARMCC_VERSION)
__attribute__((aligned(32), section(".ARM.__at_0x24070000"))) static uint8_t rs485_dma_rx_buf[RS485_UART_DMA_RX_BUF_SIZE];
#else
__attribute__((aligned(32))) static uint8_t rs485_dma_rx_buf[RS485_UART_DMA_RX_BUF_SIZE];
#endif
static volatile uint16_t rs485_dma_last_pos;
static volatile uint32_t rs485_rx_dropped;
static volatile uint32_t rs485_rx_half_irq_count;
static volatile uint32_t rs485_rx_full_irq_count;
static volatile uint32_t rs485_rx_idle_irq_count;

static void ring_reset(RS485_UART_RingBuffer *ring)
{
  ring->head = 0U;
  ring->tail = 0U;
}

static uint16_t ring_used(const RS485_UART_RingBuffer *ring)
{
  return (uint16_t)((ring->tail - ring->head) & RS485_UART_RX_RING_MASK);
}

static uint16_t ring_free(const RS485_UART_RingBuffer *ring)
{
  return (uint16_t)((RS485_UART_RX_RING_SIZE - 1U) - ring_used(ring));
}

static uint16_t ring_write(RS485_UART_RingBuffer *ring, const uint8_t *src, uint16_t len)
{
  uint16_t free_len;
  uint16_t first;

  if ((ring == 0) || (src == 0) || (len == 0U))
  {
    return 0U;
  }

  free_len = ring_free(ring);
  if (len > free_len)
  {
    len = free_len;
  }

  first = (uint16_t)(RS485_UART_RX_RING_SIZE - ring->tail);
  if (first > len)
  {
    first = len;
  }

  memcpy(&ring->buffer[ring->tail], src, first);
  if (first < len)
  {
    memcpy(&ring->buffer[0], src + first, (uint16_t)(len - first));
  }

  ring->tail = (uint16_t)((ring->tail + len) & RS485_UART_RX_RING_MASK);
  return len;
}

static uint16_t ring_read(RS485_UART_RingBuffer *ring, uint8_t *dst, uint16_t len)
{
  uint16_t used_len;
  uint16_t first;

  if ((ring == 0) || (dst == 0) || (len == 0U))
  {
    return 0U;
  }

  used_len = ring_used(ring);
  if (len > used_len)
  {
    len = used_len;
  }

  first = (uint16_t)(RS485_UART_RX_RING_SIZE - ring->head);
  if (first > len)
  {
    first = len;
  }

  memcpy(dst, &ring->buffer[ring->head], first);
  if (first < len)
  {
    memcpy(dst + first, &ring->buffer[0], (uint16_t)(len - first));
  }

  ring->head = (uint16_t)((ring->head + len) & RS485_UART_RX_RING_MASK);
  return len;
}

static void RS485_UART_PumpRxFromDmaISR(void)
{
  uint16_t pos;
  uint16_t last;

  if (huart3.hdmarx == 0)
  {
    return;
  }

  pos = (uint16_t)(RS485_UART_DMA_RX_BUF_SIZE - __HAL_DMA_GET_COUNTER(huart3.hdmarx));
  last = rs485_dma_last_pos;

  if (pos == last)
  {
    return;
  }

  if (pos > last)
  {
    uint16_t len = (uint16_t)(pos - last);
    uint16_t written = ring_write(&rs485_rx_ring, &rs485_dma_rx_buf[last], len);
    rs485_rx_dropped += (uint32_t)(len - written);
  }
  else
  {
    uint16_t first = (uint16_t)(RS485_UART_DMA_RX_BUF_SIZE - last);
    uint16_t written1 = ring_write(&rs485_rx_ring, &rs485_dma_rx_buf[last], first);
    uint16_t second = pos;
    uint16_t written2 = ring_write(&rs485_rx_ring, &rs485_dma_rx_buf[0], second);

    rs485_rx_dropped += (uint32_t)(first - written1);
    rs485_rx_dropped += (uint32_t)(second - written2);
  }

  rs485_dma_last_pos = pos;
}

void RS485_UART_Init(void)
{
  ring_reset(&rs485_rx_ring);
  rs485_dma_last_pos = 0U;
  rs485_rx_dropped = 0U;
  rs485_rx_half_irq_count = 0U;
  rs485_rx_full_irq_count = 0U;
  rs485_rx_idle_irq_count = 0U;

  if (HAL_UART_Receive_DMA(&huart3, rs485_dma_rx_buf, RS485_UART_DMA_RX_BUF_SIZE) != HAL_OK)
  {
    return;
  }

  __HAL_UART_ENABLE_IT(&huart3, UART_IT_IDLE);
}

void RS485_UART_PumpRxFromDma(void)
{
  if (huart3.hdmarx == 0)
  {
    return;
  }

  HAL_NVIC_DisableIRQ(DMA1_Stream0_IRQn);
  HAL_NVIC_DisableIRQ(USART3_IRQn);
  RS485_UART_PumpRxFromDmaISR();
  HAL_NVIC_EnableIRQ(USART3_IRQn);
  HAL_NVIC_EnableIRQ(DMA1_Stream0_IRQn);
}

void RS485_UART_IdleIRQHandler(void)
{
  rs485_rx_idle_irq_count++;
  RS485_UART_PumpRxFromDmaISR();
}

void RS485_UART_Flush(void)
{
  if (huart3.hdmarx == 0)
  {
    ring_reset(&rs485_rx_ring);
    rs485_dma_last_pos = 0U;
    return;
  }

  HAL_NVIC_DisableIRQ(DMA1_Stream0_IRQn);
  HAL_NVIC_DisableIRQ(USART3_IRQn);
  rs485_dma_last_pos = (uint16_t)(RS485_UART_DMA_RX_BUF_SIZE - __HAL_DMA_GET_COUNTER(huart3.hdmarx));
  ring_reset(&rs485_rx_ring);
  HAL_NVIC_EnableIRQ(USART3_IRQn);
  HAL_NVIC_EnableIRQ(DMA1_Stream0_IRQn);
}

uint16_t RS485_UART_Available(void)
{
  return ring_used(&rs485_rx_ring);
}

uint16_t RS485_UART_Read(uint8_t *dst, uint16_t len)
{
  return ring_read(&rs485_rx_ring, dst, len);
}

uint16_t RS485_UART_ReadClaim(uint8_t **pptr)
{
  uint16_t used;
  uint16_t first;

  if (pptr == 0)
  {
    return 0U;
  }

  used = ring_used(&rs485_rx_ring);
  if (used == 0U)
  {
    *pptr = 0;
    return 0U;
  }

  first = (uint16_t)(RS485_UART_RX_RING_SIZE - rs485_rx_ring.head);
  if (first > used)
  {
    first = used;
  }

  *pptr = &rs485_rx_ring.buffer[rs485_rx_ring.head];
  return first;
}

void RS485_UART_ReadCommit(uint16_t len)
{
  uint16_t used = ring_used(&rs485_rx_ring);

  if (len > used)
  {
    len = used;
  }

  rs485_rx_ring.head = (uint16_t)((rs485_rx_ring.head + len) & RS485_UART_RX_RING_MASK);
}

uint32_t RS485_UART_RxDropped(void)
{
  return rs485_rx_dropped;
}

uint32_t RS485_UART_RxHalfIrqCount(void)
{
  return rs485_rx_half_irq_count;
}

uint32_t RS485_UART_RxFullIrqCount(void)
{
  return rs485_rx_full_irq_count;
}

uint32_t RS485_UART_RxIdleIrqCount(void)
{
  return rs485_rx_idle_irq_count;
}

void HAL_UART_RxHalfCpltCallback(UART_HandleTypeDef *huart)
{
  if (huart->Instance == USART3)
  {
    rs485_rx_half_irq_count++;
    RS485_UART_PumpRxFromDmaISR();
  }
}

void HAL_UART_RxCpltCallback(UART_HandleTypeDef *huart)
{
  if (huart->Instance == USART3)
  {
    rs485_rx_full_irq_count++;
    RS485_UART_PumpRxFromDmaISR();
  }
}
