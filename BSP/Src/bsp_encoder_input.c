#include "bsp_encoder_input.h"

#include "board_pins.h"

typedef struct
{
  volatile int32_t position;
  volatile int32_t index_position;
  volatile int8_t direction;
  volatile uint8_t previous_ab;
  volatile uint32_t index_count;
  volatile uint32_t invalid_transition_count;
} BSP_ENCODER_INPUT_State;

static BSP_ENCODER_INPUT_State encoder_state;

/*
 * 下标由“上一AB状态 << 2 | 当前AB状态”组成。
 * +1/-1表示一次合法的正/反向四倍频计数，0表示无变化或非法跳变。
 */
static const int8_t quadrature_delta[16] = {
   0,  1, -1,  0,
  -1,  0,  0,  1,
   1,  0,  0, -1,
   0, -1,  1,  0
};

static uint8_t encoder_read_active(GPIO_TypeDef *port, uint16_t pin)
{
  return (HAL_GPIO_ReadPin(port, pin) == GPIO_PIN_RESET) ? 1U : 0U;
}

static uint8_t encoder_read_ab(void)
{
  uint8_t a = encoder_read_active(BOARD_SWITCH_X6_GPIO_Port, BOARD_SWITCH_X6_Pin);
  uint8_t b = encoder_read_active(BOARD_SWITCH_X7_GPIO_Port, BOARD_SWITCH_X7_Pin);

  return (uint8_t)((a << 1) | b);
}

void BSP_ENCODER_INPUT_Init(void)
{
  GPIO_InitTypeDef gpio = {0};

  __HAL_RCC_GPIOB_CLK_ENABLE();
  __HAL_RCC_GPIOE_CLK_ENABLE();
  __HAL_RCC_SYSCFG_CLK_ENABLE();

  gpio.Mode = GPIO_MODE_IT_RISING_FALLING;
  gpio.Pull = GPIO_PULLUP;
  gpio.Speed = GPIO_SPEED_FREQ_VERY_HIGH;

  gpio.Pin = BOARD_SWITCH_X6_Pin | BOARD_SWITCH_X7_Pin;
  HAL_GPIO_Init(GPIOB, &gpio);

  gpio.Pin = BOARD_SWITCH_X8_Pin;
  HAL_GPIO_Init(BOARD_SWITCH_X8_GPIO_Port, &gpio);

  encoder_state.position = 0;
  encoder_state.index_position = 0;
  encoder_state.direction = 0;
  encoder_state.previous_ab = encoder_read_ab();
  encoder_state.index_count = 0U;
  encoder_state.invalid_transition_count = 0U;

  __HAL_GPIO_EXTI_CLEAR_IT(BOARD_SWITCH_X6_Pin);
  __HAL_GPIO_EXTI_CLEAR_IT(BOARD_SWITCH_X7_Pin);
  __HAL_GPIO_EXTI_CLEAR_IT(BOARD_SWITCH_X8_Pin);

  /* 中断不调用RTOS API，优先级6保留了足够的系统中断屏蔽裕量。 */
  HAL_NVIC_SetPriority(EXTI0_IRQn, 6U, 0U);
  HAL_NVIC_SetPriority(EXTI1_IRQn, 6U, 0U);
  HAL_NVIC_SetPriority(EXTI9_5_IRQn, 6U, 0U);
  HAL_NVIC_EnableIRQ(EXTI0_IRQn);
  HAL_NVIC_EnableIRQ(EXTI1_IRQn);
  HAL_NVIC_EnableIRQ(EXTI9_5_IRQn);
}

void BSP_ENCODER_INPUT_HandleExti(uint16_t gpio_pin)
{
  if ((gpio_pin == BOARD_SWITCH_X6_Pin) || (gpio_pin == BOARD_SWITCH_X7_Pin))
  {
    uint8_t current_ab = encoder_read_ab();
    uint8_t previous_ab = encoder_state.previous_ab;
    int8_t delta = quadrature_delta[(previous_ab << 2) | current_ab];

    if (current_ab != previous_ab)
    {
      if (delta != 0)
      {
        encoder_state.position += delta;
        encoder_state.direction = (delta > 0) ? 1 : -1;
      }
      else
      {
        encoder_state.invalid_transition_count++;
      }
      encoder_state.previous_ab = current_ab;
    }
  }
  else if (gpio_pin == BOARD_SWITCH_X8_Pin)
  {
    /* Z相为低有效，只在下降沿到达后记录一次索引。 */
    if (encoder_read_active(BOARD_SWITCH_X8_GPIO_Port, BOARD_SWITCH_X8_Pin) != 0U)
    {
      encoder_state.index_position = encoder_state.position;
      encoder_state.index_count++;
    }
  }
}

void BSP_ENCODER_INPUT_GetSnapshot(BSP_ENCODER_INPUT_Snapshot *snapshot)
{
  uint32_t primask;

  if (snapshot == NULL)
  {
    return;
  }

  primask = __get_PRIMASK();
  __disable_irq();
  snapshot->position = encoder_state.position;
  snapshot->index_position = encoder_state.index_position;
  snapshot->direction = encoder_state.direction;
  snapshot->ab_state = encoder_state.previous_ab;
  snapshot->index_count = encoder_state.index_count;
  snapshot->invalid_transition_count = encoder_state.invalid_transition_count;
  __set_PRIMASK(primask);
}

void BSP_ENCODER_INPUT_ResetPosition(void)
{
  uint32_t primask = __get_PRIMASK();

  __disable_irq();
  encoder_state.position = 0;
  encoder_state.index_position = 0;
  encoder_state.direction = 0;
  __set_PRIMASK(primask);
}
