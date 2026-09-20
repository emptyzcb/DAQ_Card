#ifndef MODBUS_REGISTER_MAP_H
#define MODBUS_REGISTER_MAP_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

int MODBUS_REGISTER_ReadHolding(uint16_t address, uint16_t *value);
int MODBUS_REGISTER_IsWritable(uint16_t address);
int MODBUS_REGISTER_IsValueValid(uint16_t address, uint16_t value);
int MODBUS_REGISTER_WriteHolding(uint16_t address, uint16_t value);

#ifdef __cplusplus
}
#endif

#endif /* MODBUS_REGISTER_MAP_H */
