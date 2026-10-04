#ifndef DIGITAL_IO_SERVICE_H
#define DIGITAL_IO_SERVICE_H

#include <stdint.h>

#include "bsp_digital_io.h"

#define DIGITAL_IO_SERVICE_INPUT_COUNT  BSP_DIGITAL_IO_INPUT_COUNT
#define DIGITAL_IO_SERVICE_OUTPUT_COUNT BSP_DIGITAL_IO_OUTPUT_COUNT

#define DIGITAL_IO_SERVICE_NORMAL_INPUT_MASK  0x001CU /* bit2~bit4：X3~X5普通开关量输入 */
#define DIGITAL_IO_SERVICE_PULSE_INPUT_MASK   0x0003U /* bit0~bit1：X1~X2高速脉冲输入 */
#define DIGITAL_IO_SERVICE_ENCODER_INPUT_MASK 0x00E0U /* bit5~bit7：X6(A)~X8(Z)编码器输入 */

/*
 * 数字IO服务完整输入快照。
 *
 * 该结构由DIGITAL_IO_SERVICE_ProcessInputs()统一更新，
 * 调用者应通过DIGITAL_IO_SERVICE_GetInputState()获取一致快照，
 * 不应绕过服务层直接组合GPIO、定时器或编码器数据。
 */
typedef struct
{
  uint16_t input_mask;               /* 8路输入逻辑状态：bit0~bit7对应X1~X8，1=有效 */
  uint16_t normal_input_mask;        /* X3~X5消抖后状态；仅bit2~bit4有效 */
  uint16_t output_mask;              /* 8路输出当前状态：bit0~3=继电器1~4，bit4~7=晶体管1~4 */
  uint16_t status_flags;             /* 服务状态位；按DIGITAL_IO_SERVICE_STATUS_xxx解析 */
  uint64_t pulse_count[2];           /* 高速脉冲累计值：[0]=X1，[1]=X2；上电后从0累计 */
  uint32_t pulse_frequency_hz[2];    /* 脉冲频率：[0]=X1，[1]=X2；单位Hz，100 ms窗口更新 */
  int32_t encoder_position;          /* X6/X7编码器A/B相四倍频有符号累计位置，单位count */
  int32_t encoder_speed_cps;         /* 编码器速度，单位count/s，100 ms窗口更新 */
  int32_t encoder_index_position;    /* 最近一次X8/Z相有效时记录的encoder_position */
  int8_t encoder_direction;          /* 编码器最近方向：1=正向，-1=反向，0=上电后尚未运动 */
  uint8_t encoder_ab_state;          /* 编码器A/B当前逻辑状态：bit1=A，bit0=B，1=有效 */
  uint32_t encoder_index_count;      /* X8/Z相有效脉冲累计次数 */
  uint32_t encoder_error_count;      /* A/B相非法状态跳变累计次数，用于信号质量诊断 */
  uint32_t timestamp_ms;             /* 本快照最后更新时的HAL毫秒时标 */
} DIGITAL_IO_SERVICE_InputState;

#define DIGITAL_IO_SERVICE_STATUS_PULSE_COUNTER_READY 0x0001U /* bit0=1：X1/X2硬件计数器已就绪 */

typedef struct
{
  uint8_t initialized;          /* 1=数字IO服务已完成初始化 */
  uint8_t pulse_counter_ready;  /* 1=X1/X2硬件计数器初始化成功 */
  uint16_t input_mask;          /* 最近一次采集的8路输入逻辑状态 */
  uint16_t output_mask;         /* 最近一次读取的8路输出逻辑状态 */
  uint32_t scan_count;          /* 输入处理函数累计执行次数 */
  uint32_t output_write_count;  /* 服务层累计输出写入次数 */
  uint32_t last_scan_tick;      /* 最近一次输入处理的HAL毫秒时标 */
  uint32_t last_output_tick;    /* 最近一次输出写入的HAL毫秒时标 */
} DIGITAL_IO_SERVICE_Diagnostics;

/* 初始化输入、输出、脉冲计数器和编码器采集资源。 */
void DIGITAL_IO_SERVICE_Init(void);

/* 执行一次输入处理；now_ms为当前HAL毫秒时标，由1 ms周期任务调用。 */
void DIGITAL_IO_SERVICE_ProcessInputs(uint32_t now_ms);

/* 原子复制最新输入快照到state；state为NULL时不执行操作。 */
void DIGITAL_IO_SERVICE_GetInputState(DIGITAL_IO_SERVICE_InputState *state);

/* 返回8路输入逻辑状态位掩码：bit0~bit7对应X1~X8。 */
uint16_t DIGITAL_IO_SERVICE_ReadInputs(void);

/* 返回指定输入的逻辑状态：1=有效，0=无效或参数非法。 */
int DIGITAL_IO_SERVICE_ReadInput(BSP_DIGITAL_IO_Input input);

/* 设置指定输出的逻辑状态：active非0=打开，0=关闭。 */
void DIGITAL_IO_SERVICE_SetOutput(BSP_DIGITAL_IO_Output output, int active);

/* 一次设置8路输出：bit0~3=继电器1~4，bit4~7=晶体管1~4。 */
void DIGITAL_IO_SERVICE_SetOutputMask(uint16_t active_mask);

/* 返回8路输出当前逻辑状态位掩码。 */
uint16_t DIGITAL_IO_SERVICE_GetOutputMask(void);

/* 关闭全部4路继电器和4路晶体管输出。 */
void DIGITAL_IO_SERVICE_AllOutputsOff(void);

/* 复制服务运行诊断数据到diagnostics；参数为NULL时不执行操作。 */
void DIGITAL_IO_SERVICE_GetDiagnostics(DIGITAL_IO_SERVICE_Diagnostics *diagnostics);

#endif /* DIGITAL_IO_SERVICE_H */
