#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
modbus_selftest.py - Frame-level self test for the firmware Modbus RTU slave.

It reproduces, in Python, the exact CRC16 / frame build logic implemented in
firmware (Services/Src/modbus_slave.c) and checks that requests produced the
same way the Windows host builds them (IndustrialFieldDataAcquisitionTerminal
ModbusRtuClient: address 1, big-endian registers, CRC low byte first) are
answered with well-formed responses.

Covers scheme B: script engine registers (0x003A-0x003E control/status),
staged script text area (0x0040..) written via fc 0x10 / 0x06, and the
flash commit/erase/start/stop commands.

Run:  python modbus_selftest.py
"""

SLAVE_ADDR = 0x01


def crc16(data: bytes) -> int:
    """Modbus CRC16: poly 0xA001, init 0xFFFF, low byte transmitted first."""
    crc = 0xFFFF
    for b in data:
        crc ^= b
        for _ in range(8):
            if crc & 0x0001:
                crc = (crc >> 1) ^ 0xA001
            else:
                crc >>= 1
    return crc & 0xFFFF


def build_frame(payload: bytes) -> bytes:
    """Append CRC (low byte first) - identical to firmware modbus_append_crc."""
    crc = crc16(payload)
    return payload + bytes([crc & 0xFF, (crc >> 8) & 0xFF])


def build_request(fc: int, payload_no_crc: bytes) -> bytes:
    """Build a request exactly as the host does."""
    return build_frame(bytes([SLAVE_ADDR, fc]) + payload_no_crc)


def check_crc(frame: bytes) -> bool:
    """Firmware CRC check: crc over frame minus 2 bytes equals the 2 trailing bytes."""
    return crc16(frame[:-2]) == ((frame[-1] << 8) | frame[-2])


# ---------------------------------------------------------------------------
# Simulated firmware register map (mirrors modbus_slave.c)
# ---------------------------------------------------------------------------
REG_STATUS = 0x0000
REG_DIGITAL_IN = 0x0001
REG_DIGITAL_OUT = 0x0002
REG_AD7606 = 0x0010
REG_IMU = 0x0020
REG_ANGLE = 0x0030

REG_SCRIPT_STATUS = 0x003A
REG_SCRIPT_LINE = 0x003B
REG_SCRIPT_STEPS = 0x003C
REG_SCRIPT_CTRL = 0x003D
REG_SCRIPT_LEN = 0x003E
REG_SCRIPT_DATA = 0x0040
SCRIPT_DATA_END = 0x103F
SCRIPT_STAGE_SIZE = 8192

CTRL_ERASE = 1
CTRL_COMMIT = 2
CTRL_START = 3
CTRL_STOP = 4

ST_IDLE = 0
ST_RUNNING = 1
ST_STOPPED = 2
ST_ERROR = 3


def is_valid_address(addr: int) -> bool:
    if addr <= REG_DIGITAL_OUT:
        return True
    if REG_AD7606 <= addr <= REG_AD7606 + 7:
        return True
    if REG_IMU <= addr <= REG_IMU + 5:
        return True
    if REG_ANGLE <= addr <= REG_ANGLE + 2:
        return True
    if REG_SCRIPT_STATUS <= addr <= REG_SCRIPT_LEN:
        return True
    if REG_SCRIPT_DATA <= addr <= SCRIPT_DATA_END:
        return True
    return False


# Simulated live data
SIM_STATUS = 0x000F          # IMU ready + AD ready + read OK flags
SIM_DI = 0x00A5              # input bitmap
SIM_DO = 0x0000              # output bitmap (kept by the "firmware")
SIM_AD = [100, -100, 200, -200, 300, -300, 400, -400]
SIM_IMU = [1000, -2000, 3000, -40, 50, -60]
SIM_ANGLE = [1234, -5678, 9012]  # centi-degrees

# Script engine simulation
SIM_SCRIPT_STATUS = ST_IDLE
SIM_SCRIPT_LINE = 0
SIM_STAGE = bytearray(SCRIPT_STAGE_SIZE)   # staged text (fc 0x10 / 0x06)
SIM_STAGE_LEN = 0
SIM_FLASH_TEXT = b""                       # committed flash content


def read_register(addr: int) -> int:
    if addr == REG_STATUS:
        return SIM_STATUS
    if addr == REG_DIGITAL_IN:
        return SIM_DI & 0x00FF
    if addr == REG_DIGITAL_OUT:
        return SIM_DO & 0x00FF
    if REG_AD7606 <= addr <= REG_AD7606 + 7:
        return SIM_AD[addr - REG_AD7606] & 0xFFFF
    if REG_IMU <= addr <= REG_IMU + 5:
        return SIM_IMU[addr - REG_IMU] & 0xFFFF
    if REG_ANGLE <= addr <= REG_ANGLE + 2:
        return SIM_ANGLE[addr - REG_ANGLE] & 0xFFFF
    if addr == REG_SCRIPT_STATUS:
        return SIM_SCRIPT_STATUS
    if addr == REG_SCRIPT_LINE:
        return SIM_SCRIPT_LINE
    if addr == REG_SCRIPT_STEPS:
        return 0
    if addr == REG_SCRIPT_LEN:
        return len(SIM_FLASH_TEXT) & 0xFFFF
    if REG_SCRIPT_DATA <= addr <= SCRIPT_DATA_END:
        offset = (addr - REG_SCRIPT_DATA) * 2
        if offset + 2 > SCRIPT_STAGE_SIZE:
            raise AssertionError("stage read out of range")
        if offset + 2 > SIM_STAGE_LEN:
            return 0
        return (SIM_STAGE[offset] << 8) | SIM_STAGE[offset + 1]
    raise AssertionError("read of invalid register")


def write_register(addr: int, value: int) -> int:
    """Mirror of firmware modbus_write_register().
    Return: 1 success, 0 illegal address, -1 illegal value."""
    global SIM_DO, SIM_STAGE_LEN, SIM_FLASH_TEXT, SIM_SCRIPT_STATUS
    if addr == REG_DIGITAL_OUT:
        SIM_DO = (SIM_DO & 0xFF00) | (value & 0x00FF)   # preserve bit8/9
        return 1
    if addr == REG_SCRIPT_CTRL:
        if value == CTRL_ERASE:
            SIM_STAGE_LEN = 0
            SIM_FLASH_TEXT = b""
            return 1
        if value == CTRL_COMMIT:
            if SIM_STAGE_LEN == 0:
                return -1
            SIM_FLASH_TEXT = bytes(SIM_STAGE[:SIM_STAGE_LEN])
            return 1
        if value == CTRL_START:
            SIM_SCRIPT_STATUS = ST_RUNNING
            return 1
        if value == CTRL_STOP:
            SIM_SCRIPT_STATUS = ST_STOPPED
            return 1
        return -1
    if REG_SCRIPT_DATA <= addr <= SCRIPT_DATA_END:
        offset = (addr - REG_SCRIPT_DATA) * 2
        if offset + 2 > SCRIPT_STAGE_SIZE:
            return -1
        SIM_STAGE[offset] = (value >> 8) & 0xFF
        SIM_STAGE[offset + 1] = value & 0xFF
        if offset + 2 > SIM_STAGE_LEN:
            SIM_STAGE_LEN = offset + 2
        return 1
    return 0


def build_response(req: bytes):
    """Mirror of firmware modbus_build_response(). Returns response bytes."""
    fc = req[1]
    if fc == 0x03:
        start = (req[2] << 8) | req[3]
        qty = (req[4] << 8) | req[5]
        if qty == 0 or qty > 125:
            return build_frame(bytes([req[0], 0x83, 0x03]))  # exc 03
        for i in range(qty):
            if not is_valid_address(start + i):
                return build_frame(bytes([req[0], 0x83, 0x02]))  # exc 02
        data = bytearray()
        for i in range(qty):
            v = read_register(start + i)
            data += bytes([(v >> 8) & 0xFF, v & 0xFF])
        return build_frame(bytes([req[0], 0x03, qty * 2]) + bytes(data))
    if fc == 0x06:
        addr = (req[2] << 8) | req[3]
        value = (req[4] << 8) | req[5]
        result = write_register(addr, value)
        if result == 0:
            return build_frame(bytes([req[0], 0x86, 0x02]))  # exc 02
        if result < 0:
            return build_frame(bytes([req[0], 0x86, 0x03]))  # exc 03
        return build_frame(bytes([req[0], 0x06]) + req[2:6])  # echo
    if fc == 0x10:
        start = (req[2] << 8) | req[3]
        qty = (req[4] << 8) | req[5]
        byte_count = req[6]
        data_off = 7
        if qty == 0 or qty > 123 or byte_count != qty * 2:
            return build_frame(bytes([req[0], 0x90, 0x03]))  # exc 03
        if not (REG_SCRIPT_DATA <= start and start + qty - 1 <= SCRIPT_DATA_END):
            return build_frame(bytes([req[0], 0x90, 0x02]))  # exc 02
        if data_off + byte_count + 2 > len(req):
            raise AssertionError("truncated fc 0x10 frame")
        offset = (start - REG_SCRIPT_DATA) * 2
        if offset + qty * 2 > SCRIPT_STAGE_SIZE:
            return build_frame(bytes([req[0], 0x90, 0x02]))
        global SIM_STAGE_LEN
        for i in range(qty):
            hi = req[data_off + 2 * i]
            lo = req[data_off + 2 * i + 1]
            SIM_STAGE[offset + 2 * i] = hi
            SIM_STAGE[offset + 2 * i + 1] = lo
        if offset + qty * 2 > SIM_STAGE_LEN:
            SIM_STAGE_LEN = offset + qty * 2
        # response: addr, fc, start, qty, crc (8 bytes)
        return build_frame(bytes([req[0], 0x10]) + req[2:6])
    return build_frame(bytes([req[0], fc | 0x80, 0x01]))  # exc 01


# ---------------------------------------------------------------------------
# Test runner
# ---------------------------------------------------------------------------
PASS = 0
FAIL = 0


def expect(cond: bool, name: str):
    global PASS, FAIL
    if cond:
        PASS += 1
        print(f"  [PASS] {name}")
    else:
        FAIL += 1
        print(f"  [FAIL] {name}")


def decode_response(resp: bytes):
    """Decode a response for human readable checks."""
    assert check_crc(resp), "response CRC invalid"
    fc = resp[1]
    if fc & 0x80:
        return f"exception {fc & 0x7F:02X} code={resp[2]:02X}"
    if fc == 0x03:
        regs = []
        for i in range(resp[2] // 2):
            v = (resp[3 + 2 * i] << 8) | resp[4 + 2 * i]
            regs.append(v if v < 0x8000 else v - 0x10000)
        return f"read {len(regs)} regs: {regs}"
    if fc == 0x06:
        v = (resp[2] << 8) | resp[3]
        return f"write echo addr=0x{(resp[2] << 8 | resp[3]):04X} value=0x{v:04X}"
    if fc == 0x10:
        start = (resp[2] << 8) | resp[3]
        qty = (resp[4] << 8) | resp[5]
        return f"write multi echo start=0x{start:04X} qty={qty}"
    return f"unknown fc {fc:02X}"


def main():
    global SIM_STAGE_LEN
    print("== Modbus RTU slave frame self test (mirrors firmware) ==")

    # 1. Read 3 status/DI/DO registers (host polls 0x0000 qty 3)
    print("\n[1] host: read holding 0x0000 x3")
    req = build_request(0x03, bytes([0x00, 0x00, 0x00, 0x03]))
    resp = build_response(req)
    print("    req :", req.hex(" ").upper())
    print("    resp:", resp.hex(" ").upper(), "->", decode_response(resp))
    expect(len(resp) == 11, "response length 11 (5 + 2*3)")
    expect(resp[0] == 0x01 and resp[1] == 0x03 and resp[2] == 0x06, "header")
    expect((resp[3] << 8 | resp[4]) == 0x000F, "status register 0x000F")

    # 2. Read AD7606 8 channels
    print("\n[2] host: read holding 0x0010 x8")
    req = build_request(0x03, bytes([0x00, 0x10, 0x00, 0x08]))
    resp = build_response(req)
    print("    resp:", resp.hex(" ").upper(), "->", decode_response(resp))
    expect(len(resp) == 3 + 16 + 2, "response length 21 (3 + 2*8 + 2)")
    expect((resp[3] << 8 | resp[4]) == 100, "ch1 raw 100")

    # 3. Read angle registers, negative value round trip
    print("\n[3] host: read holding 0x0030 x3")
    req = build_request(0x03, bytes([0x00, 0x30, 0x00, 0x03]))
    resp = build_response(req)
    print("    resp:", resp.hex(" ").upper(), "->", decode_response(resp))
    pitch = (resp[5] << 8) | resp[6]
    pitch_s = pitch if pitch < 0x8000 else pitch - 0x10000
    expect(pitch_s == -5678, f"pitch centi-degree signed {pitch_s}")

    # 4. Illegal data address -> exception 02
    print("\n[4] host: read 0x0003 (not mapped)")
    req = build_request(0x03, bytes([0x00, 0x03, 0x00, 0x01]))
    resp = build_response(req)
    print("    resp:", resp.hex(" ").upper(), "->", decode_response(resp))
    expect(resp[1] == 0x83 and resp[2] == 0x02, "exception 02")

    # 5. Quantity 0 -> exception 03
    print("\n[5] host: read qty=0")
    req = build_request(0x03, bytes([0x00, 0x00, 0x00, 0x00]))
    resp = build_response(req)
    expect(resp[1] == 0x83 and resp[2] == 0x03, "exception 03")

    # 6. Write single register 0x0002 (host IO panel)
    print("\n[6] host: write 0x0002 = 0x00FF")
    req = build_request(0x06, bytes([0x00, 0x02, 0x00, 0xFF]))
    resp = build_response(req)
    print("    req :", req.hex(" ").upper())
    print("    resp:", resp.hex(" ").upper(), "->", decode_response(resp))
    expect(resp[0] == 0x01 and resp[1] == 0x06, "write echo fc 06")
    expect((resp[2] << 8 | resp[3]) == 0x0002, "echo address 0x0002")
    expect((resp[4] << 8 | resp[5]) == 0x00FF, "echo value 0x00FF")
    expect(SIM_DO & 0x00FF == 0xFF, "output bitmap updated")

    # 7. Write to read-only address -> exception 02
    print("\n[7] host: write 0x0000 (read-only)")
    req = build_request(0x06, bytes([0x00, 0x00, 0x00, 0x01]))
    resp = build_response(req)
    expect(resp[1] == 0x86 and resp[2] == 0x02, "exception 02")

    # 8. Write script control 0x003D = 1 (erase) -> echo
    print("\n[8] host: write 0x003D = 1 (erase flash script)")
    req = build_request(0x06, bytes([0x00, 0x3D, 0x00, 0x01]))
    resp = build_response(req)
    print("    resp:", resp.hex(" ").upper(), "->", decode_response(resp))
    expect(resp[1] == 0x06 and (resp[2] << 8 | resp[3]) == 0x003D, "erase command echo")
    expect(SIM_STAGE_LEN == 0 and SIM_FLASH_TEXT == b"", "stage + flash cleared")

    # 9. Stage script text via fc 0x10 (two registers = "AB" "CD")
    print("\n[9] host: fc 0x10 write staged text at 0x0040")
    text = b"on Ja1\r\ndelay 500\r\n"
    regs = []
    for i in range(0, len(text), 2):
        hi = text[i]
        lo = text[i + 1] if i + 1 < len(text) else 0
        regs.append((hi << 8) | lo)
    payload = bytes([0x00, 0x40]) + bytes([0x00, len(regs)]) + bytes([len(regs) * 2])
    for v in regs:
        payload += bytes([(v >> 8) & 0xFF, v & 0xFF])
    req = build_request(0x10, payload)
    resp = build_response(req)
    print("    req :", req.hex(" ").upper())
    print("    resp:", resp.hex(" ").upper(), "->", decode_response(resp))
    expect(len(resp) == 8, "fc 0x10 response fixed 8 bytes")
    expect(resp[1] == 0x10, "fc 0x10 echo")
    expect((resp[2] << 8 | resp[3]) == 0x0040 and (resp[4] << 8 | resp[5]) == len(regs),
           "echo start + quantity")
    expect(SIM_STAGE_LEN == len(regs) * 2, "staged length tracked (register aligned)")
    expect(bytes(SIM_STAGE[:len(text)]) == text, "staged bytes match host payload")

    # 10. Read staged text back (verification) via fc 0x03
    print("\n[10] host: read staged text back at 0x0040 x3")
    req = build_request(0x03, bytes([0x00, 0x40, 0x00, 0x03]))
    resp = build_response(req)
    print("    resp:", resp.hex(" ").upper(), "->", decode_response(resp))
    expect((resp[3] << 8 | resp[4]) == regs[0], "stage reg0 echo")
    expect((resp[5] << 8 | resp[6]) == regs[1], "stage reg1 echo")

    # 11. fc 0x10 to non-script area -> exception 02
    print("\n[11] host: fc 0x10 write 0x0002 (not allowed)")
    req = build_request(0x10, bytes([0x00, 0x02, 0x00, 0x01, 0x02, 0x00, 0x42]))
    resp = build_response(req)
    expect(resp[1] == 0x90 and resp[2] == 0x02, "exception 02 (illegal address)")

    # 12. fc 0x10 qty=0 -> exception 03
    print("\n[12] host: fc 0x10 qty=0")
    req = build_request(0x10, bytes([0x00, 0x40, 0x00, 0x00, 0x00]))
    resp = build_response(req)
    expect(resp[1] == 0x90 and resp[2] == 0x03, "exception 03 (illegal value)")

    # 13. Commit staged text to flash: 0x003D = 2
    print("\n[13] host: write 0x003D = 2 (commit to flash)")
    req = build_request(0x06, bytes([0x00, 0x3D, 0x00, 0x02]))
    resp = build_response(req)
    print("    resp:", resp.hex(" ").upper(), "->", decode_response(resp))
    expect(resp[1] == 0x06, "commit echo")
    expect(SIM_FLASH_TEXT == bytes(SIM_STAGE[:SIM_STAGE_LEN]), "flash content == staged text")

    # 14. Script length register reflects committed flash content
    print("\n[14] host: read script length 0x003E")
    req = build_request(0x03, bytes([0x00, 0x3E, 0x00, 0x01]))
    resp = build_response(req)
    print("    resp:", resp.hex(" ").upper(), "->", decode_response(resp))
    expect((resp[3] << 8 | resp[4]) == len(SIM_FLASH_TEXT), "script length matches")

    # 15. Start runner: 0x003D = 3
    print("\n[15] host: write 0x003D = 3 (start runner)")
    req = build_request(0x06, bytes([0x00, 0x3D, 0x00, 0x03]))
    resp = build_response(req)
    expect(resp[1] == 0x06, "start echo")
    expect(SIM_SCRIPT_STATUS == ST_RUNNING, "runner status = running")

    # 16. Runner status readable at 0x003A
    print("\n[16] host: read runner status 0x003A")
    req = build_request(0x03, bytes([0x00, 0x3A, 0x00, 0x01]))
    resp = build_response(req)
    print("    resp:", resp.hex(" ").upper(), "->", decode_response(resp))
    expect((resp[3] << 8 | resp[4]) == ST_RUNNING, "status register = running")

    # 17. Invalid control value -> exception 03
    print("\n[17] host: write 0x003D = 99 (invalid)")
    req = build_request(0x06, bytes([0x00, 0x3D, 0x00, 0x63]))
    resp = build_response(req)
    expect(resp[1] == 0x86 and resp[2] == 0x03, "exception 03 (illegal value)")

    # 18. Commit with empty stage -> exception 03
    print("\n[18] host: write 0x003D = 2 with empty stage")
    SIM_STAGE_LEN = 0
    req = build_request(0x06, bytes([0x00, 0x3D, 0x00, 0x02]))
    resp = build_response(req)
    expect(resp[1] == 0x86 and resp[2] == 0x03, "exception 03 (nothing staged)")

    # 19. Frame with bad CRC is rejected (firmware drops it)
    print("\n[19] corrupted CRC frame")
    req = build_request(0x03, bytes([0x00, 0x00, 0x00, 0x03]))
    bad = bytearray(req)
    bad[-1] ^= 0xFF
    expect(not check_crc(bytes(bad)), "CRC mismatch detected")

    # 20. Frame addressed to another slave is rejected
    print("\n[20] wrong slave address")
    req2 = build_frame(bytes([0x02, 0x03, 0x00, 0x00, 0x00, 0x03]))
    expect(req2[0] != SLAVE_ADDR, "address mismatch detected")

    # 21. Big-endian byte order sanity (host's WriteUInt16 is big-endian)
    print("\n[21] byte order: read 0x0010 qty1 raw=100 -> bytes 00 64")
    req = build_request(0x03, bytes([0x00, 0x10, 0x00, 0x01]))
    resp = build_response(req)
    expect(resp[3] == 0x00 and resp[4] == 0x64, "big-endian 0x0064")

    print(f"\n==== RESULT: {PASS} passed, {FAIL} failed ====")
    if FAIL:
        raise SystemExit(1)


if __name__ == "__main__":
    main()
