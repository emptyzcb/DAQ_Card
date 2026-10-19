# Ethernet, RS485, and IO Controller Integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver reliable Modbus TCP communication, dynamic RS485 sensor acquisition, and complete rule-only control of every board output.

**Architecture:** W5500 terminates a standard Modbus TCP stream and adapts each PDU to the existing Modbus/IOCF handlers. The RS485 master uses a validated runtime poll table and publishes per-item snapshots through DataHub. The Windows application owns configuration and visualization while downloaded IOCF bytecode remains responsible for physical outputs.

**Tech Stack:** STM32H7 C/FreeRTOS/W5500, Modbus TCP and Modbus RTU, C# .NET 9 WinForms.

**Spec:** `docs/superpowers/specs/2026-10-15-ethernet-io-integration.md`

## Global Constraints

- Preserve existing validated BSP drivers and current uncommitted user changes.
- Ethernet endpoint remains `192.168.1.10:5000`.
- RS485 accepts runtime-variable acquisition items up to a defined capacity of 32.
- All ten physical outputs are controlled only by IOCF rules.
- Do not commit or push without an explicit user request.

## Review Focus

- TCP requests split across reads or combined in one read must be parsed exactly once.
- Invalid MBAP lengths must not overflow buffers or desynchronize the next connection.
- A silent RS485 slave must not starve other configured items.
- Applying a partially invalid RS485 table must leave the previous active table intact.
- Communication loss or invalid bytecode must never leave stale outputs energized.

---

### Task 1: Modbus TCP transport and W5500 stream handling

**Files:**
- Create: `Services/Inc/modbus_tcp.h`
- Create: `Services/Src/modbus_tcp.c`
- Create: `tests/host/test_modbus_tcp.c`
- Modify: `Application/Src/Task_eth.c`
- Modify: `Application/Inc/sys.h`
- Modify: `MDK-ARM/F430VI.uvprojx`

**Interfaces:**
- Produces: `MODBUS_TCP_PeekAduLength()` and `MODBUS_TCP_HandleAdu()`.
- Consumes: existing `MODBUS_RTU_HandleRequest()`.

- [x] Write host tests for incomplete, valid, coalesced, invalid, read, write, and IOCF ADUs.
- [x] Run the tests and verify they fail because the transport module is absent.
- [x] Implement MBAP validation and RTU/PDU adaptation.
- [x] Run the host tests and verify they pass.
- [x] Integrate a persistent RX accumulator into `Task_eth.c` and reset it on socket lifecycle changes.

### Task 2: Dynamic RS485 acquisition and DataHub snapshots

**Files:**
- Modify: `Services/Inc/modbus_master.h`
- Modify: `Services/Src/modbus_master.c`
- Modify: `Services/Inc/datahub.h`
- Modify: `Services/Src/datahub.c`
- Modify: `Services/Src/modbus_register_map.c`
- Create: `tests/host/test_modbus_master_config.c`

**Interfaces:**
- Produces: a 32-item staging/active configuration API and per-item DataHub snapshot API.
- Consumes: existing RS485 frame and UART services.

- [x] Write failing tests for table validation, atomic apply, scheduling after failure, and round-robin selection.
- [x] Implement runtime poll configuration with functions `0x03` and `0x04`.
- [x] Publish success/failure snapshots atomically through DataHub.
- [x] Expose active configuration, status, values, staging registers, and apply command in the register map.
- [x] Run host tests and static compile checks.

### Task 3: Rule compiler output completeness

**Files:**
- Modify: upper-computer `IoLogicCompiler.cs`
- Modify: upper-computer `Library/io_logic_types.h`
- Modify: upper-computer `Library/io_logic_output.h`
- Modify: upper-computer `Library/io_logic_analog.h`
- Modify: upper-computer compiler smoke tests.

**Interfaces:**
- Produces: named constants and compiler support for relays, transistor outputs, voltage output, and current output.
- Consumes: existing IOCF v2 opcodes.

- [x] Add failing compiler tests using all named physical outputs.
- [x] Add constant parsing and clear output APIs while preserving numeric compatibility.
- [x] Verify generated opcodes match the firmware VM.

### Task 4: Modbus TCP upper-computer client

**Files:**
- Replace: upper-computer `ModbusRtuClient.cs` with `ModbusTcpClient.cs`
- Modify: upper-computer `IoLogicProtocol.cs`
- Modify: upper-computer `MainForm.cs`
- Modify: upper-computer project file.
- Create: upper-computer TCP protocol tests.

**Interfaces:**
- Produces: transaction-safe `ModbusTcpClient` read/write/custom-function API.
- Consumes: Task 1 MBAP protocol and existing register/IOCF contracts.

- [x] Add a fake TCP board test for fragmented read, `0x06`, exception, and IOCF responses.
- [x] Implement and verify MBAP transaction handling.
- [x] Remove serial dependencies and obsolete direct-output write UI.
- [x] Verify IOCF download uses the Ethernet client.

### Task 5: RS485 sensor configuration and telemetry UI

**Files:**
- Create: upper-computer `Rs485Acquisition.cs`
- Modify: upper-computer `AcquisitionSnapshot.cs`
- Modify: upper-computer `MainForm.cs`
- Modify: upper-computer project metadata persistence.

**Interfaces:**
- Produces: editable acquisition rows, apply/readback workflow, and live external sensor values.
- Consumes: Task 2 register map and Task 4 client.

- [x] Add model/validation tests for 0-32 acquisition items.
- [x] Add the RS485 acquisition tab and project persistence.
- [x] Upload staging entries and atomically apply them.
- [x] Poll metadata and values without blocking IOCF downloads.

### Task 6: Versioning and full verification

**Files:**
- Modify: `Core/Inc/main.h`
- Modify: `PROJECT_STATUS.md`
- Modify: upper-computer status/about text where present.

**Interfaces:**
- Consumes all preceding tasks.

- [x] Update the firmware version and protocol documentation.
- [x] Run all C host tests and all C# tests/builds.
- [x] Inspect the final diff for accidental changes and verify no serial communication path remains active.
- [x] Perform an independent final code review and fix important findings.
