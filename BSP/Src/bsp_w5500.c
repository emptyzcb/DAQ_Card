/**
 * @file    bsp_w5500.c
 * @brief   W5500 以太网控制器板级驱动（寄存器级 + Socket 级，测试工程）
 *
 * 本模块自管理 SPI4 外设时钟与引脚配置，不修改 CubeMX 生成文件
 * （spi.c / gpio.c），避免重生成工程时丢失配置。
 *
 * SPI4 时钟源选用 D2PCLK2（APB2 派生，不依赖 PLL2 是否使能）；
 * 引脚复用 AF5，CS 与 INT 使用 GPIOE 普通 IO。
 *
 * SPI 帧遵循 W5500 数据手册：3 字节地址（偏移 16 位 + 控制 8 位）
 * + N 字节数据。控制字节：bit7~3=BSB，bit2=RWB，bit1~0=OM。
 */

#include "bsp_w5500.h"

#include "main.h"

/* SPI4 句柄（W5500 专用，不与其他外设共享） */
static SPI_HandleTypeDef bsp_w5500_spi;

/* Socket 0 内部缓冲指针暂存（TX_WR / RX_RD） */
static uint16_t bsp_w5500_txwr[8];
static uint16_t bsp_w5500_rxrd[8];

#define BSP_W5500_COMMAND_TIMEOUT_MS  100U
#define BSP_W5500_SEND_TIMEOUT_MS    3000U

/* ---------------- 私有函数 ---------------- */

/* 配置 SPI4 时钟（SPI45 组）与 PE11~PE15 引脚。 */
static uint8_t bsp_w5500_pin_clock_init(void)
{
  RCC_PeriphCLKInitTypeDef periph_clk = {0};
  GPIO_InitTypeDef gpio = {0};

  /* SPI4/SPI5 共用 SPI45 时钟组；本 HAL 库无 PLL 直选，用 APB2 派生最稳。 */
  periph_clk.PeriphClockSelection = RCC_PERIPHCLK_SPI45;
  periph_clk.Spi45ClockSelection = RCC_SPI45CLKSOURCE_D2PCLK2;
  if (HAL_RCCEx_PeriphCLKConfig(&periph_clk) != HAL_OK)
  {
    return 0U;
  }

  __HAL_RCC_SPI4_CLK_ENABLE();
  __HAL_RCC_GPIOE_CLK_ENABLE();

  /* PE12=SCK / PE13=MISO / PE14=MOSI：复用推挽，高速，AF5(SPI4)。 */
  gpio.Pin = BSP_W5500_SCK_PIN | BSP_W5500_MISO_PIN | BSP_W5500_MOSI_PIN;
  gpio.Mode = GPIO_MODE_AF_PP;
  gpio.Pull = GPIO_NOPULL;
  gpio.Speed = GPIO_SPEED_FREQ_VERY_HIGH;
  gpio.Alternate = GPIO_AF5_SPI4;
  HAL_GPIO_Init(BSP_W5500_PORT, &gpio);

  /* PE11=CS：推挽输出，空闲高电平（低有效片选）。 */
  gpio.Pin = BSP_W5500_CS_PIN;
  gpio.Mode = GPIO_MODE_OUTPUT_PP;
  gpio.Pull = GPIO_NOPULL;
  gpio.Speed = GPIO_SPEED_FREQ_HIGH;
  HAL_GPIO_Init(BSP_W5500_PORT, &gpio);
  HAL_GPIO_WritePin(BSP_W5500_PORT, BSP_W5500_CS_PIN, GPIO_PIN_SET);

  /* PE15=INT：低有效开漏输出，输入端使用上拉，避免未驱动时误触发。 */
  gpio.Pin = BSP_W5500_INT_PIN;
  gpio.Mode = GPIO_MODE_INPUT;
  gpio.Pull = GPIO_PULLUP;
  HAL_GPIO_Init(BSP_W5500_PORT, &gpio);

  return 1U;
}

/* SPI4 外设参数：主机、全双工、8 位、SPI Mode 0、MSB 先行。 */
static uint8_t bsp_w5500_spi_config(void)
{
  bsp_w5500_spi.Instance = SPI4;
  bsp_w5500_spi.Init.Mode = SPI_MODE_MASTER;
  bsp_w5500_spi.Init.Direction = SPI_DIRECTION_2LINES;
  bsp_w5500_spi.Init.DataSize = SPI_DATASIZE_8BIT;
  bsp_w5500_spi.Init.CLKPolarity = SPI_POLARITY_LOW;
  bsp_w5500_spi.Init.CLKPhase = SPI_PHASE_1EDGE;
  bsp_w5500_spi.Init.NSS = SPI_NSS_SOFT;
  bsp_w5500_spi.Init.BaudRatePrescaler = SPI_BAUDRATEPRESCALER_64;
  bsp_w5500_spi.Init.FirstBit = SPI_FIRSTBIT_MSB;
  bsp_w5500_spi.Init.TIMode = SPI_TIMODE_DISABLE;
  bsp_w5500_spi.Init.CRCCalculation = SPI_CRCCALCULATION_DISABLE;
  bsp_w5500_spi.Init.CRCPolynomial = 0x0;
  bsp_w5500_spi.Init.NSSPMode = SPI_NSS_PULSE_ENABLE;
  bsp_w5500_spi.Init.NSSPolarity = SPI_NSS_POLARITY_LOW;
  bsp_w5500_spi.Init.FifoThreshold = SPI_FIFO_THRESHOLD_01DATA;
  bsp_w5500_spi.Init.TxCRCInitializationPattern = SPI_CRC_INITIALIZATION_ALL_ZERO_PATTERN;
  bsp_w5500_spi.Init.RxCRCInitializationPattern = SPI_CRC_INITIALIZATION_ALL_ZERO_PATTERN;
  bsp_w5500_spi.Init.MasterSSIdleness = SPI_MASTER_SS_IDLENESS_00CYCLE;
  bsp_w5500_spi.Init.MasterInterDataIdleness = SPI_MASTER_INTERDATA_IDLENESS_00CYCLE;
  bsp_w5500_spi.Init.MasterReceiverAutoSusp = SPI_MASTER_RX_AUTOSUSP_DISABLE;
  bsp_w5500_spi.Init.MasterKeepIOState = SPI_MASTER_KEEP_IO_STATE_DISABLE;
  bsp_w5500_spi.Init.IOSwap = SPI_IO_SWAP_DISABLE;
  return (HAL_SPI_Init(&bsp_w5500_spi) == HAL_OK) ? 1U : 0U;
}

/*
 * 构造 24 位地址选择字：高 16 位为寄存器偏移（或 TX/RX 缓冲指针），
 * 低 8 位为控制字节（BSB<<3 | RWB<<2 | OM）。
 */
static uint32_t bsp_w5500_addrsel(uint16_t offset, uint8_t bsb, uint8_t rwb)
{
  return ((uint32_t)offset << 8U)
       | ((uint32_t)bsb << 3U)
       | ((uint32_t)rwb << BSP_W5500_RWB_SHIFT)
       | (uint32_t)BSP_W5500_OM_VDM;
}

/* 帧开始：拉低 CS 并发送 3 字节地址（全双工，与读路径同构）。 */
static void bsp_w5500_frame_start(uint32_t addrsel)
{
  uint8_t header[3];
  uint8_t rx[3];

  header[0] = (uint8_t)(addrsel >> 16U);
  header[1] = (uint8_t)(addrsel >> 8U);
  header[2] = (uint8_t)(addrsel & 0xFFU);

  HAL_GPIO_WritePin(BSP_W5500_PORT, BSP_W5500_CS_PIN, GPIO_PIN_RESET);
  (void)HAL_SPI_TransmitReceive(&bsp_w5500_spi, header, rx, 3U, 100U);
}

/* 帧内发送 N 字节数据（全双工，接收忽略）。 */
static uint8_t bsp_w5500_spi_send_data(const uint8_t *data, uint16_t len)
{
  uint8_t rx[16];
  uint16_t i;

  for (i = 0U; i < len; i += 16U)
  {
    uint16_t chunk = (uint16_t)((len - i) > 16U ? 16U : (len - i));

    if (HAL_SPI_TransmitReceive(&bsp_w5500_spi,
                                (uint8_t *)(data + i),
                                rx,
                                chunk,
                                100U) != HAL_OK)
    {
      return 0U;
    }
  }

  return 1U;
}

/* 帧结束：拉高 CS。 */
static void bsp_w5500_frame_end(void)
{
  HAL_GPIO_WritePin(BSP_W5500_PORT, BSP_W5500_CS_PIN, GPIO_PIN_SET);
}

/* ---------------- 对外接口 ---------------- */

int BSP_W5500_SpiTransfer(const uint8_t *tx_data, uint8_t *rx_data, uint16_t length)
{
  static const uint8_t dummy_tx[16] =
  {
    0xFFU, 0xFFU, 0xFFU, 0xFFU, 0xFFU, 0xFFU, 0xFFU, 0xFFU,
    0xFFU, 0xFFU, 0xFFU, 0xFFU, 0xFFU, 0xFFU, 0xFFU, 0xFFU
  };
  uint8_t dummy_rx[16];
  uint16_t offset = 0U;

  if ((length == 0U) || (bsp_w5500_spi.Instance == 0))
  {
    return 0;
  }

  while (offset < length)
  {
    uint16_t chunk = (uint16_t)(((length - offset) > 16U) ? 16U : (length - offset));
    uint8_t *tx = (uint8_t *)((tx_data != 0) ? (tx_data + offset) : dummy_tx);
    uint8_t *rx = (rx_data != 0) ? (rx_data + offset) : dummy_rx;

    if (HAL_SPI_TransmitReceive(&bsp_w5500_spi, tx, rx, chunk, 100U) != HAL_OK)
    {
      return 0;
    }
    offset = (uint16_t)(offset + chunk);
  }

  return 1;
}

uint8_t BSP_W5500_RegRead(uint16_t offset)
{
  const uint8_t dummy = 0xFFU;
  uint8_t rx = 0x00U;

  bsp_w5500_frame_start(bsp_w5500_addrsel(offset, BSP_W5500_BSB_COMMON, BSP_W5500_RWB_READ));
  (void)HAL_SPI_TransmitReceive(&bsp_w5500_spi, (uint8_t *)&dummy, &rx, 1U, 100U);
  bsp_w5500_frame_end();
  return rx;
}

void BSP_W5500_RegWrite(uint16_t offset, uint8_t value)
{
  uint8_t frame[4];
  uint8_t rx[4];

  /* 帧：地址 2 字节 + 控制字节 + 数据 1 字节，一次全双工交换完成写入 */
  frame[0] = (uint8_t)(offset >> 8U);
  frame[1] = (uint8_t)(offset & 0xFFU);
  frame[2] = (uint8_t)(((uint32_t)BSP_W5500_BSB_COMMON << 3U)
                     | ((uint32_t)BSP_W5500_RWB_WRITE << BSP_W5500_RWB_SHIFT)
                     | (uint32_t)BSP_W5500_OM_VDM);
  frame[3] = value;

  HAL_GPIO_WritePin(BSP_W5500_PORT, BSP_W5500_CS_PIN, GPIO_PIN_RESET);
  (void)HAL_SPI_TransmitReceive(&bsp_w5500_spi, frame, rx, 4U, 100U);
  HAL_GPIO_WritePin(BSP_W5500_PORT, BSP_W5500_CS_PIN, GPIO_PIN_SET);
}

uint8_t BSP_W5500_ReadVersion(void)
{
  return BSP_W5500_RegRead(BSP_W5500_VERSIONR_ADDR);
}

void BSP_W5500_SoftReset(void)
{
  uint32_t guard = 0U;

  BSP_W5500_RegWrite(BSP_W5500_MR, 0x80U); /* MR.RST=1 触发软复位 */

  /*
   * 等待复位完成：MR.RST 由硬件自动清零。
   * 复位期间 W5500 不接受寄存器访问，配置若落在窗口内会被丢弃，
   * 必须等 RST 位清零后再继续。
   */
  while ((BSP_W5500_RegRead(BSP_W5500_MR) & 0x80U) != 0U)
  {
    if (++guard > 100000U)
    {
      break; /* 异常保护，避免死循环 */
    }
  }

  /* 复位后 PHY 重新启动自动协商，等待其稳定再配置网络参数。 */
  HAL_Delay(100U);
}

void BSP_W5500_SetNetInfo(const bsp_w5500_netinfo_t *info)
{
  uint8_t i;

  if (info == 0)
  {
    return;
  }

  /* 网关 GAR（4 字节） */
  for (i = 0U; i < 4U; i++)
  {
    BSP_W5500_RegWrite(BSP_W5500_GAR + i, info->gateway[i]);
  }
  /* 子网掩码 SUBNR（4 字节） */
  for (i = 0U; i < 4U; i++)
  {
    BSP_W5500_RegWrite(BSP_W5500_SUBNR + i, info->subnet[i]);
  }
  /* 本机 MAC SHAR（6 字节） */
  for (i = 0U; i < 6U; i++)
  {
    BSP_W5500_RegWrite(BSP_W5500_SHAR + i, info->mac[i]);
  }
  /* 本机 IP SIPR（4 字节） */
  for (i = 0U; i < 4U; i++)
  {
    BSP_W5500_RegWrite(BSP_W5500_SIPR + i, info->ip[i]);
  }
}

uint8_t BSP_W5500_GetLinkState(void)
{
  uint8_t phy = BSP_W5500_RegRead(BSP_W5500_PHYCFGR);

  return ((phy & BSP_W5500_PHYCFGR_LNK) != 0U) ? 1U : 0U;
}

uint8_t BSP_W5500_ReadPhyCfg(void)
{
  return BSP_W5500_RegRead(BSP_W5500_PHYCFGR);
}

/* Socket 寄存器读写：BSB = 0x01 + sn */
static uint8_t bsp_w5500_sn_read(uint8_t sn, uint16_t offset)
{
  const uint8_t dummy = 0xFFU;
  uint8_t rx = 0x00U;

  bsp_w5500_frame_start(bsp_w5500_addrsel(offset, BSP_W5500_BSB_SOCKET(sn), BSP_W5500_RWB_READ));
  (void)HAL_SPI_TransmitReceive(&bsp_w5500_spi, (uint8_t *)&dummy, &rx, 1U, 100U);
  bsp_w5500_frame_end();
  return rx;
}

static void bsp_w5500_sn_write(uint8_t sn, uint16_t offset, uint8_t value)
{
  uint8_t frame[4];
  uint8_t rx[4];

  /* 每个 Socket 占用 4 个 BSB，寄存器块为 0x01 + 4*sn。 */
  frame[0] = (uint8_t)(offset >> 8U);
  frame[1] = (uint8_t)(offset & 0xFFU);
  frame[2] = (uint8_t)(((uint32_t)BSP_W5500_BSB_SOCKET(sn) << 3U)
                     | ((uint32_t)BSP_W5500_RWB_WRITE << BSP_W5500_RWB_SHIFT)
                     | (uint32_t)BSP_W5500_OM_VDM);
  frame[3] = value;

  HAL_GPIO_WritePin(BSP_W5500_PORT, BSP_W5500_CS_PIN, GPIO_PIN_RESET);
  (void)HAL_SPI_TransmitReceive(&bsp_w5500_spi, frame, rx, 4U, 100U);
  HAL_GPIO_WritePin(BSP_W5500_PORT, BSP_W5500_CS_PIN, GPIO_PIN_SET);
}

/* Socket 16 位寄存器读（大端）。 */
static uint16_t bsp_w5500_sn_read16(uint8_t sn, uint16_t offset)
{
  uint16_t val;

  val = (uint16_t)bsp_w5500_sn_read(sn, offset) << 8U;
  val |= (uint16_t)bsp_w5500_sn_read(sn, (uint16_t)(offset + 1U));
  return val;
}

/*
 * RX_RSR/TX_FSR 由芯片异步更新，两个字节分开读取时可能跨越一次更新。
 * 连续两次读取结果一致后才返回，避免把新高字节和旧低字节拼在一起。
 */
static uint16_t bsp_w5500_sn_read16_stable(uint8_t sn, uint16_t offset)
{
  uint16_t previous;
  uint16_t current;
  uint8_t guard = 0U;

  current = bsp_w5500_sn_read16(sn, offset);
  do
  {
    previous = current;
    current = bsp_w5500_sn_read16(sn, offset);
    guard++;
  } while ((current != previous) && (guard < 8U));

  return current;
}

/* Socket 16 位寄存器写（大端）。 */
static void bsp_w5500_sn_write16(uint8_t sn, uint16_t offset, uint16_t value)
{
  bsp_w5500_sn_write(sn, offset, (uint8_t)(value >> 8U));
  bsp_w5500_sn_write(sn, (uint16_t)(offset + 1U), (uint8_t)(value & 0xFFU));
}

/* Socket 命令执行：写 Sn_CR 后轮询至清零。 */
static uint8_t bsp_w5500_cmd(uint8_t sn, uint8_t cmd)
{
  uint32_t start_tick = HAL_GetTick();

  bsp_w5500_sn_write(sn, BSP_W5500_SN_CR, cmd);
  while (bsp_w5500_sn_read(sn, BSP_W5500_SN_CR) != 0x00U)
  {
    if ((uint32_t)(HAL_GetTick() - start_tick) >= BSP_W5500_COMMAND_TIMEOUT_MS)
    {
      return 0U;
    }
    HAL_Delay(1U);
  }
  return 1U;
}

/* Sn_CR 清零只表示命令已被接收，必须继续确认目标状态已经到达。 */
static uint8_t bsp_w5500_wait_status(uint8_t sn,
                                    uint8_t expected_status,
                                    uint32_t timeout_ms)
{
  uint32_t start_tick = HAL_GetTick();

  do
  {
    if (bsp_w5500_sn_read(sn, BSP_W5500_SN_SR) == expected_status)
    {
      return 1U;
    }
    HAL_Delay(1U);
  } while ((uint32_t)(HAL_GetTick() - start_tick) < timeout_ms);

  return 0U;
}

uint8_t BSP_W5500_SocketIrq(uint8_t sn)
{
  uint8_t irq;

  if (sn > 7U)
  {
    return 0U;
  }

  irq = bsp_w5500_sn_read(sn, BSP_W5500_SN_IR);

  if (irq != 0x00U)
  {
    bsp_w5500_sn_write(sn, BSP_W5500_SN_IR, irq); /* 写 1 清零 */
  }
  return irq;
}

uint8_t BSP_W5500_SocketStatus(uint8_t sn)
{
  if (sn > 7U)
  {
    return BSP_W5500_SOCK_CLOSED;
  }
  return bsp_w5500_sn_read(sn, BSP_W5500_SN_SR);
}

void BSP_W5500_SocketGetRemote(uint8_t sn, uint8_t *dip, uint16_t *dport)
{
  uint8_t i;

  if (sn > 7U)
  {
    return;
  }

  if (dip != 0)
  {
    for (i = 0U; i < 4U; i++)
    {
      dip[i] = bsp_w5500_sn_read(sn, (uint16_t)(BSP_W5500_SN_DIPR + i));
    }
  }
  if (dport != 0)
  {
    *dport = bsp_w5500_sn_read16(sn, BSP_W5500_SN_DPORT);
  }
}

uint16_t BSP_W5500_SocketRxSize(uint8_t sn)
{
  if (sn > 7U)
  {
    return 0U;
  }
  return bsp_w5500_sn_read16_stable(sn, BSP_W5500_SN_RX_RSR);
}

uint16_t BSP_W5500_SocketTxFree(uint8_t sn)
{
  if (sn > 7U)
  {
    return 0U;
  }
  return bsp_w5500_sn_read16_stable(sn, BSP_W5500_SN_TX_FSR);
}

uint8_t BSP_W5500_SocketOpen(uint8_t sn, uint8_t mode, uint16_t port)
{
  uint8_t expected_status;

  if (sn > 7U)
  {
    return 0U;
  }

  /* 确保 Socket 处于关闭态 */
  if (BSP_W5500_SocketStatus(sn) != BSP_W5500_SOCK_CLOSED)
  {
    (void)bsp_w5500_cmd(sn, BSP_W5500_CR_CLOSE);
    if (bsp_w5500_wait_status(sn,
                              BSP_W5500_SOCK_CLOSED,
                              BSP_W5500_COMMAND_TIMEOUT_MS) == 0U)
    {
      return 0U;
    }
  }

  /* 清除上一次连接遗留的中断状态，再配置并打开 Socket。 */
  bsp_w5500_sn_write(sn, BSP_W5500_SN_IR, 0xFFU);
  bsp_w5500_sn_write(sn, BSP_W5500_SN_MR, mode);
  bsp_w5500_sn_write16(sn, BSP_W5500_SN_PORT, port);
  if (bsp_w5500_cmd(sn, BSP_W5500_CR_OPEN) == 0U)
  {
    return 0U;
  }

  expected_status = ((mode & 0x0FU) == BSP_W5500_SN_MR_TCP)
                  ? BSP_W5500_SOCK_INIT
                  : BSP_W5500_SOCK_UDP;
  if (bsp_w5500_wait_status(sn,
                            expected_status,
                            BSP_W5500_COMMAND_TIMEOUT_MS) == 0U)
  {
    return 0U;
  }

  bsp_w5500_txwr[sn] = bsp_w5500_sn_read16(sn, BSP_W5500_SN_TX_WR);
  bsp_w5500_rxrd[sn] = bsp_w5500_sn_read16(sn, BSP_W5500_SN_RX_RD);
  return 1U;
}

void BSP_W5500_SocketClose(uint8_t sn)
{
  if (sn <= 7U)
  {
    bsp_w5500_sn_write(sn, BSP_W5500_SN_IR, 0xFFU);
    (void)bsp_w5500_cmd(sn, BSP_W5500_CR_CLOSE);
    (void)bsp_w5500_wait_status(sn,
                                BSP_W5500_SOCK_CLOSED,
                                BSP_W5500_COMMAND_TIMEOUT_MS);
  }
}

uint8_t BSP_W5500_SocketListen(uint8_t sn)
{
  if ((sn > 7U) ||
      (bsp_w5500_cmd(sn, BSP_W5500_CR_LISTEN) == 0U))
  {
    return 0U;
  }

  return bsp_w5500_wait_status(sn,
                               BSP_W5500_SOCK_LISTEN,
                               BSP_W5500_COMMAND_TIMEOUT_MS);
}

uint8_t BSP_W5500_SocketConnect(uint8_t sn, const uint8_t *dip, uint16_t dport)
{
  uint8_t i;

  if ((sn > 7U) || (dip == 0))
  {
    return 0U;
  }
  for (i = 0U; i < 4U; i++)
  {
    bsp_w5500_sn_write(sn, BSP_W5500_SN_DIPR + i, dip[i]);
  }
  bsp_w5500_sn_write16(sn, BSP_W5500_SN_DPORT, dport);
  return bsp_w5500_cmd(sn, BSP_W5500_CR_CONNECT);
}

void BSP_W5500_SocketDisconnect(uint8_t sn)
{
  if (sn <= 7U)
  {
    (void)bsp_w5500_cmd(sn, BSP_W5500_CR_DISCON);
  }
}

uint16_t BSP_W5500_SocketRecv(uint8_t sn, uint8_t *data, uint16_t len)
{
  uint32_t addrsel;

  if ((sn > 7U) || (data == 0) || (len == 0U))
  {
    return 0U;
  }

  /* 从 Socket RX 缓冲块读取，起始指针为 RX_RD。 */
  addrsel = bsp_w5500_addrsel(bsp_w5500_rxrd[sn], BSP_W5500_BSB_RXBUF(sn), BSP_W5500_RWB_READ);
  bsp_w5500_frame_start(addrsel);
  if (BSP_W5500_SpiTransfer(0, data, len) == 0)
  {
    bsp_w5500_frame_end();
    return 0U;
  }
  bsp_w5500_frame_end();

  /* 更新 RX_RD 并提交 RECV 命令释放缓冲 */
  bsp_w5500_rxrd[sn] = (uint16_t)(bsp_w5500_rxrd[sn] + len);
  bsp_w5500_sn_write16(sn, BSP_W5500_SN_RX_RD, bsp_w5500_rxrd[sn]);
  (void)bsp_w5500_cmd(sn, BSP_W5500_CR_RECV);
  return len;
}

uint8_t BSP_W5500_SocketSend(uint8_t sn, const uint8_t *data, uint16_t len)
{
  uint32_t addrsel;
  uint32_t start_tick;
  uint16_t free_size;
  uint8_t irq;

  if ((sn > 7U) || (data == 0) || (len == 0U))
  {
    return 0U;
  }

  free_size = BSP_W5500_SocketTxFree(sn);
  if (free_size < len)
  {
    return 0U; /* TX 缓冲不足 */
  }

  /* 向 Socket TX 缓冲块写入，起始指针为 TX_WR。 */
  addrsel = bsp_w5500_addrsel(bsp_w5500_txwr[sn], BSP_W5500_BSB_TXBUF(sn), BSP_W5500_RWB_WRITE);
  bsp_w5500_frame_start(addrsel);
  if (bsp_w5500_spi_send_data(data, len) == 0U)
  {
    bsp_w5500_frame_end();
    return 0U;
  }
  bsp_w5500_frame_end();

  /* 清除旧结果，更新 TX_WR，提交 SEND，并等待 SEND_OK/TIMEOUT。 */
  bsp_w5500_sn_write(sn,
                     BSP_W5500_SN_IR,
                     (uint8_t)(BSP_W5500_IR_SEND_OK | BSP_W5500_IR_TIMEOUT));
  bsp_w5500_txwr[sn] = (uint16_t)(bsp_w5500_txwr[sn] + len);
  bsp_w5500_sn_write16(sn, BSP_W5500_SN_TX_WR, bsp_w5500_txwr[sn]);
  if (bsp_w5500_cmd(sn, BSP_W5500_CR_SEND) == 0U)
  {
    return 0U;
  }

  start_tick = HAL_GetTick();
  do
  {
    irq = bsp_w5500_sn_read(sn, BSP_W5500_SN_IR);
    if ((irq & BSP_W5500_IR_SEND_OK) != 0U)
    {
      bsp_w5500_sn_write(sn, BSP_W5500_SN_IR, BSP_W5500_IR_SEND_OK);
      return 1U;
    }
    if ((irq & BSP_W5500_IR_TIMEOUT) != 0U)
    {
      bsp_w5500_sn_write(sn, BSP_W5500_SN_IR, BSP_W5500_IR_TIMEOUT);
      return 0U;
    }
    HAL_Delay(1U);
  } while ((uint32_t)(HAL_GetTick() - start_tick) < BSP_W5500_SEND_TIMEOUT_MS);

  return 0U;
}

uint8_t BSP_W5500_Init(void)
{
  uint8_t i;
  uint8_t version;

  if ((bsp_w5500_pin_clock_init() == 0U) || (bsp_w5500_spi_config() == 0U))
  {
    return 0U;
  }

  for (i = 0U; i < 8U; i++)
  {
    bsp_w5500_txwr[i] = 0U;
    bsp_w5500_rxrd[i] = 0U;
  }

  version = BSP_W5500_ReadVersion();
  if (version != BSP_W5500_EXPECT_VERSION)
  {
    return version;
  }

  BSP_W5500_SoftReset();
  return BSP_W5500_ReadVersion();
}
