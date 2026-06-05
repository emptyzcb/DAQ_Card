/* USER CODE BEGIN Header */
/**
  ******************************************************************************
  * File Name          : freertos.c
  * Description        : Code for freertos applications
  ******************************************************************************
  * @attention
  *
  * Copyright (c) 2026 STMicroelectronics.
  * All rights reserved.
  *
  * This software is licensed under terms that can be found in the LICENSE file
  * in the root directory of this software component.
  * If no LICENSE file comes with this software, it is provided AS-IS.
  *
  ******************************************************************************
  */
/* USER CODE END Header */

/* Includes ------------------------------------------------------------------*/
#include "FreeRTOS.h"
#include "task.h"
#include "main.h"
#include "cmsis_os.h"

/* Private includes ----------------------------------------------------------*/
/* USER CODE BEGIN Includes */
#include "app.h"
/* USER CODE END Includes */

/* Private typedef -----------------------------------------------------------*/
/* USER CODE BEGIN PTD */

/* USER CODE END PTD */

/* Private define ------------------------------------------------------------*/
/* USER CODE BEGIN PD */

//启动任务定义,该任务用于创建其他任务
TaskHandle_t  TASKS_START_Handler;
configSTACK_DEPTH_TYPE  DEPTH_TYPE_TASKS_START=128;
#define TASKS_START_Priority 10
void TASKS_START(void *arg);

//TASK01相关参数配置//初始时被挂起，中断触发而被其他任务恢复
TaskHandle_t  TASK01_PRINTF_Handler;
configSTACK_DEPTH_TYPE  DEPTH_TYPE_TASK01_PRINTF=128;
#define TASK01_PRINTF_Priority 3
void TASK01_PRINTF(void *arg);

//TASK02相关参数配置
TaskHandle_t  TASK02_Resume_Handler;
configSTACK_DEPTH_TYPE  DEPTH_TYPE_TASK02_Resume=128;
#define TASK02_Resume_Priority 2
void TASK02_Resume(void *arg);

/* USER CODE END PD */

/* Private macro -------------------------------------------------------------*/
/* USER CODE BEGIN PM */

/* USER CODE END PM */

/* Private variables ---------------------------------------------------------*/
/* USER CODE BEGIN Variables */

/* USER CODE END Variables */
osThreadId defaultTaskHandle;
osThreadId ledTaskHandle;

/* Private function prototypes -----------------------------------------------*/
/* USER CODE BEGIN FunctionPrototypes */
void vMyFreeRTOS_Task_Start(void);
void TASKS_START(void *arg);

/* USER CODE END FunctionPrototypes */

void StartDefaultTask(void const * argument);
void StartLedTask(void const * argument);

void MX_FREERTOS_Init(void); /* (MISRA C 2004 rule 8.1) */

/* GetIdleTaskMemory prototype (linked to static allocation support) */
void vApplicationGetIdleTaskMemory( StaticTask_t **ppxIdleTaskTCBBuffer, StackType_t **ppxIdleTaskStackBuffer, uint32_t *pulIdleTaskStackSize );

/* USER CODE BEGIN GET_IDLE_TASK_MEMORY */
static StaticTask_t xIdleTaskTCBBuffer;
static StackType_t xIdleStack[configMINIMAL_STACK_SIZE];

void vApplicationGetIdleTaskMemory( StaticTask_t **ppxIdleTaskTCBBuffer, StackType_t **ppxIdleTaskStackBuffer, uint32_t *pulIdleTaskStackSize )
{
  *ppxIdleTaskTCBBuffer = &xIdleTaskTCBBuffer;
  *ppxIdleTaskStackBuffer = &xIdleStack[0];
  *pulIdleTaskStackSize = configMINIMAL_STACK_SIZE;
  /* place for user code */
}
/* USER CODE END GET_IDLE_TASK_MEMORY */

/**
  * @brief  FreeRTOS initialization
  * @param  None
  * @retval None
  */
void MX_FREERTOS_Init(void) {
  /* USER CODE BEGIN Init */

  /* USER CODE END Init */

  /* USER CODE BEGIN RTOS_MUTEX */
  /* add mutexes, ... */
  /* USER CODE END RTOS_MUTEX */

  /* USER CODE BEGIN RTOS_SEMAPHORES */
  /* add semaphores, ... */
  /* USER CODE END RTOS_SEMAPHORES */

  /* USER CODE BEGIN RTOS_TIMERS */
  /* start timers, add new ones, ... */
  /* USER CODE END RTOS_TIMERS */

  /* USER CODE BEGIN RTOS_QUEUES */
  /* add queues, ... */
  /* USER CODE END RTOS_QUEUES */

  /* Create the thread(s) */
  /* definition and creation of defaultTask */
  osThreadDef(defaultTask, StartDefaultTask, osPriorityNormal, 0, 128);
  defaultTaskHandle = osThreadCreate(osThread(defaultTask), NULL);
  
  /* USER CODE BEGIN RTOS_THREADS */
  /* add threads, ... */
  vMyFreeRTOS_Task_Start();
    
  osThreadDef(ledTask, StartLedTask, osPriorityBelowNormal, 0, 128);
  ledTaskHandle = osThreadCreate(osThread(ledTask), NULL);
  /* USER CODE END RTOS_THREADS */


}

/* USER CODE BEGIN Header_StartDefaultTask */
/**
  * @brief  Function implementing the defaultTask thread.
  * @param  argument: Not used
  * @retval None
  */
/* USER CODE END Header_StartDefaultTask */
void StartDefaultTask(void const * argument)
{
  /* USER CODE BEGIN StartDefaultTask */
  APP_Init();

  /* Infinite loop */
  for(;;)
  {
    APP_RunOnce();
    osDelay(1);
  }
  /* USER CODE END StartDefaultTask */
}

/* Private application code --------------------------------------------------*/
/* USER CODE BEGIN Application */
//此函数不是线程
void vMyFreeRTOS_Task_Start(void)
{
    xTaskCreate(
    
                TASKS_START,
                "TASKS_START",
                DEPTH_TYPE_TASKS_START,
                NULL,
                TASKS_START_Priority,
                &TASKS_START_Handler
    
                );
    
    vTaskStartScheduler();
}

//此任务用于创建其他任务，创建完后被删除
void TASKS_START(void *arg)
{
    //关闭任务调度器
    vTaskSuspendAll();	

    xTaskCreate(

                TASK01_PRINTF,
                "TASK01_PRINTF",
                DEPTH_TYPE_TASK01_PRINTF,
                NULL,
                TASK01_PRINTF_Priority,
                &TASK01_PRINTF_Handler
    );
    
    
    //删除本任务
    vTaskDelete(NULL);    
    //开启任务调度器
    xTaskResumeAll();
}


void StartLedTask(void const * argument)
{
  (void)argument;
  APP_RunLedBlink();
}



/* USER CODE END Application */
