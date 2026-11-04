# 联合控制器输入条件

将以下条件写在 `IO_Mode_x_Callback` 或 `IO_Logic_Main` 的 `if` / `while` 中。各输入的可用比较运算符见库头文件；条件可用 `!`、`&&`、`||` 和括号组合，布尔运算优先级为 `!` > `&&` > `||`，并按短路规则执行。对比较式取反时必须写 `!(IO_AI_Mv(1) > 2500)`；布尔输入可直接写 `!IO_AI_Valid(1)`。

| 输入 | 函数 | 编号/单位 |
|---|---|---|
| 数字输入 | `IO_DI_Level(INPUT_1)`、`IO_DI_Rising(INPUT_1)`、`IO_DI_Falling(INPUT_1)` | IN1～IN8；有效为 1 |
| AD7606 | `IO_AI_Valid(3)`、`IO_AI_Mv(3)` | CH1～CH8；有符号 mV，范围 -10000～10000 |
| 4～20 mA | `IO_Current_Valid(1)`、`IO_Current_uA(1)` | 电流输入 1～2 对应 AD7606 CH1/CH2；单位 µA |
| RS485 | `IO_RS485_Valid(1)`、`IO_RS485_Value(1, 0)` | 第 1～32 个轮询项；第二个参数为该项起始寄存器偏移 0～15；返回原始无符号 16 位寄存器值 |

例如，把以下语句放在模式回调中：

```c
if (IO_AI_Valid(3) == 1 && IO_AI_Mv(3) > 2500)
{
    IO_DO_Set(RELAY_1, 1);
}
else
{
    IO_DO_Set(RELAY_1, 0);
}

if (IO_Current_Valid(1) == 1 && IO_Current_uA(1) > 12000)
{
    IO_DO_Set(TRANSISTOR_1, 1);
}
else
{
    IO_DO_Set(TRANSISTOR_1, 0);
}

if (IO_RS485_Valid(1) == 1 && IO_RS485_Value(1, 0) > 500)
{
    IO_DO_Set(RELAY_2, 1);
}
else
{
    IO_DO_Set(RELAY_2, 0);
}
```

AD7606 的样本需在最近 500 ms 内成功取得。RS485 项需已启用、最近轮询成功，且数据年龄不超过 `max(1000 ms, 3 × 轮询周期 + 活动项数 × 250 ms)`。数据无效、未配置、超时或请求的寄存器超出该项数量时，**任何数值比较（包括 `!=`）都为假**；对这个比较结果取反则会变为真。应先用 `Valid` 判断，并通过 `else` 指定安全输出。电流换算按板载电路 `uA = max(0, CH_mV) × 1000 / 155`，实际精度以校准结果为准。

新输入指令需要板卡运行 V1.2.6 或更高版本固件。上位机下载前会读取能力寄存器 `0x0006`，若板卡不支持则拒绝下载；原有仅使用数字输入的规则不受影响。
