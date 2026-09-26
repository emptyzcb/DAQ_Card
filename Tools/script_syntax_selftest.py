#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
script_syntax_selftest.py - Syntax/execution self test for the firmware
script interpreter (Services/Src/script_runner.c).

The interpreter must behave identically to the Windows host
(ScriptConsoleForm.cs Parse/RunAsync).  This file re-implements the exact
firmware parser/executor semantics in Python and checks:

  * accepted syntax  (on/off/toggle/write/all/delay/read/loop n|forever/endloop)
  * channel names    (Ja1..Ja4, Jb1..Jb4, case-insensitive)
  * hex write        ("0x" prefix optional, 16-bit truncation)
  * comments/blank lines / trailing NUL padding (register-aligned staging)
  * rejected syntax  (unknown command, missing arg, bad channel/delay/loop)
  * runtime errors   (endloop without loop, step runaway)
  * loop semantics   (n times / forever) and bit manipulation

Run:  python script_syntax_selftest.py
"""

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

# ---------------------------------------------------------------------------
# Firmware parser/executor re-implementation (mirrors script_runner.c)
# ---------------------------------------------------------------------------
CMD_ON, CMD_OFF, CMD_TOGGLE, CMD_WRITE, CMD_ALL_ON, CMD_ALL_OFF = 0, 1, 2, 3, 4, 5
CMD_DELAY, CMD_READ, CMD_LOOP, CMD_ENDLOOP = 6, 7, 8, 9

CHANNELS = ["ja1", "ja2", "ja3", "ja4", "jb1", "jb2", "jb3", "jb4"]

class ParseError(Exception):
    def __init__(self, line):
        self.line = line

def ieq(a: str, b: str) -> bool:
    return a.lower() == b

def parse_uint(s: str):
    if not s or not s.isdigit():
        raise ValueError
    v = int(s)
    if v > 0x7FFFFFFF:
        raise ValueError
    return v

def parse_hex(s: str):
    t = s[2:] if s[:2].lower() == "0x" else s
    if not t or any(c not in "0123456789abcdefABCDEF" for c in t):
        raise ValueError
    return int(t, 16) & 0xFFFF

def parse_line(line: str, line_no: int):
    """Mirror of firmware parse_line(). Returns (kind, value) or raises ParseError."""
    # comment / NUL truncation (NUL represents register-aligned padding)
    cut = len(line)
    for i, c in enumerate(line):
        if c == "#" or c == "\0":
            cut = i
            break
    raw = line[:cut].strip()
    if not raw:
        return None
    parts = raw.split()
    cmd = parts[0].lower()
    arg = parts[1] if len(parts) > 1 else ""

    def need_arg():
        if len(parts) < 2:
            raise ParseError(line_no)

    if cmd in ("on", "off", "toggle"):
        need_arg()
        try:
            bit = CHANNELS.index(arg.lower())
        except ValueError:
            raise ParseError(line_no)
        kind = {"on": CMD_ON, "off": CMD_OFF, "toggle": CMD_TOGGLE}[cmd]
        return (kind, bit)
    if cmd == "write":
        need_arg()
        try:
            return (CMD_WRITE, parse_hex(arg))
        except ValueError:
            raise ParseError(line_no)
    if cmd == "all":
        if arg.lower() not in ("on", "off"):
            raise ParseError(line_no)
        return (CMD_ALL_ON if arg.lower() == "on" else CMD_ALL_OFF, 0)
    if cmd == "delay":
        need_arg()
        try:
            ms = parse_uint(arg)
        except ValueError:
            raise ParseError(line_no)
        return (CMD_DELAY, ms)
    if cmd == "read":
        if arg:
            raise ParseError(line_no)
        return (CMD_READ, 0)
    if cmd == "loop":
        need_arg()
        if arg.lower() == "forever":
            return (CMD_LOOP, 0)
        try:
            n = parse_uint(arg)
        except ValueError:
            raise ParseError(line_no)
        if n < 1:
            raise ParseError(line_no)
        return (CMD_LOOP, n)
    if cmd == "endloop":
        if arg:
            raise ParseError(line_no)
        return (CMD_ENDLOOP, 0)
    raise ParseError(line_no)

def parse_script(text: str):
    """Mirror of firmware parse_script(): text -> list of (kind, value, line)."""
    cmds = []
    for i, line in enumerate(text.split("\n")):
        if line.endswith("\r"):
            line = line[:-1]
        r = parse_line(line, i + 1)
        if r is not None:
            cmds.append((r[0], r[1], i + 1))
    return cmds

class RunError(Exception):
    pass

def run(cmds, start_bits=0x0000, max_steps=5000000, stop_after=None):
    """Mirror of firmware SCRIPT_RUNNER_Run() loop semantics.
    Returns (final_bits, steps, status) where status: 0 idle, 1 running,
    2 stopped, 3 error.  stop_after: stop after N executed commands (test aid)."""
    bits = start_bits
    loop_stack = []
    pc = 0
    steps = 0
    status = 1
    error_line = 0
    while pc < len(cmds):
        if stop_after is not None and steps >= stop_after:
            break
        kind, value, line = cmds[pc]
        if kind == CMD_ON:
            bits |= 1 << value
            pc += 1
        elif kind == CMD_OFF:
            bits &= ~(1 << value)
            pc += 1
        elif kind == CMD_TOGGLE:
            bits ^= 1 << value
            pc += 1
        elif kind == CMD_WRITE:
            bits = value
            pc += 1
        elif kind == CMD_ALL_ON:
            bits = 0x00FF
            pc += 1
        elif kind == CMD_ALL_OFF:
            bits = 0x0000
            pc += 1
        elif kind == CMD_DELAY:
            pc += 1
        elif kind == CMD_READ:
            pc += 1
        elif kind == CMD_LOOP:
            if len(loop_stack) >= 32:
                status = 3; error_line = line; break
            loop_stack.append((pc + 1, value))
            pc += 1
        elif kind == CMD_ENDLOOP:
            if not loop_stack:
                status = 3; error_line = line; break
            ret_pc, rem = loop_stack.pop()
            if rem == 0:
                loop_stack.append((ret_pc, 0))
                pc = ret_pc
            else:
                rem -= 1
                if rem > 0:
                    loop_stack.append((ret_pc, rem))
                    pc = ret_pc
                else:
                    pc += 1
        steps += 1
        if steps > max_steps:
            status = 3; error_line = line; break
    if status == 1 and pc >= len(cmds):
        status = 0
    return bits, steps, status, error_line

# ---------------------------------------------------------------------------
# Tests
# ---------------------------------------------------------------------------
def test_valid_syntax():
    print("\n== valid syntax ==")
    s = """# header comment
on Ja1
OFF jb4
Toggle JB1
write 0x00AA
write FF
all on
all off
delay 500
delay 0
read
loop 3
  on Ja2
endloop
loop forever
  off Ja3
endloop"""
    cmds = parse_script(s)
    expect(len(cmds) == 16, f"parsed 16 commands (got {len(cmds)})")
    expect(cmds[0][0] == CMD_ON and cmds[0][1] == 0, "on Ja1 -> bit0")
    expect(cmds[1][0] == CMD_OFF and cmds[1][1] == 7, "off jb4 -> bit7 (case-insensitive)")
    expect(cmds[2][0] == CMD_TOGGLE and cmds[2][1] == 4, "toggle JB1 -> bit4")
    expect(cmds[3][0] == CMD_WRITE and cmds[3][1] == 0x00AA, "write 0x00AA")
    expect(cmds[4][0] == CMD_WRITE and cmds[4][1] == 0x00FF, "write FF (no 0x prefix)")
    expect(cmds[5][0] == CMD_ALL_ON and cmds[6][0] == CMD_ALL_OFF, "all on / all off")
    expect(cmds[7][1] == 500 and cmds[8][1] == 0, "delay 500 / delay 0")
    expect(cmds[9][0] == CMD_READ, "read")
    expect(cmds[10][1] == 3 and cmds[13][1] == 0, "loop 3 / loop forever")

def test_trailing_newline_with_nul_padding():
    print("\n== NUL padding (fc 0x10 register-aligned staging) ==")
    # editor text ending with a newline gets a trailing NUL byte after staging
    text = "on Ja1\r\n" + "delay 500\r\n" + "\x00"
    cmds = parse_script(text)
    expect(len(cmds) == 2, f"NUL padding ignored (got {len(cmds)} commands)")

def test_runtime_bits():
    print("\n== runtime bit manipulation ==")
    s = "on Ja1\noff Ja1\ntoggle Ja1\ntoggle Ja1\nwrite 0x0005\nall on\nall off"
    cmds = parse_script(s)
    bits, steps, status, _ = run(cmds)
    expect(bits == 0x0000, f"final bits 0x0000 (got 0x{bits:04X})")
    expect(status == 0, "finished idle")

def test_loop_count():
    print("\n== loop n semantics ==")
    s = "loop 3\n  on Ja1\nendloop"
    cmds = parse_script(s)
    bits, steps, status, _ = run(cmds)
    expect(bits == 0x0001, f"on executed 3x, bit0 set (got 0x{bits:04X})")
    # each command (loop / on / endloop) counts as one step, host-compatible
    expect(steps == 7, f"7 steps incl. control commands (got {steps})")

def test_nested_loop():
    print("\n== nested loops ==")
    s = "loop 2\n  loop 3\n    on Ja1\n  endloop\nendloop"
    cmds = parse_script(s)
    bits, steps, status, _ = run(cmds)
    expect(bits == 0x0001, "bit0 set")
    expect(steps == 17, f"inner body executed 6x, total 17 steps incl. control (got {steps})")

def test_forever_stop():
    print("\n== loop forever + stop ==")
    s = "loop forever\n  on Ja1\n  off Ja1\nendloop"
    cmds = parse_script(s)
    bits, steps, status, _ = run(cmds, stop_after=5)
    expect(status == 1, "still running when stopped by host")
    expect(steps == 5, "stopped at 5 steps")

def test_step_runaway():
    print("\n== 5M step protection ==")
    s = "loop forever\n  read\nendloop"
    cmds = parse_script(s)
    bits, steps, status, error_line = run(cmds, max_steps=5000000)
    expect(status == 3, "runaway loop -> error status")

def test_invalid_syntax():
    print("\n== invalid syntax (host-identical rejection) ==")
    cases = {
        "on xxx": "unknown channel",
        "on": "missing arg",
        "write": "missing arg",
        "write 0xZZ": "bad hex",
        "all": "all without on/off",
        "all xx": "all with bad arg",
        "delay -1": "negative delay",
        "delay abc": "non-numeric delay",
        "loop 0": "loop zero",
        "loop abc": "loop non-numeric",
        "xyz 1": "unknown command",
        "endloop extra": "endloop with arg",
        "read extra": "read with arg",
    }
    for s, name in cases.items():
        try:
            parse_script(s)
            expect(False, f"{name}: should have raised")
        except ParseError:
            expect(True, f"{name}: rejected")

def test_error_line_reported():
    print("\n== error line number ==")
    s = "on Ja1\non bad\n"
    try:
        parse_script(s)
        expect(False, "should raise")
    except ParseError as e:
        expect(e.line == 2, f"error reported at line 2 (got {e.line})")

def test_endloop_without_loop_runtime():
    print("\n== endloop without loop (runtime error) ==")
    cmds = parse_script("endloop")
    bits, steps, status, error_line = run(cmds)
    expect(status == 3, "runtime error status")
    expect(error_line == 1, f"error at line 1 (got {error_line})")

def test_host_example_script():
    print("\n== host default example (blink) ==")
    s = ("# 示例：让 Ja1 一直闪烁（点\"停止\"结束）\r\n"
         "loop forever\r\n"
         "  on Ja1\r\n"
         "  delay 500\r\n"
         "  off Ja1\r\n"
         "  delay 500\r\n"
         "endloop")
    cmds = parse_script(s)
    expect(len(cmds) == 6, f"example parsed to 6 commands (got {len(cmds)})")
    expect(cmds[0][0] == CMD_LOOP and cmds[0][1] == 0, "loop forever")
    expect(cmds[1][1] == 0 and cmds[3][1] == 0, "on/off Ja1 = bit0")

def main():
    print("== script interpreter syntax/execution self test (mirrors host) ==")
    test_valid_syntax()
    test_trailing_newline_with_nul_padding()
    test_runtime_bits()
    test_loop_count()
    test_nested_loop()
    test_forever_stop()
    test_step_runaway()
    test_invalid_syntax()
    test_error_line_reported()
    test_endloop_without_loop_runtime()
    test_host_example_script()
    print(f"\n==== RESULT: {PASS} passed, {FAIL} failed ====")
    if FAIL:
        raise SystemExit(1)

if __name__ == "__main__":
    main()
