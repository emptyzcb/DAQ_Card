#include "ad7606_service.h"
#include "datahub.h"
#include "bsp_console.h"

#include <stdio.h>

#define AD7606_LOG_LITERAL(text) ad7606_log_text((text), (int)(sizeof(text) - 1U))

static AD7606_SERVICE_Config ad7606_config = {
  10U,
  10U,
  BSP_AD7606_RANGE_10V,
  BSP_AD7606_OS_NONE
};

static AD7606_SERVICE_Diagnostics ad7606_diag;
static BSP_AD7606_Sample ad7606_latest_sample;
static uint32_t ad7606_last_log_tick;
static GPIO_PinState ad7606_busy_pullup_state;

static void ad7606_log_text(const char *text, int length)
{
  if ((text != 0) && (length > 0))
  {
    (void)BSP_CONSOLE_Write((const uint8_t *)text, (uint16_t)length);
  }
}

static void ad7606_log_sample(const char *label)
{
  char line[192];
  int length;

  length = snprintf(line, sizeof(line),
                    "[AD7606] %s raw=%d,%d,%d,%d,%d,%d,%d,%d sample=%lu errors=%lu\r\n",
                    label,
                    (int)ad7606_latest_sample.raw[0],
                    (int)ad7606_latest_sample.raw[1],
                    (int)ad7606_latest_sample.raw[2],
                    (int)ad7606_latest_sample.raw[3],
                    (int)ad7606_latest_sample.raw[4],
                    (int)ad7606_latest_sample.raw[5],
                    (int)ad7606_latest_sample.raw[6],
                    (int)ad7606_latest_sample.raw[7],
                    (unsigned long)ad7606_diag.sample_count,
                    (unsigned long)ad7606_diag.timeout_count);
  if ((length > 0) && (length < (int)sizeof(line)))
  {
    ad7606_log_text(line, length);
  }
}

static void ad7606_log_self_test(void)
{
  static const char *const result_text[] = {
    "NOT_RUN", "PASS", "BUSY_STUCK_HIGH", "BUSY_DID_NOT_ASSERT",
    "BUSY_TIMEOUT", "DATA_SUSPICIOUS"
  };
  char line[96];
  uint32_t result = (uint32_t)ad7606_diag.self_test;
  int length;

  if (result >= (sizeof(result_text) / sizeof(result_text[0])))
  {
    result = 0U;
  }
  length = snprintf(line, sizeof(line),
                    "[AD7606] self-test=%lu (%s), range=+/-10V, OS=none\r\n",
                    (unsigned long)ad7606_diag.self_test, result_text[result]);
  if ((length > 0) && (length < (int)sizeof(line)))
  {
    ad7606_log_text(line, length);
  }
}

static void ad7606_log_control_pins(void)
{
  char line[96];
  int length = snprintf(line, sizeof(line),
                        "[AD7606] pins: CONVST=%u RESET=%u BUSY=%u BUSY_PULLUP=%u\r\n",
                        (unsigned int)HAL_GPIO_ReadPin(BSP_AD7606_CONVST_GPIO_Port,
                                                      BSP_AD7606_CONVST_Pin),
                        (unsigned int)HAL_GPIO_ReadPin(BSP_AD7606_RESET_GPIO_Port,
                                                      BSP_AD7606_RESET_Pin),
                        (unsigned int)HAL_GPIO_ReadPin(BSP_AD7606_BUSY_GPIO_Port,
                                                      BSP_AD7606_BUSY_Pin),
                        (unsigned int)ad7606_busy_pullup_state);
  if ((length > 0) && (length < (int)sizeof(line)))
  {
    ad7606_log_text(line, length);
  }
}

static void ad7606_publish_to_datahub(int read_ok)
{
  DataHubAd7606Data hub_ad7606;
  uint8_t channel;

  DataHub_GetAd7606(&hub_ad7606);
  hub_ad7606.timestamp_ms = ad7606_latest_sample.timestamp_ms;
  hub_ad7606.sample_count = ad7606_diag.sample_count;
  hub_ad7606.timeout_count = ad7606_diag.timeout_count;
  hub_ad7606.ad7606_ready = (ad7606_diag.state == AD7606_SERVICE_STATE_RUNNING) ? 1 : 0;
  hub_ad7606.last_read_ok = read_ok;

  if (read_ok != 0)
  {
    for (channel = 0U; channel < BSP_AD7606_CHANNEL_COUNT; channel++)
    {
      hub_ad7606.raw[channel] = ad7606_latest_sample.raw[channel];
      hub_ad7606.mv[channel] = ad7606_latest_sample.mv[channel];
    }

    DataHub_PublishAd7606(&hub_ad7606);
  }
  else
  {
    DataHub_UpdateAd7606Status(ad7606_diag.sample_count,
                               ad7606_diag.timeout_count,
                               hub_ad7606.ad7606_ready,
                               0);
  }
}

static int ad7606_validate_config(const AD7606_SERVICE_Config *config)
{
  if (config == 0)
  {
    return 0;
  }

  if ((config->sample_period_ms == 0U) || (config->timeout_ms == 0U))
  {
    return 0;
  }

  if ((config->range != BSP_AD7606_RANGE_5V) &&
      (config->range != BSP_AD7606_RANGE_10V))
  {
    return 0;
  }

  if (config->oversampling > BSP_AD7606_OS_64)
  {
    return 0;
  }

  return 1;
}

void AD7606_SERVICE_Init(void)
{
  ad7606_diag.state = AD7606_SERVICE_STATE_IDLE;
  ad7606_diag.sample_count = 0U;
  ad7606_diag.timeout_count = 0U;
  ad7606_diag.last_sample_tick = HAL_GetTick();
  ad7606_diag.self_test = BSP_AD7606_TEST_NOT_RUN;
  ad7606_last_log_tick = ad7606_diag.last_sample_tick;

  AD7606_LOG_LITERAL("\r\n[AD7606] init: dual-DOUT serial, PD4-PD9/PE3-PE7\r\n");

  BSP_AD7606_Init(ad7606_config.range, ad7606_config.oversampling);
  ad7606_diag.self_test = BSP_AD7606_RunSelfTest(&ad7606_latest_sample,
                                                 ad7606_config.timeout_ms);
  ad7606_busy_pullup_state = BSP_AD7606_ProbeBusyWithPullup();
  if ((ad7606_diag.self_test == BSP_AD7606_TEST_PASS) ||
      (ad7606_diag.self_test == BSP_AD7606_TEST_DATA_SUSPICIOUS))
  {
    ad7606_diag.state = AD7606_SERVICE_STATE_RUNNING;
    ad7606_diag.sample_count = 1U;
    ad7606_publish_to_datahub(1);
  }
  else
  {
    ad7606_diag.state = AD7606_SERVICE_STATE_ERROR;
    ad7606_publish_to_datahub(0);
  }
  ad7606_log_self_test();
  ad7606_log_control_pins();
  if (ad7606_diag.state == AD7606_SERVICE_STATE_RUNNING)
  {
    ad7606_log_sample("first");
  }
  DataHub_UpdateAd7606Status(ad7606_diag.sample_count,
                             ad7606_diag.timeout_count,
                             ad7606_diag.state == AD7606_SERVICE_STATE_RUNNING,
                             ad7606_diag.state == AD7606_SERVICE_STATE_RUNNING);
}

void AD7606_SERVICE_Process(void)
{
  uint32_t now = HAL_GetTick();

  if (ad7606_diag.state != AD7606_SERVICE_STATE_RUNNING)
  {
    if ((now - ad7606_last_log_tick) >= 1000U)
    {
      ad7606_last_log_tick = now;
      ad7606_diag.self_test = BSP_AD7606_RunSelfTest(&ad7606_latest_sample,
                                                     ad7606_config.timeout_ms);
      ad7606_busy_pullup_state = BSP_AD7606_ProbeBusyWithPullup();
      ad7606_log_self_test();
      ad7606_log_control_pins();
      if ((ad7606_diag.self_test == BSP_AD7606_TEST_PASS) ||
          (ad7606_diag.self_test == BSP_AD7606_TEST_DATA_SUSPICIOUS))
      {
        ad7606_diag.state = AD7606_SERVICE_STATE_RUNNING;
        ad7606_diag.sample_count++;
        ad7606_publish_to_datahub(1);
        ad7606_log_sample("recovered");
      }
    }
    return;
  }

  if ((now - ad7606_diag.last_sample_tick) < ad7606_config.sample_period_ms)
  {
    return;
  }

  ad7606_diag.last_sample_tick = now;

  if (BSP_AD7606_ReadSample(&ad7606_latest_sample, ad7606_config.timeout_ms))
  {
    ad7606_diag.sample_count++;
    ad7606_publish_to_datahub(1);
  }
  else
  {
    ad7606_diag.timeout_count++;
    ad7606_publish_to_datahub(0);
  }

  if ((now - ad7606_last_log_tick) >= 1000U)
  {
    ad7606_last_log_tick = now;
    ad7606_log_sample((ad7606_diag.timeout_count == 0U) ? "running" : "check");
  }
}

const BSP_AD7606_Sample *AD7606_SERVICE_GetLatestSample(void)
{
  return &ad7606_latest_sample;
}

void AD7606_SERVICE_GetDiagnostics(AD7606_SERVICE_Diagnostics *diagnostics)
{
  if (diagnostics == 0)
  {
    return;
  }

  *diagnostics = ad7606_diag;
}

const AD7606_SERVICE_Config *AD7606_SERVICE_GetConfig(void)
{
  return &ad7606_config;
}

int AD7606_SERVICE_SetConfig(const AD7606_SERVICE_Config *config)
{
  if (!ad7606_validate_config(config))
  {
    return 0;
  }

  ad7606_config = *config;
  BSP_AD7606_SetRange(ad7606_config.range);
  BSP_AD7606_SetOversampling(ad7606_config.oversampling);
  ad7606_diag.state = AD7606_SERVICE_STATE_RUNNING;
  DataHub_UpdateAd7606Status(ad7606_diag.sample_count,
                             ad7606_diag.timeout_count,
                             1,
                             0);

  return 1;
}
