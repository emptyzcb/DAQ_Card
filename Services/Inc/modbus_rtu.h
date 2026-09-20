#ifndef MODBUS_RTU_H
#define MODBUS_RTU_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#define MODBUS_RTU_DEFAULT_SLAVE_ADDRESS 1U
#define MODBUS_RTU_MAX_ADU_SIZE          256U

uint16_t MODBUS_RTU_Crc16(const uint8_t *data, uint16_t length);
uint16_t MODBUS_RTU_HandleRequest(const uint8_t *request,
                                  uint16_t request_length,
                                  uint8_t *response,
                                  uint16_t response_capacity);

#ifdef __cplusplus
}
#endif

#endif /* MODBUS_RTU_H */
