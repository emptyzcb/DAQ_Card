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

static bsp_w5500_netinfo_t g_eth_netinfo =
{
  { 192U, 168U, 1U, 1U },
  { 255U, 255U, 255U, 0U },
  { 0x02U, 0x00U, 0x00U, 0x00U, 0x00U, 0x10U },
  { 192U, 168U, 1U, 10U }
};
static uint16_t g_eth_port = APP_ETH_TCP_PORT;

/* Bank 2 sectors 4/5 are reserved for network settings; sectors 6/7 hold IO rules. */
#define NET_SLOT_A 0x08180000UL
#define NET_SLOT_B 0x081A0000UL
#define NET_MAGIC  0x4E455431UL
typedef struct {
  uint32_t magic, generation;
  uint8_t ip[4], mask[4], gateway[4];
  uint16_t port, reserved;
  uint32_t checksum, marker;
} NetRecord;
typedef char net_record_size_must_be_32[(sizeof(NetRecord) == 32U) ? 1 : -1];
static uint8_t net_reset_pending;

static uint32_t net_checksum(const NetRecord *r)
{
  const uint8_t *p = (const uint8_t *)r;
  uint32_t c = 2166136261UL;
  unsigned i;
  for (i = 0; i < 24U; i++) c = (c ^ p[i]) * 16777619UL;
  return c;
}

static uint32_t net_u32(const uint8_t *p)
{ return ((uint32_t)p[0]<<24) | ((uint32_t)p[1]<<16) | ((uint32_t)p[2]<<8) | p[3]; }

static uint8_t net_valid(const NetRecord *r)
{
  uint32_t ip = net_u32(r->ip), mask = net_u32(r->mask), gw = net_u32(r->gateway);
  uint32_t host = ip & ~mask;
  if (r->magic != NET_MAGIC || r->marker != ~NET_MAGIC || r->checksum != net_checksum(r) ||
      r->port == 0U || r->ip[0] == 0U || r->ip[0] == 127U || r->ip[0] >= 224U ||
      mask == 0U || mask == 0xFFFFFFFFUL || ((~mask & (~mask + 1U)) != 0U) ||
      host == 0U || host == ~mask) return 0U;
  if (gw != 0U && ((gw & mask) != (ip & mask) || (gw & ~mask) == 0U ||
                    (gw & ~mask) == ~mask || gw == ip)) return 0U;
  return 1U;
}

static void net_load(void)
{
  const NetRecord *a = (const NetRecord *)NET_SLOT_A, *b = (const NetRecord *)NET_SLOT_B, *r = 0;
  uint8_t av = net_valid(a), bv = net_valid(b);
  if (av && bv) r = ((int32_t)(b->generation - a->generation) > 0) ? b : a;
  else if (av) r = a; else if (bv) r = b;
  if (r) { memcpy(g_eth_netinfo.ip,r->ip,4); memcpy(g_eth_netinfo.subnet,r->mask,4);
           memcpy(g_eth_netinfo.gateway,r->gateway,4); g_eth_port = r->port; }
}

static uint8_t net_save(NetRecord *r)
{
  const NetRecord *a = (const NetRecord *)NET_SLOT_A, *b = (const NetRecord *)NET_SLOT_B;
  uint8_t av = net_valid(a), bv = net_valid(b), write_b;
  uint32_t addr, error = 0U;
  FLASH_EraseInitTypeDef erase = {0};
  static NetRecord word __ALIGNED(32);
  write_b = av && (!bv || (int32_t)(a->generation - b->generation) >= 0);
  r->generation = (av || bv) ? ((write_b ? a->generation : b->generation) + 1U) : 1U;
  r->magic = NET_MAGIC; r->marker = ~NET_MAGIC; r->reserved = 0U;
  r->checksum = net_checksum(r);
  if (!net_valid(r)) return 0U;
  addr = write_b ? NET_SLOT_B : NET_SLOT_A;
  erase.TypeErase = FLASH_TYPEERASE_SECTORS; erase.Banks = FLASH_BANK_2;
  erase.Sector = write_b ? FLASH_SECTOR_5 : FLASH_SECTOR_4;
  erase.NbSectors = 1U; erase.VoltageRange = FLASH_VOLTAGE_RANGE_3;
  if (HAL_FLASH_Unlock() != HAL_OK) return 0U;
  word = *r;
  if (HAL_FLASHEx_Erase(&erase, &error) != HAL_OK ||
      HAL_FLASH_Program(FLASH_TYPEPROGRAM_FLASHWORD, addr, (uint32_t)&word) != HAL_OK)
  { HAL_FLASH_Lock(); return 0U; }
  HAL_FLASH_Lock();
  SCB_InvalidateDCache_by_Addr((uint32_t *)addr, 32);
  return net_valid((const NetRecord *)addr);
}

/* Private Modbus TCP function 0x42: read or atomically save network settings. */
static uint16_t net_handle(const uint8_t *req, uint16_t len, uint8_t *out)
{
  uint8_t cmd, status = 0U;
  uint16_t n;
  NetRecord r;
  if (len < 9U || req[6] != 1U || req[7] != 0x42U) return 0U;
  cmd = req[8];
  if (cmd == 1U && len == 9U) {
    memcpy(out+10,g_eth_netinfo.ip,4); memcpy(out+14,g_eth_netinfo.subnet,4);
    memcpy(out+18,g_eth_netinfo.gateway,4); out[22] = (uint8_t)(g_eth_port>>8); out[23] = (uint8_t)g_eth_port;
    n = 24U;
  } else if (cmd == 2U && len == 23U) {
    memset(&r,0,sizeof(r)); memcpy(r.ip,req+9,4); memcpy(r.mask,req+13,4); memcpy(r.gateway,req+17,4);
    r.port = (uint16_t)(((uint16_t)req[21]<<8)|req[22]);
    if (!net_save(&r)) status = 1U; else net_reset_pending = 1U;
    n = 10U;
  } else { status = 2U; n = 10U; }
  memcpy(out,req,4); out[4]=0U; out[5]=(uint8_t)(n-6U); out[6]=req[6]; out[7]=0x42U; out[8]=cmd; out[9]=status;
  return n;
}

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

  net_load();
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
         (unsigned int)g_eth_port);

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

    response_length = (g_eth_rx_buf[7] == 0x42U) ? net_handle(g_eth_rx_buf, adu_length, g_eth_tx_buf) : MODBUS_TCP_HandleAdu(g_eth_rx_buf,
                                            adu_length,
                                            g_eth_tx_buf,
                                            (uint16_t)sizeof(g_eth_tx_buf));
    if ((response_length > 0U) &&
        (BSP_W5500_SocketSend(0U, g_eth_tx_buf, response_length) == 0U))
    {
      printf("[ETH] TX %u bytes failed\r\n", (unsigned int)response_length);
      if (net_reset_pending) { vTaskDelay(pdMS_TO_TICKS(100U)); NVIC_SystemReset(); }
      return 0U;
    }
    if (net_reset_pending) { vTaskDelay(pdMS_TO_TICKS(100U)); NVIC_SystemReset(); }

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
                               g_eth_port) == 0U)
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
             (unsigned int)g_eth_port);
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
