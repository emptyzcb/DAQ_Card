#include "sys.h"

#define DIGITAL_IO_DEBOUNCE_SAMPLE_COUNT 5U
#define DIGITAL_IO_RATE_WINDOW_MS        100U

static DIGITAL_IO_SERVICE_Diagnostics digital_io_diag;
static DIGITAL_IO_SERVICE_InputState digital_input_state;
static uint8_t normal_debounce_count[3];
static uint16_t pulse_previous_counter[2];
static uint32_t pulse_window_count[2];
static uint32_t pulse_window_start_ms;
static int32_t encoder_window_position;
static uint32_t encoder_window_start_ms;

static void digital_io_process_normal_inputs(uint16_t raw_mask)
{
  uint32_t channel;

  for (channel = 0U; channel < 3U; channel++)
  {
    uint16_t bit = (uint16_t)(1U << (channel + 2U)); /* X3~X5 -> bit2~bit4 */
    uint8_t raw_active = ((raw_mask & bit) != 0U) ? 1U : 0U;
    uint8_t stable_active = ((digital_input_state.normal_input_mask & bit) != 0U) ? 1U : 0U;

    if (raw_active == stable_active)
    {
      normal_debounce_count[channel] = 0U;
      continue;
    }

    if (normal_debounce_count[channel] < DIGITAL_IO_DEBOUNCE_SAMPLE_COUNT)
    {
      normal_debounce_count[channel]++;
    }

    if (normal_debounce_count[channel] >= DIGITAL_IO_DEBOUNCE_SAMPLE_COUNT)
    {
      if (raw_active != 0U)
      {
        digital_input_state.normal_input_mask |= bit;
      }
      else
      {
        digital_input_state.normal_input_mask &= (uint16_t)~bit;
      }
      normal_debounce_count[channel] = 0U;
    }
  }
}

static void digital_io_process_pulse_counters(uint32_t now_ms)
{
  uint32_t channel;

  if (digital_io_diag.pulse_counter_ready == 0U)
  {
    return;
  }

  for (channel = 0U; channel < 2U; channel++)
  {
    uint16_t current = BSP_PULSE_COUNTER_Read((BSP_PULSE_COUNTER_Channel)channel);
    uint16_t delta = (uint16_t)(current - pulse_previous_counter[channel]);

    pulse_previous_counter[channel] = current;
    digital_input_state.pulse_count[channel] += delta;
    pulse_window_count[channel] += delta;
  }

  if ((uint32_t)(now_ms - pulse_window_start_ms) >= DIGITAL_IO_RATE_WINDOW_MS)
  {
    uint32_t elapsed_ms = now_ms - pulse_window_start_ms;

    for (channel = 0U; channel < 2U; channel++)
    {
      digital_input_state.pulse_frequency_hz[channel] =
          (pulse_window_count[channel] * 1000U) / elapsed_ms;
      pulse_window_count[channel] = 0U;
    }
    pulse_window_start_ms = now_ms;
  }
}

static void digital_io_process_encoder(uint32_t now_ms)
{
  BSP_ENCODER_INPUT_Snapshot encoder;

  BSP_ENCODER_INPUT_GetSnapshot(&encoder);
  digital_input_state.encoder_position = encoder.position;
  digital_input_state.encoder_index_position = encoder.index_position;
  digital_input_state.encoder_direction = encoder.direction;
  digital_input_state.encoder_ab_state = encoder.ab_state;
  digital_input_state.encoder_index_count = encoder.index_count;
  digital_input_state.encoder_error_count = encoder.invalid_transition_count;

  if ((uint32_t)(now_ms - encoder_window_start_ms) >= DIGITAL_IO_RATE_WINDOW_MS)
  {
    uint32_t elapsed_ms = now_ms - encoder_window_start_ms;
    int32_t position_delta = encoder.position - encoder_window_position;

    digital_input_state.encoder_speed_cps =
        (int32_t)(((int64_t)position_delta * 1000) / (int32_t)elapsed_ms);
    encoder_window_position = encoder.position;
    encoder_window_start_ms = now_ms;
  }
}

void DIGITAL_IO_SERVICE_Init(void)
{
  uint16_t raw_mask;

  BSP_DIGITAL_IO_Init();
  digital_io_diag.pulse_counter_ready = (uint8_t)BSP_PULSE_COUNTER_Init();
  BSP_ENCODER_INPUT_Init();

  memset(&digital_input_state, 0, sizeof(digital_input_state));
  memset(normal_debounce_count, 0, sizeof(normal_debounce_count));
  memset(pulse_window_count, 0, sizeof(pulse_window_count));

  raw_mask = BSP_DIGITAL_IO_ReadInputMask();
  digital_input_state.normal_input_mask =
      (uint16_t)(raw_mask & DIGITAL_IO_SERVICE_NORMAL_INPUT_MASK);
  digital_input_state.input_mask = raw_mask;
  digital_input_state.output_mask = BSP_DIGITAL_IO_GetOutputMask();
  if (digital_io_diag.pulse_counter_ready != 0U)
  {
    digital_input_state.status_flags |= DIGITAL_IO_SERVICE_STATUS_PULSE_COUNTER_READY;
  }
  digital_input_state.timestamp_ms = HAL_GetTick();

  pulse_previous_counter[0] = BSP_PULSE_COUNTER_Read(BSP_PULSE_COUNTER_X1);
  pulse_previous_counter[1] = BSP_PULSE_COUNTER_Read(BSP_PULSE_COUNTER_X2);
  pulse_window_start_ms = digital_input_state.timestamp_ms;
  encoder_window_start_ms = digital_input_state.timestamp_ms;
  encoder_window_position = 0;

  digital_io_diag.initialized = 1U;
  digital_io_diag.input_mask = digital_input_state.input_mask;
  digital_io_diag.output_mask = digital_input_state.output_mask;
  digital_io_diag.scan_count = 0U;
  digital_io_diag.output_write_count = 0U;
  digital_io_diag.last_scan_tick = HAL_GetTick();
  digital_io_diag.last_output_tick = 0U;
}

void DIGITAL_IO_SERVICE_ProcessInputs(uint32_t now_ms)
{
  uint16_t raw_mask;

  if (digital_io_diag.initialized == 0U)
  {
    return;
  }

  raw_mask = BSP_DIGITAL_IO_ReadInputMask();
  digital_io_process_normal_inputs(raw_mask);
  digital_io_process_pulse_counters(now_ms);
  digital_io_process_encoder(now_ms);

  /* X1/X2和X6~X8保留当前物理状态，X3~X5使用消抖后的稳定状态。 */
  digital_input_state.input_mask =
      (uint16_t)((raw_mask & (DIGITAL_IO_SERVICE_PULSE_INPUT_MASK |
                              DIGITAL_IO_SERVICE_ENCODER_INPUT_MASK)) |
                 digital_input_state.normal_input_mask);
  digital_input_state.output_mask = BSP_DIGITAL_IO_GetOutputMask();
  digital_input_state.timestamp_ms = now_ms;

  digital_io_diag.input_mask = digital_input_state.input_mask;
  digital_io_diag.output_mask = digital_input_state.output_mask;
  digital_io_diag.scan_count++;
  digital_io_diag.last_scan_tick = now_ms;
}

void DIGITAL_IO_SERVICE_GetInputState(DIGITAL_IO_SERVICE_InputState *state)
{
  if (state == NULL)
  {
    return;
  }

  taskENTER_CRITICAL();
  *state = digital_input_state;
  taskEXIT_CRITICAL();
}

uint16_t DIGITAL_IO_SERVICE_ReadInputs(void)
{
  if (digital_io_diag.initialized == 0U)
  {
    return 0U;
  }

  return digital_input_state.input_mask;
}

int DIGITAL_IO_SERVICE_ReadInput(BSP_DIGITAL_IO_Input input)
{
  if ((digital_io_diag.initialized == 0U) ||
      ((uint32_t)input >= (uint32_t)BSP_DIGITAL_IO_INPUT_COUNT))
  {
    return 0;
  }

  return ((digital_input_state.input_mask & (uint16_t)(1U << (uint32_t)input)) != 0U) ? 1 : 0;
}

void DIGITAL_IO_SERVICE_SetOutput(BSP_DIGITAL_IO_Output output, int active)
{
  if (digital_io_diag.initialized == 0U)
  {
    return;
  }

  BSP_DIGITAL_IO_SetOutput(output, active);
  digital_io_diag.output_mask = BSP_DIGITAL_IO_GetOutputMask();
  digital_io_diag.output_write_count++;
  digital_io_diag.last_output_tick = HAL_GetTick();
}

void DIGITAL_IO_SERVICE_SetOutputMask(uint16_t active_mask)
{
  if (digital_io_diag.initialized == 0U)
  {
    return;
  }

  BSP_DIGITAL_IO_SetOutputMask(active_mask);
  digital_io_diag.output_mask = BSP_DIGITAL_IO_GetOutputMask();
  digital_io_diag.output_write_count++;
  digital_io_diag.last_output_tick = HAL_GetTick();
}

uint16_t DIGITAL_IO_SERVICE_GetOutputMask(void)
{
  if (digital_io_diag.initialized == 0U)
  {
    return 0U;
  }

  digital_io_diag.output_mask = BSP_DIGITAL_IO_GetOutputMask();
  return digital_io_diag.output_mask;
}

void DIGITAL_IO_SERVICE_AllOutputsOff(void)
{
  DIGITAL_IO_SERVICE_SetOutputMask(0U);
}

void DIGITAL_IO_SERVICE_GetDiagnostics(DIGITAL_IO_SERVICE_Diagnostics *diagnostics)
{
  if (diagnostics == NULL)
  {
    return;
  }

  digital_io_diag.input_mask = digital_input_state.input_mask;
  digital_io_diag.output_mask = DIGITAL_IO_SERVICE_GetOutputMask();
  *diagnostics = digital_io_diag;
}
