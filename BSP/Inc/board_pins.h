#ifndef BOARD_PINS_H
#define BOARD_PINS_H

#include "stm32h7xx_hal.h"

/*
 * Board pin map from the project schematic and pin definition PDF, page 2.
 * Keep these macros as the hardware source of truth for BSP adapters.
 */

/* BMI270 IMU, shared SPI-style signal group */
#define BOARD_BMI270_SCK_GPIO_Port          GPIOB
#define BOARD_BMI270_SCK_Pin                GPIO_PIN_3
#define BOARD_BMI270_MISO_GPIO_Port         GPIOB
#define BOARD_BMI270_MISO_Pin               GPIO_PIN_4
#define BOARD_BMI270_MOSI_GPIO_Port         GPIOB
#define BOARD_BMI270_MOSI_Pin               GPIO_PIN_5
#define BOARD_BMI270_CS_GPIO_Port           GPIOC
#define BOARD_BMI270_CS_Pin                 GPIO_PIN_4

/* Debug UART */
#define BOARD_DEBUG_UART_TX_GPIO_Port       GPIOA
#define BOARD_DEBUG_UART_TX_Pin             GPIO_PIN_2
#define BOARD_DEBUG_UART_RX_GPIO_Port       GPIOA
#define BOARD_DEBUG_UART_RX_Pin             GPIO_PIN_3

/* TF card */
#define BOARD_TF_SD_D0_GPIO_Port            GPIOC
#define BOARD_TF_SD_D0_Pin                  GPIO_PIN_8
#define BOARD_TF_SD_D1_GPIO_Port            GPIOC
#define BOARD_TF_SD_D1_Pin                  GPIO_PIN_9
#define BOARD_TF_SD_D2_GPIO_Port            GPIOC
#define BOARD_TF_SD_D2_Pin                  GPIO_PIN_10
#define BOARD_TF_SD_D3_GPIO_Port            GPIOC
#define BOARD_TF_SD_D3_Pin                  GPIO_PIN_11
#define BOARD_TF_SD_CLK_GPIO_Port           GPIOC
#define BOARD_TF_SD_CLK_Pin                 GPIO_PIN_12
#define BOARD_TF_SD_CMD_GPIO_Port           GPIOD
#define BOARD_TF_SD_CMD_Pin                 GPIO_PIN_2

/* SPI NOR flash */
#define BOARD_SPI_NOR_CLK_GPIO_Port         GPIOB
#define BOARD_SPI_NOR_CLK_Pin               GPIO_PIN_3
#define BOARD_SPI_NOR_MISO_GPIO_Port        GPIOB
#define BOARD_SPI_NOR_MISO_Pin              GPIO_PIN_4
#define BOARD_SPI_NOR_MOSI_GPIO_Port        GPIOB
#define BOARD_SPI_NOR_MOSI_Pin              GPIO_PIN_5
#define BOARD_SPI_NOR_CS_GPIO_Port          GPIOD
#define BOARD_SPI_NOR_CS_Pin                GPIO_PIN_3

/* QSPI NOR flash */
#define BOARD_QSPI_CLK_GPIO_Port            GPIOB
#define BOARD_QSPI_CLK_Pin                  GPIO_PIN_2
#define BOARD_QSPI_BK1_NCS_GPIO_Port        GPIOB
#define BOARD_QSPI_BK1_NCS_Pin              GPIO_PIN_6
#define BOARD_QSPI_BK1_IO0_GPIO_Port        GPIOD
#define BOARD_QSPI_BK1_IO0_Pin              GPIO_PIN_11
#define BOARD_QSPI_BK1_IO1_GPIO_Port        GPIOD
#define BOARD_QSPI_BK1_IO1_Pin              GPIO_PIN_12
#define BOARD_QSPI_BK1_IO2_GPIO_Port        GPIOE
#define BOARD_QSPI_BK1_IO2_Pin              GPIO_PIN_2
#define BOARD_QSPI_BK1_IO3_GPIO_Port        GPIOD
#define BOARD_QSPI_BK1_IO3_Pin              GPIO_PIN_13

/* RS485 */
#define BOARD_RS485_RX_GPIO_Port            GPIOA
#define BOARD_RS485_RX_Pin                  GPIO_PIN_9
#define BOARD_RS485_TX_GPIO_Port            GPIOA
#define BOARD_RS485_TX_Pin                  GPIO_PIN_10
#define BOARD_RS485_DIR_GPIO_Port           GPIOA
#define BOARD_RS485_DIR_Pin                 GPIO_PIN_12

/* CAN */
#define BOARD_CAN1_RX_GPIO_Port             GPIOD
#define BOARD_CAN1_RX_Pin                   GPIO_PIN_0
#define BOARD_CAN1_TX_GPIO_Port             GPIOD
#define BOARD_CAN1_TX_Pin                   GPIO_PIN_1

/* PT100 / MAX31865 */
#define BOARD_PT100_CS_GPIO_Port            GPIOB
#define BOARD_PT100_CS_Pin                  GPIO_PIN_12
#define BOARD_PT100_SCLK_GPIO_Port          GPIOB
#define BOARD_PT100_SCLK_Pin                GPIO_PIN_13
#define BOARD_PT100_MISO_GPIO_Port          GPIOB
#define BOARD_PT100_MISO_Pin                GPIO_PIN_14
#define BOARD_PT100_MOSI_GPIO_Port          GPIOB
#define BOARD_PT100_MOSI_Pin                GPIO_PIN_15

/* AHT10 */
#define BOARD_AHT10_SCL_GPIO_Port           GPIOB
#define BOARD_AHT10_SCL_Pin                 GPIO_PIN_8
#define BOARD_AHT10_SDA_GPIO_Port           GPIOB
#define BOARD_AHT10_SDA_Pin                 GPIO_PIN_9

/* W5500 Ethernet */
#define BOARD_W5500_CS_GPIO_Port            GPIOE
#define BOARD_W5500_CS_Pin                  GPIO_PIN_11
#define BOARD_W5500_SCLK_GPIO_Port          GPIOE
#define BOARD_W5500_SCLK_Pin                GPIO_PIN_12
#define BOARD_W5500_MISO_GPIO_Port          GPIOE
#define BOARD_W5500_MISO_Pin                GPIO_PIN_13
#define BOARD_W5500_MOSI_GPIO_Port          GPIOE
#define BOARD_W5500_MOSI_Pin                GPIO_PIN_14
#define BOARD_W5500_INT_GPIO_Port           GPIOE
#define BOARD_W5500_INT_Pin                 GPIO_PIN_15

/* Relay outputs */
#define BOARD_RELAY_OUT1_GPIO_Port          GPIOA
#define BOARD_RELAY_OUT1_Pin                GPIO_PIN_0
#define BOARD_RELAY_OUT2_GPIO_Port          GPIOA
#define BOARD_RELAY_OUT2_Pin                GPIO_PIN_1
#define BOARD_RELAY_OUT3_GPIO_Port          GPIOA
#define BOARD_RELAY_OUT3_Pin                GPIO_PIN_8
#define BOARD_RELAY_OUT4_GPIO_Port          GPIOA
#define BOARD_RELAY_OUT4_Pin                GPIO_PIN_11

/* Transistor outputs */
#define BOARD_TRANSISTOR_OUT1_GPIO_Port     GPIOD
#define BOARD_TRANSISTOR_OUT1_Pin           GPIO_PIN_14
#define BOARD_TRANSISTOR_OUT2_GPIO_Port     GPIOD
#define BOARD_TRANSISTOR_OUT2_Pin           GPIO_PIN_15
#define BOARD_TRANSISTOR_OUT3_GPIO_Port     GPIOE
#define BOARD_TRANSISTOR_OUT3_Pin           GPIO_PIN_0
#define BOARD_TRANSISTOR_OUT4_GPIO_Port     GPIOE
#define BOARD_TRANSISTOR_OUT4_Pin           GPIO_PIN_1

/* Switch inputs */
#define BOARD_SWITCH_X1_GPIO_Port           GPIOD
#define BOARD_SWITCH_X1_Pin                 GPIO_PIN_10
#define BOARD_SWITCH_X2_GPIO_Port           GPIOA
#define BOARD_SWITCH_X2_Pin                 GPIO_PIN_15
#define BOARD_SWITCH_X3_GPIO_Port           GPIOB
#define BOARD_SWITCH_X3_Pin                 GPIO_PIN_0
#define BOARD_SWITCH_X4_GPIO_Port           GPIOB
#define BOARD_SWITCH_X4_Pin                 GPIO_PIN_1
#define BOARD_SWITCH_X5_GPIO_Port           GPIOB
#define BOARD_SWITCH_X5_Pin                 GPIO_PIN_7
#define BOARD_SWITCH_X6_GPIO_Port           GPIOC
#define BOARD_SWITCH_X6_Pin                 GPIO_PIN_0
#define BOARD_SWITCH_X7_GPIO_Port           GPIOE
#define BOARD_SWITCH_X7_Pin                 GPIO_PIN_8
#define BOARD_SWITCH_X8_GPIO_Port           GPIOE
#define BOARD_SWITCH_X8_Pin                 GPIO_PIN_9

/* Analog output control */
#define BOARD_DO_I_MCU_GPIO_Port            GPIOA
#define BOARD_DO_I_MCU_Pin                  GPIO_PIN_4
#define BOARD_DO_U_MCU_GPIO_Port            GPIOA
#define BOARD_DO_U_MCU_Pin                  GPIO_PIN_5

/* ESP32-S3 link */
#define BOARD_ESP32_RX_GPIO_Port            GPIOB
#define BOARD_ESP32_RX_Pin                  GPIO_PIN_10
#define BOARD_ESP32_TX_GPIO_Port            GPIOB
#define BOARD_ESP32_TX_Pin                  GPIO_PIN_11

#endif /* BOARD_PINS_H */
