#ifndef DIGITAL_IO_SERVICE_H
#define DIGITAL_IO_SERVICE_H

#include <stdint.h>

#include "bsp_digital_io.h"

#define DIGITAL_IO_SERVICE_INPUT_COUNT  BSP_DIGITAL_IO_INPUT_COUNT
#define DIGITAL_IO_SERVICE_OUTPUT_COUNT BSP_DIGITAL_IO_OUTPUT_COUNT

#define DIGITAL_IO_SERVICE_NORMAL_INPUT_MASK 0x001CU /* X3~X5 */
#define DIGITAL_IO_SERVICE_PULSE_INPUT_MASK  0x0003U /* X1~X2 */
#define DIGITAL_IO_SERVICE_ENCODER_INPUT_MASK 0x00E0U /* X6~X8 */

typedef struct
{
  uint16_t input_mask;
  uint16_t normal_input_mask;
  uint16_t output_mask;
  uint16_t status_flags;
  uint64_t pulse_count[2];
  uint32_t pulse_frequency_hz[2];
  int32_t encoder_position;
  int32_t encoder_speed_cps;
  int32_t encoder_index_position;
  int8_t encoder_direction;
  uint8_t encoder_ab_state;
  uint32_t encoder_index_count;
  uint32_t encoder_error_count;
  uint32_t timestamp_ms;
} DIGITAL_IO_SERVICE_InputState;

#define DIGITAL_IO_SERVICE_STATUS_PULSE_COUNTER_READY 0x0001U

typedef struct
{
  uint8_t initialized;
  uint8_t pulse_counter_ready;
  uint16_t input_mask;
  uint16_t output_mask;
  uint32_t scan_count;
  uint32_t output_write_count;
  uint32_t last_scan_tick;
  uint32_t last_output_tick;
} DIGITAL_IO_SERVICE_Diagnostics;

void DIGITAL_IO_SERVICE_Init(void);
void DIGITAL_IO_SERVICE_ProcessInputs(uint32_t now_ms);
void DIGITAL_IO_SERVICE_GetInputState(DIGITAL_IO_SERVICE_InputState *state);
uint16_t DIGITAL_IO_SERVICE_ReadInputs(void);
int DIGITAL_IO_SERVICE_ReadInput(BSP_DIGITAL_IO_Input input);
void DIGITAL_IO_SERVICE_SetOutput(BSP_DIGITAL_IO_Output output, int active);
void DIGITAL_IO_SERVICE_SetOutputMask(uint16_t active_mask);
uint16_t DIGITAL_IO_SERVICE_GetOutputMask(void);
void DIGITAL_IO_SERVICE_AllOutputsOff(void);
void DIGITAL_IO_SERVICE_GetDiagnostics(DIGITAL_IO_SERVICE_Diagnostics *diagnostics);

#endif /* DIGITAL_IO_SERVICE_H */
