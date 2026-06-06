#include "sys.h"

void BSP_LED_Init(void)
{
  GPIO_InitTypeDef gpio = {0};

  if (BSP_LED_GPIO_Port == GPIOA) { __HAL_RCC_GPIOA_CLK_ENABLE(); }
  else if (BSP_LED_GPIO_Port == GPIOB) { __HAL_RCC_GPIOB_CLK_ENABLE(); }
  else if (BSP_LED_GPIO_Port == GPIOC) { __HAL_RCC_GPIOC_CLK_ENABLE(); }
  else if (BSP_LED_GPIO_Port == GPIOD) { __HAL_RCC_GPIOD_CLK_ENABLE(); }
  else if (BSP_LED_GPIO_Port == GPIOE) { __HAL_RCC_GPIOE_CLK_ENABLE(); }

  HAL_GPIO_WritePin(BSP_LED_GPIO_Port, BSP_LED_Pin, GPIO_PIN_RESET);

  gpio.Pin   = BSP_LED_Pin;
  gpio.Mode  = GPIO_MODE_OUTPUT_PP;
  gpio.Pull  = GPIO_NOPULL;
  gpio.Speed = GPIO_SPEED_FREQ_LOW;
  HAL_GPIO_Init(BSP_LED_GPIO_Port, &gpio);
}

void BSP_LED_On(void)
{
  HAL_GPIO_WritePin(BSP_LED_GPIO_Port, BSP_LED_Pin, GPIO_PIN_SET);
}

void BSP_LED_Off(void)
{
  HAL_GPIO_WritePin(BSP_LED_GPIO_Port, BSP_LED_Pin, GPIO_PIN_RESET);
}

void BSP_LED_Toggle(void)
{
  HAL_GPIO_TogglePin(BSP_LED_GPIO_Port, BSP_LED_Pin);
}
