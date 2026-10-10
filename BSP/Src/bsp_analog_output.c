#include "bsp_analog_output.h"

#include "board_pins.h"
#include "stm32h7xx_hal.h"

#define ANALOG_DAC_REFERENCE_MV 3300U
#define ANALOG_DAC_MAX_CODE     4095U
#define ANALOG_CURRENT_MAX_UA   20000U
#define ANALOG_VOLTAGE_MAX_MV   10000U

typedef struct
{
  uint16_t minimum_mv;
  uint16_t maximum_mv;
  uint16_t cycle_ms;
  uint32_t start_ms;
  uint8_t active;
} BSP_ANALOG_OUTPUT_BreathState;

static DAC_HandleTypeDef analog_dac;
static BSP_ANALOG_OUTPUT_BreathState breath_state;

void HAL_DAC_MspInit(DAC_HandleTypeDef *hdac)
{
  GPIO_InitTypeDef gpio = {0};
  if ((hdac == NULL) || (hdac->Instance != DAC1)) return;
  __HAL_RCC_DAC12_CLK_ENABLE();
  __HAL_RCC_GPIOA_CLK_ENABLE();
  gpio.Pin = BOARD_DO_I_MCU_Pin | BOARD_DO_U_MCU_Pin;
  gpio.Mode = GPIO_MODE_ANALOG;
  gpio.Pull = GPIO_NOPULL;
  HAL_GPIO_Init(GPIOA, &gpio);
}

static uint32_t analog_mv_to_code(uint32_t millivolts)
{
  if (millivolts >= ANALOG_DAC_REFERENCE_MV) return ANALOG_DAC_MAX_CODE;
  return (millivolts * ANALOG_DAC_MAX_CODE) / ANALOG_DAC_REFERENCE_MV;
}

static void analog_set_dac(uint32_t channel, uint32_t millivolts)
{
  (void)HAL_DAC_SetValue(&analog_dac, channel, DAC_ALIGN_12B_R,
                         analog_mv_to_code(millivolts));
}

static uint32_t analog_smoothstep(uint32_t position)
{
  return (position * position * (3000U - (2U * position))) / 1000000U;
}

void BSP_ANALOG_OUTPUT_Init(void)
{
  DAC_ChannelConfTypeDef config = {0};
  analog_dac.Instance = DAC1;
  (void)HAL_DAC_Init(&analog_dac);
  config.DAC_SampleAndHold = DAC_SAMPLEANDHOLD_DISABLE;
  config.DAC_Trigger = DAC_TRIGGER_NONE;
  config.DAC_OutputBuffer = DAC_OUTPUTBUFFER_ENABLE;
  config.DAC_ConnectOnChipPeripheral = DAC_CHIPCONNECT_DISABLE;
  config.DAC_UserTrimming = DAC_TRIMMING_FACTORY;
  (void)HAL_DAC_ConfigChannel(&analog_dac, &config, DAC_CHANNEL_1);
  (void)HAL_DAC_ConfigChannel(&analog_dac, &config, DAC_CHANNEL_2);
  (void)HAL_DAC_Start(&analog_dac, DAC_CHANNEL_1);
  (void)HAL_DAC_Start(&analog_dac, DAC_CHANNEL_2);
  BSP_ANALOG_OUTPUT_AllOff();
}

void BSP_ANALOG_OUTPUT_SetCurrentUa(uint16_t current_ua)
{
  uint32_t limited = current_ua > ANALOG_CURRENT_MAX_UA ?
                     ANALOG_CURRENT_MAX_UA : current_ua;
  analog_set_dac(DAC_CHANNEL_1, (limited * 165U) / 1000U);
}

void BSP_ANALOG_OUTPUT_SetVoltageMv(uint16_t voltage_mv)
{
  uint32_t limited = voltage_mv > ANALOG_VOLTAGE_MAX_MV ?
                     ANALOG_VOLTAGE_MAX_MV : voltage_mv;
  breath_state.active = 0U;
  analog_set_dac(DAC_CHANNEL_2, (limited * 51U) / 151U);
}

void BSP_ANALOG_OUTPUT_StartVoltageBreath(uint16_t minimum_mv,
                                         uint16_t maximum_mv,
                                         uint16_t cycle_ms,
                                         uint32_t now_ms)
{
  if ((minimum_mv > maximum_mv) || (maximum_mv > ANALOG_VOLTAGE_MAX_MV) ||
      (cycle_ms < 20U)) return;
  if ((breath_state.active != 0U) &&
      (breath_state.minimum_mv == minimum_mv) &&
      (breath_state.maximum_mv == maximum_mv) &&
      (breath_state.cycle_ms == cycle_ms)) return;
  breath_state.minimum_mv = minimum_mv;
  breath_state.maximum_mv = maximum_mv;
  breath_state.cycle_ms = cycle_ms;
  breath_state.start_ms = now_ms;
  breath_state.active = 1U;
}

void BSP_ANALOG_OUTPUT_Process(uint32_t now_ms)
{
  uint32_t elapsed;
  uint32_t half_cycle;
  uint32_t position;
  uint32_t shaped;
  uint32_t output_mv;
  if (breath_state.active == 0U) return;
  elapsed = (now_ms - breath_state.start_ms) % breath_state.cycle_ms;
  half_cycle = breath_state.cycle_ms / 2U;
  if (half_cycle == 0U) return;
  position = (elapsed <= half_cycle) ?
             ((elapsed * 1000U) / half_cycle) :
             (((breath_state.cycle_ms - elapsed) * 1000U) / half_cycle);
  if (position > 1000U) position = 1000U;
  shaped = analog_smoothstep(position);
  output_mv = breath_state.minimum_mv +
              (((uint32_t)(breath_state.maximum_mv - breath_state.minimum_mv) * shaped) / 1000U);
  analog_set_dac(DAC_CHANNEL_2, (output_mv * 51U) / 151U);
}

void BSP_ANALOG_OUTPUT_AllOff(void)
{
  breath_state.active = 0U;
  analog_set_dac(DAC_CHANNEL_1, 0U);
  analog_set_dac(DAC_CHANNEL_2, 0U);
}
