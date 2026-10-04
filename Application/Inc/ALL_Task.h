#ifndef ALL_TASK_H
#define ALL_TASK_H

#include "FreeRTOS.h"
#include "task.h"

/**
 * @brief Create all product application tasks before the scheduler starts.
 * @return pdPASS when every task was created; pdFAIL after any failure.
 *
 * This function does not start the scheduler. The caller owns system-level
 * fault handling so a partially created task set can never enter production.
 */
BaseType_t APP_TASKS_Create(void);

#endif /* ALL_TASK_H */
