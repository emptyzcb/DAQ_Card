#include "bsp_ad7606.h"

static BSP_AD7606_Range active_range = BSP_AD7606_RANGE_10V;

static void ad7606_delay_cycles(uint32_t cycles)
{
  for (volatile uint32_t i = 0U; i < cycles; i++)
  {
    __NOP();
  }
}

static void ad7606_write_pin(GPIO_TypeDef *port, uint16_t pin, GPIO_PinState state)
{
  HAL_GPIO_WritePin(port, pin, state);
}

static void ad7606_gpio_init(void)
{
  GPIO_InitTypeDef gpio = {0};

  __HAL_RCC_GPIOB_CLK_ENABLE();
  __HAL_RCC_GPIOD_CLK_ENABLE();
  __HAL_RCC_GPIOE_CLK_ENABLE();

  gpio.Pin = BSP_AD7606_DATA_MASK;
  gpio.Mode = GPIO_MODE_INPUT;
  gpio.Pull = GPIO_NOPULL;
  gpio.Speed = GPIO_SPEED_FREQ_VERY_HIGH;
  HAL_GPIO_Init(BSP_AD7606_DATA_GPIO_Port, &gpio);

  HAL_GPIO_WritePin(BSP_AD7606_CONVST_GPIO_Port,
                    BSP_AD7606_CONVST_Pin | BSP_AD7606_RD_Pin | BSP_AD7606_CS_Pin,
                    GPIO_PIN_SET);
  HAL_GPIO_WritePin(BSP_AD7606_RESET_GPIO_Port, BSP_AD7606_RESET_Pin, GPIO_PIN_RESET);
  HAL_GPIO_WritePin(BSP_AD7606_RANGE_GPIO_Port, BSP_AD7606_RANGE_Pin, GPIO_PIN_SET);
  HAL_GPIO_WritePin(BSP_AD7606_OS0_GPIO_Port, BSP_AD7606_OS0_Pin | BSP_AD7606_OS1_Pin, GPIO_PIN_RESET);
  HAL_GPIO_WritePin(BSP_AD7606_OS2_GPIO_Port, BSP_AD7606_OS2_Pin | BSP_AD7606_STBY_Pin, GPIO_PIN_RESET);

  gpio.Mode = GPIO_MODE_OUTPUT_PP;
  gpio.Pull = GPIO_NOPULL;
  gpio.Speed = GPIO_SPEED_FREQ_VERY_HIGH;

  gpio.Pin = BSP_AD7606_CONVST_Pin | BSP_AD7606_RD_Pin | BSP_AD7606_CS_Pin |
             BSP_AD7606_RESET_Pin | BSP_AD7606_RANGE_Pin |
             BSP_AD7606_OS0_Pin | BSP_AD7606_OS1_Pin;
  HAL_GPIO_Init(GPIOD, &gpio);

  gpio.Pin = BSP_AD7606_OS2_Pin | BSP_AD7606_STBY_Pin;
  HAL_GPIO_Init(GPIOB, &gpio);

  gpio.Pin = BSP_AD7606_BUSY_Pin;
  gpio.Mode = GPIO_MODE_INPUT;
  gpio.Pull = GPIO_NOPULL;
  gpio.Speed = GPIO_SPEED_FREQ_VERY_HIGH;
  HAL_GPIO_Init(BSP_AD7606_BUSY_GPIO_Port, &gpio);
}

static int ad7606_wait_busy_reset(uint32_t timeout_ms)
{
  uint32_t start = HAL_GetTick();

  while (HAL_GPIO_ReadPin(BSP_AD7606_BUSY_GPIO_Port, BSP_AD7606_BUSY_Pin) == GPIO_PIN_SET)
  {
    if ((HAL_GetTick() - start) >= timeout_ms)
    {
      return 0;
    }
  }

  return 1;
}

static int32_t ad7606_raw_to_mv(int16_t raw)
{
  return ((int32_t)raw * (int32_t)active_range) / 32768;
}

void BSP_AD7606_Reset(void)
{
  ad7606_write_pin(BSP_AD7606_RESET_GPIO_Port, BSP_AD7606_RESET_Pin, GPIO_PIN_SET);
  ad7606_delay_cycles(200U);
  ad7606_write_pin(BSP_AD7606_RESET_GPIO_Port, BSP_AD7606_RESET_Pin, GPIO_PIN_RESET);
  ad7606_delay_cycles(200U);
}

void BSP_AD7606_SetRange(BSP_AD7606_Range range)
{
  active_range = range;

  if (range == BSP_AD7606_RANGE_5V)
  {
    ad7606_write_pin(BSP_AD7606_RANGE_GPIO_Port, BSP_AD7606_RANGE_Pin, GPIO_PIN_RESET);
  }
  else
  {
    ad7606_write_pin(BSP_AD7606_RANGE_GPIO_Port, BSP_AD7606_RANGE_Pin, GPIO_PIN_SET);
  }
}

void BSP_AD7606_SetOversampling(BSP_AD7606_Oversampling oversampling)
{
  uint32_t os = (uint32_t)oversampling;

  ad7606_write_pin(BSP_AD7606_OS0_GPIO_Port, BSP_AD7606_OS0_Pin,
                   ((os & 0x01U) != 0U) ? GPIO_PIN_SET : GPIO_PIN_RESET);
  ad7606_write_pin(BSP_AD7606_OS1_GPIO_Port, BSP_AD7606_OS1_Pin,
                   ((os & 0x02U) != 0U) ? GPIO_PIN_SET : GPIO_PIN_RESET);
  ad7606_write_pin(BSP_AD7606_OS2_GPIO_Port, BSP_AD7606_OS2_Pin,
                   ((os & 0x04U) != 0U) ? GPIO_PIN_SET : GPIO_PIN_RESET);
}

void BSP_AD7606_Init(BSP_AD7606_Range range, BSP_AD7606_Oversampling oversampling)
{
  ad7606_gpio_init();

  ad7606_write_pin(BSP_AD7606_STBY_GPIO_Port, BSP_AD7606_STBY_Pin, GPIO_PIN_SET);
  ad7606_write_pin(BSP_AD7606_CS_GPIO_Port, BSP_AD7606_CS_Pin, GPIO_PIN_SET);
  ad7606_write_pin(BSP_AD7606_RD_GPIO_Port, BSP_AD7606_RD_Pin, GPIO_PIN_SET);
  ad7606_write_pin(BSP_AD7606_CONVST_GPIO_Port, BSP_AD7606_CONVST_Pin, GPIO_PIN_SET);

  BSP_AD7606_SetRange(range);
  BSP_AD7606_SetOversampling(oversampling);
  BSP_AD7606_Reset();
}

int BSP_AD7606_ReadSample(BSP_AD7606_Sample *sample, uint32_t timeout_ms)
{
  if (sample == 0)
  {
    return 0;
  }

  ad7606_write_pin(BSP_AD7606_CONVST_GPIO_Port, BSP_AD7606_CONVST_Pin, GPIO_PIN_RESET);
  ad7606_delay_cycles(20U);
  ad7606_write_pin(BSP_AD7606_CONVST_GPIO_Port, BSP_AD7606_CONVST_Pin, GPIO_PIN_SET);

  if (!ad7606_wait_busy_reset(timeout_ms))
  {
    return 0;
  }

  ad7606_write_pin(BSP_AD7606_CS_GPIO_Port, BSP_AD7606_CS_Pin, GPIO_PIN_RESET);
  ad7606_delay_cycles(5U);

  for (uint8_t channel = 0U; channel < BSP_AD7606_CHANNEL_COUNT; channel++)
  {
    uint16_t raw_u16;

    ad7606_write_pin(BSP_AD7606_RD_GPIO_Port, BSP_AD7606_RD_Pin, GPIO_PIN_RESET);
    ad7606_delay_cycles(5U);
    raw_u16 = (uint16_t)(BSP_AD7606_DATA_GPIO_Port->IDR & BSP_AD7606_DATA_MASK);
    ad7606_write_pin(BSP_AD7606_RD_GPIO_Port, BSP_AD7606_RD_Pin, GPIO_PIN_SET);
    ad7606_delay_cycles(5U);

    sample->raw[channel] = (int16_t)raw_u16;
    sample->mv[channel] = ad7606_raw_to_mv(sample->raw[channel]);
  }

  ad7606_write_pin(BSP_AD7606_CS_GPIO_Port, BSP_AD7606_CS_Pin, GPIO_PIN_SET);
  sample->timestamp_ms = HAL_GetTick();

  return 1;
}
