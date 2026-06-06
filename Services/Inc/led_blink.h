#ifndef LED_BLINK_H
#define LED_BLINK_H

#include <stdint.h>

void LED_BLINK_Init(void);
void LED_BLINK_Process(void);
void LED_BLINK_SetInterval(uint32_t ms);

#endif /* LED_BLINK_H */
