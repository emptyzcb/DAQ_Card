#include "sys.h"

static DIGITAL_IO_SERVICE_Diagnostics digital_io_diag;

void DIGITAL_IO_SERVICE_Init(void)
{
  BSP_DIGITAL_IO_Init();

  digital_io_diag.initialized = 1U;
  digital_io_diag.input_mask = BSP_DIGITAL_IO_ReadInputMask();
  digital_io_diag.output_mask = BSP_DIGITAL_IO_GetOutputMask();
  digital_io_diag.scan_count = 0U;
  digital_io_diag.output_write_count = 0U;
  digital_io_diag.last_scan_tick = HAL_GetTick();
  digital_io_diag.last_output_tick = 0U;
}

uint16_t DIGITAL_IO_SERVICE_ReadInputs(void)
{
  if (digital_io_diag.initialized == 0U)
  {
    return 0U;
  }

  digital_io_diag.input_mask = BSP_DIGITAL_IO_ReadInputMask();
  digital_io_diag.scan_count++;
  digital_io_diag.last_scan_tick = HAL_GetTick();

  return digital_io_diag.input_mask;
}

int DIGITAL_IO_SERVICE_ReadInput(BSP_DIGITAL_IO_Input input)
{
  if (digital_io_diag.initialized == 0U)
  {
    return 0;
  }

  return BSP_DIGITAL_IO_ReadInputActive(input);
}

void DIGITAL_IO_SERVICE_SetOutput(BSP_DIGITAL_IO_Output output, int active)
{
  if (digital_io_diag.initialized == 0U)
  {
    return;
  }

  BSP_DIGITAL_IO_SetOutput(output, active);
  digital_io_diag.output_mask = BSP_DIGITAL_IO_GetOutputMask();
  digital_io_diag.output_write_count++;
  digital_io_diag.last_output_tick = HAL_GetTick();
}

void DIGITAL_IO_SERVICE_SetOutputMask(uint16_t active_mask)
{
  if (digital_io_diag.initialized == 0U)
  {
    return;
  }

  BSP_DIGITAL_IO_SetOutputMask(active_mask);
  digital_io_diag.output_mask = BSP_DIGITAL_IO_GetOutputMask();
  digital_io_diag.output_write_count++;
  digital_io_diag.last_output_tick = HAL_GetTick();
}

uint16_t DIGITAL_IO_SERVICE_GetOutputMask(void)
{
  if (digital_io_diag.initialized == 0U)
  {
    return 0U;
  }

  digital_io_diag.output_mask = BSP_DIGITAL_IO_GetOutputMask();
  return digital_io_diag.output_mask;
}

void DIGITAL_IO_SERVICE_AllOutputsOff(void)
{
  DIGITAL_IO_SERVICE_SetOutputMask(0U);
}

void DIGITAL_IO_SERVICE_GetDiagnostics(DIGITAL_IO_SERVICE_Diagnostics *diagnostics)
{
  if (diagnostics == NULL)
  {
    return;
  }

  digital_io_diag.input_mask = DIGITAL_IO_SERVICE_ReadInputs();
  digital_io_diag.output_mask = DIGITAL_IO_SERVICE_GetOutputMask();
  *diagnostics = digital_io_diag;
}
