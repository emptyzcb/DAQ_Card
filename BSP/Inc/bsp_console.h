#ifndef BSP_CONSOLE_H
#define BSP_CONSOLE_H

#include <stdint.h>

void BSP_CONSOLE_Init(void);
int BSP_CONSOLE_Write(const uint8_t *data, uint16_t length);

#endif /* BSP_CONSOLE_H */
