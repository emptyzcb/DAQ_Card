# Board Pin Map

Source: `采集卡电路原理图和引脚定义表.pdf`, page 2.

## Active Firmware Baseline

| Function | STM32H743 pin | Net |
| --- | --- | --- |
| BMI270 SCK | PB3 | `IMU_SPI_SCK` |
| BMI270 MISO | PB4 | `IMU_SPI_MISO` |
| BMI270 MOSI | PB5 | `IMU_SPI_MOSI` |
| BMI270 CS | PC4 | `IMU_SPI_CS` |
| Debug UART TX | PA2 | `TX_DEBUG` |
| Debug UART RX | PA3 | `RX_DEBUG` |

## Planned Peripheral Pins

### TF Card

| STM32H743 pin | Net |
| --- | --- |
| PC8 | `SD_D0` |
| PC9 | `SD_D1` |
| PC10 | `SD_D2` |
| PC11 | `SD_D3` |
| PC12 | `SD_CLK` |
| PD2 | `SD_CMD` |

### SPI NOR Flash

| STM32H743 pin | Net |
| --- | --- |
| PB3 | `SPI_CLK_PB3` |
| PB4 | `SPI_MISO_PB4` |
| PB5 | `SPI_MOSI_PB5` |
| PD3 | `SPI_CS_PD3` |
| PB2 | `QSPI_CLK` |
| PB6 | `QSPI_BK1_NCS` |
| PD11 | `QSPI_BK1_IO0` |
| PD12 | `QSPI_BK1_IO1` |
| PE2 | `QSPI_BK1_IO2` |
| PD13 | `QSPI_BK1_IO3` |

### RS485

| STM32H743 pin | Net |
| --- | --- |
| PA9 | `485_USART1_RX` |
| PA10 | `485_USART1_TX` |
| PA12 | `485_USART1_DIR` |

### CAN

| STM32H743 pin | Net |
| --- | --- |
| PD0 | `MCU_CAN1_RX` |
| PD1 | `MCU_CAN1_TX` |

### PT100 / MAX31865

| STM32H743 pin | Net |
| --- | --- |
| PB12 | `PT_SPI2_CS` |
| PB13 | `PT_SPI2_SCLK` |
| PB14 | `PT_SPI2_MISO` |
| PB15 | `PT_SPI2_MOSI` |

### AHT10

| STM32H743 pin | Net |
| --- | --- |
| PB8 | `AHT10_I2C1_SCL` |
| PB9 | `AHT10_I2C1_SDA` |

### W5500 Ethernet

| STM32H743 pin | Net |
| --- | --- |
| PE11 | `W5500_SPI4_CS` |
| PE12 | `W5500_SPI4_SCLK` |
| PE13 | `W5500_SPI4_MISO` |
| PE14 | `W5500_SPI4_MOSI` |
| PE15 | `W5500_INT` |

### Relay Outputs

| STM32H743 pin | Net |
| --- | --- |
| PD15 | `RELAY_1` |
| PD14 | `RELAY_2` |
| PE0 | `RELAY_3` |
| PE1 | `RELAY_4` |

### Transistor Outputs

| STM32H743 pin | Net |
| --- | --- |
| PA11 | `TRANSISTOR_1` |
| PA8 | `TRANSISTOR_2` |
| PC7 | `TRANSISTOR_3` |
| PC6 | `TRANSISTOR_4` |

### Switch Inputs

| STM32H743 pin | Net |
| --- | --- |
| PE9 | `INPUT_1` |
| PB7 | `INPUT_2` |
| PD10 | `INPUT_3` |
| PA15 | `INPUT_4` |
| PC0 | `INPUT_5` |
| PB0 | `INPUT_6` |
| PB1 | `INPUT_7` |
| PE8 | `INPUT_8` |

### DO Control

| STM32H743 pin | Net |
| --- | --- |
| PA4 | `DO_I_MCU` |
| PA5 | `DO_U_MCU` |

### ESP32-S3 Link

| STM32H743 pin | Net |
| --- | --- |
| PB10 | `STtoESP_RX` |
| PB11 | `STtoESP_TX` |

## Implementation Phases

1. Align existing BMI270 BSP to `board_pins.h`.
2. Add GPIO BSPs for relay outputs, transistor outputs, switch inputs, and
   basic DO control.
3. Add bus BSPs for AHT10 I2C, PT100 SPI, W5500 SPI, TF card, and SPI/QSPI
   flash after CubeMX generates the corresponding peripheral handles.
4. Add AD7606 BSP from the schematic-page net names after its MCU pin mapping
   is traced and validated.
5. Move high-rate acquisition and transport paths to non-blocking task/DMA
   flows before enabling sustained telemetry.

## Digital IO Bring-Up Notes

- The digital IO interface is strictly limited to eight inputs and eight
  outputs. Input channels are named `INPUT_1` to `INPUT_8`.
- Output channels are split into four relay outputs (`RELAY_1` to `RELAY_4`)
  and four transistor outputs (`TRANSISTOR_1` to `TRANSISTOR_4`).
- `DO_I_MCU` and `DO_U_MCU` are not part of this eight-output digital IO
  interface.
- Outputs are initialized to the inactive state.
- Inputs default to active-low because the schematic uses isolated input
  conditioning. Override `BSP_DIGITAL_IO_INPUT_ACTIVE_LEVEL` if board testing
  proves a different polarity.
- The current IO bring-up enables all four relay outputs and all four transistor
  outputs, while `Task_digital_io` prints input and output masks for smoke
  testing.
