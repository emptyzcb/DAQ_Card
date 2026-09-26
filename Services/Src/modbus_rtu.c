#include "modbus_rtu.h"

#include "modbus_register_map.h"
#include "io_logic_protocol.h"

#define MODBUS_RTU_EXCEPTION_ILLEGAL_FUNCTION 1U
#define MODBUS_RTU_EXCEPTION_ILLEGAL_ADDRESS  2U
#define MODBUS_RTU_EXCEPTION_ILLEGAL_VALUE    3U
#define MODBUS_RTU_EXCEPTION_DEVICE_FAILURE   4U

static uint16_t modbus_read_u16(const uint8_t *data)
{
  return (uint16_t)(((uint16_t)data[0] << 8) | data[1]);
}

static void modbus_write_u16(uint8_t *data, uint16_t value)
{
  data[0] = (uint8_t)(value >> 8);
  data[1] = (uint8_t)value;
}

static void modbus_append_crc(uint8_t *frame, uint16_t payload_length)
{
  uint16_t crc = MODBUS_RTU_Crc16(frame, payload_length);

  frame[payload_length] = (uint8_t)crc;
  frame[payload_length + 1U] = (uint8_t)(crc >> 8);
}

static uint16_t modbus_exception(uint8_t address,
                                 uint8_t function,
                                 uint8_t exception,
                                 uint8_t *response,
                                 uint16_t capacity)
{
  if (capacity < 5U)
  {
    return 0U;
  }

  response[0] = address;
  response[1] = (uint8_t)(function | 0x80U);
  response[2] = exception;
  modbus_append_crc(response, 3U);
  return 5U;
}

uint16_t MODBUS_RTU_Crc16(const uint8_t *data, uint16_t length)
{
  uint16_t crc = 0xFFFFU;
  uint16_t index;
  uint8_t bit;

  if (data == 0)
  {
    return 0U;
  }

  for (index = 0U; index < length; index++)
  {
    crc ^= data[index];
    for (bit = 0U; bit < 8U; bit++)
    {
      if ((crc & 0x0001U) != 0U)
      {
        crc = (uint16_t)((crc >> 1) ^ 0xA001U);
      }
      else
      {
        crc >>= 1;
      }
    }
  }

  return crc;
}

uint16_t MODBUS_RTU_HandleRequest(const uint8_t *request,
                                  uint16_t request_length,
                                  uint8_t *response,
                                  uint16_t response_capacity)
{
  uint8_t address;
  uint8_t function;
  uint16_t received_crc;
  uint16_t calculated_crc;
  uint16_t start_address;
  uint16_t quantity;
  uint16_t index;
  uint16_t value;
  uint16_t response_payload_length;
  uint8_t broadcast;

  if ((request == 0) || (response == 0) || (request_length < 4U) ||
      (request_length > MODBUS_RTU_MAX_ADU_SIZE))
  {
    return 0U;
  }

  address = request[0];
  function = request[1];
  received_crc = (uint16_t)(request[request_length - 2U] |
                            ((uint16_t)request[request_length - 1U] << 8));
  calculated_crc = MODBUS_RTU_Crc16(request, (uint16_t)(request_length - 2U));
  if (received_crc != calculated_crc)
  {
    return 0U;
  }

  broadcast = (address == 0U) ? 1U : 0U;
  if ((broadcast == 0U) && (address != MODBUS_RTU_DEFAULT_SLAVE_ADDRESS))
  {
    return 0U;
  }

  if (function == IO_LOGIC_PROTOCOL_FUNCTION)
  {
    if (broadcast != 0U)
    {
      return 0U;
    }

    response_payload_length = IO_LOGIC_PROTOCOL_Handle(address,
                                                        &request[2],
                                                        (uint16_t)(request_length - 4U),
                                                        response,
                                                        response_capacity);
    if (response_payload_length == 0U)
    {
      return 0U;
    }

    modbus_append_crc(response, response_payload_length);
    return (uint16_t)(response_payload_length + 2U);
  }

  if (function == 0x03U)
  {
    if (broadcast != 0U)
    {
      return 0U;
    }

    if (request_length != 8U)
    {
      return modbus_exception(address, function, MODBUS_RTU_EXCEPTION_ILLEGAL_VALUE,
                              response, response_capacity);
    }

    start_address = modbus_read_u16(&request[2]);
    quantity = modbus_read_u16(&request[4]);
    if ((quantity == 0U) || (quantity > 125U) ||
        (start_address > (uint16_t)(0xFFFFU - (quantity - 1U))))
    {
      return modbus_exception(address, function, MODBUS_RTU_EXCEPTION_ILLEGAL_VALUE,
                              response, response_capacity);
    }

    response_payload_length = (uint16_t)(3U + (2U * quantity));
    if ((response_capacity < (uint16_t)(response_payload_length + 2U)) ||
        (response_payload_length > MODBUS_RTU_MAX_ADU_SIZE - 2U))
    {
      return modbus_exception(address, function, MODBUS_RTU_EXCEPTION_DEVICE_FAILURE,
                              response, response_capacity);
    }

    response[0] = address;
    response[1] = function;
    response[2] = (uint8_t)(quantity * 2U);
    for (index = 0U; index < quantity; index++)
    {
      if (MODBUS_REGISTER_ReadHolding((uint16_t)(start_address + index), &value) == 0)
      {
        return modbus_exception(address, function, MODBUS_RTU_EXCEPTION_ILLEGAL_ADDRESS,
                                response, response_capacity);
      }

      modbus_write_u16(&response[3U + (2U * index)], value);
    }

    modbus_append_crc(response, response_payload_length);
    return (uint16_t)(response_payload_length + 2U);
  }

  if (function == 0x06U)
  {
    if (request_length != 8U)
    {
      if (broadcast != 0U)
      {
        return 0U;
      }

      return modbus_exception(address, function, MODBUS_RTU_EXCEPTION_ILLEGAL_VALUE,
                              response, response_capacity);
    }

    start_address = modbus_read_u16(&request[2]);
    value = modbus_read_u16(&request[4]);
    if (MODBUS_REGISTER_IsWritable(start_address) == 0)
    {
      if (broadcast != 0U)
      {
        return 0U;
      }

      return modbus_exception(address, function, MODBUS_RTU_EXCEPTION_ILLEGAL_ADDRESS,
                              response, response_capacity);
    }

    if (MODBUS_REGISTER_IsValueValid(start_address, value) == 0)
    {
      if (broadcast != 0U)
      {
        return 0U;
      }

      return modbus_exception(address, function, MODBUS_RTU_EXCEPTION_ILLEGAL_VALUE,
                              response, response_capacity);
    }

    if (MODBUS_REGISTER_WriteHolding(start_address, value) == 0)
    {
      if (broadcast != 0U)
      {
        return 0U;
      }

      return modbus_exception(address, function, MODBUS_RTU_EXCEPTION_DEVICE_FAILURE,
                              response, response_capacity);
    }

    if (broadcast != 0U)
    {
      return 0U;
    }

    if (response_capacity < 8U)
    {
      return 0U;
    }

    response[0] = address;
    response[1] = function;
    response[2] = request[2];
    response[3] = request[3];
    response[4] = request[4];
    response[5] = request[5];
    modbus_append_crc(response, 6U);
    return 8U;
  }

  if (function == 0x10U)
  {
    uint8_t byte_count;

    if (request_length < 9U)
    {
      if (broadcast != 0U)
      {
        return 0U;
      }

      return modbus_exception(address, function, MODBUS_RTU_EXCEPTION_ILLEGAL_VALUE,
                              response, response_capacity);
    }

    start_address = modbus_read_u16(&request[2]);
    quantity = modbus_read_u16(&request[4]);
    byte_count = request[6];
    if ((quantity == 0U) || (quantity > 123U) ||
        (start_address > (uint16_t)(0xFFFFU - (quantity - 1U))) ||
        (byte_count != (uint8_t)(quantity * 2U)) ||
        (request_length != (uint16_t)(9U + byte_count)))
    {
      if (broadcast != 0U)
      {
        return 0U;
      }

      return modbus_exception(address, function, MODBUS_RTU_EXCEPTION_ILLEGAL_VALUE,
                              response, response_capacity);
    }

    for (index = 0U; index < quantity; index++)
    {
      value = modbus_read_u16(&request[7U + (2U * index)]);
      if (MODBUS_REGISTER_IsWritable((uint16_t)(start_address + index)) == 0)
      {
        if (broadcast != 0U)
        {
          return 0U;
        }

        return modbus_exception(address, function, MODBUS_RTU_EXCEPTION_ILLEGAL_ADDRESS,
                                response, response_capacity);
      }

      if (MODBUS_REGISTER_IsValueValid((uint16_t)(start_address + index), value) == 0)
      {
        if (broadcast != 0U)
        {
          return 0U;
        }

        return modbus_exception(address, function, MODBUS_RTU_EXCEPTION_ILLEGAL_VALUE,
                                response, response_capacity);
      }
    }

    for (index = 0U; index < quantity; index++)
    {
      value = modbus_read_u16(&request[7U + (2U * index)]);
      if (MODBUS_REGISTER_WriteHolding((uint16_t)(start_address + index), value) == 0)
      {
        if (broadcast != 0U)
        {
          return 0U;
        }

        return modbus_exception(address, function, MODBUS_RTU_EXCEPTION_DEVICE_FAILURE,
                                response, response_capacity);
      }
    }

    if (broadcast != 0U)
    {
      return 0U;
    }

    if (response_capacity < 8U)
    {
      return 0U;
    }

    response[0] = address;
    response[1] = function;
    response[2] = request[2];
    response[3] = request[3];
    response[4] = request[4];
    response[5] = request[5];
    modbus_append_crc(response, 6U);
    return 8U;
  }

  if (broadcast != 0U)
  {
    return 0U;
  }

  return modbus_exception(address, function, MODBUS_RTU_EXCEPTION_ILLEGAL_FUNCTION,
                          response, response_capacity);
}
