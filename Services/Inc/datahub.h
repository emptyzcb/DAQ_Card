#ifndef DATAHUB_H
#define DATAHUB_H

#include <stdint.h>

#include "attitude_est.h"
#include "bsp_ad7606.h"

#ifdef __cplusplus
extern "C" {
#endif

/*
 * DataHubImuData
 *
 * IMU/姿态最新值快照。
 * 后续 RS485 线程读取这个结构后，可以再转换成正式通信 packet。
 */
typedef struct
{
    uint32_t seq;            /* 数据序号，每发布一次成功 IMU 姿态数据加 1 */
    uint32_t timestamp_tick; /* 当前数据对应的 FreeRTOS tick，后续可替换成 us 时间戳 */
    float dt;                /* 本次姿态更新周期，单位 s */
    int last_read_rslt;      /* 最近一次 IMU 读取结果 */
    int imu_ready;           /* IMU 服务是否 ready */

    int16_t accel_raw_x;     /* 加速度 X 轴原始值 */
    int16_t accel_raw_y;     /* 加速度 Y 轴原始值 */
    int16_t accel_raw_z;     /* 加速度 Z 轴原始值 */
    int16_t gyro_raw_x;      /* 陀螺仪 X 轴原始值 */
    int16_t gyro_raw_y;      /* 陀螺仪 Y 轴原始值 */
    int16_t gyro_raw_z;      /* 陀螺仪 Z 轴原始值 */

    float accel_g_x;         /* 加速度 X 轴物理量，单位 g */
    float accel_g_y;         /* 加速度 Y 轴物理量，单位 g */
    float accel_g_z;         /* 加速度 Z 轴物理量，单位 g */
    float gyro_dps_x;        /* 陀螺仪 X 轴物理量，单位 deg/s */
    float gyro_dps_y;        /* 陀螺仪 Y 轴物理量，单位 deg/s */
    float gyro_dps_z;        /* 陀螺仪 Z 轴物理量，单位 deg/s */

    EulerAngle euler;        /* 当前欧拉角，单位 deg，仅调试/显示用 */
    Quaternion quat;         /* 当前四元数，w/x/y/z，推荐作为 SLAM 姿态参考 */
} DataHubImuData;

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
 * DataHubState
 *
 * 全局数据中心。
 * 后续新增传感器时，在这里继续添加 latest_xxx 字段即可。
 */
typedef struct
{
    DataHubImuData imu;      /* IMU/姿态最新快照 */
    DataHubAd7606Data ad7606; /* AD7606 latest sample snapshot */
} DataHubState;

extern DataHubState g_datahub; /* Keil Watch 可直接观察这个全局数据中心 */

void DataHub_Init(void);
void DataHub_PublishImu(const DataHubImuData *imu);
void DataHub_GetImu(DataHubImuData *imu);
uint32_t DataHub_GetImuSeq(void);
void DataHub_SetImuReady(int ready);
void DataHub_SetImuReadResult(int read_rslt);
void DataHub_PublishAd7606(const DataHubAd7606Data *ad7606);
void DataHub_GetAd7606(DataHubAd7606Data *ad7606);
uint32_t DataHub_GetAd7606Seq(void);
void DataHub_SetAd7606Ready(int ready);
void DataHub_SetAd7606ReadResult(int read_ok);
void DataHub_UpdateAd7606Status(uint32_t sample_count,
                                uint32_t timeout_count,
                                int ready,
                                int read_ok);
#ifdef __cplusplus
}
#endif

#endif /* DATAHUB_H */
