#include "bsp_ad7606.h"

static BSP_AD7606_Range active_range = BSP_AD7606_RANGE_10V;

static void ad7606_delay_cycles(uint32_t cycles)
{
  volatile uint32_t index;
  for (index = 0U; index < cycles; index++)
  {
    __NOP();
  }
}

static void ad7606_write(GPIO_TypeDef *port, uint16_t pin, GPIO_PinState state)
{
  HAL_GPIO_WritePin(port, pin, state);
}

static void ad7606_gpio_init(void)
{
  GPIO_InitTypeDef gpio = {0};

  __HAL_RCC_GPIOD_CLK_ENABLE();
  __HAL_RCC_GPIOE_CLK_ENABLE();

  ad7606_write(BSP_AD7606_CONVST_GPIO_Port, BSP_AD7606_CONVST_Pin, GPIO_PIN_SET);
  ad7606_write(BSP_AD7606_RESET_GPIO_Port, BSP_AD7606_RESET_Pin, GPIO_PIN_RESET);
  ad7606_write(BSP_AD7606_SCLK_GPIO_Port, BSP_AD7606_SCLK_Pin, GPIO_PIN_RESET);
  ad7606_write(BSP_AD7606_CS_GPIO_Port, BSP_AD7606_CS_Pin, GPIO_PIN_SET);

  gpio.Pin = BSP_AD7606_OS0_Pin | BSP_AD7606_OS1_Pin | BSP_AD7606_OS2_Pin |
             BSP_AD7606_RANGE_Pin | BSP_AD7606_CONVST_Pin | BSP_AD7606_RESET_Pin;
  gpio.Mode = GPIO_MODE_OUTPUT_PP;
  gpio.Pull = GPIO_NOPULL;
  gpio.Speed = GPIO_SPEED_FREQ_VERY_HIGH;
  HAL_GPIO_Init(GPIOD, &gpio);

  gpio.Pin = BSP_AD7606_SCLK_Pin | BSP_AD7606_CS_Pin;
  HAL_GPIO_Init(GPIOE, &gpio);

  gpio.Pin = BSP_AD7606_DOUTA_Pin | BSP_AD7606_DOUTB_Pin | BSP_AD7606_BUSY_Pin;
  gpio.Mode = GPIO_MODE_INPUT;
  gpio.Pull = GPIO_NOPULL;
  HAL_GPIO_Init(GPIOE, &gpio);
}

static int ad7606_wait_busy(GPIO_PinState state, uint32_t timeout_ms)
{
  uint32_t start = HAL_GetTick();

  while (HAL_GPIO_ReadPin(BSP_AD7606_BUSY_GPIO_Port, BSP_AD7606_BUSY_Pin) != state)
  {
    if ((HAL_GetTick() - start) >= timeout_ms)
    {
      return 0;
    }
  }
  return 1;
}

static void ad7606_start_conversion(void)
{
  ad7606_write(BSP_AD7606_CONVST_GPIO_Port, BSP_AD7606_CONVST_Pin, GPIO_PIN_RESET);
  ad7606_delay_cycles(4000U);
  ad7606_write(BSP_AD7606_CONVST_GPIO_Port, BSP_AD7606_CONVST_Pin, GPIO_PIN_SET);
}

static void ad7606_read_serial(int16_t raw[BSP_AD7606_CHANNEL_COUNT])
{
  uint16_t dout_a[4] = {0U};
  uint16_t dout_b[4] = {0U};
  uint32_t bit;

  ad7606_write(BSP_AD7606_SCLK_GPIO_Port, BSP_AD7606_SCLK_Pin, GPIO_PIN_RESET);
  ad7606_write(BSP_AD7606_CS_GPIO_Port, BSP_AD7606_CS_Pin, GPIO_PIN_RESET);
  ad7606_delay_cycles(4U);

  for (bit = 0U; bit < 64U; bit++)
  {
    uint32_t word = bit / 16U;

    ad7606_write(BSP_AD7606_SCLK_GPIO_Port, BSP_AD7606_SCLK_Pin, GPIO_PIN_SET);
    ad7606_delay_cycles(4U);

    dout_a[word] = (uint16_t)((dout_a[word] << 1) |
      (HAL_GPIO_ReadPin(BSP_AD7606_DOUTA_GPIO_Port, BSP_AD7606_DOUTA_Pin) == GPIO_PIN_SET));
    dout_b[word] = (uint16_t)((dout_b[word] << 1) |
      (HAL_GPIO_ReadPin(BSP_AD7606_DOUTB_GPIO_Port, BSP_AD7606_DOUTB_Pin) == GPIO_PIN_SET));

    ad7606_write(BSP_AD7606_SCLK_GPIO_Port, BSP_AD7606_SCLK_Pin, GPIO_PIN_RESET);
    ad7606_delay_cycles(4U);
  }

  ad7606_write(BSP_AD7606_CS_GPIO_Port, BSP_AD7606_CS_Pin, GPIO_PIN_SET);
  for (bit = 0U; bit < 4U; bit++)
  {
    raw[bit] = (int16_t)dout_a[bit];
    raw[bit + 4U] = (int16_t)dout_b[bit];
  }
}

static void ad7606_finish_sample(BSP_AD7606_Sample *sample)
{
  uint32_t channel;
  for (channel = 0U; channel < BSP_AD7606_CHANNEL_COUNT; channel++)
  {
    sample->mv[channel] = ((int32_t)sample->raw[channel] * (int32_t)active_range) / 32768;
  }
  sample->timestamp_ms = HAL_GetTick();
}

void BSP_AD7606_Reset(void)
{
  ad7606_write(BSP_AD7606_RESET_GPIO_Port, BSP_AD7606_RESET_Pin, GPIO_PIN_SET);
  ad7606_delay_cycles(200U);
  ad7606_write(BSP_AD7606_RESET_GPIO_Port, BSP_AD7606_RESET_Pin, GPIO_PIN_RESET);
  HAL_Delay(1U);
}

void BSP_AD7606_SetRange(BSP_AD7606_Range range)
{
  active_range = range;
  ad7606_write(BSP_AD7606_RANGE_GPIO_Port, BSP_AD7606_RANGE_Pin,
               (range == BSP_AD7606_RANGE_5V) ? GPIO_PIN_RESET : GPIO_PIN_SET);
}

void BSP_AD7606_SetOversampling(BSP_AD7606_Oversampling oversampling)
{
  uint32_t os = (uint32_t)oversampling;
  ad7606_write(BSP_AD7606_OS0_GPIO_Port, BSP_AD7606_OS0_Pin,
               (os & 1U) ? GPIO_PIN_SET : GPIO_PIN_RESET);
  ad7606_write(BSP_AD7606_OS1_GPIO_Port, BSP_AD7606_OS1_Pin,
               (os & 2U) ? GPIO_PIN_SET : GPIO_PIN_RESET);
  ad7606_write(BSP_AD7606_OS2_GPIO_Port, BSP_AD7606_OS2_Pin,
               (os & 4U) ? GPIO_PIN_SET : GPIO_PIN_RESET);
}

void BSP_AD7606_Init(BSP_AD7606_Range range, BSP_AD7606_Oversampling oversampling)
{
  ad7606_gpio_init();
  HAL_Delay(10U);
  BSP_AD7606_SetRange(range);
  BSP_AD7606_SetOversampling(oversampling);
  BSP_AD7606_Reset();
}

int BSP_AD7606_ReadSample(BSP_AD7606_Sample *sample, uint32_t timeout_ms)
{
  if ((sample == 0) ||
      (HAL_GPIO_ReadPin(BSP_AD7606_BUSY_GPIO_Port, BSP_AD7606_BUSY_Pin) == GPIO_PIN_SET))
  {
    return 0;
  }

  ad7606_start_conversion();
  if (!ad7606_wait_busy(GPIO_PIN_SET, timeout_ms) ||
      !ad7606_wait_busy(GPIO_PIN_RESET, timeout_ms))
  {
    return 0;
  }

  ad7606_read_serial(sample->raw);
  ad7606_finish_sample(sample);
  return 1;
}

BSP_AD7606_HealthResult BSP_AD7606_CheckHealth(BSP_AD7606_Sample *sample,
                                               uint32_t timeout_ms)
{
  uint32_t channel;
  uint32_t suspicious = 0U;
  uint32_t attempt;

  if (sample == 0)
  {
    return BSP_AD7606_HEALTH_DATA_SUSPICIOUS;
  }
  if (HAL_GPIO_ReadPin(BSP_AD7606_BUSY_GPIO_Port, BSP_AD7606_BUSY_Pin) == GPIO_PIN_SET)
  {
    return BSP_AD7606_HEALTH_BUSY_STUCK_HIGH;
  }

  for (attempt = 0U; attempt < 3U; attempt++)
  {
    ad7606_start_conversion();
    if (ad7606_wait_busy(GPIO_PIN_SET, timeout_ms))
    {
      break;
    }
    HAL_Delay(1U);
  }
  if (attempt == 3U)
  {
    return BSP_AD7606_HEALTH_BUSY_DID_NOT_ASSERT;
  }
  if (!ad7606_wait_busy(GPIO_PIN_RESET, timeout_ms))
  {
    return BSP_AD7606_HEALTH_BUSY_TIMEOUT;
  }

  ad7606_read_serial(sample->raw);
  ad7606_finish_sample(sample);
  for (channel = 0U; channel < BSP_AD7606_CHANNEL_COUNT; channel++)
  {
    if ((sample->raw[channel] == 0) || (sample->raw[channel] == (int16_t)-1))
    {
      suspicious++;
    }
  }
  return (suspicious == BSP_AD7606_CHANNEL_COUNT) ?
         BSP_AD7606_HEALTH_DATA_SUSPICIOUS : BSP_AD7606_HEALTH_OK;
}
