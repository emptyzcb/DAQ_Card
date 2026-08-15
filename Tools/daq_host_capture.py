#!/usr/bin/env python3
"""DAQ binary stream capture and parser.

The firmware stream frame is:
  A5 5A VERSION TYPE SEQUENCE TIMESTAMP_MS PAYLOAD_LENGTH PAYLOAD CRC16

The CRC is the same little-endian CRC16/Modbus implementation used by
Services/Src/stream_protocol.c.
"""

from __future__ import annotations

import argparse
import csv
import struct
import sys
import time
from dataclasses import dataclass
from pathlib import Path
from typing import BinaryIO, Iterable, Iterator, Optional


SOF = b"\xA5\x5A"
VERSION = 1
HEADER_SIZE = 12
CRC_SIZE = 2
MAX_PAYLOAD = 64
FRAME_TYPE_STATUS = 1
FRAME_TYPE_GYRO = 2
FRAME_TYPE_ERROR = 3
GYRO_PAYLOAD_SIZE = 18


@dataclass(frozen=True)
class Frame:
    version: int
    frame_type: int
    sequence: int
    timestamp_ms: int
    payload: bytes
    raw: bytes


@dataclass
class DecodeStats:
    bytes_in: int = 0
    frames_ok: int = 0
    frames_bad_crc: int = 0
    frames_bad_length: int = 0
    bytes_resynced: int = 0
    invalid_version: int = 0
    sequence_gaps: int = 0
    missing_frames: int = 0
    gyro_frames: int = 0
    unknown_frames: int = 0


def crc16_modbus(data: bytes) -> int:
    crc = 0xFFFF
    for byte in data:
        crc ^= byte
        for _ in range(8):
            if crc & 0x0001:
                crc = (crc >> 1) ^ 0xA001
            else:
                crc >>= 1
            crc &= 0xFFFF
    return crc


def encode_frame(frame_type: int, sequence: int, timestamp_ms: int, payload: bytes) -> bytes:
    if len(payload) > MAX_PAYLOAD:
        raise ValueError("payload too large")
    header = SOF + struct.pack("<BBHIH", VERSION, frame_type, sequence & 0xFFFF, timestamp_ms & 0xFFFFFFFF, len(payload))
    crc = crc16_modbus(header + payload)
    return header + payload + struct.pack("<H", crc)


def encode_gyro_frame(
    sequence: int,
    timestamp_ms: int,
    x_raw: int,
    y_raw: int,
    z_raw: int,
    x_mdps: int,
    y_mdps: int,
    z_mdps: int,
) -> bytes:
    payload = struct.pack("<hhhiii", x_raw, y_raw, z_raw, x_mdps, y_mdps, z_mdps)
    return encode_frame(FRAME_TYPE_GYRO, sequence, timestamp_ms, payload)


def decode_gyro_payload(payload: bytes) -> Optional[dict[str, int]]:
    if len(payload) != GYRO_PAYLOAD_SIZE:
        return None
    x_raw, y_raw, z_raw, x_mdps, y_mdps, z_mdps = struct.unpack("<hhhiii", payload)
    return {
        "x_raw": x_raw,
        "y_raw": y_raw,
        "z_raw": z_raw,
        "x_mdps": x_mdps,
        "y_mdps": y_mdps,
        "z_mdps": z_mdps,
    }


class StreamDecoder:
    def __init__(self) -> None:
        self.buffer = bytearray()
        self.stats = DecodeStats()
        self._expected_sequence: Optional[int] = None

    def feed(self, data: bytes) -> Iterator[Frame]:
        if not data:
            return

        self.stats.bytes_in += len(data)
        self.buffer.extend(data)

        while True:
            sof_index = self.buffer.find(SOF)
            if sof_index < 0:
                self.stats.bytes_resynced += len(self.buffer)
                self.buffer.clear()
                return

            if sof_index > 0:
                del self.buffer[:sof_index]
                self.stats.bytes_resynced += sof_index

            if len(self.buffer) < HEADER_SIZE:
                return

            payload_length = struct.unpack_from("<H", self.buffer, 10)[0]
            if payload_length > MAX_PAYLOAD:
                del self.buffer[0]
                self.stats.frames_bad_length += 1
                self.stats.bytes_resynced += 1
                continue

            frame_length = HEADER_SIZE + payload_length + CRC_SIZE
            if len(self.buffer) < frame_length:
                return

            raw = bytes(self.buffer[:frame_length])
            expected_crc = struct.unpack_from("<H", raw, frame_length - CRC_SIZE)[0]
            actual_crc = crc16_modbus(raw[:-CRC_SIZE])
            if actual_crc != expected_crc:
                del self.buffer[0]
                self.stats.frames_bad_crc += 1
                self.stats.bytes_resynced += 1
                continue

            del self.buffer[:frame_length]
            version, frame_type, sequence, timestamp_ms = struct.unpack_from("<BBHI", raw, 2)
            payload = raw[HEADER_SIZE:-CRC_SIZE]
            frame = Frame(version, frame_type, sequence, timestamp_ms, payload, raw)
            self._update_stats(frame)
            yield frame

    def _update_stats(self, frame: Frame) -> None:
        self.stats.frames_ok += 1
        if frame.version != VERSION:
            self.stats.invalid_version += 1

        if self._expected_sequence is not None and frame.sequence != self._expected_sequence:
            delta = (frame.sequence - self._expected_sequence) & 0xFFFF
            self.stats.sequence_gaps += 1
            if delta > 0:
                self.stats.missing_frames += delta
        self._expected_sequence = (frame.sequence + 1) & 0xFFFF

        if frame.frame_type == FRAME_TYPE_GYRO:
            self.stats.gyro_frames += 1
        elif frame.frame_type not in (FRAME_TYPE_STATUS, FRAME_TYPE_ERROR):
            self.stats.unknown_frames += 1


class GyroCsvWriter:
    def __init__(self, path: Path) -> None:
        self.file = path.open("w", newline="", encoding="utf-8")
        self.writer = csv.writer(self.file)
        self.writer.writerow(
            [
                "pc_time_s",
                "sequence",
                "timestamp_ms",
                "x_raw",
                "y_raw",
                "z_raw",
                "x_mdps",
                "y_mdps",
                "z_mdps",
            ]
        )

    def write(self, frame: Frame) -> bool:
        sample = decode_gyro_payload(frame.payload)
        if sample is None:
            return False
        self.writer.writerow(
            [
                f"{time.time():.3f}",
                frame.sequence,
                frame.timestamp_ms,
                sample["x_raw"],
                sample["y_raw"],
                sample["z_raw"],
                sample["x_mdps"],
                sample["y_mdps"],
                sample["z_mdps"],
            ]
        )
        return True

    def close(self) -> None:
        self.file.close()


def read_file_chunks(path: Path, chunk_size: int) -> Iterable[bytes]:
    with path.open("rb") as file:
        while True:
            chunk = file.read(chunk_size)
            if not chunk:
                break
            yield chunk


def read_serial_chunks(port: str, baud: int, timeout: float, chunk_size: int) -> Iterable[bytes]:
    try:
        import serial  # type: ignore
    except ImportError as exc:
        raise SystemExit("pyserial is required for --port mode. Install it with: python -m pip install pyserial") from exc

    with serial.Serial(port=port, baudrate=baud, timeout=timeout) as link:
        while True:
            chunk = link.read(chunk_size)
            if chunk:
                yield chunk


def capture(chunks: Iterable[bytes], args: argparse.Namespace) -> DecodeStats:
    decoder = StreamDecoder()
    raw_log: Optional[BinaryIO] = None
    csv_writer: Optional[GyroCsvWriter] = None
    last_report = time.monotonic()
    seen_frames = 0

    if args.raw_log:
        raw_log = Path(args.raw_log).open("ab")
    if args.gyro_csv:
        csv_writer = GyroCsvWriter(Path(args.gyro_csv))

    try:
        for chunk in chunks:
            if raw_log is not None:
                raw_log.write(chunk)
            for frame in decoder.feed(chunk):
                seen_frames += 1
                if csv_writer is not None and frame.frame_type == FRAME_TYPE_GYRO:
                    csv_writer.write(frame)
                if args.print_frames:
                    print_frame(frame)
                if args.max_frames and seen_frames >= args.max_frames:
                    return decoder.stats

            now = time.monotonic()
            if args.report_interval > 0 and now - last_report >= args.report_interval:
                print_stats(decoder.stats)
                last_report = now
    except KeyboardInterrupt:
        print("\nInterrupted.", file=sys.stderr)
    finally:
        if raw_log is not None:
            raw_log.close()
        if csv_writer is not None:
            csv_writer.close()

    return decoder.stats


def print_frame(frame: Frame) -> None:
    if frame.frame_type == FRAME_TYPE_GYRO:
        sample = decode_gyro_payload(frame.payload)
        if sample is not None:
            print(
                "GYRO "
                f"seq={frame.sequence} tick={frame.timestamp_ms} "
                f"raw=({sample['x_raw']},{sample['y_raw']},{sample['z_raw']}) "
                f"mdps=({sample['x_mdps']},{sample['y_mdps']},{sample['z_mdps']})"
            )
            return

    print(
        f"FRAME type={frame.frame_type} version={frame.version} "
        f"seq={frame.sequence} tick={frame.timestamp_ms} payload_len={len(frame.payload)}"
    )


def print_stats(stats: DecodeStats) -> None:
    print(
        "stats "
        f"bytes={stats.bytes_in} ok={stats.frames_ok} gyro={stats.gyro_frames} "
        f"crc_err={stats.frames_bad_crc} len_err={stats.frames_bad_length} "
        f"seq_gaps={stats.sequence_gaps} missing={stats.missing_frames} "
        f"resync_bytes={stats.bytes_resynced}"
    )


def run_self_test() -> None:
    good0 = encode_gyro_frame(0, 100, 1, -2, 3, 1000, -2000, 3000)
    good2 = encode_gyro_frame(2, 300, 4, 5, -6, 4000, 5000, -6000)
    bad = bytearray(encode_gyro_frame(1, 200, 0, 0, 0, 0, 0, 0))
    bad[-1] ^= 0x55

    decoder = StreamDecoder()
    frames = list(decoder.feed(b"noise" + good0[:7]))
    frames.extend(decoder.feed(good0[7:] + bytes(bad) + good2))

    assert len(frames) == 2, frames
    assert frames[0].sequence == 0
    assert frames[1].sequence == 2
    assert decoder.stats.frames_ok == 2
    assert decoder.stats.frames_bad_crc >= 1
    assert decoder.stats.sequence_gaps == 1
    assert decoder.stats.missing_frames == 1
    assert decode_gyro_payload(frames[0].payload)["y_raw"] == -2
    print("self-test ok")
    print_stats(decoder.stats)


def build_arg_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Capture and decode DAQ card binary stream frames.")
    source = parser.add_mutually_exclusive_group()
    source.add_argument("--input", type=Path, help="Read a raw binary stream file.")
    source.add_argument("--port", help="Read from a serial port, for example COM5.")
    parser.add_argument("--baud", type=int, default=115200, help="Serial baud rate.")
    parser.add_argument("--timeout", type=float, default=0.2, help="Serial read timeout in seconds.")
    parser.add_argument("--chunk-size", type=int, default=256, help="Input read chunk size.")
    parser.add_argument("--raw-log", help="Append raw bytes to a binary log file.")
    parser.add_argument("--gyro-csv", help="Write decoded gyro frames to a CSV file.")
    parser.add_argument("--max-frames", type=int, default=0, help="Stop after this many valid frames.")
    parser.add_argument("--report-interval", type=float, default=2.0, help="Stats print interval; 0 disables periodic reports.")
    parser.add_argument("--print-frames", action="store_true", help="Print each decoded frame.")
    parser.add_argument("--self-test", action="store_true", help="Run parser self-test and exit.")
    return parser


def main(argv: Optional[list[str]] = None) -> int:
    parser = build_arg_parser()
    args = parser.parse_args(argv)

    if args.self_test:
        run_self_test()
        return 0

    if args.input is None and args.port is None:
        parser.error("choose --input, --port, or --self-test")

    if args.input is not None:
        chunks = read_file_chunks(args.input, args.chunk_size)
    else:
        chunks = read_serial_chunks(args.port, args.baud, args.timeout, args.chunk_size)

    stats = capture(chunks, args)
    print_stats(stats)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
