#include "sys.h"
//启动线程
TaskHandle_t TASKS_START_Handler;
configSTACK_DEPTH_TYPE DEPTH_TYPE_TASKS_START = 128;
#define TASKS_START_Priority 10
void TASKS_START(void *arg);

//姿态解算：Attitude_Estimation
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
