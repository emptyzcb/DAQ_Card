#ifndef DAQ_SERVICE_H
#define DAQ_SERVICE_H

typedef enum
{
  DAQ_SERVICE_STATE_IDLE = 0,
  DAQ_SERVICE_STATE_RUNNING,
  DAQ_SERVICE_STATE_ERROR
} DAQ_SERVICE_State;

void DAQ_SERVICE_Init(void);
void DAQ_SERVICE_Process(void);
DAQ_SERVICE_State DAQ_SERVICE_GetState(void);

#endif /* DAQ_SERVICE_H */
