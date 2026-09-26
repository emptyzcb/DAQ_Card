#ifndef IO_LOGIC_PROTOCOL_H
#define IO_LOGIC_PROTOCOL_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#define IO_LOGIC_PROTOCOL_FUNCTION 0x41U

uint16_t IO_LOGIC_PROTOCOL_Handle(uint8_t address,
                                  const uint8_t *payload,
                                  uint16_t payload_length,
                                  uint8_t *response,
                                  uint16_t response_capacity);

#ifdef __cplusplus
}
#endif

#endif /* IO_LOGIC_PROTOCOL_H */
