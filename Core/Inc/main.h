/* USER CODE BEGIN Header */
/**
  ******************************************************************************
  * @file           : main.h
  * @brief          : Header for main.c file.
  *                   This file contains the common defines of the application.
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

/* Define to prevent recursive inclusion -------------------------------------*/
#ifndef __MAIN_H
#define __MAIN_H

#ifdef __cplusplus
extern "C" {
#endif

/* Includes ------------------------------------------------------------------*/
#include "stm32h7xx_hal.h"

/* Private includes ----------------------------------------------------------*/
/* USER CODE BEGIN Includes */

/* USER CODE END Includes */

/* Exported types ------------------------------------------------------------*/
/* USER CODE BEGIN ET */

/* USER CODE END ET */

/* Exported constants --------------------------------------------------------*/
/* USER CODE BEGIN EC */

/* USER CODE END EC */

/* Exported macro ------------------------------------------------------------*/
/* USER CODE BEGIN EM */

/* USER CODE END EM */

/* Exported functions prototypes ---------------------------------------------*/
void Error_Handler(void);

/* USER CODE BEGIN EFP */

/* USER CODE END EFP */

/* Private defines -----------------------------------------------------------*/
#define RS485_DE_RE_Pin GPIO_PIN_15
#define RS485_DE_RE_GPIO_Port GPIOE

/* USER CODE BEGIN Private defines */
#define PA0_LED_Pin GPIO_PIN_0
#define PA0_LED_GPIO_Port GPIOA

#define RS485_DIR_Pin GPIO_PIN_12
#define RS485_DIR_GPIO_Port GPIOA

/* PA0 LED is active-high: GPIO high turns the indicator on. */
#define PA0_LED_ON GPIO_PIN_SET
#define PA0_LED_OFF GPIO_PIN_RESET

/* Verified RS485 direction polarity: high transmits, low receives. */
#define RS485_DIR_TX GPIO_PIN_SET
#define RS485_DIR_RX GPIO_PIN_RESET

/*
 * RS485 communication diagnostic LED (PA0).
 *  - OFF   : no valid RS485 frame received within the watchdog window.
 *  - ON    : the last received frame passed Modbus validation.
 *  - BLINK : the last received frame failed validation (CRC/address/length).
 * Set to 0U to disable the LED service and its per-frame bookkeeping overhead.
 */
#define RS485_COMM_LED_DIAGNOSTIC 1U

/*
 * 固件版本号（联合控制器平台化移植 V1.0.0 起）。
 * 每次修改固件后递增，串口启动日志打印以便上位机确认烧录版本。
 */
#define APP_FIRMWARE_VERSION   "V1.1.0"

/* USER CODE END Private defines */

#ifdef __cplusplus
}
#endif

#endif /* __MAIN_H */
