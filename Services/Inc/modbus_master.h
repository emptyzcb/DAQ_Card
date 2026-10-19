#ifndef MODBUS_MASTER_H
#define MODBUS_MASTER_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#define MODBUS_MASTER_MAX_ITEMS          32U
#define MODBUS_MASTER_REGS_PER_ITEM      16U
#define MODBUS_MASTER_MIN_INTERVAL_MS    20U
#define MODBUS_MASTER_MAX_INTERVAL_MS 60000U

#define MODBUS_MASTER_FUNCTION_HOLDING   0x03U
#define MODBUS_MASTER_FUNCTION_INPUT     0x04U

typedef struct
{
  uint8_t  enabled;
  uint8_t  slave_addr;
  uint8_t  function;
  uint8_t  reserved;
  uint16_t reg_start;
  uint16_t quantity;
  uint16_t interval_ms;
} ModbusMasterPollEntry;

void MODBUS_MASTER_Init(void);
void MODBUS_MASTER_Poll(uint32_t now_ms);

/* Ethernet task writes a staging table and applies it atomically. */
int MODBUS_MASTER_StageEntry(uint8_t index, const ModbusMasterPollEntry *entry);
int MODBUS_MASTER_GetStagedEntry(uint8_t index, ModbusMasterPollEntry *entry);
int MODBUS_MASTER_SetStagedCount(uint8_t count);
uint8_t MODBUS_MASTER_GetStagedCount(void);
int MODBUS_MASTER_ApplyStaged(void);

uint8_t MODBUS_MASTER_GetActiveCount(void);
int MODBUS_MASTER_GetActiveEntry(uint8_t index, ModbusMasterPollEntry *entry);
uint32_t MODBUS_MASTER_GetConfigGeneration(void);

/* Scheduler API is public so its timing behavior can be host-tested. */
int MODBUS_MASTER_TakeDue(uint32_t now_ms,
                          uint8_t *slot,
                          ModbusMasterPollEntry *entry);
void MODBUS_MASTER_Complete(uint8_t slot,
                            uint8_t success,
                            const uint16_t *values,
                            uint16_t quantity,
                            uint32_t now_ms);

#ifdef __cplusplus
}
#endif

#endif /* MODBUS_MASTER_H */
