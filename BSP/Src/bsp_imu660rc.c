#include "sys.h"
#include "spi.h"

#include "bmi2.h"
#include "bmi270.h"

#define IMU_SPI_READ_MASK 0x80U
#define BSP_IMU660RC_USE_BITBANG_SPI 0
#define IMU_SPI_MAX_TRANSFER_LEN 64U

#ifndef BSP_IMU660RC_DEBUG
#define BSP_IMU660RC_DEBUG 1
#endif

#define IMU_LOG(...) do { if (BSP_IMU660RC_DEBUG) { printf(__VA_ARGS__); } } while (0)

static struct bmi2_dev bmi_dev;
static uint8_t bmi_ready;
static BSP_IMU660RC_AccelRange active_accel_range = BSP_IMU660RC_ACCEL_RANGE_8G;
static BSP_IMU660RC_GyroRange active_gyro_range = BSP_IMU660RC_GYRO_RANGE_2000DPS;

static void imu_bitbang_gpio_init(void);
static uint8_t imu_bitbang_transfer(uint8_t tx);
static void imu_hardware_spi_probe_mode(const char *name, uint32_t polarity, uint32_t phase);

static void imu_delay_cycles(uint32_t cycles)
{
  for (volatile uint32_t i = 0U; i < cycles; i++)
  {
    __NOP();
  }
}

static void imu_cs_init(void)
{
  GPIO_InitTypeDef gpio = {0};

  __HAL_RCC_GPIOD_CLK_ENABLE();
  HAL_GPIO_WritePin(BSP_IMU660RC_CS_GPIO_Port, BSP_IMU660RC_CS_Pin, GPIO_PIN_SET);

  gpio.Pin = BSP_IMU660RC_CS_Pin;
  gpio.Mode = GPIO_MODE_OUTPUT_PP;
  gpio.Pull = GPIO_NOPULL;
  gpio.Speed = GPIO_SPEED_FREQ_VERY_HIGH;
  HAL_GPIO_Init(BSP_IMU660RC_CS_GPIO_Port, &gpio);
}

static void imu_cs(GPIO_PinState state)
{
  HAL_GPIO_WritePin(BSP_IMU660RC_CS_GPIO_Port, BSP_IMU660RC_CS_Pin, state);
}

static int8_t imu_spi_transfer(uint8_t tx, uint8_t *rx)
{
  if (rx == NULL)
  {
    return BMI2_E_NULL_PTR;
  }

#if BSP_IMU660RC_USE_BITBANG_SPI
  *rx = imu_bitbang_transfer(tx);
  return BMI2_OK;
#else
  if (HAL_SPI_TransmitReceive(&hspi1, &tx, rx, 1U, 10U) != HAL_OK)
  {
    return BMI2_E_COM_FAIL;
  }

  return BMI2_OK;
#endif
}

static void imu_bitbang_delay(void)
{
  imu_delay_cycles(500U);
}

static uint8_t imu_bitbang_transfer(uint8_t tx)
{
  uint8_t rx = 0U;

  for (uint8_t mask = 0x80U; mask != 0U; mask >>= 1U)
  {
    HAL_GPIO_WritePin(BSP_IMU660RC_MOSI_GPIO_Port, BSP_IMU660RC_MOSI_Pin,
                      ((tx & mask) != 0U) ? GPIO_PIN_SET : GPIO_PIN_RESET);
    imu_bitbang_delay();
    HAL_GPIO_WritePin(BSP_IMU660RC_SCK_GPIO_Port, BSP_IMU660RC_SCK_Pin, GPIO_PIN_SET);
    imu_bitbang_delay();
    if (HAL_GPIO_ReadPin(BSP_IMU660RC_MISO_GPIO_Port, BSP_IMU660RC_MISO_Pin) == GPIO_PIN_SET)
    {
      rx |= mask;
    }
    HAL_GPIO_WritePin(BSP_IMU660RC_SCK_GPIO_Port, BSP_IMU660RC_SCK_Pin, GPIO_PIN_RESET);
    imu_bitbang_delay();
  }

  return rx;
}

static void imu_bitbang_gpio_init(void)
{
  GPIO_InitTypeDef gpio = {0};

  __HAL_RCC_GPIOA_CLK_ENABLE();
  __HAL_RCC_GPIOD_CLK_ENABLE();

  HAL_GPIO_WritePin(BSP_IMU660RC_SCK_GPIO_Port, BSP_IMU660RC_SCK_Pin, GPIO_PIN_RESET);
  HAL_GPIO_WritePin(BSP_IMU660RC_MOSI_GPIO_Port, BSP_IMU660RC_MOSI_Pin, GPIO_PIN_SET);
  HAL_GPIO_WritePin(BSP_IMU660RC_CS_GPIO_Port, BSP_IMU660RC_CS_Pin, GPIO_PIN_SET);

  gpio.Pin = BSP_IMU660RC_SCK_Pin | BSP_IMU660RC_MOSI_Pin;
  gpio.Mode = GPIO_MODE_OUTPUT_PP;
  gpio.Pull = GPIO_NOPULL;
  gpio.Speed = GPIO_SPEED_FREQ_LOW;
  HAL_GPIO_Init(BSP_IMU660RC_SCK_GPIO_Port, &gpio);

  gpio.Pin = BSP_IMU660RC_MISO_Pin;
  gpio.Mode = GPIO_MODE_INPUT;
  gpio.Pull = GPIO_NOPULL;
  HAL_GPIO_Init(BSP_IMU660RC_MISO_GPIO_Port, &gpio);

  gpio.Pin = BSP_IMU660RC_CS_Pin;
  gpio.Mode = GPIO_MODE_OUTPUT_PP;
  gpio.Pull = GPIO_NOPULL;
  gpio.Speed = GPIO_SPEED_FREQ_LOW;
  HAL_GPIO_Init(BSP_IMU660RC_CS_GPIO_Port, &gpio);
}

static void imu_hardware_spi_probe_mode(const char *name, uint32_t polarity, uint32_t phase)
{
  uint8_t tx[5] = { (uint8_t)(BMI2_CHIP_ID_ADDR | IMU_SPI_READ_MASK), 0xFFU, 0xFFU, 0xFFU, 0xFFU };
  uint8_t rx[5] = { 0U, 0U, 0U, 0U, 0U };
  HAL_StatusTypeDef status;

  (void)HAL_SPI_DeInit(&hspi1);
  MX_SPI1_Init();
  hspi1.Init.CLKPolarity = polarity;
  hspi1.Init.CLKPhase = phase;
  hspi1.Init.BaudRatePrescaler = SPI_BAUDRATEPRESCALER_256;
  if (HAL_SPI_Init(&hspi1) != HAL_OK)
  {
    printf("[BMI270] hw %-5s init failed\r\n", name);
    return;
  }

  imu_cs_init();
  HAL_Delay(2U);

  imu_cs(GPIO_PIN_RESET);
  imu_delay_cycles(100U);
  status = HAL_SPI_TransmitReceive(&hspi1, tx, rx, sizeof(tx), 100U);
  imu_delay_cycles(100U);
  imu_cs(GPIO_PIN_SET);
  imu_delay_cycles(100U);

  printf("[BMI270] hw %-5s status=%d rx=0x%02X,0x%02X,0x%02X,0x%02X,0x%02X\r\n",
         name,
         (int)status,
         rx[0],
         rx[1],
         rx[2],
         rx[3],
         rx[4]);
}

static uint8_t imu_spi_read_reg_raw(uint8_t reg)
{
  uint8_t value = 0U;
#if BSP_IMU660RC_USE_BITBANG_SPI
  uint8_t dummy = 0U;
#else
  uint8_t tx[3] = { 0U, 0xFFU, 0xFFU };
  uint8_t rx[3] = { 0U, 0U, 0U };
#endif

  imu_cs(GPIO_PIN_RESET);
  imu_delay_cycles(100U);
#if BSP_IMU660RC_USE_BITBANG_SPI
  if (imu_spi_transfer((uint8_t)(reg | IMU_SPI_READ_MASK), &dummy) != BMI2_OK)
  {
    imu_cs(GPIO_PIN_SET);
    return 0U;
  }

  if (imu_spi_transfer(0xFFU, &dummy) != BMI2_OK)
  {
    imu_cs(GPIO_PIN_SET);
    return 0U;
  }

  if (imu_spi_transfer(0xFFU, &value) != BMI2_OK)
  {
    imu_cs(GPIO_PIN_SET);
    return 0U;
  }

  imu_delay_cycles(100U);
  imu_cs(GPIO_PIN_SET);
  imu_delay_cycles(100U);

  return value;
#else
  tx[0] = (uint8_t)(reg | IMU_SPI_READ_MASK);
  if (HAL_SPI_TransmitReceive(&hspi1, tx, rx, sizeof(tx), 100U) != HAL_OK)
  {
    imu_cs(GPIO_PIN_SET);
    return 0U;
  }

  imu_delay_cycles(100U);
  imu_cs(GPIO_PIN_SET);
  imu_delay_cycles(100U);

  value = rx[2];
  return value;
#endif
}

static BMI2_INTF_RETURN_TYPE bmi2_spi_read(uint8_t reg_addr, uint8_t *reg_data, uint32_t len, void *intf_ptr)
{
#if BSP_IMU660RC_USE_BITBANG_SPI
  uint32_t index;
  uint8_t dummy = 0U;
#else
  uint8_t tx[IMU_SPI_MAX_TRANSFER_LEN + 1U];
  uint8_t rx[IMU_SPI_MAX_TRANSFER_LEN + 1U];
  uint32_t index;
#endif

  (void)intf_ptr;

  if (reg_data == NULL)
  {
    return BMI2_E_NULL_PTR;
  }

#if BSP_IMU660RC_USE_BITBANG_SPI
  imu_cs(GPIO_PIN_RESET);
  imu_delay_cycles(100U);
  if (imu_spi_transfer(reg_addr, &dummy) != BMI2_OK)
  {
    imu_cs(GPIO_PIN_SET);
    return BMI2_E_COM_FAIL;
  }

  for (index = 0U; index < len; index++)
  {
    if (imu_spi_transfer(0xFFU, &reg_data[index]) != BMI2_OK)
    {
      imu_cs(GPIO_PIN_SET);
      return BMI2_E_COM_FAIL;
    }
  }

  imu_delay_cycles(100U);
  imu_cs(GPIO_PIN_SET);
  imu_delay_cycles(100U);
#else
  if (len > IMU_SPI_MAX_TRANSFER_LEN)
  {
    return BMI2_E_COM_FAIL;
  }

  tx[0] = reg_addr;
  rx[0] = 0U;
  for (index = 0U; index < len; index++)
  {
    tx[index + 1U] = 0xFFU;
    rx[index + 1U] = 0U;
  }

  imu_cs(GPIO_PIN_RESET);
  imu_delay_cycles(100U);
  if (HAL_SPI_TransmitReceive(&hspi1, tx, rx, (uint16_t)(len + 1U), 100U) != HAL_OK)
  {
    imu_cs(GPIO_PIN_SET);
    return BMI2_E_COM_FAIL;
  }
  imu_delay_cycles(100U);
  imu_cs(GPIO_PIN_SET);
  imu_delay_cycles(100U);

  for (index = 0U; index < len; index++)
  {
    reg_data[index] = rx[index + 1U];
  }
#endif

  return BMI2_INTF_RET_SUCCESS;
}

static BMI2_INTF_RETURN_TYPE bmi2_spi_write(uint8_t reg_addr, const uint8_t *reg_data, uint32_t len, void *intf_ptr)
{
#if BSP_IMU660RC_USE_BITBANG_SPI
  uint32_t index;
  uint8_t dummy = 0U;
#else
  uint8_t tx[IMU_SPI_MAX_TRANSFER_LEN + 1U];
  uint8_t rx[IMU_SPI_MAX_TRANSFER_LEN + 1U];
  uint32_t index;
#endif

  (void)intf_ptr;

  if (reg_data == NULL)
  {
    return BMI2_E_NULL_PTR;
  }

#if BSP_IMU660RC_USE_BITBANG_SPI
  imu_cs(GPIO_PIN_RESET);
  imu_delay_cycles(100U);
  if (imu_spi_transfer((uint8_t)(reg_addr & BMI2_SPI_WR_MASK), &dummy) != BMI2_OK)
  {
    imu_cs(GPIO_PIN_SET);
    return BMI2_E_COM_FAIL;
  }

  for (index = 0U; index < len; index++)
  {
    if (imu_spi_transfer(reg_data[index], &dummy) != BMI2_OK)
    {
      imu_cs(GPIO_PIN_SET);
      return BMI2_E_COM_FAIL;
    }
  }

  imu_delay_cycles(100U);
  imu_cs(GPIO_PIN_SET);
  imu_delay_cycles(100U);
#else
  if (len > IMU_SPI_MAX_TRANSFER_LEN)
  {
    return BMI2_E_COM_FAIL;
  }

  tx[0] = (uint8_t)(reg_addr & BMI2_SPI_WR_MASK);
  rx[0] = 0U;
  for (index = 0U; index < len; index++)
  {
    tx[index + 1U] = reg_data[index];
    rx[index + 1U] = 0U;
  }

  imu_cs(GPIO_PIN_RESET);
  imu_delay_cycles(100U);
  if (HAL_SPI_TransmitReceive(&hspi1, tx, rx, (uint16_t)(len + 1U), 100U) != HAL_OK)
  {
    imu_cs(GPIO_PIN_SET);
    return BMI2_E_COM_FAIL;
  }
  imu_delay_cycles(100U);
  imu_cs(GPIO_PIN_SET);
  imu_delay_cycles(100U);
#endif

  return BMI2_INTF_RET_SUCCESS;
}

static void bmi2_delay_us(uint32_t period, void *intf_ptr)
{
  (void)intf_ptr;

  if (period >= 1000U)
  {
    HAL_Delay(period / 1000U);
    return;
  }

  imu_delay_cycles((period * 60U) + 1U);
}

static uint8_t accel_range_to_bmi2(BSP_IMU660RC_AccelRange range)
{
  switch (range)
  {
    case BSP_IMU660RC_ACCEL_RANGE_2G:
      return BMI2_ACC_RANGE_2G;
    case BSP_IMU660RC_ACCEL_RANGE_4G:
      return BMI2_ACC_RANGE_4G;
    case BSP_IMU660RC_ACCEL_RANGE_16G:
      return BMI2_ACC_RANGE_16G;
    case BSP_IMU660RC_ACCEL_RANGE_8G:
    default:
      return BMI2_ACC_RANGE_8G;
  }
}

static uint8_t gyro_range_to_bmi2(BSP_IMU660RC_GyroRange range)
{
  switch (range)
  {
    case BSP_IMU660RC_GYRO_RANGE_125DPS:
      return BMI2_GYR_RANGE_125;
    case BSP_IMU660RC_GYRO_RANGE_250DPS:
      return BMI2_GYR_RANGE_250;
    case BSP_IMU660RC_GYRO_RANGE_500DPS:
      return BMI2_GYR_RANGE_500;
    case BSP_IMU660RC_GYRO_RANGE_1000DPS:
      return BMI2_GYR_RANGE_1000;
    case BSP_IMU660RC_GYRO_RANGE_4000DPS:
      return BMI2_GYR_RANGE_2000;
    case BSP_IMU660RC_GYRO_RANGE_2000DPS:
    default:
      return BMI2_GYR_RANGE_2000;
  }
}

static float accel_lsb_per_g(BSP_IMU660RC_AccelRange range)
{
  switch (range)
  {
    case BSP_IMU660RC_ACCEL_RANGE_2G:
      return 16384.0f;
    case BSP_IMU660RC_ACCEL_RANGE_4G:
      return 8192.0f;
    case BSP_IMU660RC_ACCEL_RANGE_16G:
      return 2048.0f;
    case BSP_IMU660RC_ACCEL_RANGE_8G:
    default:
      return 4096.0f;
  }
}

static float gyro_lsb_per_dps(BSP_IMU660RC_GyroRange range)
{
  switch (range)
  {
    case BSP_IMU660RC_GYRO_RANGE_125DPS:
      return 262.144f;
    case BSP_IMU660RC_GYRO_RANGE_250DPS:
      return 131.072f;
    case BSP_IMU660RC_GYRO_RANGE_500DPS:
      return 65.536f;
    case BSP_IMU660RC_GYRO_RANGE_1000DPS:
      return 32.768f;
    case BSP_IMU660RC_GYRO_RANGE_4000DPS:
      return 8.192f;
    case BSP_IMU660RC_GYRO_RANGE_2000DPS:
    default:
      return 16.384f;
  }
}

static int8_t bmi270_configure(void)
{
  int8_t rslt;
  struct bmi2_sens_config sens_cfg[2];
  const uint8_t sens_list[2] = { BMI2_ACCEL, BMI2_GYRO };

  bmi_dev.intf = BMI2_SPI_INTF;
  bmi_dev.intf_ptr = NULL;
  bmi_dev.read = bmi2_spi_read;
  bmi_dev.write = bmi2_spi_write;
  bmi_dev.delay_us = bmi2_delay_us;
  bmi_dev.read_write_len = 16U;
  bmi_dev.config_file_ptr = NULL;

  rslt = bmi270_init(&bmi_dev);
  if (rslt != BMI2_OK)
  {
    return rslt;
  }

  rslt = bmi2_set_spi3_interface_mode(BMI2_DISABLE, &bmi_dev);
  if (rslt != BMI2_OK)
  {
    return rslt;
  }

  sens_cfg[0].type = BMI2_ACCEL;
  sens_cfg[1].type = BMI2_GYRO;

  rslt = bmi2_get_sensor_config(sens_cfg, 2U, &bmi_dev);
  if (rslt != BMI2_OK)
  {
    return rslt;
  }

  sens_cfg[0].cfg.acc.odr = BMI2_ACC_ODR_1600HZ;
  sens_cfg[0].cfg.acc.range = accel_range_to_bmi2(active_accel_range);
  sens_cfg[0].cfg.acc.bwp = BMI2_ACC_NORMAL_AVG4;
  sens_cfg[0].cfg.acc.filter_perf = BMI2_PERF_OPT_MODE;

  sens_cfg[1].cfg.gyr.odr = BMI2_GYR_ODR_1600HZ;
  sens_cfg[1].cfg.gyr.range = gyro_range_to_bmi2(active_gyro_range);
  sens_cfg[1].cfg.gyr.bwp = BMI2_GYR_NORMAL_MODE;
  sens_cfg[1].cfg.gyr.noise_perf = BMI2_POWER_OPT_MODE;
  sens_cfg[1].cfg.gyr.filter_perf = BMI2_PERF_OPT_MODE;

  rslt = bmi2_set_sensor_config(sens_cfg, 2U, &bmi_dev);
  if (rslt != BMI2_OK)
  {
    return rslt;
  }

  rslt = bmi270_sensor_enable(sens_list, 2U, &bmi_dev);
  if (rslt != BMI2_OK)
  {
    return rslt;
  }

  rslt = bmi2_set_adv_power_save(BMI2_DISABLE, &bmi_dev);
  if (rslt != BMI2_OK)
  {
    return rslt;
  }

  bmi_ready = 1U;
  return BMI2_OK;
}

static int8_t bmi270_read_sample(struct bmi2_sens_data *sample)
{
  int8_t rslt;
  uint8_t tries;
  uint8_t status = 0U;

  if ((sample == NULL) || (bmi_ready == 0U))
  {
    return BMI2_E_NULL_PTR;
  }

  for (tries = 0U; tries < 20U; tries++)
  {
    rslt = bmi2_get_status(&status, &bmi_dev);
    if (rslt != BMI2_OK)
    {
      return rslt;
    }

    if ((status & (BMI2_DRDY_ACC | BMI2_DRDY_GYR)) == (BMI2_DRDY_ACC | BMI2_DRDY_GYR))
    {
      rslt = bmi2_get_sensor_data(sample, &bmi_dev);
      if (rslt == BMI2_OK)
      {
        return BMI2_OK;
      }
      return rslt;
    }

    HAL_Delay(5U);
  }

  return BMI2_E_INVALID_STATUS;
}

static void convert_accel(const struct bmi2_sens_axes_data *src, BSP_IMU660RC_AccelData *dst)
{
  float lsb_per_g;

  if ((src == NULL) || (dst == NULL))
  {
    return;
  }

  lsb_per_g = accel_lsb_per_g(active_accel_range);

  dst->x_raw = src->x;
  dst->y_raw = src->y;
  dst->z_raw = src->z;
  dst->x_g = (float)src->x / lsb_per_g;
  dst->y_g = (float)src->y / lsb_per_g;
  dst->z_g = (float)src->z / lsb_per_g;
  dst->x_mps2 = dst->x_g * 9.80665f;
  dst->y_mps2 = dst->y_g * 9.80665f;
  dst->z_mps2 = dst->z_g * 9.80665f;
}

static void convert_gyro(const struct bmi2_sens_axes_data *src, BSP_IMU660RC_GyroData *dst)
{
  float lsb_per_dps;

  if ((src == NULL) || (dst == NULL))
  {
    return;
  }

  lsb_per_dps = gyro_lsb_per_dps(active_gyro_range);

  dst->x_raw = src->x;
  dst->y_raw = src->y;
  dst->z_raw = src->z;
  dst->x_dps = (float)src->x / lsb_per_dps;
  dst->y_dps = (float)src->y / lsb_per_dps;
  dst->z_dps = (float)src->z / lsb_per_dps;
}

uint8_t BSP_IMU660RC_ReadChipId(void)
{
  uint8_t chip_id = 0U;

  if ((bmi_ready != 0U) &&
      (bmi2_get_regs(BMI2_CHIP_ID_ADDR, &chip_id, 1U, &bmi_dev) == BMI2_OK))
  {
    return chip_id;
  }

  return imu_spi_read_reg_raw(BMI2_CHIP_ID_ADDR);
}

void BSP_IMU660RC_PrintSpiProbe(void)
{
  uint8_t bb_addr_rx = 0U;
  uint8_t bb_rx0 = 0U;
  uint8_t bb_rx1 = 0U;
  uint8_t bb_rx2 = 0U;
  uint8_t bb_rx3 = 0U;

  imu_hardware_spi_probe_mode("M0", SPI_POLARITY_LOW, SPI_PHASE_1EDGE);
  imu_hardware_spi_probe_mode("M1", SPI_POLARITY_LOW, SPI_PHASE_2EDGE);
  imu_hardware_spi_probe_mode("M2", SPI_POLARITY_HIGH, SPI_PHASE_1EDGE);
  imu_hardware_spi_probe_mode("M3", SPI_POLARITY_HIGH, SPI_PHASE_2EDGE);

  imu_bitbang_gpio_init();
  HAL_Delay(2U);

  imu_cs(GPIO_PIN_RESET);
  imu_bitbang_delay();
  bb_addr_rx = imu_bitbang_transfer((uint8_t)(BMI2_CHIP_ID_ADDR | IMU_SPI_READ_MASK));
  bb_rx0 = imu_bitbang_transfer(0xFFU);
  bb_rx1 = imu_bitbang_transfer(0xFFU);
  bb_rx2 = imu_bitbang_transfer(0xFFU);
  bb_rx3 = imu_bitbang_transfer(0xFFU);
  imu_bitbang_delay();
  imu_cs(GPIO_PIN_SET);
  imu_bitbang_delay();

  printf("[BMI270] bitbang probe: addr_rx=0x%02X rx=0x%02X,0x%02X,0x%02X,0x%02X\r\n",
         bb_addr_rx, bb_rx0, bb_rx1, bb_rx2, bb_rx3);

#if !BSP_IMU660RC_USE_BITBANG_SPI
  (void)HAL_SPI_DeInit(&hspi1);
  MX_SPI1_Init();
#endif
}

void BSP_IMU660RC_PrintDebug(void)
{
  uint8_t status = 0U;
  uint8_t internal = 0U;
  uint8_t aps = 0U;
  uint8_t pwr_ctrl = 0U;

  IMU_LOG("[BMI270] chip_id=0x%02X ready=%u\r\n", BSP_IMU660RC_ReadChipId(), bmi_ready);

  if (bmi_ready != 0U)
  {
    if (bmi2_get_adv_power_save(&aps, &bmi_dev) == BMI2_OK)
    {
      IMU_LOG("[BMI270] aps=%u\r\n", aps);
    }
    if (bmi2_get_regs(BMI2_PWR_CTRL_ADDR, &pwr_ctrl, 1U, &bmi_dev) == BMI2_OK)
    {
      IMU_LOG("[BMI270] pwr_ctrl=0x%02X\r\n", pwr_ctrl);
    }
    if (bmi2_get_status(&status, &bmi_dev) == BMI2_OK)
    {
      IMU_LOG("[BMI270] status=0x%02X\r\n", status);
    }
    if (bmi2_get_internal_status(&internal, &bmi_dev) == BMI2_OK)
    {
      IMU_LOG("[BMI270] internal=0x%02X\r\n", internal);
    }
  }
}

int BSP_IMU660RC_Init(BSP_IMU660RC_AccelRange accel_range,
                      BSP_IMU660RC_GyroRange gyro_range)
{
  int8_t rslt;

  active_accel_range = accel_range;
  active_gyro_range = gyro_range;
  bmi_ready = 0U;

#if BSP_IMU660RC_USE_BITBANG_SPI
  imu_bitbang_gpio_init();
#else
  imu_cs_init();
#endif
  HAL_Delay(20U);

  rslt = bmi270_configure();
  if (rslt != BMI2_OK)
  {
    IMU_LOG("[BMI270] init failed: %d, chip_id=0x%02X\r\n", rslt, BSP_IMU660RC_ReadChipId());
    BSP_IMU660RC_PrintDebug();
    return 0;
  }

  IMU_LOG("[BMI270] init ok, chip_id=0x%02X\r\n", BSP_IMU660RC_ReadChipId());
  return 1;
}

int BSP_IMU660RC_AccelInit(BSP_IMU660RC_AccelRange range)
{
  return BSP_IMU660RC_Init(range, BSP_IMU660RC_GYRO_RANGE_2000DPS);
}

int BSP_IMU660RC_GyroInit(BSP_IMU660RC_GyroRange range)
{
  return BSP_IMU660RC_Init(BSP_IMU660RC_ACCEL_RANGE_8G, range);
}

void BSP_IMU660RC_AccelRead(BSP_IMU660RC_AccelData *data)
{
  struct bmi2_sens_data sample = { { 0 } };
  int8_t rslt;

  if (data == NULL)
  {
    return;
  }

  rslt = bmi270_read_sample(&sample);
  if (rslt != BMI2_OK)
  {
    IMU_LOG("[BMI270] accel read fail: %d status=0x%02X\r\n", rslt, sample.status);
    return;
  }

  convert_accel(&sample.acc, data);
}

void BSP_IMU660RC_GyroRead(BSP_IMU660RC_GyroData *data)
{
  struct bmi2_sens_data sample = { { 0 } };
  int8_t rslt;

  if (data == NULL)
  {
    return;
  }

  rslt = bmi270_read_sample(&sample);
  if (rslt != BMI2_OK)
  {
    IMU_LOG("[BMI270] gyro read fail: %d status=0x%02X\r\n", rslt, sample.status);
    return;
  }

  convert_gyro(&sample.gyr, data);
}

int BSP_IMU660RC_Read6Axis(BSP_IMU660RC_6AxisData *data)
{
  struct bmi2_sens_data sample = { { 0 } };
  int8_t rslt;

  if (data == NULL)
  {
    return BMI2_E_NULL_PTR;
  }

  rslt = bmi270_read_sample(&sample);
  if (rslt != BMI2_OK)
  {
    IMU_LOG("[BMI270] 6axis read fail: %d status=0x%02X\r\n", rslt, sample.status);
    return rslt;
  }

  if ((sample.status & (BMI2_DRDY_ACC | BMI2_DRDY_GYR)) != (BMI2_DRDY_ACC | BMI2_DRDY_GYR))
  {
    return BMI2_E_INVALID_STATUS;
  }

  convert_accel(&sample.acc, &data->accel);
  convert_gyro(&sample.gyr, &data->gyro);
  return BMI2_OK;
}

static int imu_sensor_init(uint32_t range)
{
  return BSP_IMU660RC_Init(BSP_IMU660RC_ACCEL_RANGE_8G,
                           (BSP_IMU660RC_GyroRange)range);
}

static void imu_sensor_read(BSP_SensorData *data)
{
  BSP_IMU660RC_GyroData gyro;

  if (data == NULL)
  {
    return;
  }

  BSP_IMU660RC_GyroRead(&gyro);
  data->x_raw = gyro.x_raw;
  data->y_raw = gyro.y_raw;
  data->z_raw = gyro.z_raw;
  data->x_conv = gyro.x_dps;
  data->y_conv = gyro.y_dps;
  data->z_conv = gyro.z_dps;
}

static int imu_sensor_validate_range(uint32_t range)
{
  return (range <= BSP_IMU660RC_GYRO_RANGE_4000DPS);
}

const BSP_SensorDriver BSP_IMU660RC_Driver = {
  .name           = "BMI270",
  .Init           = imu_sensor_init,
  .Read           = imu_sensor_read,
  .ReadChipId     = BSP_IMU660RC_ReadChipId,
  .ValidateRange  = imu_sensor_validate_range,
  .default_range  = (uint32_t)BSP_IMU660RC_GYRO_RANGE_2000DPS
};
