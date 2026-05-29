# CubeMX Configuration Task Book

## Goal

Bring the STM32F407VET6 CubeMX configuration from the current UART + GPIO +
FreeRTOS skeleton to a practical data acquisition baseline. This task book
only covers CubeMX-generated peripheral configuration. Product code should stay
in `Application/`, `Platform/`, `Services/`, and `BSP/`.

## Current Baseline

- MCU: STM32F407VETx.
- RTOS: FreeRTOS CMSIS-RTOS v1 default task.
- Enabled peripherals: GPIO, USART1, TIM14 as HAL time base.
- USART1 pins: PA9 TX, PA10 RX.
- IMU660RC currently uses software SPI on PC9, PC10, PC11, PC12 from the BSP.

## Task 1: ADC Sampling Peripheral

Configure one ADC instance as the first acquisition source.

- Enable `ADC1`.
- Select the required analog input channels according to the schematic.
- Use 12-bit resolution unless the analog front end requires a different mode.
- Disable scan conversion for a single channel; enable scan conversion for
  multi-channel sampling.
- Use external trigger from a timer update event for deterministic sampling.
- Disable continuous conversion when timer-triggered sampling is used.
- Enable DMA continuous requests.

Acceptance checks:

- `Core/Src/adc.c` and `Core/Inc/adc.h` are generated.
- `HAL_ADC_MODULE_ENABLED` is enabled in `stm32f4xx_hal_conf.h`.
- ADC GPIO pins are configured as analog with no pull.

## Task 2: ADC DMA

Configure DMA for ADC sample transfer.

- Use the DMA stream/channel mapped to the selected ADC instance.
- Direction: peripheral to memory.
- Peripheral increment: disabled.
- Memory increment: enabled.
- Data alignment: half-word for 12-bit ADC samples.
- Mode: circular for continuous acquisition.
- Priority: high or very high.
- Enable half-transfer and transfer-complete interrupts if block processing is
  required.

Acceptance checks:

- `MX_DMA_Init()` is generated and called before ADC initialization.
- DMA IRQ handler is generated in `stm32f4xx_it.c`.
- The ADC handle is linked to the DMA handle in MSP init code.

## Task 3: Sampling Timer

Configure one general-purpose timer as the ADC sample clock.

- Prefer a timer that is not used by HAL tick or other board timing.
- Configure the timer period to the first target sampling rate.
- Set master output trigger to update event.
- Keep TIM14 reserved for the HAL time base.

Recommended first target:

- Start with 1 kHz sampling for bring-up.
- Increase only after UART/USB transport and buffering are verified.

Acceptance checks:

- Timer init source/header files are generated.
- Timer master trigger is visible in the generated init function.
- Timer IRQ is only enabled if the firmware needs timer callbacks.

## Task 4: Hardware SPI Option for IMU660RC

Keep the current software SPI path working, but prepare a hardware SPI option
if the final PCB routes the IMU to an STM32 SPI peripheral.

- Enable the matching `SPIx` peripheral only after pinout is confirmed.
- Mode: full-duplex master.
- Data size: 8 bit.
- Clock polarity/phase: match IMU660RC timing from the datasheet.
- NSS: software.
- Baud-rate prescaler: start conservatively, then raise after signal checks.

Acceptance checks:

- Hardware SPI pins do not conflict with ADC, USART1, SWD, or board straps.
- The BSP can be switched between software SPI and hardware SPI with a small
  adapter change.

## Task 5: USART Transport Upgrade

USART1 is already enabled for debug output. For sustained telemetry:

- Keep USART1 at the board debug connector.
- Raise baud rate after host-side validation if needed.
- Enable USART1 DMA TX for non-blocking telemetry.
- Optionally enable RX interrupt or DMA RX for command input.

Acceptance checks:

- DMA TX stream is configured and linked to USART1.
- Existing blocking console path still builds during the migration.
- No CubeMX regeneration removes `APP_Init()` or `APP_RunOnce()` from
  `Core/Src/freertos.c`.

## Task 6: FreeRTOS Review

Review RTOS resources after adding ADC/DMA processing.

- Increase default task stack if formatting, protocol encoding, or command
  parsing grows.
- Add a dedicated acquisition task only when ADC callbacks need deferred
  processing.
- Add queues or stream buffers for ADC blocks and telemetry frames.

Acceptance checks:

- No blocking UART transfer runs in high-rate acquisition paths.
- ADC/DMA callbacks only do short ISR-safe work.

## Regeneration Checklist

After pressing Generate Code in CubeMX:

- Confirm `Core/Src/freertos.c` still includes `app.h`.
- Confirm `StartDefaultTask()` still calls `APP_Init()` once and
  `APP_RunOnce()` in the loop.
- Add any new generated source files to the Keil project groups if CubeMX does
  not do it automatically.
- Rebuild in Keil and verify there are no missing HAL module sources.
- Run a board smoke test: boot banner, IMU ready/failure line, ADC DMA start,
  and no HardFault.
