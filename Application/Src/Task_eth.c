#include "sys.h"

/*
 * 以太网服务任务
 * ---------------
 * 职责：初始化 W5500 以太网控制器，维护 TCP Server 监听状态，
 *       处理客户端接入、数据收发与断开事件，并在串口输出
 *       链接状态与数据交互日志（供联调追溯）。
 *
 * 网络参数（产品级固定值，如有需要后续接入配置存储）：
 *   IP  = 192.168.1.10，掩码 255.255.255.0，网关 192.168.1.1，
 *   MAC = 02:00:00:00:00:10（本地管理地址，避免与真实网卡冲突）。
 *
 * 任务周期：10 ms。所有 SPI 操作均为阻塞式，且只在本任务
 * 上下文中执行，避免与外设访问产生并发竞争。
 */

/* 端口与缓冲配置 */
#define APP_ETH_TCP_PORT        5000U   /* TCP 监听端口 */
#define APP_ETH_RX_BUF_SIZE     512U    /* 单帧最大接收长度 */
#define APP_ETH_LINK_GUARD      500U    /* PHY 链接等待轮询上限（x10 ms） */

/* 网络参数：网关 / 子网 / MAC / 本机 IP */
static const bsp_w5500_netinfo_t g_eth_netinfo =
{
  { 192U, 168U, 1U, 1U },            /* 网关 */
  { 255U, 255U, 255U, 0U },          /* 子网掩码 */
  { 0x02U, 0x00U, 0x00U, 0x00U, 0x00U, 0x10U }, /* MAC */
  { 192U, 168U, 1U, 10U }            /* 本机 IP */
};

static uint8_t  g_eth_rx_buf[APP_ETH_RX_BUF_SIZE];
static uint8_t  g_eth_initialized = 0U;
static uint8_t  g_eth_link_state  = 0U;

/* 打印 IPv4 地址 */
static void eth_print_ip(const uint8_t *ip)
{
  if (ip != NULL)
  {
    printf("%u.%u.%u.%u",
           (unsigned int)ip[0], (unsigned int)ip[1],
           (unsigned int)ip[2], (unsigned int)ip[3]);
  }
}

/* 打印一帧数据 HEX（每行最多 64 字节，超长截断并提示） */
static void eth_print_hex(const uint8_t *data, uint16_t len)
{
  uint16_t i;
  uint16_t show = (len > 64U) ? 64U : len;

  for (i = 0U; i < show; i++)
  {
    printf("%02X ", (unsigned int)data[i]);
  }
  if (show < len)
  {
    printf("... (+%u bytes) ", (unsigned int)(len - show));
  }
}

/* 初始化 W5500：版本校验、网络参数写入、写后读回验证、PHY 链接等待 */
static uint8_t eth_init(void)
{
  uint32_t guard = 0U;
  uint8_t version;
  uint8_t ip_back[4];
  uint8_t i;

  /* 芯片初始化并校验版本寄存器 */
  version = BSP_W5500_Init();
  if (version != BSP_W5500_EXPECT_VERSION)
  {
    printf("[ETH] W5500 init failed: VERSIONR=0x%02X\r\n",
           (unsigned int)version);
    return 0U;
  }
  printf("[ETH] W5500 init OK: VERSIONR=0x%02X\r\n",
         (unsigned int)version);

  /* 写入网络参数（BSP 已完成软复位） */
  BSP_W5500_SetNetInfo(&g_eth_netinfo);

  /* 写后读回验证：确认网络参数真正写入 W5500 寄存器 */
  for (i = 0U; i < 4U; i++)
  {
    ip_back[i] = BSP_W5500_RegRead((uint16_t)(BSP_W5500_SIPR + i));
  }
  printf("[ETH] network: IP=");
  eth_print_ip(g_eth_netinfo.ip);
  printf(" readback=");
  eth_print_ip(ip_back);
  printf("\r\n");
  if (memcmp(ip_back, g_eth_netinfo.ip, sizeof(ip_back)) != 0)
  {
    printf("[ETH] network register readback failed\r\n");
    return 0U;
  }

  printf("[ETH] MAC=%02X:%02X:%02X:%02X:%02X:%02X GW=",
         (unsigned int)g_eth_netinfo.mac[0],
         (unsigned int)g_eth_netinfo.mac[1],
         (unsigned int)g_eth_netinfo.mac[2],
         (unsigned int)g_eth_netinfo.mac[3],
         (unsigned int)g_eth_netinfo.mac[4],
         (unsigned int)g_eth_netinfo.mac[5]);
  eth_print_ip(g_eth_netinfo.gateway);
  printf("\r\n");

  /* 等待 PHY 链接（网线接入）。未插网线也允许继续，链接后自动恢复。 */
  while (BSP_W5500_GetLinkState() == 0U)
  {
    if (++guard >= APP_ETH_LINK_GUARD)
    {
      printf("[ETH] PHY link down: PHYCFGR=0x%02X\r\n",
             (unsigned int)BSP_W5500_ReadPhyCfg());
      g_eth_initialized = 1U;
      return 1U;
    }
    HAL_Delay(10U);
  }
  printf("[ETH] PHY link up: PHYCFGR=0x%02X\r\n",
         (unsigned int)BSP_W5500_ReadPhyCfg());
  g_eth_link_state = 1U;
  g_eth_initialized = 1U;
  return 1U;
}

/* TCP Server 状态机：监听 / 收发 / 断开重建 */
static void eth_process(void)
{
  uint8_t sock_state;
  uint8_t irq;
  uint8_t link_state;
  uint16_t rx_size;

  if (g_eth_initialized == 0U)
  {
    return;
  }

  /* 链接状态变化仅在跳变时打印一次 */
  link_state = BSP_W5500_GetLinkState();
  if (link_state != g_eth_link_state)
  {
    g_eth_link_state = link_state;
    printf("[ETH] PHY link %s: PHYCFGR=0x%02X\r\n",
           (link_state != 0U) ? "up" : "down",
           (unsigned int)BSP_W5500_ReadPhyCfg());
  }
  if (link_state == 0U)
  {
    return;
  }

  sock_state = BSP_W5500_SocketStatus(0U);

  switch (sock_state)
  {
    case BSP_W5500_SOCK_CLOSED:
      /* 关闭态：打开 Socket 并进入监听 */
      if (BSP_W5500_SocketOpen(0U, BSP_W5500_SN_MR_TCP,
                               APP_ETH_TCP_PORT) == 0U)
      {
        printf("[ETH] socket open failed\r\n");
        return;
      }
      if (BSP_W5500_SocketListen(0U) == 0U)
      {
        printf("[ETH] listen failed; rebuilding socket\r\n");
        BSP_W5500_SocketClose(0U);
        return;
      }
      printf("[ETH] TCP server listening: ");
      eth_print_ip(g_eth_netinfo.ip);
      printf(":%u\r\n", (unsigned int)APP_ETH_TCP_PORT);
      break;

    case BSP_W5500_SOCK_LISTEN:
      /* 监听态：等待客户端接入，状态自动迁移至 ESTABLISHED */
      break;

    case BSP_W5500_SOCK_ESTABLISHED:
      /* 建立态：处理断开事件与数据收发 */
      irq = BSP_W5500_SocketIrq(0U);
      if ((irq & (BSP_W5500_IR_DISCON | BSP_W5500_IR_TIMEOUT)) != 0U)
      {
        printf("[ETH] client disconnected\r\n");
        BSP_W5500_SocketClose(0U);
        break;
      }

      rx_size = BSP_W5500_SocketRxSize(0U);
      if (rx_size > 0U)
      {
        uint16_t requested_len;
        uint16_t recv_len;

        requested_len = (rx_size > APP_ETH_RX_BUF_SIZE)
                      ? APP_ETH_RX_BUF_SIZE : rx_size;
        recv_len = BSP_W5500_SocketRecv(0U, g_eth_rx_buf, requested_len);
        if (recv_len != requested_len)
        {
          printf("[ETH] RX SPI transfer failed\r\n");
          BSP_W5500_SocketClose(0U);
          break;
        }
        printf("[ETH] RX %u bytes: ", (unsigned int)recv_len);
        eth_print_hex(g_eth_rx_buf, recv_len);
        printf("\r\n");

        /* 原样回显接收到的载荷（联调期行为，后续替换为正式协议处理） */
        if (BSP_W5500_SocketSend(0U, g_eth_rx_buf, recv_len) != 0U)
        {
          printf("[ETH] TX echo %u bytes OK\r\n", (unsigned int)recv_len);
        }
        else
        {
          printf("[ETH] TX echo failed\r\n");
        }
      }
      break;

    case BSP_W5500_SOCK_CLOSE_WAIT:
      /* 对端发起关闭：本地关闭，回到监听态 */
      printf("[ETH] client closed connection\r\n");
      BSP_W5500_SocketClose(0U);
      break;

    default:
      /* 其他异常状态：关闭重建 */
      BSP_W5500_SocketClose(0U);
      break;
  }
}

/*
 * 以太网任务入口。
 *
 * 初始化失败时（芯片版本异常 / 网络参数写回失败）任务保持存活，
 * 周期重试监听，不阻塞其他业务任务。
 */
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
    HAL_Delay(10U);
  }
}
