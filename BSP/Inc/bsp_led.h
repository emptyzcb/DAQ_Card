#ifndef BSP_LED_H
#define BSP_LED_H

#include "main.h"

#ifndef BSP_LED_GPIO_Port
#define BSP_LED_GPIO_Port  PA0_LED_GPIO_Port
#define BSP_LED_Pin        PA0_LED_Pin
#endif

void BSP_LED_Init(void);
void BSP_LED_On(void);
void BSP_LED_Off(void);
void BSP_LED_Toggle(void);

#endif /* BSP_LED_H */
