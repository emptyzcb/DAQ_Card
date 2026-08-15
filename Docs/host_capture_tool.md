# Host Capture Tool

`Tools/daq_host_capture.py` decodes the binary stream frames produced by
`Services/stream_protocol`.

## Commands

```powershell
python Tools\daq_host_capture.py --self-test
python Tools\daq_host_capture.py --input capture.bin --gyro-csv gyro.csv --print-frames
python Tools\daq_host_capture.py --port COM5 --baud 115200 --raw-log capture.bin --gyro-csv gyro.csv
```

Serial mode requires `pyserial`:

```powershell
python -m pip install pyserial
```

## What It Checks

- Finds and resynchronizes on the `A5 5A` frame header.
- Validates CRC16/Modbus over header and payload.
- Tracks sequence jumps and missing-frame counts.
- Saves the original byte stream with `--raw-log`.
- Exports gyroscope frames to CSV with `--gyro-csv`.

The self-test generates valid frames, a corrupted CRC frame, and a sequence
gap, so it can verify the parser without hardware.
