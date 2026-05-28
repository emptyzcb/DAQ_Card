#include "bsp_imu660rc.h"

#define IMU660RC_FUNC_CFG_ACCESS 0x01U
#define IMU660RC_CHIP_ID         0x0FU
#define IMU660RC_CTRL2           0x11U
#define IMU660RC_CTRL3           0x12U
#define IMU660RC_CTRL6           0x15U
#define IMU660RC_CTRL7           0x16U
#define IMU660RC_OUTX_L_G        0x22U

#define IMU660RC_SPI_READ        0x80U
#define IMU660RC_CHIP_ID_VALUE   0x70U
#define IMU660RC_SELF_CHECK_TRY  255U

static float gyro_factor = 14.2857f;

static void imu_gpio_clock_enable(GPIO_TypeDef *port)
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

static void imu_gpio_init(void)
{
  GPIO_InitTypeDef gpio = {0};

  imu_gpio_clock_enable(BSP_IMU660RC_SCK_GPIO_Port);
  imu_gpio_clock_enable(BSP_IMU660RC_MOSI_GPIO_Port);
  imu_gpio_clock_enable(BSP_IMU660RC_MISO_GPIO_Port);
  imu_gpio_clock_enable(BSP_IMU660RC_CS_GPIO_Port);

  HAL_GPIO_WritePin(BSP_IMU660RC_CS_GPIO_Port, BSP_IMU660RC_CS_Pin, GPIO_PIN_SET);
  HAL_GPIO_WritePin(BSP_IMU660RC_SCK_GPIO_Port, BSP_IMU660RC_SCK_Pin, GPIO_PIN_RESET);
  HAL_GPIO_WritePin(BSP_IMU660RC_MOSI_GPIO_Port, BSP_IMU660RC_MOSI_Pin, GPIO_PIN_RESET);

  gpio.Mode = GPIO_MODE_OUTPUT_PP;
  gpio.Pull = GPIO_NOPULL;
  gpio.Speed = GPIO_SPEED_FREQ_VERY_HIGH;
  gpio.Pin = BSP_IMU660RC_SCK_Pin;
  HAL_GPIO_Init(BSP_IMU660RC_SCK_GPIO_Port, &gpio);
  gpio.Pin = BSP_IMU660RC_MOSI_Pin;
  HAL_GPIO_Init(BSP_IMU660RC_MOSI_GPIO_Port, &gpio);
  gpio.Pin = BSP_IMU660RC_CS_Pin;
  HAL_GPIO_Init(BSP_IMU660RC_CS_GPIO_Port, &gpio);

  gpio.Mode = GPIO_MODE_INPUT;
  gpio.Pull = GPIO_NOPULL;
  gpio.Pin = BSP_IMU660RC_MISO_Pin;
  HAL_GPIO_Init(BSP_IMU660RC_MISO_GPIO_Port, &gpio);
}

static void imu_cs(GPIO_PinState state)
{
  HAL_GPIO_WritePin(BSP_IMU660RC_CS_GPIO_Port, BSP_IMU660RC_CS_Pin, state);
}

static uint8_t imu_spi_transfer(uint8_t tx)
{
  uint8_t rx = 0;

  for (uint8_t mask = 0x80U; mask != 0U; mask >>= 1U)
  {
    HAL_GPIO_WritePin(BSP_IMU660RC_MOSI_GPIO_Port, BSP_IMU660RC_MOSI_Pin,
                      (tx & mask) ? GPIO_PIN_SET : GPIO_PIN_RESET);
    HAL_GPIO_WritePin(BSP_IMU660RC_SCK_GPIO_Port, BSP_IMU660RC_SCK_Pin, GPIO_PIN_SET);
    if (HAL_GPIO_ReadPin(BSP_IMU660RC_MISO_GPIO_Port, BSP_IMU660RC_MISO_Pin) == GPIO_PIN_SET)
    {
      rx |= mask;
    }
    HAL_GPIO_WritePin(BSP_IMU660RC_SCK_GPIO_Port, BSP_IMU660RC_SCK_Pin, GPIO_PIN_RESET);
  }

  return rx;
}

static void imu_write_register(uint8_t reg, uint8_t value)
{
  imu_cs(GPIO_PIN_RESET);
  (void)imu_spi_transfer(reg);
  (void)imu_spi_transfer(value);
  imu_cs(GPIO_PIN_SET);
}

static uint8_t imu_read_register(uint8_t reg)
{
  uint8_t value;

  imu_cs(GPIO_PIN_RESET);
  (void)imu_spi_transfer(reg | IMU660RC_SPI_READ);
  value = imu_spi_transfer(0xFFU);
  imu_cs(GPIO_PIN_SET);

  return value;
}

static void imu_read_registers(uint8_t reg, uint8_t *data, uint32_t length)
{
  imu_cs(GPIO_PIN_RESET);
  (void)imu_spi_transfer(reg | IMU660RC_SPI_READ);
  while (length-- > 0U)
  {
    *data++ = imu_spi_transfer(0xFFU);
  }
  imu_cs(GPIO_PIN_SET);
}

static int imu_self_check(void)
{
  for (uint32_t retry = 0; retry < IMU660RC_SELF_CHECK_TRY; retry++)
  {
    if (imu_read_register(IMU660RC_CHIP_ID) == IMU660RC_CHIP_ID_VALUE)
    {
      return 1;
    }
    HAL_Delay(1U);
  }

  return 0;
}

static uint8_t gyro_range_register_value(BSP_IMU660RC_GyroRange range)
{
  switch (range)
  {
    case BSP_IMU660RC_GYRO_RANGE_125DPS:
      gyro_factor = 228.5714f;
      return 0x00U;
    case BSP_IMU660RC_GYRO_RANGE_250DPS:
      gyro_factor = 114.2857f;
      return 0x01U;
    case BSP_IMU660RC_GYRO_RANGE_500DPS:
      gyro_factor = 57.1428f;
      return 0x02U;
    case BSP_IMU660RC_GYRO_RANGE_1000DPS:
      gyro_factor = 28.5714f;
      return 0x03U;
    case BSP_IMU660RC_GYRO_RANGE_4000DPS:
      gyro_factor = 7.14285f;
      return 0x0CU;
    case BSP_IMU660RC_GYRO_RANGE_2000DPS:
    default:
      gyro_factor = 14.2857f;
      return 0x04U;
  }
}

uint8_t BSP_IMU660RC_ReadChipId(void)
{
  return imu_read_register(IMU660RC_CHIP_ID);
}

int BSP_IMU660RC_GyroInit(BSP_IMU660RC_GyroRange range)
{
  imu_gpio_init();
  HAL_Delay(10U);

  if (!imu_self_check())
  {
    return 0;
  }

  imu_write_register(IMU660RC_FUNC_CFG_ACCESS, 0x04U);
  HAL_Delay(30U);

  imu_write_register(IMU660RC_CTRL3, 0x44U);
  imu_write_register(IMU660RC_CTRL6, gyro_range_register_value(range));
  imu_write_register(IMU660RC_CTRL2, 0x18U);
  imu_write_register(IMU660RC_CTRL7, 0x01U);

  return 1;
}

void BSP_IMU660RC_GyroRead(BSP_IMU660RC_GyroData *data)
{
  int16_t raw[3];

  if (data == NULL)
  {
    return;
  }

  imu_read_registers(IMU660RC_OUTX_L_G, (uint8_t *)raw, sizeof(raw));
  data->x_raw = raw[0];
  data->y_raw = raw[1];
  data->z_raw = raw[2];
  data->x_dps = (float)data->x_raw / gyro_factor;
  data->y_dps = (float)data->y_raw / gyro_factor;
  data->z_dps = (float)data->z_raw / gyro_factor;
}
