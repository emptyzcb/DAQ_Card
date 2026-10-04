#include "bsp_pulse_counter.h"

#include "board_pins.h"

static TIM_HandleTypeDef pulse_timer_x1;
static TIM_HandleTypeDef pulse_timer_x2;

static int pulse_counter_configure_timer(TIM_HandleTypeDef *timer,
                                         TIM_TypeDef *instance,
                                         uint32_t trigger)
{
  TIM_SlaveConfigTypeDef slave = {0};

  timer->Instance = instance;
  timer->Init.Prescaler = 0U;
  timer->Init.CounterMode = TIM_COUNTERMODE_UP;
  timer->Init.Period = 0xFFFFU;
  timer->Init.ClockDivision = TIM_CLOCKDIVISION_DIV1;
  timer->Init.AutoReloadPreload = TIM_AUTORELOAD_PRELOAD_DISABLE;

  if (HAL_TIM_Base_Init(timer) != HAL_OK)
  {
    return 0;
  }

  /*
   * 外部有24 V输入时，光耦将MCU引脚由高拉低。使用下降沿计数，
   * 每个有效输入脉冲只增加一次计数。不开启数字滤波以保留最高带宽，
   * 干扰抑制由隔离输入电路和后续板上频率测试确认。
   */
  slave.SlaveMode = TIM_SLAVEMODE_EXTERNAL1;
  slave.InputTrigger = trigger;
  slave.TriggerPolarity = TIM_TRIGGERPOLARITY_FALLING;
  slave.TriggerPrescaler = TIM_TRIGGERPRESCALER_DIV1;
  slave.TriggerFilter = 0U;

  if (HAL_TIM_SlaveConfigSynchro(timer, &slave) != HAL_OK)
  {
    return 0;
  }

  __HAL_TIM_SET_COUNTER(timer, 0U);
  return (HAL_TIM_Base_Start(timer) == HAL_OK) ? 1 : 0;
}

int BSP_PULSE_COUNTER_Init(void)
{
  GPIO_InitTypeDef gpio = {0};

  __HAL_RCC_GPIOE_CLK_ENABLE();
  __HAL_RCC_GPIOB_CLK_ENABLE();
  __HAL_RCC_TIM1_CLK_ENABLE();
  __HAL_RCC_TIM4_CLK_ENABLE();

  gpio.Mode = GPIO_MODE_AF_PP;
  gpio.Pull = GPIO_PULLUP;
  gpio.Speed = GPIO_SPEED_FREQ_VERY_HIGH;

  gpio.Pin = BOARD_SWITCH_X1_Pin;
  gpio.Alternate = GPIO_AF1_TIM1;
  HAL_GPIO_Init(BOARD_SWITCH_X1_GPIO_Port, &gpio);

  gpio.Pin = BOARD_SWITCH_X2_Pin;
  gpio.Alternate = GPIO_AF2_TIM4;
  HAL_GPIO_Init(BOARD_SWITCH_X2_GPIO_Port, &gpio);

  if (pulse_counter_configure_timer(&pulse_timer_x1, TIM1, TIM_TS_TI1FP1) == 0)
  {
    return 0;
  }

  if (pulse_counter_configure_timer(&pulse_timer_x2, TIM4, TIM_TS_TI2FP2) == 0)
  {
    HAL_TIM_Base_Stop(&pulse_timer_x1);
    return 0;
  }

  return 1;
}

uint16_t BSP_PULSE_COUNTER_Read(BSP_PULSE_COUNTER_Channel channel)
{
  if (channel == BSP_PULSE_COUNTER_X1)
  {
    return (uint16_t)__HAL_TIM_GET_COUNTER(&pulse_timer_x1);
  }

  if (channel == BSP_PULSE_COUNTER_X2)
  {
    return (uint16_t)__HAL_TIM_GET_COUNTER(&pulse_timer_x2);
  }

  return 0U;
}
