#ifndef AD7606_SERVICE_H
#define AD7606_SERVICE_H

#include <stdint.h>

#include "bsp_ad7606.h"

#ifdef __cplusplus
extern "C" {
#endif

typedef enum
{
  AD7606_SERVICE_STATE_IDLE = 0,
  AD7606_SERVICE_STATE_RUNNING,
  AD7606_SERVICE_STATE_ERROR
} AD7606_SERVICE_State;

typedef struct
{
  uint32_t sample_period_ms;
  uint32_t timeout_ms;
  BSP_AD7606_Range range;
  BSP_AD7606_Oversampling oversampling;
} AD7606_SERVICE_Config;

typedef struct
{
  AD7606_SERVICE_State state;
  uint32_t sample_count;
  uint32_t timeout_count;
  uint32_t last_sample_tick;
} AD7606_SERVICE_Diagnostics;

void AD7606_SERVICE_Init(void);
void AD7606_SERVICE_Process(void);
const BSP_AD7606_Sample *AD7606_SERVICE_GetLatestSample(void);
void AD7606_SERVICE_GetDiagnostics(AD7606_SERVICE_Diagnostics *diagnostics);
const AD7606_SERVICE_Config *AD7606_SERVICE_GetConfig(void);
int AD7606_SERVICE_SetConfig(const AD7606_SERVICE_Config *config);

#ifdef __cplusplus
}
#endif

#endif /* AD7606_SERVICE_H */
