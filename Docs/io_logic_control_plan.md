# IO 逻辑控制与掉电保存方案

## 1. 目标

实现以下功能：

1. 在 Windows 上位机中编辑板卡 IO 控制逻辑。
2. 上位机将逻辑编译成 MCU 可执行的紧凑二进制程序。
3. 通过当前 RS485 链路下载到 STM32H743VIT6。
4. MCU 将程序保存到内部 Flash，掉电、复位后自动恢复。
5. MCU 脱离上位机后，仍然可以独立扫描输入并控制输出。
6. 保留现有 Modbus RTU 采集、状态读取和手动输出功能。

本方案中的“烧录控制程序”指下载 IO 逻辑配置，不是通过 RS485 下载任意 C 代码。基础固件仍然使用 Keil 和 ST-Link 烧录。这样可以避免动态执行未知代码，也不会破坏现有 RS485、采集和保护功能。

## 1.1 最终实现方式

| 内容 | 最终选择 |
| --- | --- |
| 上位机编写方式 | 图形化“条件-动作”规则卡片 |
| 用户是否直接写 C | 不支持 |
| 上位机保存格式 | JSON 工程文件 |
| MCU 保存格式 | 编译后的二进制规则文件 |
| MCU 执行方式 | 固件内置规则解释器 |
| 规则是否依赖上位机 | 不依赖，下载后 MCU 独立运行 |
| IO 状态显示 | 需要，默认 250 ms 周期刷新 |

不选择 C 语言编辑器的原因：

- C 代码需要交叉编译、链接和完整固件升级。
- 任意 C 代码可能破坏 FreeRTOS、RS485、Flash 和输出安全策略。
- C 代码无法直接保证规则下载过程掉电安全。
- 用户只需要配置 IO 逻辑，不需要重新开发 MCU 固件。

因此用户在上位机中看到的是图形化规则，MCU 中运行的是经过校验的规则二进制。

## 1.2 两端职责边界

### 上位机负责

1. 创建、打开、保存 IO 工程。
2. 通过图形化控件编辑规则。
3. 检查通道、时间、优先级和输出冲突。
4. 将规则编译为 `IOCF` 二进制程序。
5. 通过 RS485 分包下载程序。
6. 发送 VERIFY 和 ACTIVATE。
7. 读取 MCU 的配置版本、CRC 和运行状态。
8. 周期读取输入、输出和规则状态并实时显示。
9. 保存本地工程和下载记录。

### MCU 负责

1. 接收、缓存和校验配置文件。
2. 将配置保存到内部 Flash 双槽。
3. 上电选择最新的有效配置。
4. 周期扫描 X1~X8。
5. 执行规则和定时器。
6. 根据逻辑状态控制 4 路继电器和 4 路晶体管。
7. 通过 Modbus 返回实时 IO 状态。
8. 在配置错误、掉电或通信中断时执行安全策略。

## 1.3 上位机规则编辑器的具体形式

第一版使用“规则卡片列表”，不做自由连线的复杂梯形图。每张卡片表示一条完整规则，编辑简单、容易校验，也方便编译成固定格式。

单条规则的界面字段固定为：

```text
规则名称
启用开关
优先级
触发方式：电平 / 上升沿 / 下降沿
输入条件：X1~X8 多选
组合方式：AND / OR
期望状态：有效 / 无效
延时：0~65535 ms
动作输出：Relay1~Relay4、Transistor1~Transistor4 多选
动作：打开 / 关闭 / 翻转 / 脉冲
脉冲时间：0~65535 ms
```

上位机操作示例：

```text
[启用] 规则 1：启动水泵
触发：电平
条件：X1 = 有效 AND X2 = 无效
延时：100 ms
动作：Relay1 打开

[启用] 规则 2：报警脉冲
触发：X3 上升沿
条件：X3 = 有效
延时：0 ms
动作：Transistor1 脉冲 1000 ms
```

上位机最终生成的工程文件示例：

```json
{
  "projectName": "现场泵站控制",
  "slaveAddress": 1,
  "scanPeriodMs": 10,
  "rules": [
    {
      "name": "启动水泵",
      "enabled": true,
      "priority": 100,
      "conditionType": "level",
      "operator": "and",
      "inputMask": 3,
      "inputExpected": 1,
      "delayMs": 100,
      "outputMask": 1,
      "action": "set",
      "pulseMs": 0
    }
  ]
}
```

这个 JSON 只保存在上位机，下载前由 `IoLogicCompiler` 转换为 MCU 使用的 `IOCF` 二进制。

## 1.4 上位机实时 IO 显示

需要实时显示。原因是现场调试时必须同时确认：输入是否进入 MCU、规则是否执行、输出是否真正发出、通信是否仍在线。

上位机页面增加“IO 实时监视”区域：

| 区域 | 显示内容 |
| --- | --- |
| 输入区 | X1~X8 当前状态、有效/无效、变化时间 |
| 输出区 | Relay1~Relay4、Transistor1~Transistor4 当前逻辑状态 |
| MCU 区 | 自动/手动/安全模式、规则运行/停止 |
| 配置区 | 规则版本、generation、CRC32、Flash 槽 |
| 通信区 | 串口状态、最后响应时间、超时计数、CRC 错误计数 |
| 日志区 | 原始 TX/RX 帧、规则下载过程和错误原因 |

显示颜色约定：

```text
绿色：当前有效或打开
灰色：当前无效或关闭
黄色：状态过期，超过 1 秒未收到响应
红色：通信错误、配置错误或 MCU 安全状态
```

实时显示采用上位机主动轮询，不采用 MCU 主动推送，原因是当前系统已经是 Modbus RTU 主从结构，主动推送会破坏总线仲裁。

推荐轮询周期：

| 状态 | 周期 |
| --- | ---: |
| 普通实时监视 | 250 ms |
| 高速调试 | 100 ms |
| 低速节省通信 | 1000 ms |

每个轮询周期至少读取：

```text
0x0000：设备状态
0x0001：数字输入位图
0x0002：数字输出位图
```

上位机必须记录 `lastResponseTime`。超过 1 秒没有有效响应时，所有 IO 指示灯显示为“状态过期”，不能继续显示为实时状态。

## 2. 硬件和通道定义

### 2.1 MCU

| 项目 | 定义 |
| --- | --- |
| MCU | STM32H743VIT6 |
| 固件工具链 | Keil MDK-ARM |
| 通信 | USART1 + RS485 |
| 从机地址 | 默认 `0x01` |
| 默认波特率 | `115200` |
| 数据格式 | `8-N-1` |
| 规则扫描周期 | 默认 `10 ms` |

### 2.2 RS485 引脚

| MCU 引脚 | 网络名称 | 作用 |
| --- | --- | --- |
| PA9 | `485_USART1_RX` | USART1 接收，连接 485 芯片 RXD |
| PA10 | `485_USART1_TX` | USART1 发送，连接 485 芯片 TXD |
| PA12 | `485_USART1_DIR` | 收发方向控制 |

RS485 方向保持当前已经验证正确的逻辑：

| PA12 | 485 状态 |
| --- | --- |
| `0` | 接收 |
| `1` | 发送 |

发送顺序必须严格为：

```text
PA12 = 1
等待发送完成
发送最后一个停止位
PA12 = 0
```

### 2.3 数字输入

输入通道按当前板卡软件定义如下。输入默认采用低电平有效，并使用 GPIO 上拉；如果后续硬件验证为高电平有效，只修改 BSP 有效电平定义，不修改协议位号。

| 通道 | MCU 引脚 | 位 | 默认有效电平 |
| --- | --- | ---: | --- |
| X1 | PE9 | bit0 | 低电平 |
| X2 | PB7 | bit1 | 低电平 |
| X3 | PD10 | bit2 | 低电平 |
| X4 | PA15 | bit3 | 低电平 |
| X5 | PC0 | bit4 | 低电平 |
| X6 | PB0 | bit5 | 低电平 |
| X7 | PB1 | bit6 | 低电平 |
| X8 | PE8 | bit7 | 低电平 |

输入状态寄存器 `0x0001` 的定义：

```text
bit0 = X1
bit1 = X2
bit2 = X3
bit3 = X4
bit4 = X5
bit5 = X6
bit6 = X7
bit7 = X8
bit8..bit15 = 0
```

### 2.4 数字输出

逻辑程序只控制 8 路数字输出，不控制模拟量 `DO_I` 和 `DO_U`。

| 输出 | MCU 引脚 | 位 | MCU 有效电平 | 逻辑含义 |
| --- | --- | ---: | --- | --- |
| Relay1 | PD15 | bit0 | 低电平 | `1` = 继电器吸合 |
| Relay2 | PD14 | bit1 | 低电平 | `1` = 继电器吸合 |
| Relay3 | PE0 | bit2 | 低电平 | `1` = 继电器吸合 |
| Relay4 | PE1 | bit3 | 低电平 | `1` = 继电器吸合 |
| Transistor1 | PA11 | bit4 | 高电平 | `1` = 输出打开 |
| Transistor2 | PA8 | bit5 | 高电平 | `1` = 输出打开 |
| Transistor3 | PC7 | bit6 | 高电平 | `1` = 输出打开 |
| Transistor4 | PC6 | bit7 | 高电平 | `1` = 输出打开 |

输出状态寄存器 `0x0002` 的定义：

```text
bit0 = Relay1
bit1 = Relay2
bit2 = Relay3
bit3 = Relay4
bit4 = Transistor1
bit5 = Transistor2
bit6 = Transistor3
bit7 = Transistor4
bit8..bit9 = 预留给 DO_I / DO_U，不属于 IO 逻辑程序
bit10..bit15 = 保留，必须为 0
```

逻辑层使用“有效输出状态”表示输出，BSP 层负责转换为真实 GPIO 电平。这样上位机不需要关心继电器低电平有效的问题。

## 3. IO 逻辑模型

### 3.1 第一版支持的条件

每条规则由一个条件和一个动作组成。

条件支持：

- 单个输入有效或无效。
- 多个输入 AND。
- 多个输入 OR。
- 输入上升沿。
- 输入下降沿。
- 输入条件成立后延时执行。

动作支持：

- 输出置位。
- 输出复位。
- 输出翻转。
- 输出脉冲。
- 脉冲宽度设置。
- 规则优先级。

示例：

```text
规则 1：X1 有效 -> Relay1 置位
规则 2：X2 有效 AND X3 有效 -> Transistor1 置位
规则 3：X4 上升沿 -> Relay2 输出 1000 ms 脉冲
规则 4：X5 无效 -> Relay3 复位
```

### 3.2 规则限制

第一版固定限制如下：

| 项目 | 限制 |
| --- | ---: |
| 最大规则数量 | 32 |
| 最大程序大小 | 4096 字节 |
| 最大同时运行定时器 | 16 |
| 最小扫描周期 | 1 ms |
| 默认扫描周期 | 10 ms |
| 最大脉冲时间 | 65535 ms |
| 规则输出范围 | bit0..bit7 |

上位机编译阶段必须检查：

- 使用了不存在的输入或输出。
- 输出掩码使用 bit8 以上。
- 规则数量超限。
- 程序大小超限。
- 定时器数量超限。
- 同优先级规则同时控制同一输出。
- 规则包含不支持的动作。

## 4. MCU 内部程序格式

上位机本地工程使用 JSON 保存，下载给 MCU 的内容使用二进制格式。MCU 不解析 JSON。

### 4.1 程序头

程序头固定 32 字节，所有多字节字段使用大端序；Flash 内部的单片机结构体不得直接依赖 C 编译器对齐，必须按字节读写。

| 偏移 | 长度 | 字段 | 定义 |
| ---: | ---: | --- | --- |
| 0 | 4 | magic | `0x494F4346`，ASCII 为 `IOCF` |
| 4 | 2 | format_version | 当前为 `0x0001` |
| 6 | 2 | header_length | 当前为 `32` |
| 8 | 4 | generation | 配置生成序号，递增 |
| 12 | 4 | flags | bit0=启用，bit1=掉电恢复 |
| 16 | 2 | scan_period_ms | 规则扫描周期 |
| 18 | 2 | rule_count | 规则数量 |
| 20 | 4 | body_length | 规则体长度 |
| 24 | 4 | body_crc32 | 规则体 CRC32 |
| 28 | 4 | header_crc32 | 前 28 字节 CRC32 |

### 4.2 单条规则

每条规则固定 20 字节，规则按优先级从高到低执行。

| 偏移 | 长度 | 字段 | 定义 |
| ---: | ---: | --- | --- |
| 0 | 1 | condition_type | `0`=电平，`1`=边沿 |
| 1 | 1 | input_operator | `0`=AND，`1`=OR |
| 2 | 2 | input_mask | 选择参与判断的输入位 |
| 4 | 2 | input_expected | 期望输入值 |
| 6 | 1 | edge_type | `0`=无，`1`=上升沿，`2`=下降沿 |
| 7 | 1 | action | `0`=置位，`1`=复位，`2`=翻转，`3`=脉冲 |
| 8 | 2 | output_mask | 选择要控制的输出位，仅 bit0..bit7 |
| 10 | 2 | action_value | 预留，当前置 0 |
| 12 | 2 | delay_ms | 条件成立后延时 |
| 14 | 2 | pulse_ms | 脉冲动作持续时间 |
| 16 | 1 | priority | `0`最低，`255`最高 |
| 17 | 1 | enabled | `0`禁用，`1`启用 |
| 18 | 2 | reserved | 必须为 0 |

电平条件的判断定义：

```text
selected = input_state & input_mask
expected = input_expected & input_mask
AND：selected == expected
OR ：至少有一个被选择的输入满足期望值
```

边沿条件使用本次扫描值和上次扫描值计算。规则执行顺序固定，输出冲突由优先级解决；相同优先级冲突由上位机禁止下载。

## 5. Flash 掉电保存

### 5.1 Flash 分区

STM32H743VIT6 使用 2 MB 内部 Flash。建议从固件可用代码空间的末尾预留两个 128 KB 扇区：

| 区域 | 地址 | 大小 | 用途 |
| --- | ---: | ---: | --- |
| 固件代码区 | `0x08000000` | `0x001C0000` | 程序代码和常量 |
| 配置槽 A | `0x081C0000` | `0x00020000` | 当前/备用配置 |
| 配置槽 B | `0x081E0000` | `0x00020000` | 当前/备用配置 |

实际修改 Keil scatter 文件前必须核对芯片 Flash 容量和当前 map 文件，确认固件不会超过 `0x081C0000`。

### 5.2 双槽提交流程

MCU 始终保留一份已经验证成功的配置。

```text
读取当前有效槽
        |
接收新配置到 RAM
        |
检查长度、magic、版本和 CRC
        |
擦除非当前槽
        |
写入非当前槽
        |
回读并再次校验
        |
写入 VALID 标志和 generation
        |
切换当前槽
```

启动时：

1. 读取 A、B 两个槽。
2. 检查 magic、长度、CRC 和 VALID 标志。
3. 选择 generation 较大的有效槽。
4. 两个槽都无效时加载安全默认配置。
5. 默认配置为所有继电器和晶体管关闭。

配置下载过程中断电时，旧槽仍然有效；只有完整回读校验成功后，新槽才会被激活。

### 5.3 Flash 接口

建议增加以下 MCU 模块：

```text
Services/Inc/io_config_storage.h
Services/Src/io_config_storage.c
Services/Inc/io_logic_engine.h
Services/Src/io_logic_engine.c
Services/Inc/io_logic_protocol.h
Services/Src/io_logic_protocol.c
```

接口建议：

```c
int IO_CONFIG_Load(IO_CONFIG_Image *image);
int IO_CONFIG_Begin(uint16_t session, uint32_t size, uint32_t crc32);
int IO_CONFIG_WriteChunk(uint32_t offset, const uint8_t *data, uint16_t length);
int IO_CONFIG_Verify(void);
int IO_CONFIG_Activate(void);
int IO_CONFIG_Clear(void);
int IO_CONFIG_GetInfo(IO_CONFIG_Info *info);
```

Flash 写入只在配置下载时发生，正常 IO 扫描不写 Flash，避免频繁擦写。

## 6. RS485 配置传输协议

### 6.1 保留的标准 Modbus 功能

现有功能保持不变：

| 功能码 | 用途 |
| ---: | --- |
| `0x03` | 读取保持寄存器 |
| `0x06` | 写单个保持寄存器 |
| `0x10` | 写多个保持寄存器 |

现有寄存器：

| 地址 | 说明 |
| ---: | --- |
| `0x0000` | 设备状态 |
| `0x0001` | 数字输入位图 |
| `0x0002` | 数字输出位图 |

### 6.2 自定义功能码

增加自定义功能码 `0x41`，只用于 IO 逻辑程序管理。

标准 RTU 帧结构：

```text
地址(1) + 功能码(1) + 子命令和数据(N) + CRC_L(1) + CRC_H(1)
```

说明：

- 地址使用当前 Modbus 从机地址，默认 `0x01`。
- `0x41` 的多字节字段使用大端序。
- CRC16 使用 Modbus CRC，初值 `0xFFFF`，多项式 `0xA001`，低字节先发送。
- CRC 计算范围为地址到最后一个数据字节，不包含 CRC 本身。
- 单帧最大数据块为 128 字节。
- 接收方校验失败时不执行任何动作。

### 6.3 响应状态码

所有 `0x41` 正常响应的第二个字段为状态码。

| 状态码 | 含义 |
| ---: | --- |
| `0x00` | 成功 |
| `0x01` | 忙 |
| `0x02` | 会话不存在 |
| `0x03` | 偏移错误 |
| `0x04` | 长度错误 |
| `0x05` | CRC 错误 |
| `0x06` | 格式版本不支持 |
| `0x07` | 程序大小超限 |
| `0x08` | 规则数量超限 |
| `0x09` | 规则内容非法 |
| `0x0A` | Flash 写入失败 |
| `0x0B` | Flash 回读校验失败 |
| `0x0C` | 无效输出位 |
| `0x0D` | 当前状态禁止操作 |
| `0x0E` | 超时自动取消 |

错误响应使用：

```text
地址 + 0xC1 + 原功能码 + 错误码 + CRC_L + CRC_H
```

### 6.4 GET_INFO：读取配置状态

请求：

```text
01 41 01 CRC_L CRC_H
```

响应数据区：

| 字段 | 长度 |
| --- | ---: |
| 子命令 `0x01` | 1 |
| 状态码 | 1 |
| 格式版本 | 2 |
| 运行状态 | 1 |
| 配置状态 | 1 |
| 规则数量 | 2 |
| 配置长度 | 4 |
| 配置 CRC32 | 4 |
| generation | 4 |
| 扫描周期 ms | 2 |
| 最后错误码 | 2 |

运行状态：

```text
0 = 停止
1 = 运行
2 = 配置下载中
3 = 配置校验失败
```

配置状态：

```text
0 = 无有效配置
1 = 槽 A 有效
2 = 槽 B 有效
3 = 使用安全默认配置
```

### 6.5 BEGIN：开始下载

请求数据区：

| 字段 | 长度 | 定义 |
| --- | ---: | --- |
| 子命令 `0x02` | 1 | BEGIN |
| session | 2 | 上位机生成的会话号 |
| format_version | 2 | 程序格式版本 |
| flags | 2 | bit0=下载后启用 |
| image_length | 4 | 完整配置文件长度 |
| image_crc32 | 4 | 完整配置文件 CRC32 |
| rule_count | 2 | 规则数量 |

示例：

```text
01 41 02 SS SS VV VV FF FF LL LL LL LL CC CC CC CC RR RR CRC_L CRC_H
```

MCU 响应：

```text
地址 41 02 状态码 session_H session_L 最大块长度 CRC_L CRC_H
```

BEGIN 成功后，MCU 继续使用旧规则运行，新配置写入 RAM 和非当前 Flash 槽。

### 6.6 DATA：发送配置数据块

请求数据区：

| 字段 | 长度 | 定义 |
| --- | ---: | --- |
| 子命令 `0x03` | 1 | DATA |
| session | 2 | 当前会话号 |
| offset | 4 | 相对于配置文件起始位置的偏移 |
| data_length | 1 | 数据长度，1~128 |
| data | N | 配置文件数据 |

示例：

```text
01 41 03 SS SS OO OO OO OO NN DATA... CRC_L CRC_H
```

建议上位机按连续偏移发送：

```text
offset = 0
offset = 128
offset = 256
...
```

MCU 响应：

```text
地址 41 03 状态码 session_H session_L offset(4) accepted_length CRC_L CRC_H
```

### 6.7 VERIFY：校验完整配置

请求：

```text
01 41 04 SS SS CRC_L CRC_H
```

响应：

```text
地址 41 04 状态码
      received_length(4)
      calculated_crc32(4)
      CRC_L CRC_H
```

只有 VERIFY 成功后才允许 ACTIVATE。

### 6.8 ACTIVATE：激活配置

请求：

```text
01 41 05 SS SS CRC_L CRC_H
```

响应：

```text
地址 41 05 状态码 generation(4) active_slot(1) CRC_L CRC_H
```

ACTIVATE 成功后，MCU 在下一个 IO 扫描边界切换到新规则。默认不自动改变输出，规则由下一次扫描结果决定。

### 6.9 READ_DATA：回读配置

请求数据区：

| 字段 | 长度 |
| --- | ---: |
| 子命令 `0x06` | 1 |
| offset | 4 |
| length | 1 |

`length` 最大 128 字节。

响应数据区：

```text
子命令 0x06
状态码
offset(4)
length(1)
data(length)
```

上位机下载后必须回读头部和至少一部分规则数据；完整工程校验时回读全部数据并计算 CRC32。

### 6.10 ABORT、CLEAR、RUN、STOP

| 子命令 | 定义 | 请求 |
| ---: | --- | --- |
| `0x07` | ABORT | `子命令 + session` |
| `0x08` | CLEAR | `子命令`，清除配置并恢复安全默认配置 |
| `0x09` | RUN | `子命令`，启动当前有效规则 |
| `0x0A` | STOP | `子命令`，停止规则并关闭逻辑输出 |

CLEAR、RUN、STOP 均需要 MCU 返回状态码。CLEAR 不擦除固件，只处理配置区。

## 7. 传输时序和可靠性

上位机必须使用请求-响应方式，不允许连续无间隔发送多个 DATA 帧。

建议参数：

| 项目 | 参数 |
| --- | --- |
| 响应超时 | 500 ms |
| 单帧最大数据 | 128 字节 |
| 单帧失败重试 | 3 次 |
| 重试方式 | 使用相同 session、offset 和数据 |
| DATA 顺序 | 必须连续递增 |
| 下载总超时 | 30 s |
| 传输完成后 | VERIFY -> ACTIVATE -> GET_INFO |

完整下载流程：

```text
GET_INFO
BEGIN
DATA(offset=0)
DATA(offset=128)
DATA(...)
VERIFY
ACTIVATE
GET_INFO
READ_DATA(可选)
```

任何步骤失败时：

```text
发送 ABORT
保持旧规则运行
记录错误日志
```

## 8. 上位机工程文件

上位机本地工程建议保存为 JSON：

```text
Projects/<工程名称>.iojson
```

工程包含：

```text
工程名称
板卡型号
从机地址
输入通道名称
输出通道名称
扫描周期
规则列表
程序格式版本
最后生成的 CRC32
最后下载时间
```

上位机代码建议增加：

```text
Host/IndustrialFieldDataAcquisitionTerminal/Logic/
    IoLogicProject.cs
    IoLogicRule.cs
    IoLogicCompiler.cs
    IoLogicBinaryFormat.cs
    IoLogicProtocol.cs
    IoLogicDownloadService.cs
```

界面增加“IO逻辑配置”页面，至少包含：

- 输入条件选择。
- AND/OR 选择。
- 边沿选择。
- 输出动作选择。
- 延时和脉冲时间。
- 规则优先级。
- 规则启用/禁用。
- 编译检查。
- 下载、校验、激活、回读和清除。
- 当前配置版本、CRC、Flash 槽和运行状态。

## 9. MCU 任务划分

```text
Task_digital_io
    周期读取输入，维护当前输入位图

Task_io_logic
    执行规则、边沿检测、定时器和输出仲裁

Task_modbus_rtu
    处理标准寄存器和 0x41 配置协议
```

任务之间的数据访问必须通过共享结构体和临界区保护：

```text
input_state
previous_input_state
logical_output_state
active_program
program_generation
```

规则运行时不写 Flash。只有 BEGIN/DATA/VERIFY/ACTIVATE 流程成功后才擦写配置区。

## 10. 安全策略

默认安全行为：

- 无有效配置：所有继电器和晶体管输出关闭。
- 配置校验失败：继续运行上一份有效配置。
- 下载中断：继续运行上一份有效配置。
- STOP 命令：逻辑停止，所有逻辑输出关闭。
- RS485 断开：规则继续本地运行；是否关闭输出由工程中的通信失联策略决定。
- 上位机写 `0x0002`：仅作为手动覆盖，不能直接修改 Flash 规则。

建议增加手动覆盖模式：

```text
AUTO       按 Flash 中的逻辑运行
MANUAL     上位机直接控制输出
SAFE       关闭全部逻辑输出
```

## 11. 验证项目

### 软件测试

- CRC16 和 CRC32 测试向量。
- 程序头编解码测试。
- 规则编译测试。
- AND/OR 条件测试。
- 上升沿、下降沿测试。
- 延时和脉冲测试。
- 输出冲突检测测试。
- 配置下载状态机测试。

### 硬件测试

1. 下载规则并确认继电器、晶体管输出动作。
2. 读取 `0x0001` 确认输入位图。
3. 读取 `0x0002` 确认逻辑输出位图。
4. 断电重启，确认规则自动恢复。
5. 下载过程中断开 RS485，确认旧规则仍可运行。
6. 修改配置 CRC，确认 MCU 拒绝激活。
7. 擦除配置，确认恢复为全部输出关闭。
8. 固件重新下载后，确认配置区未被误擦除。

## 12. 实施顺序

1. 固定 `IOCF` 二进制格式和通道映射。
2. 修改 Keil scatter，预留两个配置 Flash 扇区。
3. 实现 MCU Flash 双槽存储和 CRC 校验。
4. 实现 MCU IO 规则执行器。
5. 实现 Modbus `0x41` 配置协议。
6. 为上位机增加规则模型和编译器。
7. 增加上位机下载、激活、回读和工程保存。
8. 完成断电、断线、CRC 错误和恢复测试。
9. 生成新的上位机可执行文件。
10. 确认硬件测试通过后，再提交 GitHub。

## 13. 单片机侧直接执行方案

### 13.1 MCU 模块划分

| 模块 | 文件 | 责任 |
| --- | --- | --- |
| GPIO BSP | `BSP/Src/bsp_digital_io.c` | 按当前引脚和有效电平读写 GPIO |
| IO 服务 | `Services/Src/digital_io_service.c` | 提供输入、输出和位图接口 |
| Flash 配置 | `Services/Src/io_config_storage.c` | A/B 槽擦除、写入、校验和选择 |
| 规则引擎 | `Services/Src/io_logic_engine.c` | 执行规则、边沿和计时器 |
| 下载协议 | `Services/Src/io_logic_protocol.c` | 处理 `0x41` 子命令 |
| 规则任务 | `Application/Src/Task_io_logic.c` | 周期调用规则引擎 |
| Modbus 任务 | `Application/Src/Task_modbus_rtu.c` | 处理标准寄存器和配置帧 |

### 13.2 上电初始化顺序

固件必须按照以下顺序初始化：

```text
HAL_Init
SystemClock_Config
MX_GPIO_Init
MX_USART1_UART_Init
DIGITAL_IO_SERVICE_Init
IO_CONFIG_Init
IO_LOGIC_ENGINE_Init
IO_CONFIG_Load
创建 Task_io_logic
创建 Task_modbus_rtu
启动调度器
```

如果 `IO_CONFIG_Load` 找到有效配置，规则引擎进入 AUTO；如果没有有效配置，加载安全默认配置并保持所有逻辑输出关闭。

### 13.3 规则任务伪代码

```c
void Task_io_logic(void *argument)
{
    IO_LOGIC_ENGINE_Init();

    for (;;)
    {
        uint16_t input_state = DIGITAL_IO_SERVICE_ReadInputs();
        uint32_t now = HAL_GetTick();

        if (IO_LOGIC_ENGINE_GetMode() == IO_MODE_AUTO)
        {
            uint16_t output_state = IO_LOGIC_ENGINE_Step(
                input_state,
                now);

            DIGITAL_IO_SERVICE_SetOutputMask(output_state);
        }

        vTaskDelay(pdMS_TO_TICKS(active_program.scan_period_ms));
    }
}
```

实际代码不能让 Modbus 线程直接修改 GPIO。Modbus 写 `0x0002` 时只允许在 MANUAL 模式生效；AUTO 模式下应返回 `0x0D`，防止上位机手动写入覆盖规则。

### 13.4 规则执行顺序

每次扫描固定执行：

```text
读取输入
计算上升沿和下降沿
更新计时器
按 priority 从高到低计算规则
执行输出仲裁
输出有效状态转换为真实 GPIO 电平
更新状态寄存器
```

同一输出由多条规则控制时，优先级高的规则生效。规则引擎只保存逻辑状态，继电器低有效和晶体管高有效的转换由 `bsp_digital_io.c` 负责。

## 14. 上位机侧直接执行方案

### 14.1 C# 工程模块

在当前 WinForms 工程中增加：

```text
Host/IndustrialFieldDataAcquisitionTerminal/Logic/
    IoLogicProject.cs
    IoLogicRule.cs
    IoLogicCompiler.cs
    IoLogicBinaryFormat.cs
    IoLogicProtocol.cs
    IoLogicDownloadService.cs
    IoLogicValidator.cs
```

模块职责：

| 类 | 责任 |
| --- | --- |
| `IoLogicProject` | 工程 JSON 的读取、保存和版本管理 |
| `IoLogicRule` | 单条条件-动作规则模型 |
| `IoLogicValidator` | 通道、优先级、计时器和大小检查 |
| `IoLogicCompiler` | JSON 规则转换为 `IOCF` 二进制 |
| `IoLogicBinaryFormat` | 程序头、规则体、CRC32 编解码 |
| `IoLogicProtocol` | 构造和解析 `0x41` 帧 |
| `IoLogicDownloadService` | BEGIN、DATA、VERIFY、ACTIVATE 流程 |

### 14.2 上位机页面

当前主窗口增加三个页面：

#### IO 实时监视

显示 8 路输入、8 路输出、运行模式、配置版本、CRC 和通信状态。

#### IO 逻辑编辑

使用 `DataGridView` 或规则卡片编辑器，每行一条规则：

```text
启用 | 名称 | 触发方式 | 输入条件 | AND/OR | 延时 | 输出 | 动作 | 脉冲时间 | 优先级
```

第一版使用下拉框和多选框，不允许用户输入任意 C 代码。

#### 配置下载日志

显示：

```text
编译结果
程序大小
规则数量
CRC32
当前传输块
重试次数
VERIFY 结果
ACTIVATE 结果
MCU 当前 generation
```

### 14.3 上位机下载状态机

```text
Idle
  -> Compile
  -> Validate
  -> Connect
  -> GetInfo
  -> Begin
  -> SendData
  -> Verify
  -> Activate
  -> ReadInfo
  -> Completed
```

任意步骤失败进入 `Failed`，执行：

```text
记录失败帧和错误码
发送 ABORT
不改变 MCU 当前有效规则
允许用户重试
```

`SendData` 必须使用同一个 session，分块大小不超过 128 字节，收到 ACK 后才能发送下一块。

### 14.4 上位机实时轮询

实时轮询独立于规则下载任务，使用现有 `ModbusRtuClient` 的请求-响应通道，并通过 `SemaphoreSlim` 与下载操作互斥。

默认每 250 ms 执行一次：

```text
读 0x0000，数量 3
```

得到：

```text
status[0] = 设备状态
status[1] = 数字输入位图
status[2] = 数字输出位图
```

当下载状态不是 Idle 或 Completed 时，暂停实时轮询；下载完成后立即读取一次状态，再恢复 250 ms 轮询。

超过 1000 ms 没有有效响应：

1. 标记通信状态为过期。
2. 输入和输出指示灯变为黄色。
3. 不把上一次值继续显示为实时值。
4. 日志记录超时次数。

## 15. 第一阶段交付内容

第一阶段直接实现以下最小闭环：

1. 上位机图形化规则表。
2. X1~X8 电平条件。
3. Relay1~Relay4、Transistor1~Transistor4 置位和复位。
4. 规则编译为 `IOCF` 二进制。
5. `0x41` 的 BEGIN、DATA、VERIFY、ACTIVATE、GET_INFO。
6. MCU Flash A/B 双槽保存。
7. MCU 上电自动加载规则。
8. 上位机 250 ms 实时显示 8 路输入和 8 路输出。
9. 规则版本、CRC、下载日志和断线提示。
10. 断电重启后规则保持并继续运行。

第一阶段验证通过后，再增加上升沿、下降沿、脉冲、复杂 AND/OR 和梯形图连线编辑器。
