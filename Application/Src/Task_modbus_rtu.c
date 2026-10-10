#include "sys.h"

void AppTask_ModbusRtu(void *argument)
{
  uint8_t request[RS485_UART_FRAME_MAX_SIZE];
  uint8_t response[MODBUS_RTU_MAX_ADU_SIZE];

  (void)argument;

  for (;;)
  {
    uint16_t request_length = 0U;
    int receive_result = RS485_UART_TryReceiveFrame(request,
                                                    (uint16_t)sizeof(request),
                                                    &request_length);

    if (receive_result > 0)
    {
      uint16_t response_length = MODBUS_RTU_HandleRequest(request,
                                                           request_length,
                                                           response,
                                                           (uint16_t)sizeof(response));

      /*
       * A normal Modbus response echoes the request function code, while an
       * exception response sets bit 7. Use this to mark the frame as valid
       * or invalid for the link statistics and the PA0 diagnostic LED.
       */
      RS485_UART_DiagnosticMarkFrame((response_length > 1U) &&
                                     (response[1] == request[1]));

      if (response_length > 0U)
      {
        (void)RS485_UART_Send(response, response_length, 100U);
      }
    }
    else if (receive_result < 0)
    {
      /* Capacity error: the received frame exceeds the request buffer. */
      RS485_UART_DiagnosticMarkFrame(0U);
    }

    RS485_UART_DiagnosticProcess();
    vTaskDelay(pdMS_TO_TICKS(1U));
  }
}
