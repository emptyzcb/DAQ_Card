#ifndef IO_LOGIC_ENGINE_H
#define IO_LOGIC_ENGINE_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

void IO_LOGIC_ENGINE_Init(void);
void IO_LOGIC_ENGINE_Step(uint16_t input_mask, uint32_t now_ms);
void IO_LOGIC_ENGINE_Stop(void);
uint16_t IO_LOGIC_ENGINE_GetOutputMask(void);

#ifdef __cplusplus
}
#endif

#endif /* IO_LOGIC_ENGINE_H */
