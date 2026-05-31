#ifndef BSP_LED_H
#define BSP_LED_H

#include "stm32f4xx_hal.h"

#ifndef BSP_LED_GPIO_Port
#define BSP_LED_GPIO_Port  GPIOB
#define BSP_LED_Pin        GPIO_PIN_0
#endif

void BSP_LED_Init(void);
void BSP_LED_On(void);
void BSP_LED_Off(void);
void BSP_LED_Toggle(void);

#endif /* BSP_LED_H */
