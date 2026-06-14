#include "ad7606_service.h"
#include "datahub.h"

static AD7606_SERVICE_Config ad7606_config = {
  10U,
  10U,
  BSP_AD7606_RANGE_10V,
  BSP_AD7606_OS_NONE
};

static AD7606_SERVICE_Diagnostics ad7606_diag;
static BSP_AD7606_Sample ad7606_latest_sample;

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

  BSP_AD7606_Init(ad7606_config.range, ad7606_config.oversampling);
  ad7606_diag.state = AD7606_SERVICE_STATE_RUNNING;
  DataHub_UpdateAd7606Status(ad7606_diag.sample_count,
                             ad7606_diag.timeout_count,
                             1,
                             0);
}

void AD7606_SERVICE_Process(void)
{
  uint32_t now = HAL_GetTick();

  if (ad7606_diag.state != AD7606_SERVICE_STATE_RUNNING)
  {
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
    ad7606_diag.state = AD7606_SERVICE_STATE_ERROR;
    ad7606_publish_to_datahub(0);
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
