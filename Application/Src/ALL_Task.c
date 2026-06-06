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

    xTaskResumeAll();
    vTaskDelete(NULL);
}






