#include "sys.h"
#include <stdarg.h>

/*
 * 以太网服务任务（承载原 RS485 业务）
 * ----------------------------------
 * W5500 TCP Server：监听 5000 端口，按标准 Modbus TCP 的 MBAP 长度字段
 * 从 TCP 字节流中组帧，再复用现有 Modbus RTU/IOCF 业务处理器。
 *
 * 这样原 RS485 从机上跑的全部业务——0x03/0x06/0x10 寄存器读写、
 * 以及 IOCF 规则下载（自定义功能码）——零改动迁移到以太网。
 *
 * 网络参数（产品级固定值）：
 *   IP  = 192.168.1.10，掩码 255.255.255.0，网关 192.168.1.1，
 *   MAC = 02:00:00:00:00:10。
 *
 * 任务周期：10 ms。所有 SPI 操作阻塞且只在本任务上下文执行。
 */

#define APP_ETH_TCP_PORT        5000U
#define APP_ETH_RX_BUF_SIZE     (MODBUS_TCP_MAX_ADU_SIZE * 2U)
#define APP_ETH_LINK_GUARD      500U   /* PHY 链接等待轮询上限（x10 ms） */

static const bsp_w5500_netinfo_t g_eth_netinfo =
{
  { 192U, 168U, 1U, 1U },
  { 255U, 255U, 255U, 0U },
  { 0x02U, 0x00U, 0x00U, 0x00U, 0x00U, 0x10U },
  { 192U, 168U, 1U, 10U }
};

static uint8_t  g_eth_rx_buf[APP_ETH_RX_BUF_SIZE];
static uint8_t  g_eth_tx_buf[MODBUS_TCP_MAX_ADU_SIZE];
static uint16_t g_eth_rx_length = 0U;
static uint8_t  g_eth_initialized = 0U;
static uint8_t  g_eth_link_state  = 0U;
static uint8_t  g_eth_client_connected = 0U;
static uint8_t  g_eth_spi_read_failed = 0U;

static int eth_printf(const char *format, ...)
{
  char line[160];
  va_list args;
  int length;

  va_start(args, format);
  length = vsnprintf(line, sizeof(line), format, args);
  va_end(args);
  if (length > 0)
  {
    uint16_t send_length = (length >= (int)sizeof(line))
                         ? (uint16_t)(sizeof(line) - 1U) : (uint16_t)length;
    (void)BSP_CONSOLE_Write((const uint8_t *)line, send_length);
  }
  return length;
}

#define printf eth_printf

static void eth_log_link_down(uint8_t phy)
{
  uint8_t version = 0U;

  if (BSP_W5500_RegReadChecked(BSP_W5500_VERSIONR_ADDR, &version) == 0U)
  {
    printf("[ETH] PHY=0x%02X; VERSIONR SPI read failed\r\n", (unsigned int)phy);
  }
  else
  {
    printf("[ETH] PHY link down: PHYCFGR=0x%02X VERSIONR=0x%02X\r\n",
           (unsigned int)phy, (unsigned int)version);
  }
}

static uint8_t eth_init(void)
{
  uint32_t guard = 0U;
  uint8_t version;
  uint8_t phy = 0U;
  uint8_t ip_back[4];

  version = BSP_W5500_Init();
  if (version != BSP_W5500_EXPECT_VERSION)
  {
    printf("[ETH] W5500 init failed: VERSIONR=0x%02X\r\n",
           (unsigned int)version);
    return 0U;
  }
  printf("[ETH] W5500 init OK: VERSIONR=0x%02X\r\n", (unsigned int)version);
  printf("[ETH] diag=checked-PHY-read FW=%s\r\n", APP_FIRMWARE_VERSION);

  BSP_W5500_SetNetInfo(&g_eth_netinfo);

  for (uint8_t i = 0U; i < 4U; i++)
  {
    ip_back[i] = BSP_W5500_RegRead((uint16_t)(BSP_W5500_SIPR + i));
  }
  if (memcmp(ip_back, g_eth_netinfo.ip, sizeof(ip_back)) != 0)
  {
    printf("[ETH] network register readback failed\r\n");
    return 0U;
  }

  printf("[ETH] IP=%u.%u.%u.%u:%u (Modbus TCP)\r\n",
         (unsigned int)g_eth_netinfo.ip[0], (unsigned int)g_eth_netinfo.ip[1],
         (unsigned int)g_eth_netinfo.ip[2], (unsigned int)g_eth_netinfo.ip[3],
         (unsigned int)APP_ETH_TCP_PORT);

  for (;;)
  {
    if ((BSP_W5500_RegReadChecked(BSP_W5500_PHYCFGR, &phy) != 0U) &&
        ((phy & BSP_W5500_PHYCFGR_LNK) != 0U))
    {
      break;
    }
    if (++guard >= APP_ETH_LINK_GUARD)
    {
      if (BSP_W5500_RegReadChecked(BSP_W5500_PHYCFGR, &phy) == 0U)
      {
        printf("[ETH] PHY SPI read failed; check SPI4 and W5500 power\r\n");
        g_eth_spi_read_failed = 1U;
      }
      else
      {
        eth_log_link_down(phy);
      }
      g_eth_initialized = 1U;
      return 1U;
    }
    vTaskDelay(pdMS_TO_TICKS(10U));
  }
  printf("[ETH] PHY link up: PHYCFGR=0x%02X\r\n", (unsigned int)phy);
  g_eth_link_state = 1U;
  g_eth_initialized = 1U;
  return 1U;
}

static void eth_reset_stream(void)
{
  g_eth_rx_length = 0U;
}

/* 处理缓存中所有完整 ADU；返回 0 表示 MBAP 非法，应关闭本连接。 */
static uint8_t eth_process_stream(void)
{
  for (;;)
  {
    ModbusTcpParseResult parse_result;
    uint16_t adu_length;
    uint16_t response_length;

    parse_result = MODBUS_TCP_PeekAduLength(g_eth_rx_buf,
                                             g_eth_rx_length,
                                             &adu_length);
    if (parse_result == MODBUS_TCP_NEED_MORE)
    {
      return 1U;
    }
    if (parse_result == MODBUS_TCP_INVALID_ADU)
    {
      printf("[ETH] invalid Modbus TCP MBAP, closing client\r\n");
      return 0U;
    }

    response_length = MODBUS_TCP_HandleAdu(g_eth_rx_buf,
                                            adu_length,
                                            g_eth_tx_buf,
                                            (uint16_t)sizeof(g_eth_tx_buf));
    if ((response_length > 0U) &&
        (BSP_W5500_SocketSend(0U, g_eth_tx_buf, response_length) == 0U))
    {
      printf("[ETH] TX %u bytes failed\r\n", (unsigned int)response_length);
      return 0U;
    }

    g_eth_rx_length = (uint16_t)(g_eth_rx_length - adu_length);
    if (g_eth_rx_length > 0U)
    {
      memmove(g_eth_rx_buf, &g_eth_rx_buf[adu_length], g_eth_rx_length);
    }
  }
}

static void eth_process(void)
{
  uint8_t sock_state;
  uint8_t irq;
  uint8_t link_state;
  uint8_t phy;
  uint16_t rx_size;

  if (g_eth_initialized == 0U)
  {
    return;
  }

  if (BSP_W5500_RegReadChecked(BSP_W5500_PHYCFGR, &phy) == 0U)
  {
    if (g_eth_spi_read_failed == 0U)
    {
      printf("[ETH] PHY SPI read failed; check SPI4 and W5500 power\r\n");
      g_eth_spi_read_failed = 1U;
    }
    return;
  }
  g_eth_spi_read_failed = 0U;
  link_state = ((phy & BSP_W5500_PHYCFGR_LNK) != 0U) ? 1U : 0U;
  if (link_state != g_eth_link_state)
  {
    g_eth_link_state = link_state;
    if (link_state != 0U)
    {
      printf("[ETH] PHY link up: PHYCFGR=0x%02X\r\n", (unsigned int)phy);
    }
    else
    {
      eth_log_link_down(phy);
    }
  }
  if (link_state == 0U)
  {
    return;
  }

  sock_state = BSP_W5500_SocketStatus(0U);

  switch (sock_state)
  {
    case BSP_W5500_SOCK_CLOSED:
      g_eth_client_connected = 0U;
      eth_reset_stream();
      if (BSP_W5500_SocketOpen(0U, BSP_W5500_SN_MR_TCP,
                               APP_ETH_TCP_PORT) == 0U)
      {
        return;
      }
      if (BSP_W5500_SocketListen(0U) == 0U)
      {
        BSP_W5500_SocketClose(0U);
        return;
      }
      printf("[ETH] TCP server listening: %u.%u.%u.%u:%u\r\n",
             (unsigned int)g_eth_netinfo.ip[0], (unsigned int)g_eth_netinfo.ip[1],
             (unsigned int)g_eth_netinfo.ip[2], (unsigned int)g_eth_netinfo.ip[3],
             (unsigned int)APP_ETH_TCP_PORT);
      break;

    case BSP_W5500_SOCK_LISTEN:
      g_eth_client_connected = 0U;
      eth_reset_stream();
      break;

    case BSP_W5500_SOCK_ESTABLISHED:
      irq = BSP_W5500_SocketIrq(0U);
      if ((g_eth_client_connected == 0U) || ((irq & BSP_W5500_IR_CON) != 0U))
      {
        g_eth_client_connected = 1U;
        eth_reset_stream();
        printf("[ETH] Modbus TCP client connected\r\n");
      }
      if ((irq & (BSP_W5500_IR_DISCON | BSP_W5500_IR_TIMEOUT)) != 0U)
      {
        g_eth_client_connected = 0U;
        eth_reset_stream();
        BSP_W5500_SocketClose(0U);
        break;
      }

      rx_size = BSP_W5500_SocketRxSize(0U);
      if (rx_size > 0U)
      {
        uint16_t free_length = (uint16_t)(APP_ETH_RX_BUF_SIZE - g_eth_rx_length);
        uint16_t requested_len;
        uint16_t recv_len;

        if (free_length == 0U)
        {
          printf("[ETH] RX stream buffer overflow\r\n");
          g_eth_client_connected = 0U;
          eth_reset_stream();
          BSP_W5500_SocketClose(0U);
          break;
        }

        requested_len = (rx_size > free_length) ? free_length : rx_size;
        recv_len = BSP_W5500_SocketRecv(0U,
                                        &g_eth_rx_buf[g_eth_rx_length],
                                        requested_len);
        if (recv_len != requested_len)
        {
          g_eth_client_connected = 0U;
          eth_reset_stream();
          BSP_W5500_SocketClose(0U);
          break;
        }
        g_eth_rx_length = (uint16_t)(g_eth_rx_length + recv_len);
        if (eth_process_stream() == 0U)
        {
          g_eth_client_connected = 0U;
          eth_reset_stream();
          BSP_W5500_SocketClose(0U);
        }
      }
      break;

    case BSP_W5500_SOCK_CLOSE_WAIT:
      g_eth_client_connected = 0U;
      eth_reset_stream();
      BSP_W5500_SocketClose(0U);
      break;

    default:
      g_eth_client_connected = 0U;
      eth_reset_stream();
      BSP_W5500_SocketClose(0U);
      break;
  }
}

void AppTask_Eth(void *argument)
{
  (void)argument;

  if (eth_init() == 0U)
  {
    printf("[ETH] fatal: verify power, SPI wiring and W5500 soldering\r\n");
  }

  for (;;)
  {
    eth_process();
    vTaskDelay(pdMS_TO_TICKS(10U));
  }
}
