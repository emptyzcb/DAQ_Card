#include "sys.h"

typedef struct
{
  GPIO_TypeDef *port;
  uint16_t pin;
} BSP_DIGITAL_IO_PinDef;

static const BSP_DIGITAL_IO_PinDef input_pins[BSP_DIGITAL_IO_INPUT_COUNT] = {
  /* 板上端子从左到右，对应原理图连接器P3从下到上。 */
  { BOARD_SWITCH_X4_GPIO_Port, BOARD_SWITCH_X4_Pin }, /* IN1 */
  { BOARD_SWITCH_X3_GPIO_Port, BOARD_SWITCH_X3_Pin }, /* IN2 */
  { BOARD_SWITCH_X2_GPIO_Port, BOARD_SWITCH_X2_Pin }, /* IN3 */
  { BOARD_SWITCH_X1_GPIO_Port, BOARD_SWITCH_X1_Pin }, /* IN4 */
  { BOARD_SWITCH_X8_GPIO_Port, BOARD_SWITCH_X8_Pin }, /* IN5 */
  { BOARD_SWITCH_X7_GPIO_Port, BOARD_SWITCH_X7_Pin }, /* IN6 */
  { BOARD_SWITCH_X6_GPIO_Port, BOARD_SWITCH_X6_Pin }, /* IN7 */
  { BOARD_SWITCH_X5_GPIO_Port, BOARD_SWITCH_X5_Pin }  /* IN8 */
};

static const BSP_DIGITAL_IO_PinDef output_pins[BSP_DIGITAL_IO_OUTPUT_COUNT] = {
  { BOARD_RELAY_OUT1_GPIO_Port, BOARD_RELAY_OUT1_Pin },
  { BOARD_RELAY_OUT2_GPIO_Port, BOARD_RELAY_OUT2_Pin },
  { BOARD_RELAY_OUT3_GPIO_Port, BOARD_RELAY_OUT3_Pin },
  { BOARD_RELAY_OUT4_GPIO_Port, BOARD_RELAY_OUT4_Pin },
  { BOARD_TRANSISTOR_OUT1_GPIO_Port, BOARD_TRANSISTOR_OUT1_Pin },
  { BOARD_TRANSISTOR_OUT2_GPIO_Port, BOARD_TRANSISTOR_OUT2_Pin },
  { BOARD_TRANSISTOR_OUT3_GPIO_Port, BOARD_TRANSISTOR_OUT3_Pin },
  { BOARD_TRANSISTOR_OUT4_GPIO_Port, BOARD_TRANSISTOR_OUT4_Pin }
};

/*
 * 板级电路真实有效电平：DO1~DO4 继电器低电平导通（低=打开）；
 * DO5~DO8 晶体管低边驱动，实测低电平导通（低=打开）。
 * 上层只使用“打开/关闭”逻辑语义，不应感知硬件电平差异。
 */
static const GPIO_PinState output_active_levels[BSP_DIGITAL_IO_OUTPUT_COUNT] = {
  GPIO_PIN_RESET,
  GPIO_PIN_RESET,
  GPIO_PIN_RESET,
  GPIO_PIN_RESET,
  GPIO_PIN_RESET,
  GPIO_PIN_RESET,
  GPIO_PIN_RESET,
  GPIO_PIN_RESET
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

static GPIO_PinState output_active_to_pin_state(BSP_DIGITAL_IO_Output output, int active)
{
  if (active)
  {
    return output_active_levels[output];
  }

  return (output_active_levels[output] == GPIO_PIN_SET) ? GPIO_PIN_RESET : GPIO_PIN_SET;
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

  /*
   * 先写入每路输出的关闭电平，再把引脚切换为输出模式。
   * 这个顺序可避免上电初始化过程中继电器或晶体管瞬间误动作。
   */
  for (uint32_t index = 0U; index < (uint32_t)BSP_DIGITAL_IO_OUTPUT_COUNT; index++)
  {
    digital_io_gpio_clock_enable(output_pins[index].port);
    HAL_GPIO_WritePin(output_pins[index].port,
                      output_pins[index].pin,
                      output_active_to_pin_state((BSP_DIGITAL_IO_Output)index, 0));
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
  /* 将统一的逻辑状态转换为该路硬件所需的真实高低电平。 */
  HAL_GPIO_WritePin(output_pins[output].port,
                    output_pins[output].pin,
                    output_active_to_pin_state(output, active));

  /* 缓存的是逻辑打开状态，而不是GPIO引脚的物理电平。 */
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
