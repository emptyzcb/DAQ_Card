#include "sys.h"

/*
 * Task configuration policy
 * -------------------------
 * Stack depths are FreeRTOS StackType_t elements, not bytes. Priorities are
 * relative within this product: communication is serviced first, acquisition
 * and deterministic rule execution follow, and background IO supervision is
 * lowest. Tasks must use finite waits so one failed peripheral cannot stall
 * unrelated product functions.
 */
#define APP_TASK_ATTITUDE_STACK_DEPTH      1024U
#define APP_TASK_ATTITUDE_PRIORITY         3U
#define APP_TASK_MODBUS_STACK_DEPTH        512U
#define APP_TASK_MODBUS_PRIORITY           4U
#define APP_TASK_IO_LOGIC_STACK_DEPTH      512U
#define APP_TASK_IO_LOGIC_PRIORITY         3U
#define APP_TASK_AD7606_STACK_DEPTH        512U
#define APP_TASK_AD7606_PRIORITY           3U
#define APP_TASK_DIGITAL_IO_STACK_DEPTH    256U
#define APP_TASK_DIGITAL_IO_PRIORITY       2U

static TaskHandle_t g_attitude_task_handle;
static TaskHandle_t g_modbus_task_handle;
static TaskHandle_t g_io_logic_task_handle;
static TaskHandle_t g_ad7606_task_handle;
static TaskHandle_t g_digital_io_task_handle;

BaseType_t APP_TASKS_Create(void)
{
  BaseType_t result;

  /*
   * Attitude task: acquires BMI270 data and publishes the fused orientation.
   * Input: IMU service. Output: DataHub IMU snapshot. Nominal period: 1 ms.
   * A large stack is reserved for floating-point fusion state. Read failures
   * are published as status and retried on the next finite-period iteration.
   */
  result = xTaskCreate(AppTask_Attitude, "attitude",
                       APP_TASK_ATTITUDE_STACK_DEPTH, NULL,
                       APP_TASK_ATTITUDE_PRIORITY, &g_attitude_task_handle);
  if (result != pdPASS) { return pdFAIL; }

  /*
   * Modbus task: receives RS485 frames, validates RTU requests and transmits
   * bounded responses. Input/output: USART1 RS485. Poll period: 1 ms.
   * It has the highest application priority to avoid serial frame loss; all
   * UART operations and protocol responses use finite timeouts.
   */
  result = xTaskCreate(AppTask_ModbusRtu, "modbus_rtu",
                       APP_TASK_MODBUS_STACK_DEPTH, NULL,
                       APP_TASK_MODBUS_PRIORITY, &g_modbus_task_handle);
  if (result != pdPASS) { return pdFAIL; }

  /*
   * IO logic task: evaluates the active persistent IOCF rule image against
   * debounced digital inputs and updates the eight outputs. Period: 10 ms.
   * Invalid or absent configuration drives the engine to its safe stop state.
   */
  result = xTaskCreate(AppTask_IoLogic, "io_logic",
                       APP_TASK_IO_LOGIC_STACK_DEPTH, NULL,
                       APP_TASK_IO_LOGIC_PRIORITY, &g_io_logic_task_handle);
  if (result != pdPASS) { return pdFAIL; }

  /*
   * AD7606 task: advances the non-blocking converter service and publishes
   * eight-channel samples to DataHub. Period: 1 ms. Hardware timeouts are
   * contained by the service and reported through product diagnostics.
   */
  result = xTaskCreate(AppTask_Ad7606, "ad7606",
                       APP_TASK_AD7606_STACK_DEPTH, NULL,
                       APP_TASK_AD7606_PRIORITY, &g_ad7606_task_handle);
  if (result != pdPASS) { return pdFAIL; }

  /*
   * Digital IO task: supervises the eight isolated inputs and output state.
   * Input/output: digital IO service. Period: 50 ms. It is background work,
   * so it runs below protocol, acquisition and rule-processing tasks.
   */
  result = xTaskCreate(AppTask_DigitalIo, "digital_io",
                       APP_TASK_DIGITAL_IO_STACK_DEPTH, NULL,
                       APP_TASK_DIGITAL_IO_PRIORITY, &g_digital_io_task_handle);
  if (result != pdPASS) { return pdFAIL; }

  return pdPASS;
}
