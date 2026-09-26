#include "sys.h"

TaskHandle_t TASKS_START_Handler;
configSTACK_DEPTH_TYPE DEPTH_TYPE_TASKS_START = 128;
#define TASKS_START_Priority 10
void TASKS_START(void *arg);

TaskHandle_t Task_att_est_Handler;
configSTACK_DEPTH_TYPE DEPTH_TYPE_Task_att_est = 1024;
#define Task_att_est_Priority 3
void Task_att_est(void *arg);

TaskHandle_t Task_modbus_rtu_Handler;
configSTACK_DEPTH_TYPE DEPTH_TYPE_Task_modbus_rtu = 512;
#define Task_modbus_rtu_Priority 4

TaskHandle_t Task_io_logic_Handler;
configSTACK_DEPTH_TYPE DEPTH_TYPE_Task_io_logic = 512;
#define Task_io_logic_Priority 3

TaskHandle_t Task_ad7606_Handler;
configSTACK_DEPTH_TYPE DEPTH_TYPE_Task_ad7606 = 512;
#define Task_ad7606_Priority 3
void Task_ad7606(void *arg);

TaskHandle_t Task_digital_io_Handler;
configSTACK_DEPTH_TYPE DEPTH_TYPE_Task_digital_io = 256;
#define Task_digital_io_Priority 2
void Task_digital_io(void *arg);

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
        Task_modbus_rtu,
        "Task_modbus_rtu",
        DEPTH_TYPE_Task_modbus_rtu,
        NULL,
        Task_modbus_rtu_Priority,
        &Task_modbus_rtu_Handler);

    xTaskCreate(
        Task_io_logic,
        "Task_io_logic",
        DEPTH_TYPE_Task_io_logic,
        NULL,
        Task_io_logic_Priority,
        &Task_io_logic_Handler);

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

    xTaskResumeAll();
    vTaskDelete(NULL);
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
