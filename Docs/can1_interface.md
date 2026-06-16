# CAN1 接口说明

## 引脚

| 信号 | STM32H743 引脚 | 复用功能 | 方向 |
| --- | --- | --- | --- |
| CAN1_RX | PD0 | FDCAN1_RX AF9 | MCU 输入 |
| CAN1_TX | PA1 | FDCAN1_TX AF9 | MCU 输出 |

> 注意：CAN 总线不能直接接 MCU 引脚，需要外接 CAN 收发器，例如 TJA1050、SN65HVD230、TJA1042 等。MCU 侧为 FDCAN_TX/RX，收发器侧再接 CANH/CANL。

## 默认通信参数

| 参数 | 配置 |
| --- | --- |
| 外设 | FDCAN1 |
| 模式 | Classic CAN |
| 波特率 | 500 kbit/s |
| Kernel clock | PLLQ 96 MHz |
| Prescaler | 12 |
| TimeSeg1 | 13 tq |
| TimeSeg2 | 2 tq |
| SJW | 2 tq |
| 采样点 | 约 87.5% |

## 软件结构

| 文件 | 作用 |
| --- | --- |
| `Core/Inc/fdcan.h` / `Core/Src/fdcan.c` | FDCAN1 底层初始化，配置 PD0/PA1、500 kbit/s、中断 |
| `Services/Inc/can_service.h` / `Services/Src/can_service.c` | CAN 服务层，提供发送接口、接收回调和诊断计数 |
| `Services/Src/can_service_irq.c` | FDCAN1 IT0 中断入口 |

## 当前测试功能

当前服务层开启接收 FIFO0 中断，接受标准帧和扩展帧的数据帧。收到任意 CAN 数据帧后，会将相同 ID、帧类型、长度和数据原样回发，方便使用 CAN 分析仪做收发闭环测试。

可观察诊断变量：

```c
CAN_SERVICE_Diagnostics diag;
CAN_SERVICE_GetDiagnostics(&diag);
```

重点字段：

| 字段 | 含义 |
| --- | --- |
| `rx_count` | 接收帧计数 |
| `tx_count` | 发送帧计数 |
| `echo_count` | 成功回发计数 |
| `rx_error_count` | 接收错误计数 |
| `tx_error_count` | 发送错误计数 |
| `last_rx` | 最近一帧接收数据 |
| `last_tx` | 最近一帧发送数据 |
