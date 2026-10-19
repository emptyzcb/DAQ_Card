#ifndef DATAHUB_H
#define DATAHUB_H

#include <stdint.h>

#include "bsp_ad7606.h"

#ifdef __cplusplus
extern "C" {
#endif

/*
 * DataHubAd7606Data
 *
 * Latest AD7606 8-channel analog sample snapshot.
 */
typedef struct
{
    uint32_t seq;             /* Publish sequence, increments on each valid AD7606 sample */
    uint32_t timestamp_ms;    /* HAL tick when the sample was captured */
    uint32_t sample_count;    /* Successful AD7606 sample counter */
    uint32_t timeout_count;   /* AD7606 timeout/error counter */
    int ad7606_ready;         /* AD7606 service ready/running flag */
    int last_read_ok;         /* Last AD7606 read result: 1 ok, 0 failed */

    int16_t raw[BSP_AD7606_CHANNEL_COUNT]; /* Raw signed ADC codes */
    int32_t mv[BSP_AD7606_CHANNEL_COUNT];  /* Converted voltage, unit mV */
} DataHubAd7606Data;

/*
 * DataHubDigitalIoData
 *
 * 数字IO最新状态快照。输入和输出位掩码中的bit0~bit7分别
 * 对应第1~8路，位为1表示该路处于有效/打开状态。
 */
typedef struct
{
    uint32_t seq;             /* 发布序号，每发布一次完整快照加1 */
    uint32_t timestamp_ms;    /* 快照生成时的系统毫秒时间 */
    uint16_t input_mask;      /* 8路数字输入当前逻辑状态 */
    uint16_t normal_input_mask; /* X3~X5消抖后的普通输入状态 */
    uint16_t output_mask;     /* 4路继电器和4路晶体管的打开状态 */
    uint16_t reserved;
    uint64_t pulse_count[2];  /* X1~X2累计脉冲数，64位防止长期运行溢出 */
    uint32_t pulse_frequency_hz[2]; /* X1~X2最近100 ms窗口频率 */
    int32_t encoder_position; /* X6/X7 A/B四倍频累计位置 */
    int32_t encoder_speed_cps; /* 编码器速度，单位：count/s */
    int32_t encoder_index_position; /* 最近一次X8/Z相到达时的位置 */
    uint32_t encoder_index_count; /* Z相索引累计次数 */
    uint32_t encoder_error_count; /* A/B非法跳变累计次数 */
    int8_t encoder_direction; /* 1=正向，-1=反向，0=未运动 */
    uint8_t encoder_ab_state; /* 当前A/B相逻辑状态 */
    uint16_t status_flags;    /* bit0：高速脉冲计数器初始化成功 */
} DataHubDigitalIoData;

#define DATAHUB_RS485_MAX_ITEMS       32U
#define DATAHUB_RS485_REGS_PER_ITEM   16U

/* One runtime-configured Modbus RTU acquisition item and its latest values. */
typedef struct
{
    uint32_t seq;
    uint32_t timestamp_ms;
    uint32_t error_count;
    uint16_t values[DATAHUB_RS485_REGS_PER_ITEM];
    uint16_t reg_start;
    uint16_t interval_ms;
    uint8_t configured;
    uint8_t enabled;
    uint8_t slave_addr;
    uint8_t function;
    uint8_t quantity;
    uint8_t last_ok;
} DataHubRs485SensorData;

/*
 * DataHubState
 *
 * 全局数据中心。
 * 后续新增传感器时，在这里继续添加 latest_xxx 字段即可。
 */
typedef struct
{
    DataHubAd7606Data ad7606; /* AD7606 latest sample snapshot */
    DataHubDigitalIoData digital_io; /* 数字输入输出最新快照 */
    DataHubRs485SensorData rs485_sensors[DATAHUB_RS485_MAX_ITEMS];
} DataHubState;

extern DataHubState g_datahub; /* Keil Watch 可直接观察这个全局数据中心 */

void DataHub_Init(void);
void DataHub_PublishAd7606(const DataHubAd7606Data *ad7606);
void DataHub_GetAd7606(DataHubAd7606Data *ad7606);
uint32_t DataHub_GetAd7606Seq(void);
void DataHub_SetAd7606Ready(int ready);
void DataHub_SetAd7606ReadResult(int read_ok);
void DataHub_UpdateAd7606Status(uint32_t sample_count,
                                uint32_t timeout_count,
                                int ready,
                                int read_ok);
void DataHub_PublishDigitalIo(const DataHubDigitalIoData *digital_io);
void DataHub_GetDigitalIo(DataHubDigitalIoData *digital_io);
void DataHub_ClearRs485Sensors(void);
void DataHub_PublishRs485Sensor(uint8_t index, const DataHubRs485SensorData *sensor);
void DataHub_GetRs485Sensor(uint8_t index, DataHubRs485SensorData *sensor);
#ifdef __cplusplus
}
#endif

#endif /* DATAHUB_H */
