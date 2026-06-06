#include "sys.h"

static uint32_t blink_interval_ms = 500U;
static uint32_t last_toggle_tick;

void LED_BLINK_Init(void)
{
  BSP_LED_Init();
  BSP_LED_On();
  last_toggle_tick = HAL_GetTick();
}

void LED_BLINK_Process(void)
{
  uint32_t now = HAL_GetTick();

  if ((now - last_toggle_tick) >= blink_interval_ms)
  {
    last_toggle_tick = now;
    BSP_LED_Toggle();
  }
}

void LED_BLINK_SetInterval(uint32_t ms)
{
  if (ms > 0U)
  {
    blink_interval_ms = ms;
  }
}
