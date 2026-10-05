#include "bsp_w25q128.h"

#include <string.h>

#include "board_pins.h"
#include "stm32h7xx_hal.h"

#define W25Q128_CMD_RESET_ENABLE       0x66U
#define W25Q128_CMD_RESET_DEVICE       0x99U
#define W25Q128_CMD_RELEASE_POWER_DOWN 0xABU
#define W25Q128_CMD_READ_JEDEC_ID      0x9FU
#define W25Q128_CMD_READ_STATUS_1      0x05U

#define W25Q128_COMMAND_TIMEOUT_MS     100U
#define W25Q128_RESET_DELAY_MS         1U

static QSPI_HandleTypeDef g_qspi;
static BSP_W25Q128_Info g_info;
static BSP_W25Q128_Status g_status = BSP_W25Q128_STATUS_NOT_INITIALIZED;

static HAL_StatusTypeDef w25q128_qspi_init(void)
{
  g_qspi.Instance = QUADSPI;
  g_qspi.Init.ClockPrescaler = 9U;
  g_qspi.Init.FifoThreshold = 4U;
  g_qspi.Init.SampleShifting = QSPI_SAMPLE_SHIFTING_HALFCYCLE;
  g_qspi.Init.FlashSize = 23U;
  g_qspi.Init.ChipSelectHighTime = QSPI_CS_HIGH_TIME_2_CYCLE;
  g_qspi.Init.ClockMode = QSPI_CLOCK_MODE_0;
  g_qspi.Init.FlashID = QSPI_FLASH_ID_1;
  g_qspi.Init.DualFlash = QSPI_DUALFLASH_DISABLE;

  return HAL_QSPI_Init(&g_qspi);
}

static HAL_StatusTypeDef w25q128_send_instruction(uint8_t instruction)
{
  QSPI_CommandTypeDef command = {0};

  command.InstructionMode = QSPI_INSTRUCTION_1_LINE;
  command.Instruction = instruction;
  command.AddressMode = QSPI_ADDRESS_NONE;
  command.AlternateByteMode = QSPI_ALTERNATE_BYTES_NONE;
  command.DataMode = QSPI_DATA_NONE;
  command.DummyCycles = 0U;
  command.DdrMode = QSPI_DDR_MODE_DISABLE;
  command.DdrHoldHalfCycle = QSPI_DDR_HHC_ANALOG_DELAY;
  command.SIOOMode = QSPI_SIOO_INST_EVERY_CMD;

  return HAL_QSPI_Command(&g_qspi, &command, W25Q128_COMMAND_TIMEOUT_MS);
}

static HAL_StatusTypeDef w25q128_read(uint8_t instruction,
                                     uint8_t *data,
                                     uint32_t length)
{
  QSPI_CommandTypeDef command = {0};
  HAL_StatusTypeDef hal_status;

  if ((data == NULL) || (length == 0U))
  {
    return HAL_ERROR;
  }

  command.InstructionMode = QSPI_INSTRUCTION_1_LINE;
  command.Instruction = instruction;
  command.AddressMode = QSPI_ADDRESS_NONE;
  command.AlternateByteMode = QSPI_ALTERNATE_BYTES_NONE;
  command.DataMode = QSPI_DATA_1_LINE;
  command.NbData = length;
  command.DummyCycles = 0U;
  command.DdrMode = QSPI_DDR_MODE_DISABLE;
  command.DdrHoldHalfCycle = QSPI_DDR_HHC_ANALOG_DELAY;
  command.SIOOMode = QSPI_SIOO_INST_EVERY_CMD;

  hal_status = HAL_QSPI_Command(&g_qspi, &command,
                                W25Q128_COMMAND_TIMEOUT_MS);
  if (hal_status != HAL_OK)
  {
    return hal_status;
  }

  return HAL_QSPI_Receive(&g_qspi, data, W25Q128_COMMAND_TIMEOUT_MS);
}

void HAL_QSPI_MspInit(QSPI_HandleTypeDef *hqspi)
{
  GPIO_InitTypeDef gpio = {0};

  if ((hqspi == NULL) || (hqspi->Instance != QUADSPI))
  {
    return;
  }

  __HAL_RCC_QSPI_CLK_ENABLE();
  __HAL_RCC_GPIOB_CLK_ENABLE();
  __HAL_RCC_GPIOD_CLK_ENABLE();
  __HAL_RCC_GPIOE_CLK_ENABLE();

  gpio.Mode = GPIO_MODE_AF_PP;
  gpio.Pull = GPIO_NOPULL;
  gpio.Speed = GPIO_SPEED_FREQ_VERY_HIGH;

  gpio.Pin = BOARD_QSPI_CLK_Pin;
  gpio.Alternate = GPIO_AF9_QUADSPI;
  HAL_GPIO_Init(BOARD_QSPI_CLK_GPIO_Port, &gpio);

  gpio.Pin = BOARD_QSPI_BK1_NCS_Pin;
  gpio.Pull = GPIO_PULLUP;
  gpio.Alternate = GPIO_AF10_QUADSPI;
  HAL_GPIO_Init(BOARD_QSPI_BK1_NCS_GPIO_Port, &gpio);

  gpio.Pin = BOARD_QSPI_BK1_IO0_Pin | BOARD_QSPI_BK1_IO1_Pin |
             BOARD_QSPI_BK1_IO3_Pin;
  gpio.Pull = GPIO_NOPULL;
  gpio.Alternate = GPIO_AF9_QUADSPI;
  HAL_GPIO_Init(GPIOD, &gpio);

  gpio.Pin = BOARD_QSPI_BK1_IO2_Pin;
  gpio.Pull = GPIO_PULLUP;
  gpio.Alternate = GPIO_AF9_QUADSPI;
  HAL_GPIO_Init(BOARD_QSPI_BK1_IO2_GPIO_Port, &gpio);
}

BSP_W25Q128_Status BSP_W25Q128_RefreshInfo(void)
{
  uint8_t jedec_id[3];

  if (w25q128_read(W25Q128_CMD_READ_JEDEC_ID,
                   jedec_id, sizeof(jedec_id)) != HAL_OK)
  {
    g_status = BSP_W25Q128_STATUS_COMMUNICATION_ERROR;
    return g_status;
  }

  if (w25q128_read(W25Q128_CMD_READ_STATUS_1,
                   &g_info.status_register, 1U) != HAL_OK)
  {
    g_status = BSP_W25Q128_STATUS_COMMUNICATION_ERROR;
    return g_status;
  }

  g_info.manufacturer_id = jedec_id[0];
  g_info.memory_type = jedec_id[1];
  g_info.capacity_id = jedec_id[2];

  if ((g_info.manufacturer_id != BSP_W25Q128_EXPECTED_MANUFACTURER_ID) ||
      (g_info.memory_type != BSP_W25Q128_EXPECTED_MEMORY_TYPE) ||
      (g_info.capacity_id != BSP_W25Q128_EXPECTED_CAPACITY_ID))
  {
    g_status = BSP_W25Q128_STATUS_UNSUPPORTED_DEVICE;
    return g_status;
  }

  g_status = BSP_W25Q128_STATUS_READY;
  return g_status;
}

BSP_W25Q128_Status BSP_W25Q128_Init(void)
{
  memset(&g_info, 0, sizeof(g_info));
  g_status = BSP_W25Q128_STATUS_NOT_INITIALIZED;

  if (w25q128_qspi_init() != HAL_OK)
  {
    g_status = BSP_W25Q128_STATUS_PERIPHERAL_ERROR;
    return g_status;
  }

  (void)w25q128_send_instruction(W25Q128_CMD_RELEASE_POWER_DOWN);
  HAL_Delay(W25Q128_RESET_DELAY_MS);

  if ((w25q128_send_instruction(W25Q128_CMD_RESET_ENABLE) != HAL_OK) ||
      (w25q128_send_instruction(W25Q128_CMD_RESET_DEVICE) != HAL_OK))
  {
    g_status = BSP_W25Q128_STATUS_COMMUNICATION_ERROR;
    return g_status;
  }

  HAL_Delay(W25Q128_RESET_DELAY_MS);
  return BSP_W25Q128_RefreshInfo();
}

BSP_W25Q128_Status BSP_W25Q128_GetStatus(void)
{
  return g_status;
}

int BSP_W25Q128_GetInfo(BSP_W25Q128_Info *info)
{
  if (info == NULL)
  {
    return 0;
  }

  *info = g_info;
  return (g_status == BSP_W25Q128_STATUS_READY) ? 1 : 0;
}
