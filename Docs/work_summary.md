# Work Summary

## Scope

This update adds the firmware-side foundation needed before CubeMX ADC/DMA
work starts. The current board behavior remains compatible with the existing
debug flow: IMU660RC angular velocity is still printed as text on USART1 by
default.

## Code Changes

- Added `DAQ_SERVICE_Config` with sample period, IMU gyro range, and output
  mode.
- Added `DAQ_SERVICE_Diagnostics` with state, last error, IMU chip ID, sample
  count, report count, transmit failures, protocol encode failures, and last
  timing markers.
- Added DAQ service APIs for config access, config validation, last error, and
  diagnostics snapshot.
- Added `stream_protocol` service module with binary frame encoding, CRC16,
  sequence number support, timestamps, and gyroscope payload encoding.
- Added `stream_protocol.c` to the Keil project so MDK builds the new module.
- Updated architecture notes and README links.

## Runtime Behavior

- Default sample period: 100 ms.
- Default gyro range: 2000 dps.
- Default output: text only.
- Binary stream output is available through
  `DAQ_SERVICE_OUTPUT_BINARY` or `DAQ_SERVICE_OUTPUT_TEXT_AND_BINARY`.
- Sample period and output mode can be changed through the config API while the
  service is running. Gyro range changes should be applied before service init,
  because the sensor register programming happens during initialization.

## Protocol Notes

Binary frames use a fixed header:

```text
A5 5A VERSION TYPE SEQUENCE TIMESTAMP_MS PAYLOAD_LENGTH PAYLOAD CRC16
```

Gyro payloads include raw axis values and mdps-scaled axis values. This is
enough for a host tool to validate sensor data without parsing floating point
text.

## Remaining Work

- Configure ADC, DMA, and sampling timer in CubeMX.
- Add `bsp_adc_dma` and `bsp_sample_clock` after CubeMX generates handles.
- Upgrade `bsp_console` to DMA or interrupt-driven TX before high-rate
  telemetry is enabled.
- Add host-side protocol parser or a simple Python serial capture tool.
- Add command handling for changing sample rate, output mode, and gyro range
  at runtime.
