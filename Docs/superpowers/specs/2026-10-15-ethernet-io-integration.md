# Ethernet, RS485, and IO Controller Integration Specification

## Goal

Make the formal firmware and Windows upper computer communicate reliably over Ethernet, collect a runtime-configurable set of Modbus RTU sensors over RS485, and ensure every physical output is controlled through downloaded IO rules.

## Transport

- Ethernet uses standard Modbus TCP on `192.168.1.10:5000`.
- The firmware parses MBAP-framed TCP streams and therefore accepts fragmented and coalesced TCP data.
- Existing holding-register behavior and custom IOCF function `0x41` remain the application protocol.
- RS485 remains a Modbus RTU master-only bus for external sensors.

## Data Model

- DataHub owns coherent snapshots for onboard IMU, AD7606, digital IO, and external RS485 acquisition items.
- External sensor acquisition is runtime configurable rather than compiled into a fixed table.
- Capacity is 32 acquisition items. One item describes one Modbus register block and contains slave address, function `0x03` or `0x04`, start register, register count, and polling interval.
- Each item records current values, last-update timestamp, online state, and cumulative failures.
- A failed item is rescheduled at its configured interval and cannot monopolize the bus.

## IO Rules

- Relay outputs 1-4, transistor outputs 1-4, the 0-10 V output, and the 0-20 mA output are driven only by the IO rule engine.
- Register `0x0002` is read-only output feedback.
- The upper computer exposes named output constants and validates them in the rule compiler.
- Stopping, clearing, changing configuration, or entering a VM fault turns all digital and analog outputs off.

## Upper Computer

- Serial-port transport and terminology are removed from the active communication path.
- A Modbus TCP client validates transaction ID, protocol ID, unit ID, length, function, and exception responses.
- Onboard telemetry and external RS485 data are displayed over Ethernet.
- The user can edit and apply the RS485 acquisition table.
- IOCF compile, download, verify, activate, run, and stop operations use the same Ethernet connection.

## Verification

- Host-side C tests cover MBAP parsing, fragmented data, coalesced data, invalid headers, and RTU/TCP conversion.
- C# tests cover Modbus TCP read, write, exceptions, fragmentation, and IOCF custom responses.
- Compiler tests cover all eight digital outputs and both analog outputs using named constants.
- No commit or push is performed unless the user explicitly requests it.
