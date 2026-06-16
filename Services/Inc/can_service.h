#ifndef CAN_SERVICE_H
#define CAN_SERVICE_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#define CAN_SERVICE_MAX_DATA_LEN 8U
#define CAN_SERVICE_DEFAULT_STD_ID 0x123U

typedef struct
{
  uint32_t id;
  uint8_t is_extended;
  uint8_t len;
  uint8_t data[CAN_SERVICE_MAX_DATA_LEN];
} CAN_SERVICE_Frame;

typedef struct
{
  uint32_t rx_count;
  uint32_t tx_count;
  uint32_t echo_count;
  uint32_t rx_error_count;
  uint32_t tx_error_count;
  uint32_t last_rx_tick;
  uint32_t last_tx_tick;
  CAN_SERVICE_Frame last_rx;
  CAN_SERVICE_Frame last_tx;
} CAN_SERVICE_Diagnostics;

void CAN_SERVICE_Init(void);
int CAN_SERVICE_Send(const CAN_SERVICE_Frame *frame);
int CAN_SERVICE_SendStd(uint32_t std_id, const uint8_t *data, uint8_t len);
void CAN_SERVICE_GetDiagnostics(CAN_SERVICE_Diagnostics *diagnostics);

#ifdef __cplusplus
}
#endif

#endif /* CAN_SERVICE_H */
