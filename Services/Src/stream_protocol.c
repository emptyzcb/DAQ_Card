#include "sys.h"

#define STREAM_PROTOCOL_SOF0 0xA5U
#define STREAM_PROTOCOL_SOF1 0x5AU

static void write_u16_le(uint8_t *buffer, uint16_t value)
{
  buffer[0] = (uint8_t)(value & 0xFFU);
  buffer[1] = (uint8_t)((value >> 8) & 0xFFU);
}

static void write_u32_le(uint8_t *buffer, uint32_t value)
{
  buffer[0] = (uint8_t)(value & 0xFFU);
  buffer[1] = (uint8_t)((value >> 8) & 0xFFU);
  buffer[2] = (uint8_t)((value >> 16) & 0xFFU);
  buffer[3] = (uint8_t)((value >> 24) & 0xFFU);
}

static void write_i16_le(uint8_t *buffer, int16_t value)
{
  write_u16_le(buffer, (uint16_t)value);
}

static void write_i32_le(uint8_t *buffer, int32_t value)
{
  write_u32_le(buffer, (uint32_t)value);
}

uint16_t STREAM_PROTOCOL_CalcCrc16(const uint8_t *data, uint16_t length)
{
  uint16_t crc = 0xFFFFU;

  if (data == NULL)
  {
    return 0U;
  }

  while (length-- > 0U)
  {
    crc ^= *data++;
    for (uint8_t bit = 0U; bit < 8U; bit++)
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

uint16_t STREAM_PROTOCOL_EncodeFrame(uint8_t type,
                                     uint16_t sequence,
                                     uint32_t timestamp_ms,
                                     const uint8_t *payload,
                                     uint16_t payload_length,
                                     uint8_t *frame,
                                     uint16_t frame_size)
{
  uint16_t frame_length;
  uint16_t crc;

  if ((frame == NULL) ||
      ((payload == NULL) && (payload_length > 0U)) ||
      (payload_length > STREAM_PROTOCOL_MAX_PAYLOAD))
  {
    return 0U;
  }

  frame_length = (uint16_t)(STREAM_PROTOCOL_HEADER_SIZE + payload_length + STREAM_PROTOCOL_CRC_SIZE);
  if (frame_size < frame_length)
  {
    return 0U;
  }

  frame[0] = STREAM_PROTOCOL_SOF0;
  frame[1] = STREAM_PROTOCOL_SOF1;
  frame[2] = STREAM_PROTOCOL_VERSION;
  frame[3] = type;
  write_u16_le(&frame[4], sequence);
  write_u32_le(&frame[6], timestamp_ms);
  write_u16_le(&frame[10], payload_length);

  for (uint16_t index = 0U; index < payload_length; index++)
  {
    frame[STREAM_PROTOCOL_HEADER_SIZE + index] = payload[index];
  }

  crc = STREAM_PROTOCOL_CalcCrc16(frame, (uint16_t)(STREAM_PROTOCOL_HEADER_SIZE + payload_length));
  write_u16_le(&frame[STREAM_PROTOCOL_HEADER_SIZE + payload_length], crc);

  return frame_length;
}

uint16_t STREAM_PROTOCOL_EncodeGyro(uint16_t sequence,
                                    uint32_t timestamp_ms,
                                    const STREAM_PROTOCOL_GyroSample *sample,
                                    uint8_t *frame,
                                    uint16_t frame_size)
{
  uint8_t payload[18];

  if (sample == NULL)
  {
    return 0U;
  }

  write_i16_le(&payload[0], sample->x_raw);
  write_i16_le(&payload[2], sample->y_raw);
  write_i16_le(&payload[4], sample->z_raw);
  write_i32_le(&payload[6], sample->x_mdps);
  write_i32_le(&payload[10], sample->y_mdps);
  write_i32_le(&payload[14], sample->z_mdps);

  return STREAM_PROTOCOL_EncodeFrame((uint8_t)STREAM_PROTOCOL_FRAME_GYRO,
                                     sequence,
                                     timestamp_ms,
                                     payload,
                                     (uint16_t)sizeof(payload),
                                     frame,
                                     frame_size);
}
