#ifndef MODBUS_TCP_H
#define MODBUS_TCP_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#define MODBUS_TCP_MBAP_PREFIX_SIZE    6U
#define MODBUS_TCP_HEADER_SIZE         7U
#define MODBUS_TCP_MAX_RTU_ADU_SIZE  256U
#define MODBUS_TCP_MAX_ADU_SIZE       260U

typedef enum
{
  MODBUS_TCP_INVALID_ADU = -1,
  MODBUS_TCP_NEED_MORE = 0,
  MODBUS_TCP_ADU_READY = 1
} ModbusTcpParseResult;

/* Inspect the first ADU in a TCP byte stream without consuming it. */
ModbusTcpParseResult MODBUS_TCP_PeekAduLength(const uint8_t *buffer,
                                               uint16_t buffered_length,
                                               uint16_t *adu_length);

/* Convert one complete Modbus TCP ADU to the existing RTU handler and back. */
uint16_t MODBUS_TCP_HandleAdu(const uint8_t *request,
                              uint16_t request_length,
                              uint8_t *response,
                              uint16_t response_capacity);

#ifdef __cplusplus
}
#endif

#endif /* MODBUS_TCP_H */
