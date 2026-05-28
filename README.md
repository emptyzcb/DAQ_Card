# DAQ Card Firmware

STM32F407VET6 firmware project generated with STM32CubeMX and built with
Keil MDK-ARM. The current firmware provides the platform skeleton for a data
acquisition product. IMU660RC gyroscope acquisition is integrated as the first
hardware acquisition module.

## Directory Layout

| Directory | Ownership |
| --- | --- |
| `Application/` | RTOS-facing application entry points and product workflow. |
| `Services/` | Hardware-independent DAQ and protocol services. |
| `BSP/` | Board peripheral adapters built on CubeMX/HAL handles. |
| `Platform/` | Startup ordering and integration between services and BSP. |
| `Core/` | CubeMX-generated startup, interrupt, peripheral, and RTOS glue. |
| `Drivers/` | STM32 HAL and CMSIS vendor code. |
| `Middlewares/` | FreeRTOS and third-party middleware. |
| `Docs/` | Architecture notes and integration guidance. |
| `MDK-ARM/` | Keil project and build output. |

## Current Platform Entry

The generated FreeRTOS default task calls `APP_Init()` once and
`APP_RunOnce()` on each task iteration. The application delegates hardware
startup and polling to `PLATFORM_Init()` and `PLATFORM_Process()`.

The DAQ service initializes the IMU660RC gyroscope through a BSP software-SPI
adapter and prints angular velocity on USART1 every 100 ms. Default wiring is
configured in `BSP/Inc/bsp_imu660rc.h`:

| IMU660RC signal | STM32 default pin |
| --- | --- |
| SPC/SCL | PC10 |
| SDI/SDA | PC12 |
| SDO/SA0 | PC11 |
| CS | PC9 |

## Development Rules

- Keep CubeMX-managed peripheral setup in `Core/`.
- Put direct HAL handle use behind `BSP/` adapters.
- Put acquisition state and packet/application behavior in `Services/`.
- Keep `Application/` focused on scheduling and product orchestration.
- Add new sources to the Keil groups that match these directories.

See `Docs/architecture.md` for the intended growth path.
