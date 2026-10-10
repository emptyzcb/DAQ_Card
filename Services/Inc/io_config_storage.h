#ifndef IO_CONFIG_STORAGE_H
#define IO_CONFIG_STORAGE_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/*
 * IO 逻辑程序镜像存储（联合控制器运行时配置）。
 *
 * 镜像格式 v2：字节码程序（上位机 DSL 编译产物）。
 * 上位机将用户编写的 16 模式弱定义回调 + 主程序 while 编译为
 * 字节码镜像，经 Modbus IOCF 通道下载到本机 Flash A/B 双槽。
 *
 * 镜像布局（大端）：
 *   [0..3]    MAGIC = 0x494F4346
 *   [4..5]    格式版本 = 2
 *   [6..7]    头部长度 = 32
 *   [8..11]   generation（代次，上位机每次下载自增）
 *   [12..13]  scan_period_ms（扫描周期，单位 ms，默认 10）
 *   [14..15]  保留
 *   [16..19]  主体长度（16 模式入口表 32B + 字节码）
 *   [20..23]  主体 CRC32（自 BODY_OFFSET 起算）
 *   [24..27]  保留
 *   [28..31]  头部 CRC32（前 28 字节）
 *
 * A/B 双槽主备：下载先写备用槽，校验通过后切换为当前槽；
 * 下载中断时仍保留上一份有效程序，保证脱机运行安全。
 */
#define IO_CONFIG_MAGIC                 0x494F4346UL
#define IO_CONFIG_FORMAT_VERSION        2U
#define IO_CONFIG_HEADER_LENGTH         32U
#define IO_CONFIG_MAX_IMAGE_SIZE        8192U
#define IO_CONFIG_MAX_DATA_CHUNK        128U

/* 镜像 v2 布局：头 32B + 16 路模式入口表 32B + 字节码主体 */
#define IO_CONFIG_BODY_OFFSET           (IO_CONFIG_HEADER_LENGTH + 32U)

/* IOCF 协议状态码（Modbus 0x41 功能码响应） */
#define IO_CONFIG_STATUS_OK             0x00U
#define IO_CONFIG_STATUS_BUSY           0x01U
#define IO_CONFIG_STATUS_NO_SESSION     0x02U
#define IO_CONFIG_STATUS_OFFSET         0x03U
#define IO_CONFIG_STATUS_LENGTH         0x04U
#define IO_CONFIG_STATUS_CRC            0x05U
#define IO_CONFIG_STATUS_VERSION        0x06U
#define IO_CONFIG_STATUS_SIZE           0x07U
#define IO_CONFIG_STATUS_RULE_COUNT     0x08U  /* v2 无规则数概念，保留码位 */
#define IO_CONFIG_STATUS_RULE           0x09U
#define IO_CONFIG_STATUS_FLASH          0x0AU
#define IO_CONFIG_STATUS_READBACK       0x0BU
#define IO_CONFIG_STATUS_INVALID_OUTPUT 0x0CU
#define IO_CONFIG_STATUS_FORBIDDEN      0x0DU
#define IO_CONFIG_STATUS_TIMEOUT        0x0EU

/* 镜像头部字段偏移（大端） */
#define IO_CONFIG_OFF_MAGIC             0U
#define IO_CONFIG_OFF_VERSION           4U
#define IO_CONFIG_OFF_HEADER_LENGTH     6U
#define IO_CONFIG_OFF_GENERATION        8U
#define IO_CONFIG_OFF_SCAN_PERIOD_MS    12U
#define IO_CONFIG_OFF_RESERVED          14U
#define IO_CONFIG_OFF_BODY_LENGTH       16U
#define IO_CONFIG_OFF_BODY_CRC          20U
#define IO_CONFIG_OFF_HEADER_CRC        28U

/* 镜像信息快照（IOCF 0x01 GET_INFO 响应与上位机观测用） */
typedef struct
{
  uint16_t format_version;      /* 当前活动镜像格式版本 */
  uint8_t  run_state;           /* 0=停止，1=运行，2=下载会话进行中 */
  uint8_t  config_state;        /* 当前活动槽：1=A，2=B，3=无有效配置 */
  uint16_t rule_count;          /* v2 恒为 0，保留字段 */
  uint32_t image_length;        /* 活动镜像总长度（含头部与入口表） */
  uint32_t image_crc32;         /* 活动镜像整体 CRC32 */
  uint32_t generation;          /* 活动镜像代次 */
  uint16_t scan_period_ms;      /* 扫描周期，单位 ms */
  uint16_t last_error;          /* 最近一次 IOCF 操作错误码 */
  uint8_t  active_slot;         /* 活动槽号 */
} IO_CONFIG_Info;

void IO_CONFIG_Init(void);
const uint8_t *IO_CONFIG_GetActiveImage(uint32_t *length);
uint32_t IO_CONFIG_Crc32(const uint8_t *data, uint32_t length);
uint8_t IO_CONFIG_GetInfo(IO_CONFIG_Info *info);
uint8_t IO_CONFIG_IsRunning(void);
void IO_CONFIG_SetRunning(uint8_t running);

uint8_t IO_CONFIG_Begin(uint16_t session, uint16_t format_version, uint32_t image_length,
                        uint32_t image_crc32);
uint8_t IO_CONFIG_WriteChunk(uint16_t session, uint32_t offset, const uint8_t *data, uint8_t length);
uint8_t IO_CONFIG_Verify(uint16_t session, uint32_t *received_length, uint32_t *calculated_crc32);
uint8_t IO_CONFIG_Activate(uint16_t session, uint32_t *generation, uint8_t *active_slot);
uint8_t IO_CONFIG_Abort(uint16_t session);
uint8_t IO_CONFIG_Clear(void);

#ifdef __cplusplus
}
#endif

#endif /* IO_CONFIG_STORAGE_H */
