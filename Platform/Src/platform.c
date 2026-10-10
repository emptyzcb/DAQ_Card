#include "sys.h"

void PLATFORM_Init(void)
{
  /* Outputs are initialized first and forced to their inactive state. */
  DIGITAL_IO_SERVICE_Init();
  DIGITAL_IO_SERVICE_AllOutputsOff();

  /*
   * 初始化0~20 mA和0~10 V模拟输出，并在规则引擎接管之前强制输出为0。
   * 模拟输出的动态波形由IO逻辑任务周期推进，不在BSP内部阻塞等待。
   */
  BSP_ANALOG_OUTPUT_Init();

  /* Persistent IO rules must be loaded before the rule task starts. */
  IO_CONFIG_Init();

  BSP_CONSOLE_Init();
  RS485_UART_Init();
  CAN_SERVICE_Init();
  AD7606_SERVICE_Init();

  /*
   * 初始化板载W25Q128并校验JEDEC型号。驱动内部保存初始化状态，
   * Flash异常不会阻塞其他采集与通信任务，业务层可通过BSP状态接口查询。
   */
  (void)BSP_W25Q128_Init();

  LED_BLINK_Init();
}

void PLATFORM_ProcessLedBlink(void)
{
  LED_BLINK_Process();
}
