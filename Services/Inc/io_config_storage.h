#ifndef IO_CONFIG_STORAGE_H
#define IO_CONFIG_STORAGE_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#define IO_CONFIG_MAGIC                 0x494F4346UL
#define IO_CONFIG_FORMAT_VERSION        1U
#define IO_CONFIG_HEADER_LENGTH         32U
#define IO_CONFIG_RULE_LENGTH           20U
#define IO_CONFIG_MAX_RULE_COUNT        32U
#define IO_CONFIG_MAX_IMAGE_SIZE        4096U
#define IO_CONFIG_MAX_DATA_CHUNK        128U

#define IO_CONFIG_STATUS_OK             0x00U
#define IO_CONFIG_STATUS_BUSY           0x01U
#define IO_CONFIG_STATUS_NO_SESSION     0x02U
#define IO_CONFIG_STATUS_OFFSET         0x03U
#define IO_CONFIG_STATUS_LENGTH         0x04U
#define IO_CONFIG_STATUS_CRC            0x05U
#define IO_CONFIG_STATUS_VERSION        0x06U
#define IO_CONFIG_STATUS_SIZE           0x07U
#define IO_CONFIG_STATUS_RULE_COUNT     0x08U
#define IO_CONFIG_STATUS_RULE           0x09U
#define IO_CONFIG_STATUS_FLASH          0x0AU
#define IO_CONFIG_STATUS_READBACK       0x0BU
#define IO_CONFIG_STATUS_INVALID_OUTPUT 0x0CU
#define IO_CONFIG_STATUS_FORBIDDEN      0x0DU
#define IO_CONFIG_STATUS_TIMEOUT        0x0EU

typedef struct
{
  uint16_t format_version;
  uint8_t run_state;
  uint8_t config_state;
  uint16_t rule_count;
  uint32_t image_length;
  uint32_t image_crc32;
  uint32_t generation;
  uint16_t scan_period_ms;
  uint16_t last_error;
  uint8_t active_slot;
} IO_CONFIG_Info;

void IO_CONFIG_Init(void);
const uint8_t *IO_CONFIG_GetActiveImage(uint32_t *length);
uint32_t IO_CONFIG_Crc32(const uint8_t *data, uint32_t length);
uint8_t IO_CONFIG_GetInfo(IO_CONFIG_Info *info);
uint8_t IO_CONFIG_IsRunning(void);
void IO_CONFIG_SetRunning(uint8_t running);

uint8_t IO_CONFIG_Begin(uint16_t session, uint16_t format_version, uint32_t image_length,
                        uint32_t image_crc32, uint16_t rule_count);
uint8_t IO_CONFIG_WriteChunk(uint16_t session, uint32_t offset, const uint8_t *data, uint8_t length);
uint8_t IO_CONFIG_Verify(uint16_t session, uint32_t *received_length, uint32_t *calculated_crc32);
uint8_t IO_CONFIG_Activate(uint16_t session, uint32_t *generation, uint8_t *active_slot);
uint8_t IO_CONFIG_Abort(uint16_t session);
uint8_t IO_CONFIG_Clear(void);

#ifdef __cplusplus
}
#endif

#endif /* IO_CONFIG_STORAGE_H */
