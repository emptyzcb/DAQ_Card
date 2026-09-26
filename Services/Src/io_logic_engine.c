#include "io_logic_engine.h"

#include <string.h>

#include "digital_io_service.h"
#include "io_config_storage.h"

typedef struct
{
  uint32_t generation;
  uint32_t delay_start[IO_CONFIG_MAX_RULE_COUNT];
  uint8_t delay_active[IO_CONFIG_MAX_RULE_COUNT];
  uint32_t pulse_until[8];
  uint16_t previous_input;
  uint16_t output_mask;
} IO_LOGIC_ENGINE_State;

static IO_LOGIC_ENGINE_State io_logic_state;

static uint16_t io_logic_read_u16(const uint8_t *data)
{
  return (uint16_t)(((uint16_t)data[0] << 8) | data[1]);
}

static uint32_t io_logic_read_u32(const uint8_t *data)
{
  return ((uint32_t)data[0] << 24) | ((uint32_t)data[1] << 16) |
         ((uint32_t)data[2] << 8) | data[3];
}

static uint8_t io_logic_condition_matches(const uint8_t *rule,
                                          uint16_t input_mask,
                                          uint16_t rising_mask,
                                          uint16_t falling_mask)
{
  uint16_t selected = io_logic_read_u16(&rule[2]);
  uint16_t expected = io_logic_read_u16(&rule[4]);
  uint16_t candidate = (rule[6] == 1U) ? rising_mask :
                       ((rule[6] == 2U) ? falling_mask : input_mask);
  uint16_t matches = (uint16_t)(~(candidate ^ expected) & selected);

  if (rule[1] == 1U)
  {
    return (matches != 0U) ? 1U : 0U;
  }
  return (matches == selected) ? 1U : 0U;
}

static uint8_t io_logic_time_phase_matches(const uint8_t *rule, uint32_t now_ms)
{
  uint16_t phase_count = io_logic_read_u16(&rule[2]);
  uint16_t phase_index = io_logic_read_u16(&rule[4]);
  uint16_t interval_ms = io_logic_read_u16(&rule[14]);

  if ((phase_count == 0U) || (phase_index >= phase_count) || (interval_ms == 0U))
  {
    return 0U;
  }

  return (uint16_t)((now_ms / interval_ms) % phase_count) == phase_index ? 1U : 0U;
}

static uint8_t io_logic_delay_ready(uint16_t rule_index, const uint8_t *rule,
                                    uint8_t matched, uint32_t now_ms)
{
  uint16_t delay_ms = io_logic_read_u16(&rule[12]);
  if (matched == 0U)
  {
    io_logic_state.delay_active[rule_index] = 0U;
    return 0U;
  }
  if (delay_ms == 0U)
  {
    return 1U;
  }
  if (io_logic_state.delay_active[rule_index] == 0U)
  {
    io_logic_state.delay_active[rule_index] = 1U;
    io_logic_state.delay_start[rule_index] = now_ms;
    return 0U;
  }
  return ((uint32_t)(now_ms - io_logic_state.delay_start[rule_index]) >= delay_ms) ? 1U : 0U;
}

static void io_logic_apply_rule(const uint8_t *rule, uint16_t *output_mask,
                                uint16_t *claimed_mask, uint32_t now_ms)
{
  uint16_t output_mask_from_rule = io_logic_read_u16(&rule[8]);
  uint16_t available = (uint16_t)(output_mask_from_rule & (uint16_t)~(*claimed_mask));
  uint16_t pulse_ms = io_logic_read_u16(&rule[14]);

  for (uint8_t bit = 0U; bit < 8U; bit++)
  {
    uint16_t bit_mask = (uint16_t)(1U << bit);
    if ((available & bit_mask) == 0U)
    {
      continue;
    }

    switch (rule[7])
    {
      case 0U:
        *output_mask |= bit_mask;
        break;
      case 1U:
        *output_mask &= (uint16_t)~bit_mask;
        break;
      case 2U:
        *output_mask ^= bit_mask;
        break;
      case 3U:
        *output_mask |= bit_mask;
        io_logic_state.pulse_until[bit] = now_ms + pulse_ms;
        break;
      default:
        break;
    }
    *claimed_mask |= bit_mask;
  }
}

void IO_LOGIC_ENGINE_Init(void)
{
  memset(&io_logic_state, 0, sizeof(io_logic_state));
  DIGITAL_IO_SERVICE_AllOutputsOff();
}

void IO_LOGIC_ENGINE_Step(uint16_t input_mask, uint32_t now_ms)
{
  uint32_t image_length;
  const uint8_t *image = IO_CONFIG_GetActiveImage(&image_length);
  uint32_t generation;
  uint16_t rule_count;
  uint16_t rising_mask;
  uint16_t falling_mask;
  uint16_t claimed_mask = 0U;
  uint16_t output_mask;

  if ((IO_CONFIG_IsRunning() == 0U) || (image_length < IO_CONFIG_HEADER_LENGTH))
  {
    IO_LOGIC_ENGINE_Stop();
    io_logic_state.previous_input = input_mask;
    return;
  }

  generation = io_logic_read_u32(&image[8]);
  if (generation != io_logic_state.generation)
  {
    memset(io_logic_state.delay_start, 0, sizeof(io_logic_state.delay_start));
    memset(io_logic_state.delay_active, 0, sizeof(io_logic_state.delay_active));
    memset(io_logic_state.pulse_until, 0, sizeof(io_logic_state.pulse_until));
    io_logic_state.generation = generation;
    io_logic_state.output_mask = 0U;
  }

  rising_mask = (uint16_t)(input_mask & (uint16_t)~io_logic_state.previous_input);
  falling_mask = (uint16_t)(io_logic_state.previous_input & (uint16_t)~input_mask);
  io_logic_state.previous_input = input_mask;
  output_mask = io_logic_state.output_mask;

  for (uint8_t bit = 0U; bit < 8U; bit++)
  {
    uint16_t bit_mask = (uint16_t)(1U << bit);
    if ((io_logic_state.pulse_until[bit] != 0U) &&
        ((int32_t)(now_ms - io_logic_state.pulse_until[bit]) >= 0))
    {
      output_mask &= (uint16_t)~bit_mask;
      io_logic_state.pulse_until[bit] = 0U;
    }
    else if (io_logic_state.pulse_until[bit] != 0U)
    {
      output_mask |= bit_mask;
      claimed_mask |= bit_mask;
    }
  }

  rule_count = io_logic_read_u16(&image[18]);
  if (rule_count > IO_CONFIG_MAX_RULE_COUNT) rule_count = IO_CONFIG_MAX_RULE_COUNT;
  for (uint16_t index = 0U; index < rule_count; index++)
  {
    const uint8_t *rule = &image[IO_CONFIG_HEADER_LENGTH + ((uint32_t)index * IO_CONFIG_RULE_LENGTH)];
    uint8_t matched;
    if (rule[17] == 0U) continue;
    if (rule[0] == 3U)
    {
      if (io_logic_time_phase_matches(rule, now_ms) != 0U)
      {
        io_logic_apply_rule(rule, &output_mask, &claimed_mask, now_ms);
      }
      continue;
    }
    matched = io_logic_condition_matches(rule, input_mask, rising_mask, falling_mask);
    if (io_logic_delay_ready(index, rule, matched, now_ms) != 0U)
    {
      io_logic_apply_rule(rule, &output_mask, &claimed_mask, now_ms);
    }
  }

  io_logic_state.output_mask = output_mask;
  DIGITAL_IO_SERVICE_SetOutputMask(output_mask);
}

void IO_LOGIC_ENGINE_Stop(void)
{
  io_logic_state.output_mask = 0U;
  memset(io_logic_state.pulse_until, 0, sizeof(io_logic_state.pulse_until));
  DIGITAL_IO_SERVICE_AllOutputsOff();
}

uint16_t IO_LOGIC_ENGINE_GetOutputMask(void)
{
  return io_logic_state.output_mask;
}
