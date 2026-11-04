#ifndef IO_LOGIC_TYPES_H
#define IO_LOGIC_TYPES_H

/**
 * @file    io_logic_types.h
 * @brief   联合控制器（IOCF v2）公共类型与常量。
 *
 * v2 编程模型：最多 16 个可选模式回调 + 主程序 IO_Logic_Main。
 * DI/DO 既可使用 1~8 数字通道，也可使用下面的板级名称。
 */

/* 模式号范围 */
#define IO_MODE_MIN       0
#define IO_MODE_MAX       15

/* 8 路数字输入，与板卡接线端子 IN1~IN8 一一对应。 */
#define INPUT_1           1
#define INPUT_2           2
#define INPUT_3           3
#define INPUT_4           4
#define INPUT_5           5
#define INPUT_6           6
#define INPUT_7           7
#define INPUT_8           8

/* 8 路数字输出：1~4 为继电器，5~8 为晶体管。 */
#define RELAY_1           1
#define RELAY_2           2
#define RELAY_3           3
#define RELAY_4           4
#define TRANSISTOR_1      5
#define TRANSISTOR_2      6
#define TRANSISTOR_3      7
#define TRANSISTOR_4      8

/* 频率档位（IO_Freq_Class 的返回值） */
#define IO_FREQ_CLASS_NONE     0   /* 无信号 / 未配置阈值 */
#define IO_FREQ_CLASS_LOW      1
#define IO_FREQ_CLASS_NORMAL   2
#define IO_FREQ_CLASS_HIGH     3
#define IO_FREQ_CLASS_OVER     4   /* 超过规格上限 2000 Hz */

/* 规格上限 */
#define IO_FREQ_SPEC_MAX_HZ    2000

#endif /* IO_LOGIC_TYPES_H */
