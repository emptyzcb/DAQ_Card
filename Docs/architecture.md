# Firmware Architecture

## Layering

```text
FreeRTOS task / callbacks
        |
        v
Application        Application workflow and scheduling entry
        |
        v
Platform           Startup sequence and dependency composition
        |
        +-----------------+
        v                 v
Services            BSP adapters
DAQ pipeline        UART / ADC / DMA / GPIO adapters
                          |
                          v
                    CubeMX HAL handles in Core
```

`Core/`, `Drivers/`, and `Middlewares/` form the generated or vendor-owned
foundation. Product code should be added above that foundation so CubeMX
regeneration has a small, visible integration surface.

## Existing Boot Sequence

1. `main()` initializes HAL, clock, GPIO, and USART1.
2. CubeMX starts FreeRTOS and creates `defaultTask`.
3. `defaultTask` invokes `APP_Init()`.
4. `APP_Init()` invokes `PLATFORM_Init()`.
5. Platform initialization prepares BSP and DAQ services.
6. Each task iteration invokes `APP_RunOnce()` and then sleeps for one tick.

## Planned DAQ Extension Points

| Responsibility | Target layer | Expected module |
| --- | --- | --- |
| ADC and DMA HAL ownership | `BSP/` | `bsp_adc_dma` |
| Sampling trigger timer | `BSP/` | `bsp_sample_clock` |
| Raw block ingestion and buffering | `Services/` | `daq_service` |
| Frame/packet encoding | `Services/` | `stream_protocol` |
| UART/USB transport | `BSP/` | `bsp_console` or a new transport adapter |
| Device mode and commands | `Application/` | `app` or command task |

## Stream Protocol Skeleton

`Services/stream_protocol` provides a small binary frame encoder that can be
used once the firmware moves from debug text output to host-facing telemetry.
The current frame format is:

```text
SOF0 SOF1 VERSION TYPE SEQ_L SEQ_H TICK0..TICK3 LEN_L LEN_H PAYLOAD CRC_L CRC_H
```

Gyroscope payloads currently carry raw X/Y/Z `int16_t` values followed by
X/Y/Z mdps `int32_t` values. Text output remains the default so the current
USART debugging workflow is unchanged.

## CubeMX Regeneration Boundary

Changes to generated files must remain inside `USER CODE` blocks. The current
integration touches only `Core/Src/freertos.c` user sections to include and
call the application interface. After regenerating peripherals, confirm that
these calls remain present and that manually maintained Keil groups are still
included in the project.
