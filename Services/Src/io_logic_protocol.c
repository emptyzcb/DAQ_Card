/**
 * @file    io_logic_protocol.c
 * @brief   IOCF（IO Configuration）下载协议处理（Modbus 0x41 功能码）
 *
 * 协议命令（payload[0]）：
 *   0x01 GET_INFO    ：读取镜像信息（版本/长度/CRC/代次/槽位）
 *   0x02 BEGIN       ：开始下载会话（v2：15 字节载荷）
 *   0x03 DATA        ：写入数据块
 *   0x04 VERIFY      ：校验（长度 + 整体 CRC + 镜像结构）
 *   0x05 ACTIVATE    ：激活新槽（Flash 写入 + 回读校验）
 *   0x06 READ        ：回读活动镜像
 *   0x07 ABORT       ：放弃下载会话
 *   0x08 CLEAR       ：清除全部槽区
 *   0x09 START       ：启动联合控制器运行
 *   0x0A STOP        ：停止联合控制器
 *
 * 响应帧：address | 0x41 | command | status | [数据...]
 */

#include "io_logic_protocol.h"

#include <string.h>

#include "io_config_storage.h"
#include "io_logic_engine.h"

static uint16_t protocol_read_u16(const uint8_t *data)
{
  return (uint16_t)(((uint16_t)data[0] << 8) | data[1]);
}

static uint32_t protocol_read_u32(const uint8_t *data)
{
  return ((uint32_t)data[0] << 24) | ((uint32_t)data[1] << 16) |
         ((uint32_t)data[2] << 8) | data[3];
}

static void protocol_write_u16(uint8_t *data, uint16_t value)
{
  data[0] = (uint8_t)(value >> 8);
  data[1] = (uint8_t)value;
}

static void protocol_write_u32(uint8_t *data, uint32_t value)
{
  data[0] = (uint8_t)(value >> 24);
  data[1] = (uint8_t)(value >> 16);
  data[2] = (uint8_t)(value >> 8);
  data[3] = (uint8_t)value;
}

static uint16_t protocol_response_start(uint8_t address, uint8_t command, uint8_t *response)
{
  response[0] = address;
  response[1] = IO_LOGIC_PROTOCOL_FUNCTION;
  response[2] = command;
  response[3] = IO_CONFIG_STATUS_OK;
  return 4U;
}

static uint16_t protocol_status_response(uint8_t address, uint8_t command, uint8_t status,
                                         uint8_t *response, uint16_t capacity)
{
  if (capacity < 4U)
  {
    return 0U;
  }
  response[0] = address;
  response[1] = IO_LOGIC_PROTOCOL_FUNCTION;
  response[2] = command;
  response[3] = status;
  return 4U;
}

uint16_t IO_LOGIC_PROTOCOL_Handle(uint8_t address, const uint8_t *payload,
                                  uint16_t payload_length, uint8_t *response,
                                  uint16_t response_capacity)
{
  IO_CONFIG_Info info;
  uint8_t command;
  uint8_t status;
  uint16_t length;

  if ((payload == 0) || (response == 0) || (payload_length == 0U) || (response_capacity < 4U))
  {
    return 0U;
  }
  command = payload[0];

  switch (command)
  {
    case 0x01U:
      if ((payload_length != 1U) || (IO_CONFIG_GetInfo(&info) == 0U))
      {
        return protocol_status_response(address, command, IO_CONFIG_STATUS_LENGTH, response, response_capacity);
      }
      if (response_capacity < 24U)
      {
        return 0U;
      }
      length = protocol_response_start(address, command, response);
      protocol_write_u16(&response[length], info.format_version); length += 2U;
      response[length++] = info.run_state;
      response[length++] = info.config_state;
      protocol_write_u16(&response[length], info.rule_count); length += 2U;
      protocol_write_u32(&response[length], info.image_length); length += 4U;
      protocol_write_u32(&response[length], info.image_crc32); length += 4U;
      protocol_write_u32(&response[length], info.generation); length += 4U;
      protocol_write_u16(&response[length], info.scan_period_ms); length += 2U;
      protocol_write_u16(&response[length], info.last_error); length += 2U;
      return length;

    case 0x02U:
      /*
       * v2 下载开始命令（15 字节，含 command）：
       *   payload[0]      command = 0x02
       *   payload[1..2]   session
       *   payload[3..4]   format_version
       *   payload[5..6]   保留
       *   payload[7..10]  image_length
       *   payload[11..14] image_crc32
       */
      if (payload_length != 15U)
      {
        return protocol_status_response(address, command, IO_CONFIG_STATUS_LENGTH, response, response_capacity);
      }
      status = IO_CONFIG_Begin(protocol_read_u16(&payload[1]), protocol_read_u16(&payload[3]),
                               protocol_read_u32(&payload[7]), protocol_read_u32(&payload[11]));
      if (status != IO_CONFIG_STATUS_OK)
      {
        return protocol_status_response(address, command, status, response, response_capacity);
      }
      length = protocol_response_start(address, command, response);
      protocol_write_u16(&response[length], protocol_read_u16(&payload[1])); length += 2U;
      protocol_write_u16(&response[length], IO_CONFIG_MAX_DATA_CHUNK); length += 2U;
      return length;

    case 0x03U:
      if ((payload_length < 9U) || (payload_length != (uint16_t)(8U + payload[7])))
      {
        return protocol_status_response(address, command, IO_CONFIG_STATUS_LENGTH, response, response_capacity);
      }
      status = IO_CONFIG_WriteChunk(protocol_read_u16(&payload[1]), protocol_read_u32(&payload[3]),
                                    &payload[8], payload[7]);
      if (status != IO_CONFIG_STATUS_OK)
      {
        return protocol_status_response(address, command, status, response, response_capacity);
      }
      length = protocol_response_start(address, command, response);
      protocol_write_u16(&response[length], protocol_read_u16(&payload[1])); length += 2U;
      protocol_write_u32(&response[length], protocol_read_u32(&payload[3])); length += 4U;
      protocol_write_u16(&response[length], payload[7]); length += 2U;
      return length;

    case 0x04U:
      if (payload_length != 7U)
      {
        return protocol_status_response(address, command, IO_CONFIG_STATUS_LENGTH, response, response_capacity);
      }
      {
        uint32_t received_length = 0U;
        uint32_t calculated_crc32 = 0U;
        status = IO_CONFIG_Verify(protocol_read_u16(&payload[1]), &received_length, &calculated_crc32);
        if (status != IO_CONFIG_STATUS_OK)
        {
          return protocol_status_response(address, command, status, response, response_capacity);
        }
        length = protocol_response_start(address, command, response);
        protocol_write_u32(&response[length], received_length); length += 4U;
        protocol_write_u32(&response[length], calculated_crc32); length += 4U;
        return length;
      }

    case 0x05U:
      if (payload_length != 3U)
      {
        return protocol_status_response(address, command, IO_CONFIG_STATUS_LENGTH, response, response_capacity);
      }
      {
        uint32_t generation = 0U;
        uint8_t active_slot = 0U;
        status = IO_CONFIG_Activate(protocol_read_u16(&payload[1]), &generation, &active_slot);
        if (status != IO_CONFIG_STATUS_OK)
        {
          return protocol_status_response(address, command, status, response, response_capacity);
        }
        length = protocol_response_start(address, command, response);
        protocol_write_u32(&response[length], generation); length += 4U;
        response[length++] = active_slot;
        return length;
      }

    case 0x06U:
      if ((payload_length != 6U) || (payload[5] == 0U) || (payload[5] > IO_CONFIG_MAX_DATA_CHUNK))
      {
        return protocol_status_response(address, command, IO_CONFIG_STATUS_LENGTH, response, response_capacity);
      }
      {
        uint32_t image_length = 0U;
        const uint8_t *image = IO_CONFIG_GetActiveImage(&image_length);
        uint32_t offset = protocol_read_u32(&payload[1]);
        uint8_t data_length = payload[5];
        if (offset + data_length > image_length)
        {
          return protocol_status_response(address, command, IO_CONFIG_STATUS_OFFSET, response, response_capacity);
        }
        if (response_capacity < (uint16_t)(9U + data_length))
        {
          return 0U;
        }
        length = protocol_response_start(address, command, response);
        protocol_write_u32(&response[length], offset); length += 4U;
        response[length++] = data_length;
        memcpy(&response[length], &image[offset], data_length);
        length = (uint16_t)(length + data_length);
        return length;
      }

    case 0x07U:
      if (payload_length != 3U)
      {
        return protocol_status_response(address, command, IO_CONFIG_STATUS_LENGTH, response, response_capacity);
      }
      return protocol_status_response(address, command, IO_CONFIG_Abort(protocol_read_u16(&payload[1])),
                                      response, response_capacity);

    case 0x08U:
      if (payload_length != 1U)
      {
        return protocol_status_response(address, command, IO_CONFIG_STATUS_LENGTH, response, response_capacity);
      }
      IO_LOGIC_ENGINE_Stop();
      return protocol_status_response(address, command, IO_CONFIG_Clear(), response, response_capacity);

    case 0x09U:
      if (payload_length != 1U)
      {
        return protocol_status_response(address, command, IO_CONFIG_STATUS_LENGTH, response, response_capacity);
      }
      IO_CONFIG_SetRunning(1U);
      return protocol_status_response(address, command, IO_CONFIG_STATUS_OK, response, response_capacity);

    case 0x0AU:
      if (payload_length != 1U)
      {
        return protocol_status_response(address, command, IO_CONFIG_STATUS_LENGTH, response, response_capacity);
      }
      IO_LOGIC_ENGINE_Stop();
      IO_CONFIG_SetRunning(0U);
      return protocol_status_response(address, command, IO_CONFIG_STATUS_OK, response, response_capacity);

    default:
      return protocol_status_response(address, command, IO_CONFIG_STATUS_FORBIDDEN, response, response_capacity);
  }
}
