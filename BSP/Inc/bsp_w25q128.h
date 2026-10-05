#ifndef BSP_W25Q128_H
#define BSP_W25Q128_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/* W25Q128标准JEDEC标识：Winbond、SPI NOR系列、128 Mbit容量。 */
#define BSP_W25Q128_EXPECTED_MANUFACTURER_ID 0xEFU
#define BSP_W25Q128_EXPECTED_MEMORY_TYPE     0x40U
#define BSP_W25Q128_EXPECTED_CAPACITY_ID     0x18U

typedef enum
{
  BSP_W25Q128_STATUS_NOT_INITIALIZED = 0,
  BSP_W25Q128_STATUS_READY,
  BSP_W25Q128_STATUS_PERIPHERAL_ERROR,
  BSP_W25Q128_STATUS_COMMUNICATION_ERROR,
  BSP_W25Q128_STATUS_UNSUPPORTED_DEVICE
} BSP_W25Q128_Status;

typedef struct
{
  uint8_t manufacturer_id; /* JEDEC厂商标识，W25Q128应为0xEF。 */
  uint8_t memory_type;     /* JEDEC存储类型，W25Q系列应为0x40。 */
  uint8_t capacity_id;     /* JEDEC容量标识，128 Mbit应为0x18。 */
  uint8_t status_register; /* 状态寄存器1原始值，包含BUSY/WEL等状态。 */
} BSP_W25Q128_Info;

/* 初始化QSPI外设、复位Flash并校验JEDEC型号。 */
BSP_W25Q128_Status BSP_W25Q128_Init(void);

/* 重新读取芯片标识与状态寄存器，不修改Flash数据。 */
BSP_W25Q128_Status BSP_W25Q128_RefreshInfo(void);

/* 返回最近一次初始化或通信操作的状态。 */
BSP_W25Q128_Status BSP_W25Q128_GetStatus(void);

/* 复制最近一次有效读取的芯片信息，参数为空时返回0。 */
int BSP_W25Q128_GetInfo(BSP_W25Q128_Info *info);

#ifdef __cplusplus
}
#endif

#endif /* BSP_W25Q128_H */
