#ifndef BSP_W5500_H
#define BSP_W5500_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/*
 * W5500 以太网控制器板级驱动（SPI 接口）。
 *
 * 硬件连接（固定分配）：
 *   PE11 -> W5500_SPI4_CS   （片选，GPIO 软件控制，低有效）
 *   PE12 -> W5500_SPI4_SCLK （SPI4 时钟）
 *   PE13 -> W5500_SPI4_MISO （SPI4 主机输入/从机输出）
 *   PE14 -> W5500_SPI4_MOSI （SPI4 主机输出/从机输入）
 *   PE15 -> W5500_INT       （中断请求，低有效，当前轮询使用）
 *
 * W5500 SPI 帧格式（3 字节地址 + N 字节数据）：
 *   [偏移地址高 8 位][偏移地址低 8 位][控制字节][数据...]
 *   控制字节：bit7~3=BSB(块选择)，bit2=RWB(0=读、1=写)，bit1~0=OM(00=可变长)
 *   块选择：0x00=通用寄存器；每个 Socket 占用连续 4 个块，
 *           分别为寄存器块、TX 缓冲块、RX 缓冲块和保留块。
 *
 * 本驱动提供寄存器级与 Socket 级 API，供上层应用（TCP/UDP）调用。
 * 所有操作均为阻塞式 SPI，调用方需避免在中断上下文执行。
 */

/* ---- 引脚定义（对应原理图分配） ---- */
#define BSP_W5500_CS_PIN        GPIO_PIN_11   /* PE11 */
#define BSP_W5500_SCK_PIN       GPIO_PIN_12   /* PE12 */
#define BSP_W5500_MISO_PIN      GPIO_PIN_13   /* PE13 */
#define BSP_W5500_MOSI_PIN      GPIO_PIN_14   /* PE14 */
#define BSP_W5500_INT_PIN       GPIO_PIN_15   /* PE15 */
#define BSP_W5500_PORT          GPIOE

/* ---- W5500 版本寄存器 ---- */
#define BSP_W5500_VERSIONR_ADDR   0x0039U
#define BSP_W5500_EXPECT_VERSION  0x04U

/* ---- 控制字节字段（bit 定义） ---- */
#define BSP_W5500_RWB_READ         0x00U
#define BSP_W5500_RWB_WRITE        0x01U
#define BSP_W5500_RWB_SHIFT       2U
#define BSP_W5500_OM_VDM          0x00U   /* 可变数据长度模式 */

/* ---- 块选择（BSB，5 位） ---- */
#define BSP_W5500_BSB_COMMON      0x00U
#define BSP_W5500_BSB_SOCKET(n)   ((uint8_t)(0x01U + ((uint8_t)(n) << 2U))) /* Socket n 寄存器 */
#define BSP_W5500_BSB_TXBUF(n)    ((uint8_t)(0x02U + ((uint8_t)(n) << 2U))) /* Socket n TX 缓冲 */
#define BSP_W5500_BSB_RXBUF(n)    ((uint8_t)(0x03U + ((uint8_t)(n) << 2U))) /* Socket n RX 缓冲 */

/* ---- 通用寄存器偏移地址 ---- */
#define BSP_W5500_MR              0x0000U
#define BSP_W5500_GAR             0x0001U   /* 网关 4 字节 */
#define BSP_W5500_SUBNR           0x0005U   /* 子网 4 字节 */
#define BSP_W5500_SHAR            0x0009U   /* MAC 6 字节 */
#define BSP_W5500_SIPR            0x000FU   /* 本机 IP 4 字节 */
#define BSP_W5500_PHYCFGR         0x002EU
#define BSP_W5500_PHYCFGR_LNK 0x01U

/* ---- Socket n 寄存器偏移地址（相对 Socket 块） ---- */
#define BSP_W5500_SN_MR           0x0000U
#define BSP_W5500_SN_CR           0x0001U
#define BSP_W5500_SN_IR           0x0002U
#define BSP_W5500_SN_SR           0x0003U
#define BSP_W5500_SN_PORT         0x0004U   /* 源端口 2 字节 */
#define BSP_W5500_SN_DHAR          0x0006U   /* 目的 MAC 6 字节 */
#define BSP_W5500_SN_DIPR         0x000CU   /* 目的 IP 4 字节 */
#define BSP_W5500_SN_DPORT        0x0010U   /* 目的端口 2 字节 */
#define BSP_W5500_SN_IMR          0x002CU
#define BSP_W5500_SN_TX_FSR       0x0020U   /* TX 剩余空间 2 字节 */
#define BSP_W5500_SN_TX_RD        0x0022U
#define BSP_W5500_SN_TX_WR        0x0024U
#define BSP_W5500_SN_RX_RSR       0x0026U   /* RX 可读字节数 2 字节 */
#define BSP_W5500_SN_RX_RD        0x0028U
#define BSP_W5500_SN_RX_WR        0x002AU

/* ---- Socket 命令（Sn_CR） ---- */
#define BSP_W5500_CR_OPEN         0x01U
#define BSP_W5500_CR_LISTEN       0x02U
#define BSP_W5500_CR_CONNECT      0x04U
#define BSP_W5500_CR_DISCON       0x08U
#define BSP_W5500_CR_CLOSE        0x10U
#define BSP_W5500_CR_SEND         0x20U
#define BSP_W5500_CR_RECV         0x40U

/* ---- Socket 状态（Sn_SR） ---- */
#define BSP_W5500_SOCK_CLOSED     0x00U
#define BSP_W5500_SOCK_INIT       0x13U
#define BSP_W5500_SOCK_LISTEN     0x14U
#define BSP_W5500_SOCK_ESTABLISHED 0x17U
#define BSP_W5500_SOCK_CLOSE_WAIT 0x1CU
#define BSP_W5500_SOCK_UDP        0x22U

/* ---- Socket 中断（Sn_IR） ---- */
#define BSP_W5500_IR_CON          0x01U
#define BSP_W5500_IR_DISCON       0x02U
#define BSP_W5500_IR_RECV         0x04U
#define BSP_W5500_IR_TIMEOUT      0x08U
#define BSP_W5500_IR_SEND_OK      0x10U

/* ---- Socket 模式（Sn_MR） ---- */
#define BSP_W5500_SN_MR_TCP       0x01U
#define BSP_W5500_SN_MR_UDP       0x02U

/* ---- 网络参数结构 ---- */
typedef struct
{
  uint8_t gateway[4];   /* 默认网关 */
  uint8_t subnet[4];    /* 子网掩码 */
  uint8_t mac[6];       /* MAC 地址 */
  uint8_t ip[4];        /* 本机 IP */
} bsp_w5500_netinfo_t;

/**
 * @brief 初始化 W5500：SPI4 引脚/外设 + 软复位，读回版本号。
 * @return VERSIONR 值；0x04 表示芯片正常。
 */
uint8_t BSP_W5500_Init(void);

/**
 * @brief 读取版本寄存器（VERSIONR）。
 * @return 版本值。
 */
uint8_t BSP_W5500_ReadVersion(void);

/**
 * @brief 软复位（MR.RST），复位后需重新配置网络参数。
 */
void BSP_W5500_SoftReset(void);

/**
 * @brief 配置网络参数（网关/子网/MAC/IP）。
 * @param info 网络参数指针，不可为 NULL。
 */
void BSP_W5500_SetNetInfo(const bsp_w5500_netinfo_t *info);

/**
 * @brief 读取 PHY 链接状态（PHYCFGR.LNK，bit0）。
 * @return 1=链接建立，0=未链接。
 */
uint8_t BSP_W5500_GetLinkState(void);

/**
 * @brief 读取 PHYCFGR 寄存器原始值（诊断用）。
 * @return PHYCFGR 原始字节；bit0=链路状态，bit1=速率，bit2=双工模式。
 */
uint8_t BSP_W5500_ReadPhyCfg(void);

/**
 * @brief 通用寄存器 8 位读。
 * @param offset 寄存器偏移地址（16 位）。
 * @return 读回值。
 */
uint8_t BSP_W5500_RegRead(uint16_t offset);

/**
 * @brief 通用寄存器 8 位写。
 * @param offset 寄存器偏移地址（16 位）。
 * @param value  写入值。
 */
void BSP_W5500_RegWrite(uint16_t offset, uint8_t value);

/**
 * @brief 打开 Socket。
 * @param sn     Socket 编号（0~7）。
 * @param mode   BSP_W5500_SN_MR_TCP / BSP_W5500_SN_MR_UDP。
 * @param port   本地端口（大端写入由内部处理）。
 * @return 1=成功，0=失败。
 */
uint8_t BSP_W5500_SocketOpen(uint8_t sn, uint8_t mode, uint16_t port);

/**
 * @brief 关闭 Socket。
 * @param sn Socket 编号。
 */
void BSP_W5500_SocketClose(uint8_t sn);

/**
 * @brief TCP Server：进入监听状态（需先 Open）。
 * @param sn Socket 编号。
 * @return 1=成功，0=失败。
 */
uint8_t BSP_W5500_SocketListen(uint8_t sn);

/**
 * @brief TCP Client：连接远端。
 * @param sn      Socket 编号。
 * @param dip    目的 IP 4 字节。
 * @param dport  目的端口。
 * @return 1=成功，0=失败。
 */
uint8_t BSP_W5500_SocketConnect(uint8_t sn, const uint8_t *dip, uint16_t dport);

/**
 * @brief TCP 主动断开。
 * @param sn Socket 编号。
 */
void BSP_W5500_SocketDisconnect(uint8_t sn);

/**
 * @brief 读取 Socket 状态（Sn_SR）。
 * @param sn Socket 编号。
 * @return 状态值。
 */
uint8_t BSP_W5500_SocketStatus(uint8_t sn);

/**
 * @brief 读取已连接对端信息（TCP 建立后有效）。
 * @param sn    Socket 编号。
 * @param dip   输出缓冲（4 字节），可为 NULL。
 * @param dport 输出端口指针，可为 NULL。
 */
void BSP_W5500_SocketGetRemote(uint8_t sn, uint8_t *dip, uint16_t *dport);

/**
 * @brief 读取 Socket 中断标志（Sn_IR），并自动清除。
 * @param sn Socket 编号。
 * @return 中断标志位组合。
 */
uint8_t BSP_W5500_SocketIrq(uint8_t sn);

/**
 * @brief 查询 RX 可读字节数（Sn_RX_RSR）。
 * @param sn Socket 编号。
 * @return 可读字节数。
 */
uint16_t BSP_W5500_SocketRxSize(uint8_t sn);

/**
 * @brief 查询 TX 剩余空间（Sn_TX_FSR）。
 * @param sn Socket 编号。
 * @return 剩余空间字节数。
 */
uint16_t BSP_W5500_SocketTxFree(uint8_t sn);

/**
 * @brief 从 Socket 接收数据（调用方先查 RxSize）。
 * @param sn    Socket 编号。
 * @param data  接收缓冲。
 * @param len   期望长度。
 * @return 实际接收长度。
 */
uint16_t BSP_W5500_SocketRecv(uint8_t sn, uint8_t *data, uint16_t len);

/**
 * @brief 向 Socket 发送数据（调用方确认 TxFree 足够）。
 * @param sn   Socket 编号。
 * @param data 发送缓冲。
 * @param len  发送长度（不得大于 TX 剩余空间）。
 * @return 1=成功，0=失败。
 */
uint8_t BSP_W5500_SocketSend(uint8_t sn, const uint8_t *data, uint16_t len);

/**
 * @brief 通用 SPI 收发（全双工，阻塞式）。
 * @param tx_data 发送缓冲；为 NULL 时发送 0xFF 哑数据。
 * @param rx_data 接收缓冲；可为 NULL（仅发送）。
 * @param length  收发字节数。
 * @return 1=成功，0=超时或参数错误。
 */
int BSP_W5500_SpiTransfer(const uint8_t *tx_data, uint8_t *rx_data, uint16_t length);

#ifdef __cplusplus
}
#endif

#endif /* BSP_W5500_H */
