# AD7606 硬件接口与软件驱动说明

## 1. 设计目标

本工程新增 AD7606 模拟信号采集支持，用于采集 8 路同步模拟输入信号。

当前软件按 **AD7606 16 位并口模式** 设计：

```text
AD7606 8 路同步采样
  |
16 bit 并口数据总线
  |
STM32H743 GPIO 读取
  |
BSP_AD7606 驱动
  |
AD7606_SERVICE 周期采样
```

## 2. 默认硬件连接

当前代码默认使用以下引脚。若实际原理图不同，只需要修改：

[bsp_ad7606.h](</D:/OneDrive/桌面/采集板卡/Code/H743/BSP/Inc/bsp_ad7606.h>)

### 2.1 并口数据总线

| AD7606 引脚 | STM32H743 引脚 | 说明 |
|---|---|---|
| DB0 | PE0 | 数据位 0 |
| DB1 | PE1 | 数据位 1 |
| DB2 | PE2 | 数据位 2 |
| DB3 | PE3 | 数据位 3 |
| DB4 | PE4 | 数据位 4 |
| DB5 | PE5 | 数据位 5 |
| DB6 | PE6 | 数据位 6 |
| DB7 | PE7 | 数据位 7 |
| DB8 | PE8 | 数据位 8 |
| DB9 | PE9 | 数据位 9 |
| DB10 | PE10 | 数据位 10 |
| DB11 | PE11 | 数据位 11 |
| DB12 | PE12 | 数据位 12 |
| DB13 | PE13 | 数据位 13 |
| DB14 | PE14 | 数据位 14 |
| DB15 | PE15 | 数据位 15 |

代码中直接读取：

```c
raw_u16 = (uint16_t)(GPIOE->IDR & 0xFFFFU);
```

因此建议硬件上把 `DB0..DB15` 顺序接到 `PE0..PE15`，这样读取最快、代码最简单。

### 2.2 控制信号

| AD7606 引脚 | STM32H743 引脚 | 方向 | 默认电平 | 说明 |
|---|---|---|---|---|
| CONVST_A / CONVST_B | PD8 | MCU 输出 | 高 | 启动同步转换，建议 A/B 短接一起控制；PD0 已用于 CAN1_RX |
| RD | PD1 | MCU 输出 | 高 | 并口读脉冲 |
| CS | PD2 | MCU 输出 | 高 | 片选 |
| RESET | PD3 | MCU 输出 | 低 | 复位 AD7606 |
| BUSY | PD4 | MCU 输入 | - | 转换忙信号 |
| RANGE | PD5 | MCU 输出 | 高 | 量程选择 |
| OS0 | PD6 | MCU 输出 | 低 | 过采样选择 bit0 |
| OS1 | PD7 | MCU 输出 | 低 | 过采样选择 bit1 |
| OS2 | PB0 | MCU 输出 | 低 | 过采样选择 bit2 |
| STBY | PB1 | MCU 输出 | 高 | 待机控制，高电平正常工作 |

## 3. 量程选择

当前代码支持两种量程：

| 软件枚举 | RANGE 引脚 | 输入范围 | 换算单位 |
|---|---|---|---|
| `BSP_AD7606_RANGE_5V` | 低 | ±5V | mV |
| `BSP_AD7606_RANGE_10V` | 高 | ±10V | mV |

默认配置：

```c
BSP_AD7606_RANGE_10V
```

原始码值为有符号 16 位二进制补码：

```text
-32768 ~ +32767
```

软件换算：

```c
mv = raw * range_mv / 32768;
```

例如 ±10V 量程下：

```text
raw = 32767 约等于 +9999 mV
raw = 0     约等于 0 mV
raw = -32768 等于 -10000 mV
```

## 4. 过采样设置

当前代码支持：

| 软件枚举 | OS2 OS1 OS0 | 说明 |
|---|---|---|
| `BSP_AD7606_OS_NONE` | 000 | 不过采样 |
| `BSP_AD7606_OS_2` | 001 | 2 倍过采样 |
| `BSP_AD7606_OS_4` | 010 | 4 倍过采样 |
| `BSP_AD7606_OS_8` | 011 | 8 倍过采样 |
| `BSP_AD7606_OS_16` | 100 | 16 倍过采样 |
| `BSP_AD7606_OS_32` | 101 | 32 倍过采样 |
| `BSP_AD7606_OS_64` | 110 | 64 倍过采样 |

默认配置：

```c
BSP_AD7606_OS_NONE
```

## 5. 软件文件说明

| 文件 | 作用 |
|---|---|
| [bsp_ad7606.h](</D:/OneDrive/桌面/采集板卡/Code/H743/BSP/Inc/bsp_ad7606.h>) | AD7606 引脚定义、数据结构、驱动接口 |
| [bsp_ad7606.c](</D:/OneDrive/桌面/采集板卡/Code/H743/BSP/Src/bsp_ad7606.c>) | GPIO 初始化、复位、量程、过采样、并口读取 |
| [ad7606_service.h](</D:/OneDrive/桌面/采集板卡/Code/H743/Services/Inc/ad7606_service.h>) | AD7606 服务层接口 |
| [ad7606_service.c](</D:/OneDrive/桌面/采集板卡/Code/H743/Services/Src/ad7606_service.c>) | 周期采样、最新样本缓存、诊断计数 |
| [ALL_Task.c](</D:/OneDrive/桌面/采集板卡/Code/H743/Application/Src/ALL_Task.c>) | 创建 `Task_ad7606` 周期调用采样服务 |

## 6. 当前默认采样配置

| 项目 | 默认值 |
|---|---|
| 采样周期 | 10 ms |
| BUSY 等待超时 | 10 ms |
| 输入量程 | ±10V |
| 过采样 | 无 |
| 通道数 | 8 |
| 输出数据 | raw + mV |

配置位置：

```c
static AD7606_SERVICE_Config ad7606_config = {
  10U,
  10U,
  BSP_AD7606_RANGE_10V,
  BSP_AD7606_OS_NONE
};
```

## 7. 采样流程

```text
Task_ad7606
  |
AD7606_SERVICE_Process()
  |
BSP_AD7606_ReadSample()
  |
CONVST 产生转换脉冲
  |
等待 BUSY 变低
  |
CS 拉低
  |
连续产生 8 次 RD 读脉冲
  |
读取 CH1 ~ CH8 原始值
  |
换算为 mV
  |
保存为 latest sample
```

## 8. 应用层读取方式

应用层可以通过以下接口读取最新采样值：

```c
const BSP_AD7606_Sample *sample;

sample = AD7606_SERVICE_GetLatestSample();

printf("CH1 raw=%d mv=%ld\r\n",
       sample->raw[0],
       (long)sample->mv[0]);
```

也可以读取诊断信息：

```c
AD7606_SERVICE_Diagnostics diag;

AD7606_SERVICE_GetDiagnostics(&diag);

printf("AD7606 samples=%lu timeout=%lu\r\n",
       (unsigned long)diag.sample_count,
       (unsigned long)diag.timeout_count);
```

## 9. 硬件注意事项

1. AD7606 的模拟输入前端需要根据实际量程做保护、滤波和接线端子设计。
2. `VDRIVE` 应与 MCU IO 电平匹配，STM32H743 通常使用 3.3V IO。
3. 并口数据线较多，建议数据总线尽量短、等长要求不高但走线应整齐。
4. `CONVST_A` 和 `CONVST_B` 若要 8 通道同步采样，建议短接后由同一个 MCU GPIO 控制。
5. `BUSY` 必须接 MCU 输入，用于判断转换完成。
6. `RANGE`、`OS0~OS2` 可由 GPIO 控制，也可以硬件固定。
7. 如果实际使用高采样率，应使用定时器触发采样，当前 10ms 周期任务更适合低速验证。

## 10. 后续建议

1. 将 AD7606 最新数据发布到 DataHub。
2. 通过 RS485 协议支持查询 8 路模拟量。
3. 增加通道校准参数：零点、比例、滤波系数。
4. 增加过压、断线、异常值诊断。
5. 如果采样率提高，改为定时器精确触发 CONVST，并使用专门采集任务读取。
