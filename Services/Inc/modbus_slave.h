#ifndef MODBUS_SLAVE_H
#define MODBUS_SLAVE_H

#include <stdint.h>

#include "usart.h"

#ifdef __cplusplus
extern "C" {
#endif

/* Modbus RTU slave address (matches the host "slave address" input, default 1). */
#define MODBUS_SLAVE_ADDR 1U

/* RS485 transceiver direction control pin.
 * - board_pin_map.md / board_pins.h define DIR on PA12 (high = TX, low = RX).
 * - In full application mode gpio.c also configures PE15 (RS485_DE_RE) as
 *   output; if your hardware uses PE15 instead, redefine these macros. */
#ifndef MODBUS_SLAVE_DIR_GPIO_Port
#define MODBUS_SLAVE_DIR_GPIO_Port GPIOA
#define MODBUS_SLAVE_DIR_Pin       GPIO_PIN_12
#define MODBUS_SLAVE_DIR_TX        GPIO_PIN_SET
#define MODBUS_SLAVE_DIR_RX        GPIO_PIN_RESET
#endif

/* Register map, identical to the Windows host (README register table):
 *   0x0000       device status bitmap
 *   0x0001       digital input bitmap  (X1..X8)
 *   0x0002       digital output bitmap (bit0..3 relays, bit4..7 transistors), writable
 *   0x0010..17   AD7606 8 channel raw signed values
 *   0x0020..25   IMU 6 raw values (accel x/y/z, gyro x/y/z)
 *   0x0030..32   Roll / Pitch / Yaw in 0.01 deg
 *
 * Script engine registers (方案 B, host "script -> flash" feature):
 *   0x003A       script runner status (0=idle,1=running,2=stopped,3=error), read
 *   0x003B       script runner current line / error line, read
 *   0x003C       script runner step counter (low 16 bits), read
 *   0x003D       script control (write): 1=erase flash script, 2=commit staged
 *                text to flash, 3=start runner, 4=stop runner
 *   0x003E       length of script stored in flash (bytes), read
 *   0x0040..     staged script text (2 ASCII bytes per register, big-endian),
 *                written by fc 0x10 / 0x06, readable for verification
 */
#define MODBUS_REG_STATUS       0x0000U
#define MODBUS_REG_DIGITAL_IN   0x0001U
#define MODBUS_REG_DIGITAL_OUT  0x0002U
#define MODBUS_REG_AD7606       0x0010U
#define MODBUS_REG_IMU          0x0020U
#define MODBUS_REG_ANGLE        0x0030U

#define MODBUS_REG_SCRIPT_STATUS 0x003AU
#define MODBUS_REG_SCRIPT_LINE   0x003BU
#define MODBUS_REG_SCRIPT_STEPS  0x003CU
#define MODBUS_REG_SCRIPT_CTRL   0x003DU
#define MODBUS_REG_SCRIPT_LEN    0x003EU
#define MODBUS_REG_SCRIPT_DATA   0x0040U
/* 8192 bytes of staged text = 4096 registers: 0x0040 .. 0x103F */
#define MODBUS_SCRIPT_DATA_END   0x103FU

/* Script control register commands. */
#define MODBUS_SCRIPT_CTRL_ERASE  1U
#define MODBUS_SCRIPT_CTRL_COMMIT 2U
#define MODBUS_SCRIPT_CTRL_START  3U
#define MODBUS_SCRIPT_CTRL_STOP   4U

/* Script runner status values (same as ScriptRunnerStatus). */
#define MODBUS_SCRIPT_ST_IDLE     0U
#define MODBUS_SCRIPT_ST_RUNNING  1U
#define MODBUS_SCRIPT_ST_STOPPED  2U
#define MODBUS_SCRIPT_ST_ERROR    3U

/* Status register bit meanings (same as host DescribeStatus()). */
#define MODBUS_STATUS_BIT_IMU_READY   0x0001U
#define MODBUS_STATUS_BIT_AD_READY    0x0002U
#define MODBUS_STATUS_BIT_IMU_READ_OK 0x0004U
#define MODBUS_STATUS_BIT_AD_READ_OK  0x0008U

typedef struct
{
  uint32_t rx_bytes;       /* raw bytes received on USART1 */
  uint32_t rx_frames;      /* valid request frames answered */
  uint32_t crc_errors;     /* frames dropped due to bad CRC */
  uint32_t addr_errors;    /* frames dropped due to address mismatch */
  uint32_t tx_frames;      /* responses transmitted */
  uint32_t exceptions_sent;/* exception responses sent */
} MODBUS_SLAVE_Diagnostics;

void MODBUS_SLAVE_Init(void);            /* start USART1 RX interrupt + RXNE */
void MODBUS_SLAVE_Pump(void);            /* task loop: move bytes, detect frame end */
void MODBUS_SLAVE_Process(void);         /* task loop: parse one frame and reply */
void MODBUS_SLAVE_OnByteReceived(uint8_t byte); /* called from USART1 ISR */
void MODBUS_SLAVE_GetDiagnostics(MODBUS_SLAVE_Diagnostics *diag);

/* Write the output bitmap (low 8 bits; bit8/9 DO_I/DO_U preserved).
 * Shared by both the fc 0x06 register write and the script runner,
 * so Modbus host and script execution never race on the output mask. */
void MODBUS_SLAVE_WriteOutputBits(uint16_t bits);

#ifdef __cplusplus
}
#endif

#endif /* MODBUS_SLAVE_H */
