#ifndef STREAM_PROTOCOL_H
#define STREAM_PROTOCOL_H

#include <stdint.h>

#define STREAM_PROTOCOL_VERSION        1U
#define STREAM_PROTOCOL_MAX_PAYLOAD    64U
#define STREAM_PROTOCOL_HEADER_SIZE    12U
#define STREAM_PROTOCOL_CRC_SIZE       2U
#define STREAM_PROTOCOL_MAX_FRAME_SIZE (STREAM_PROTOCOL_HEADER_SIZE + STREAM_PROTOCOL_MAX_PAYLOAD + STREAM_PROTOCOL_CRC_SIZE)

typedef enum
{
  STREAM_PROTOCOL_FRAME_STATUS = 1U,
  STREAM_PROTOCOL_FRAME_GYRO = 2U,
  STREAM_PROTOCOL_FRAME_ERROR = 3U
} STREAM_PROTOCOL_FrameType;

typedef struct
{
  int16_t x_raw;
  int16_t y_raw;
  int16_t z_raw;
  int32_t x_mdps;
  int32_t y_mdps;
  int32_t z_mdps;
} STREAM_PROTOCOL_GyroSample;

uint16_t STREAM_PROTOCOL_EncodeFrame(uint8_t type,
                                     uint16_t sequence,
                                     uint32_t timestamp_ms,
                                     const uint8_t *payload,
                                     uint16_t payload_length,
                                     uint8_t *frame,
                                     uint16_t frame_size);
uint16_t STREAM_PROTOCOL_EncodeGyro(uint16_t sequence,
                                    uint32_t timestamp_ms,
                                    const STREAM_PROTOCOL_GyroSample *sample,
                                    uint8_t *frame,
                                    uint16_t frame_size);
uint16_t STREAM_PROTOCOL_CalcCrc16(const uint8_t *data, uint16_t length);

#endif /* STREAM_PROTOCOL_H */
