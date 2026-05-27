#include "daq_service.h"

static DAQ_SERVICE_State daq_state;

void DAQ_SERVICE_Init(void)
{
  daq_state = DAQ_SERVICE_STATE_IDLE;
}

void DAQ_SERVICE_Process(void)
{
  /* Sampling and transport processing will be attached here. */
}

DAQ_SERVICE_State DAQ_SERVICE_GetState(void)
{
  return daq_state;
}
