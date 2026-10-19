#include "sys.h"

/*
 * RS485 主机采集任务
 * ------------------
 * USART1 RS485 现在作为 Modbus RTU 主机，按轮询表周期读取外部传感器
 * 从机的保持寄存器（见 modbus_master.c）。本任务不再承担从机应答、
 * 上位机数据上报或 IOCF 下载——这些业务已迁移到以太网。
 *
 * 周期：10 ms。所有 UART 等待均带有限超时，单条总线故障不会
 * 阻塞其他任务。
 */

void AppTask_ModbusRtu(void *argument)
{
  (void)argument;

  MODBUS_MASTER_Init();

  for (;;)
  {
    MODBUS_MASTER_Poll(HAL_GetTick());
    vTaskDelay(pdMS_TO_TICKS(10U));
  }
}
