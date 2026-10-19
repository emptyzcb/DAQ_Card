#include "modbus_tcp.h"

#include <string.h>

#include "modbus_rtu.h"

#define MODBUS_TCP_PROTOCOL_ID       0U
#define MODBUS_TCP_MIN_LENGTH_FIELD  2U
#define MODBUS_TCP_MAX_LENGTH_FIELD 254U

static uint16_t modbus_tcp_read_u16(const uint8_t *data)
{
  return (uint16_t)(((uint16_t)data[0] << 8) | data[1]);
}

static void modbus_tcp_write_u16(uint8_t *data, uint16_t value)
{
  data[0] = (uint8_t)(value >> 8);
  data[1] = (uint8_t)value;
}

ModbusTcpParseResult MODBUS_TCP_PeekAduLength(const uint8_t *buffer,
                                               uint16_t buffered_length,
                                               uint16_t *adu_length)
{
  uint16_t protocol_id;
  uint16_t length_field;
  uint16_t total_length;

  if ((buffer == 0) || (adu_length == 0))
  {
    return MODBUS_TCP_INVALID_ADU;
  }

  *adu_length = 0U;
  if (buffered_length < MODBUS_TCP_MBAP_PREFIX_SIZE)
  {
    return MODBUS_TCP_NEED_MORE;
  }

  protocol_id = modbus_tcp_read_u16(&buffer[2]);
  length_field = modbus_tcp_read_u16(&buffer[4]);
  if ((protocol_id != MODBUS_TCP_PROTOCOL_ID) ||
      (length_field < MODBUS_TCP_MIN_LENGTH_FIELD) ||
      (length_field > MODBUS_TCP_MAX_LENGTH_FIELD))
  {
    return MODBUS_TCP_INVALID_ADU;
  }

  total_length = (uint16_t)(MODBUS_TCP_MBAP_PREFIX_SIZE + length_field);
  if (total_length > MODBUS_TCP_MAX_ADU_SIZE)
  {
    return MODBUS_TCP_INVALID_ADU;
  }

  *adu_length = total_length;
  return (buffered_length < total_length) ? MODBUS_TCP_NEED_MORE
                                          : MODBUS_TCP_ADU_READY;
}

uint16_t MODBUS_TCP_HandleAdu(const uint8_t *request,
                              uint16_t request_length,
                              uint8_t *response,
                              uint16_t response_capacity)
{
  uint8_t rtu_request[MODBUS_TCP_MAX_RTU_ADU_SIZE];
  uint8_t rtu_response[MODBUS_TCP_MAX_RTU_ADU_SIZE];
  uint16_t tcp_adu_length;
  uint16_t length_field;
  uint16_t rtu_request_length;
  uint16_t rtu_response_length;
  uint16_t tcp_response_length;
  uint16_t crc;

  if ((request == 0) || (response == 0) ||
      (MODBUS_TCP_PeekAduLength(request, request_length, &tcp_adu_length) != MODBUS_TCP_ADU_READY) ||
      (tcp_adu_length != request_length))
  {
    return 0U;
  }

  length_field = modbus_tcp_read_u16(&request[4]);
  rtu_request_length = (uint16_t)(length_field + 2U);
  if (rtu_request_length > (uint16_t)sizeof(rtu_request))
  {
    return 0U;
  }

  memcpy(rtu_request, &request[6], length_field);
  crc = MODBUS_RTU_Crc16(rtu_request, length_field);
  rtu_request[length_field] = (uint8_t)crc;
  rtu_request[length_field + 1U] = (uint8_t)(crc >> 8);

  rtu_response_length = MODBUS_RTU_HandleRequest(rtu_request,
                                                  rtu_request_length,
                                                  rtu_response,
                                                  (uint16_t)sizeof(rtu_response));
  if ((rtu_response_length < 4U) || (rtu_response[0] != request[6]))
  {
    return 0U;
  }

  tcp_response_length = (uint16_t)(rtu_response_length + 4U);
  if (response_capacity < tcp_response_length)
  {
    return 0U;
  }

  response[0] = request[0];
  response[1] = request[1];
  response[2] = 0U;
  response[3] = 0U;
  modbus_tcp_write_u16(&response[4], (uint16_t)(rtu_response_length - 2U));
  response[6] = rtu_response[0];
  memcpy(&response[7], &rtu_response[1], (uint16_t)(rtu_response_length - 3U));
  return tcp_response_length;
}
