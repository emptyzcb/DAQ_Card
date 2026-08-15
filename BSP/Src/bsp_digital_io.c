#include "sys.h"

typedef struct
{
  GPIO_TypeDef *port;
  uint16_t pin;
} BSP_DIGITAL_IO_PinDef;

static const BSP_DIGITAL_IO_PinDef input_pins[BSP_DIGITAL_IO_INPUT_COUNT] = {
  { BOARD_SWITCH_X1_GPIO_Port, BOARD_SWITCH_X1_Pin },
  { BOARD_SWITCH_X2_GPIO_Port, BOARD_SWITCH_X2_Pin },
  { BOARD_SWITCH_X3_GPIO_Port, BOARD_SWITCH_X3_Pin },
  { BOARD_SWITCH_X4_GPIO_Port, BOARD_SWITCH_X4_Pin },
  { BOARD_SWITCH_X5_GPIO_Port, BOARD_SWITCH_X5_Pin },
  { BOARD_SWITCH_X6_GPIO_Port, BOARD_SWITCH_X6_Pin },
  { BOARD_SWITCH_X7_GPIO_Port, BOARD_SWITCH_X7_Pin },
  { BOARD_SWITCH_X8_GPIO_Port, BOARD_SWITCH_X8_Pin }
};

static const BSP_DIGITAL_IO_PinDef output_pins[BSP_DIGITAL_IO_OUTPUT_COUNT] = {
  { BOARD_RELAY_OUT1_GPIO_Port, BOARD_RELAY_OUT1_Pin },
  { BOARD_RELAY_OUT2_GPIO_Port, BOARD_RELAY_OUT2_Pin },
  { BOARD_RELAY_OUT3_GPIO_Port, BOARD_RELAY_OUT3_Pin },
  { BOARD_RELAY_OUT4_GPIO_Port, BOARD_RELAY_OUT4_Pin },
  { BOARD_TRANSISTOR_OUT1_GPIO_Port, BOARD_TRANSISTOR_OUT1_Pin },
  { BOARD_TRANSISTOR_OUT2_GPIO_Port, BOARD_TRANSISTOR_OUT2_Pin },
  { BOARD_TRANSISTOR_OUT3_GPIO_Port, BOARD_TRANSISTOR_OUT3_Pin },
  { BOARD_TRANSISTOR_OUT4_GPIO_Port, BOARD_TRANSISTOR_OUT4_Pin },
  { BOARD_DO_I_MCU_GPIO_Port, BOARD_DO_I_MCU_Pin },
  { BOARD_DO_U_MCU_GPIO_Port, BOARD_DO_U_MCU_Pin }
};

static uint16_t output_state_mask;

static void digital_io_gpio_clock_enable(GPIO_TypeDef *port)
{
  if (port == GPIOA) { __HAL_RCC_GPIOA_CLK_ENABLE(); }
  else if (port == GPIOB) { __HAL_RCC_GPIOB_CLK_ENABLE(); }
  else if (port == GPIOC) { __HAL_RCC_GPIOC_CLK_ENABLE(); }
  else if (port == GPIOD) { __HAL_RCC_GPIOD_CLK_ENABLE(); }
  else if (port == GPIOE) { __HAL_RCC_GPIOE_CLK_ENABLE(); }
#ifdef GPIOF
  else if (port == GPIOF) { __HAL_RCC_GPIOF_CLK_ENABLE(); }
#endif
#ifdef GPIOG
  else if (port == GPIOG) { __HAL_RCC_GPIOG_CLK_ENABLE(); }
#endif
#ifdef GPIOH
  else if (port == GPIOH) { __HAL_RCC_GPIOH_CLK_ENABLE(); }
#endif
}

static GPIO_PinState output_active_to_pin_state(int active)
{
  if (active)
  {
    return BSP_DIGITAL_IO_OUTPUT_ACTIVE_LEVEL;
  }

  return (BSP_DIGITAL_IO_OUTPUT_ACTIVE_LEVEL == GPIO_PIN_SET) ? GPIO_PIN_RESET : GPIO_PIN_SET;
}

static int is_valid_input(BSP_DIGITAL_IO_Input input)
{
  return ((uint32_t)input < (uint32_t)BSP_DIGITAL_IO_INPUT_COUNT);
}

static int is_valid_output(BSP_DIGITAL_IO_Output output)
{
  return ((uint32_t)output < (uint32_t)BSP_DIGITAL_IO_OUTPUT_COUNT);
}

void BSP_DIGITAL_IO_Init(void)
{
  GPIO_InitTypeDef gpio = {0};

  for (uint32_t index = 0U; index < (uint32_t)BSP_DIGITAL_IO_OUTPUT_COUNT; index++)
  {
    digital_io_gpio_clock_enable(output_pins[index].port);
    HAL_GPIO_WritePin(output_pins[index].port,
                      output_pins[index].pin,
                      output_active_to_pin_state(0));
  }

  gpio.Mode = GPIO_MODE_OUTPUT_PP;
  gpio.Pull = GPIO_NOPULL;
  gpio.Speed = GPIO_SPEED_FREQ_LOW;
  for (uint32_t index = 0U; index < (uint32_t)BSP_DIGITAL_IO_OUTPUT_COUNT; index++)
  {
    gpio.Pin = output_pins[index].pin;
    HAL_GPIO_Init(output_pins[index].port, &gpio);
  }

  gpio.Mode = GPIO_MODE_INPUT;
  gpio.Pull = GPIO_PULLUP;
  gpio.Speed = GPIO_SPEED_FREQ_LOW;
  for (uint32_t index = 0U; index < (uint32_t)BSP_DIGITAL_IO_INPUT_COUNT; index++)
  {
    digital_io_gpio_clock_enable(input_pins[index].port);
    gpio.Pin = input_pins[index].pin;
    HAL_GPIO_Init(input_pins[index].port, &gpio);
  }

  output_state_mask = 0U;
}

GPIO_PinState BSP_DIGITAL_IO_ReadInputRaw(BSP_DIGITAL_IO_Input input)
{
  if (!is_valid_input(input))
  {
    return GPIO_PIN_RESET;
  }

  return HAL_GPIO_ReadPin(input_pins[input].port, input_pins[input].pin);
}

int BSP_DIGITAL_IO_ReadInputActive(BSP_DIGITAL_IO_Input input)
{
  if (!is_valid_input(input))
  {
    return 0;
  }

  return (BSP_DIGITAL_IO_ReadInputRaw(input) == BSP_DIGITAL_IO_INPUT_ACTIVE_LEVEL);
}

uint16_t BSP_DIGITAL_IO_ReadInputMask(void)
{
  uint16_t mask = 0U;

  for (uint32_t index = 0U; index < (uint32_t)BSP_DIGITAL_IO_INPUT_COUNT; index++)
  {
    if (BSP_DIGITAL_IO_ReadInputActive((BSP_DIGITAL_IO_Input)index))
    {
      mask |= (uint16_t)(1U << index);
    }
  }

  return mask;
}

void BSP_DIGITAL_IO_SetOutput(BSP_DIGITAL_IO_Output output, int active)
{
  uint16_t bit;

  if (!is_valid_output(output))
  {
    return;
  }

  bit = (uint16_t)(1U << (uint32_t)output);
  HAL_GPIO_WritePin(output_pins[output].port,
                    output_pins[output].pin,
                    output_active_to_pin_state(active));

  if (active)
  {
    output_state_mask |= bit;
  }
  else
  {
    output_state_mask &= (uint16_t)(~bit);
  }
}

int BSP_DIGITAL_IO_GetOutput(BSP_DIGITAL_IO_Output output)
{
  if (!is_valid_output(output))
  {
    return 0;
  }

  return ((output_state_mask & (uint16_t)(1U << (uint32_t)output)) != 0U);
}

void BSP_DIGITAL_IO_SetOutputMask(uint16_t active_mask)
{
  uint16_t valid_mask = (uint16_t)((1U << (uint32_t)BSP_DIGITAL_IO_OUTPUT_COUNT) - 1U);

  active_mask &= valid_mask;
  for (uint32_t index = 0U; index < (uint32_t)BSP_DIGITAL_IO_OUTPUT_COUNT; index++)
  {
    BSP_DIGITAL_IO_SetOutput((BSP_DIGITAL_IO_Output)index,
                             ((active_mask & (uint16_t)(1U << index)) != 0U));
  }
}

uint16_t BSP_DIGITAL_IO_GetOutputMask(void)
{
  return output_state_mask;
}

void BSP_DIGITAL_IO_AllOutputsOff(void)
{
  BSP_DIGITAL_IO_SetOutputMask(0U);
}
