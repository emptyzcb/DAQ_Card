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
#define APP_TASK_MODBUS_STACK_DEPTH        512U
#define APP_TASK_MODBUS_PRIORITY           4U
#define APP_TASK_IO_LOGIC_STACK_DEPTH      512U
#define APP_TASK_IO_LOGIC_PRIORITY         3U
#define APP_TASK_AD7606_STACK_DEPTH        512U
#define APP_TASK_AD7606_PRIORITY           3U
#define APP_TASK_DIGITAL_IO_STACK_DEPTH    256U
#define APP_TASK_DIGITAL_IO_PRIORITY       3U
#define APP_TASK_ETH_STACK_DEPTH           1024U
#define APP_TASK_ETH_PRIORITY              2U

/*
 * xTaskCreate() stack depths are StackType_t elements.  Keep a compile-time
 * reserve for task control blocks, the CMSIS default task and future RTOS
 * objects so adding a task cannot silently prevent the Ethernet task from
 * being created.
 */
#define APP_TASK_STACK_DEPTH_TOTAL \
  (APP_TASK_MODBUS_STACK_DEPTH + APP_TASK_IO_LOGIC_STACK_DEPTH + \
   APP_TASK_AD7606_STACK_DEPTH + \
   APP_TASK_DIGITAL_IO_STACK_DEPTH + APP_TASK_ETH_STACK_DEPTH)
#define APP_TASK_HEAP_RESERVE_BYTES 8192U

typedef char AppTaskHeapBudgetCheck[
  (configTOTAL_HEAP_SIZE >=
   ((APP_TASK_STACK_DEPTH_TOTAL * sizeof(StackType_t)) +
    APP_TASK_HEAP_RESERVE_BYTES)) ? 1 : -1];

static TaskHandle_t g_modbus_task_handle;
static TaskHandle_t g_io_logic_task_handle;
static TaskHandle_t g_ad7606_task_handle;
static TaskHandle_t g_digital_io_task_handle;
static TaskHandle_t g_eth_task_handle;

BaseType_t APP_TASKS_Create(void)
{
  BaseType_t result;

  /*
   * Modbus master task: RS485 主机，按轮询表周期读取外部传感器从机的
   * 保持寄存器。周期 10 ms。所有 UART 操作带有限超时，单条从机故障
   * 不阻塞其他任务。原 RS485 从机业务（寄存器读写 + IOCF 下载）已
   * 迁移到以太网任务。
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
   * Digital IO task: extends the X1/X2 hardware counters, debounces X3-X5,
   * snapshots the X6-X8 encoder and publishes the complete input state.
   * Period: 1 ms. Priority 3 guarantees bounded service of 16-bit counters;
   * pulse edges themselves are counted in hardware and do not depend on it.
   */
  result = xTaskCreate(AppTask_DigitalIo, "digital_io",
                       APP_TASK_DIGITAL_IO_STACK_DEPTH, NULL,
                       APP_TASK_DIGITAL_IO_PRIORITY, &g_digital_io_task_handle);
  if (result != pdPASS) { return pdFAIL; }

  /*
   * Ethernet task: W5500 Modbus TCP server on port 5000. It publishes
   * onboard and external-sensor data, accepts the runtime RS485 polling
   * table, and downloads IOCF rule images. Period: 10 ms, priority 2;
   * all SPI transactions are blocking and confined to this task.
   */
  result = xTaskCreate(AppTask_Eth, "eth",
                       APP_TASK_ETH_STACK_DEPTH, NULL,
                       APP_TASK_ETH_PRIORITY, &g_eth_task_handle);
  if (result != pdPASS) { return pdFAIL; }

  return pdPASS;
}
