# Modbus RTU Slave Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 在现有 STM32H7 平台中增加基于 USART1 的 Modbus RTU 从机线程，并保留已经验证正确的 PA12 RS485 方向逻辑。

**Architecture:** USART1 RS485 服务负责 ReceiveToIdle 中断接收、帧队列和方向控制；纯协议服务负责 CRC、帧校验和功能码；寄存器映射服务负责把 DataHub 与数字 IO 映射到 Modbus 寄存器；Application 层独立 FreeRTOS 线程串起接收、处理和发送。

**Tech Stack:** STM32H7 HAL, FreeRTOS/CMSIS-RTOS v2, C99-compatible embedded C, Keil project file.

**Spec:** `docs/superpowers/specs/2026-09-20-modbus-rtu-slave-design.md`

## Global Constraints

- Modbus 从机地址固定默认为 `1`，串口保持 `115200 8N1`。
- RS485 方向控制不得改变：PA12 高电平发送，低电平接收。
- 协议解析在任务上下文执行，中断只接收并排队完整帧。
- 不启用临时 RS485 调试死循环，不向 RS485 总线输出 `printf` 日志。
- 本轮不编译、不提交、不推送。

## Review Focus

- USART1 接收空闲后必须重新启动接收，否则只能处理第一帧；由 UART 服务帧接收检查覆盖。
- 发送必须等待最后一个停止位完成后再释放 PA12；由发送函数静态检查和协议响应路径覆盖。
- 广播写请求不能产生响应；由协议向量检查覆盖。
- CRC、地址和非法寄存器必须分别得到丢弃或标准异常行为；由协议向量检查覆盖。
- FreeRTOS 启动路径不能再进入调试死循环；由启动宏和任务注册静态检查覆盖。

---

### Task 1: 切换正常 RTOS 启动并保留硬件方向配置

**Files:**
- Modify: `Core/Inc/main.h`
- Modify: `Core/Src/main.c`
- Modify: `Core/Src/gpio.c`
- Modify: `Core/Src/usart.c`
- Modify: `Core/Src/stm32h7xx_it.c`

**Interfaces:**
- Produces USART1 interrupt entry and PA12 direction initialization for the RS485 service.

- [ ] 将 `APP_RS485_DEBUG_ONLY` 设为 `0U`，保留 `RS485_DIR_TX`/`RS485_DIR_RX` 定义不变。
- [ ] 让正常 GPIO 初始化路径配置 PA12 为默认低电平推挽输出。
- [ ] 给 USART1 打开接收中断并在 `USART1_IRQHandler` 调用 HAL IRQ 入口。
- [ ] 保留现有 USART1 SWAP 配置和 PA9/PA10 复用配置。
- [ ] 静态检查启动路径只调用 FreeRTOS 初始化，不再调用 `RS485_Debug_Run`。

### Task 2: 重建 USART1 RS485 帧服务

**Files:**
- Modify: `Services/Inc/rs485_uart.h`
- Modify: `Services/Src/rs485_uart.c`
- Modify: `Services/Src/rs485_uart_irq.c`

**Interfaces:**
- Produces `RS485_UART_Init`, `RS485_UART_TryReceiveFrame` and `RS485_UART_Send`.
- `RS485_UART_TryReceiveFrame(uint8_t *dst, uint16_t capacity, uint16_t *length)` returns one idle-delimited frame without blocking.
- `RS485_UART_Send(const uint8_t *data, uint16_t length, uint32_t timeout_ms)` controls PA12 and returns HAL success/failure.

- [ ] 使用 `HAL_UARTEx_ReceiveToIdle_IT(&huart1, ...)` 接收最大 256 字节帧。
- [ ] 在 `HAL_UARTEx_RxEventCallback` 中把接收片段复制到固定帧队列并立即重启接收。
- [ ] 在 `RS485_UART_Send` 中按“PA12 高、HAL_UART_Transmit、PA12 低”的顺序执行。
- [ ] 添加接收队列溢出计数，队列满时丢弃新帧，不阻塞中断。
- [ ] 删除当前 USART3 DMA 回调对 Modbus 服务的依赖，避免 USART3 和 USART1 混用。

### Task 3: 实现纯 Modbus RTU 协议服务

**Files:**
- Create: `Services/Inc/modbus_rtu.h`
- Create: `Services/Src/modbus_rtu.c`

**Interfaces:**
- Produces `MODBUS_RTU_Crc16` and `MODBUS_RTU_HandleRequest`.
- `MODBUS_RTU_HandleRequest(const uint8_t *request, uint16_t request_length, uint8_t *response, uint16_t response_capacity)` returns response length, or zero for discarded/broadcast requests.
- Consumes `MODBUS_REGISTER_ReadHolding` and `MODBUS_REGISTER_WriteHolding`.

- [ ] 实现 Modbus 标准 CRC16，低字节在前。
- [ ] 校验最小帧、最大帧、地址、CRC 和功能码专属长度。
- [ ] 支持 `0x03`、`0x06`、`0x10`。
- [ ] 对非法功能、地址、值/数量返回标准异常帧。
- [ ] 地址 0 的写请求执行写入但不生成响应。

### Task 4: 实现平台寄存器映射

**Files:**
- Create: `Services/Inc/modbus_register_map.h`
- Create: `Services/Src/modbus_register_map.c`

**Interfaces:**
- Produces `MODBUS_REGISTER_ReadHolding(uint16_t address, uint16_t *value)` and `MODBUS_REGISTER_WriteHolding(uint16_t address, uint16_t value)`.
- Consumes `DataHub_GetImu`, `DataHub_GetAd7606`, `DIGITAL_IO_SERVICE_ReadInputs` and `DIGITAL_IO_SERVICE_SetOutputMask`.

- [ ] 实现设计文档中的状态、数字 IO、AD7606、IMU 和姿态寄存器。
- [ ] 读操作只读快照，不直接访问硬件总线。
- [ ] 写操作仅允许数字输出寄存器 `0x0002`，其余寄存器返回不可写。
- [ ] 姿态浮点值转换为有符号 `0.01 deg`，超出 int16 范围时饱和。

### Task 5: 增加独立 Modbus FreeRTOS 线程

**Files:**
- Create: `Application/Inc/Task_modbus_rtu.h`
- Create: `Application/Src/Task_modbus_rtu.c`
- Modify: `Application/Src/ALL_Task.c`
- Modify: `Application/Inc/sys.h`
- Modify: `MDK-ARM/F430VI.uvprojx`

**Interfaces:**
- Produces `Task_modbus_rtu(void *arg)` and registers it through `vMyFreeRTOS_Task_Start`.

- [ ] 线程初始化 RS485 UART 服务和 Modbus 配置。
- [ ] 每次循环读取完整帧、调用协议服务、发送响应，空闲时延时 1 ms。
- [ ] 使用独立任务栈和优先级，不复用 USART3 echo 任务。
- [ ] 将新 `.c/.h` 文件加入 Keil 工程组和 include path 所在组。

### Task 6: 协议向量和静态验证

**Files:**
- Create: `Tools/test_modbus_rtu_vectors.py`

- [ ] 先运行协议向量脚本，确认 CRC、正常读写、广播写和异常帧行为。
- [ ] 运行 `git diff --check`。
- [ ] 使用 `rg` 核对 `APP_RS485_DEBUG_ONLY`、USART1 IRQ、PA12 方向顺序和任务注册。
- [ ] 不执行工程编译；明确记录用户需要在 Keil 中自行编译下载。
