#include "sys.h"
//�����߳�
TaskHandle_t TASKS_START_Handler;
configSTACK_DEPTH_TYPE DEPTH_TYPE_TASKS_START = 128;
#define TASKS_START_Priority 10
void TASKS_START(void *arg);

//��̬���㣺Attitude_Estimation
TaskHandle_t Task_att_est_Handler;
configSTACK_DEPTH_TYPE DEPTH_TYPE_Task_att_est = 1024;
#define Task_att_est_Priority 3
void Task_att_est(void *arg);

//USART3 echo test: RX DMA/ringbuf -> TX
TaskHandle_t Task_uart3_echo_Handler;
configSTACK_DEPTH_TYPE DEPTH_TYPE_Task_uart3_echo = 512;
#define Task_uart3_echo_Priority 4
void Task_uart3_echo(void *arg);

//AD7606 sampling service task
TaskHandle_t Task_ad7606_Handler;
configSTACK_DEPTH_TYPE DEPTH_TYPE_Task_ad7606 = 512;
#define Task_ad7606_Priority 3
void Task_ad7606(void *arg);

//Digital IO smoke-test task
TaskHandle_t Task_digital_io_Handler;
configSTACK_DEPTH_TYPE DEPTH_TYPE_Task_digital_io = 256;
#define Task_digital_io_Priority 2
void Task_digital_io(void *arg);

//Modbus RTU slave task (USART1 RS485)
TaskHandle_t Task_modbus_Handler;
configSTACK_DEPTH_TYPE DEPTH_TYPE_Task_modbus = 512;
#define Task_modbus_Priority 4
void Task_modbus(void *arg);

//Script runner task (flash-stored script interpreter, scheme B)
TaskHandle_t Task_script_Handler;
configSTACK_DEPTH_TYPE DEPTH_TYPE_Task_script = 512;
#define Task_script_Priority 2
void Task_script(void *arg);

void vMyFreeRTOS_Task_Start(void)
{
    xTaskCreate(
        TASKS_START,
        "TASKS_START",
        DEPTH_TYPE_TASKS_START,
        NULL,
        TASKS_START_Priority,
        &TASKS_START_Handler);
}


void TASKS_START(void *arg)
{
    (void)arg;

    vTaskSuspendAll();

    xTaskCreate(
        Task_att_est,
        "Task_att_est",
        DEPTH_TYPE_Task_att_est,
        NULL,
        Task_att_est_Priority,
        &Task_att_est_Handler);

    xTaskCreate(
        Task_uart3_echo,
        "Task_uart3_echo",
        DEPTH_TYPE_Task_uart3_echo,
        NULL,
        Task_uart3_echo_Priority,
        &Task_uart3_echo_Handler);

    xTaskCreate(
        Task_ad7606,
        "Task_ad7606",
        DEPTH_TYPE_Task_ad7606,
        NULL,
        Task_ad7606_Priority,
        &Task_ad7606_Handler);

    xTaskCreate(
        Task_digital_io,
        "Task_digital_io",
        DEPTH_TYPE_Task_digital_io,
        NULL,
        Task_digital_io_Priority,
        &Task_digital_io_Handler);

    xTaskCreate(
        Task_modbus,
        "Task_modbus",
        DEPTH_TYPE_Task_modbus,
        NULL,
        Task_modbus_Priority,
        &Task_modbus_Handler);

    xTaskCreate(
        Task_script,
        "Task_script",
        DEPTH_TYPE_Task_script,
        NULL,
        Task_script_Priority,
        &Task_script_Handler);

    xTaskResumeAll();
    vTaskDelete(NULL);
}

void Task_uart3_echo(void *arg)
{
    uint8_t rx_buf[10];
    uint32_t last_half_count = 0U;
    uint32_t last_full_count = 0U;
    uint32_t last_idle_count = 0U;
    uint32_t last_drop_count = 0U;

    (void)arg;

    for (;;)
    {
        uint16_t rx_len;
        uint32_t half_count;
        uint32_t full_count;
        uint32_t idle_count;
        uint32_t drop_count;

        RS485_UART_PumpRxFromDma();
        rx_len = RS485_UART_Read(rx_buf, (uint16_t)sizeof(rx_buf));

        if (rx_len > 0U)
        {
            (void)HAL_UART_Transmit(&huart3, rx_buf, rx_len, 100U);
        }

        half_count = RS485_UART_RxHalfIrqCount();
        full_count = RS485_UART_RxFullIrqCount();
        idle_count = RS485_UART_RxIdleIrqCount();
        drop_count = RS485_UART_RxDropped();

        if ((half_count != last_half_count) ||
            (full_count != last_full_count) ||
            (idle_count != last_idle_count) ||
            (drop_count != last_drop_count))
        {
            printf("USART3 RX irq: half=%lu full=%lu idle=%lu drop=%lu\r\n",
                   (unsigned long)half_count,
                   (unsigned long)full_count,
                   (unsigned long)idle_count,
                   (unsigned long)drop_count);

            last_half_count = half_count;
            last_full_count = full_count;
            last_idle_count = idle_count;
            last_drop_count = drop_count;
        }

        vTaskDelay(pdMS_TO_TICKS(1U));
    }
}

void Task_ad7606(void *arg)
{
    (void)arg;

    for (;;)
    {
        AD7606_SERVICE_Process();
        vTaskDelay(pdMS_TO_TICKS(1U));
    }
}

void Task_modbus(void *arg)
{
    (void)arg;

    for (;;)
    {
        MODBUS_SLAVE_Pump();
        MODBUS_SLAVE_Process();
        vTaskDelay(pdMS_TO_TICKS(1U));
    }
}

void Task_script(void *arg)
{
    (void)arg;

    /* Interpret the script stored in flash; starts automatically at
     * power-up when a valid script exists (scheme B). */
    SCRIPT_RUNNER_Init();
    SCRIPT_RUNNER_Start();
    SCRIPT_RUNNER_Run();
}
