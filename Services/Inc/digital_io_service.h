#ifndef DIGITAL_IO_SERVICE_H
#define DIGITAL_IO_SERVICE_H

#include <stdint.h>

#include "bsp_digital_io.h"

#define DIGITAL_IO_SERVICE_INPUT_COUNT  BSP_DIGITAL_IO_INPUT_COUNT
#define DIGITAL_IO_SERVICE_OUTPUT_COUNT BSP_DIGITAL_IO_OUTPUT_COUNT

typedef struct
{
  uint8_t initialized;
  uint16_t input_mask;
  uint16_t output_mask;
  uint32_t scan_count;
  uint32_t output_write_count;
  uint32_t last_scan_tick;
  uint32_t last_output_tick;
} DIGITAL_IO_SERVICE_Diagnostics;

void DIGITAL_IO_SERVICE_Init(void);
uint16_t DIGITAL_IO_SERVICE_ReadInputs(void);
int DIGITAL_IO_SERVICE_ReadInput(BSP_DIGITAL_IO_Input input);
void DIGITAL_IO_SERVICE_SetOutput(BSP_DIGITAL_IO_Output output, int active);
void DIGITAL_IO_SERVICE_SetOutputMask(uint16_t active_mask);
uint16_t DIGITAL_IO_SERVICE_GetOutputMask(void);
void DIGITAL_IO_SERVICE_AllOutputsOff(void);
void DIGITAL_IO_SERVICE_GetDiagnostics(DIGITAL_IO_SERVICE_Diagnostics *diagnostics);

#endif /* DIGITAL_IO_SERVICE_H */
