#include "sys.h"

#include <string.h>

DataHubState g_datahub; /* 全局最新数据中心，供调试观察和任务间读取 */

void DataHub_Init(void)
{
    taskENTER_CRITICAL();                         /* 进入临界区，防止初始化时被其他任务访问 */
    memset(&g_datahub, 0, sizeof(g_datahub));       /* 清空所有 latest 数据 */
    taskEXIT_CRITICAL();                          /* 退出临界区 */
}

void DataHub_PublishImu(const DataHubImuData *imu)
{
    uint32_t next_seq;                            /* 保存下一帧序号 */

    if (imu == 0)                                 /* 如果输入指针为空 */
    {
        return;                                   /* 不发布无效数据 */
    }

    taskENTER_CRITICAL();                         /* 进入临界区，保证整包 IMU 快照写入完整 */
    next_seq = g_datahub.imu.seq + 1U;            /* 在旧序号基础上加 1 */
    g_datahub.imu = *imu;                         /* 拷贝整包 IMU/姿态数据 */
    g_datahub.imu.seq = next_seq;                 /* 写入新的发布序号 */
    taskEXIT_CRITICAL();                          /* 退出临界区 */
}

void DataHub_GetImu(DataHubImuData *imu)
{
    if (imu == 0)                                 /* 如果输出指针为空 */
    {
        return;                                   /* 无法拷贝，直接返回 */
    }

    taskENTER_CRITICAL();                         /* 进入临界区，避免读取到一半新一半旧的数据 */
    *imu = g_datahub.imu;                         /* 拷贝最新 IMU 快照到调用者本地变量 */
    taskEXIT_CRITICAL();                          /* 退出临界区 */
}

uint32_t DataHub_GetImuSeq(void)
{
    uint32_t seq;                                 /* 保存读取到的数据序号 */

    taskENTER_CRITICAL();                         /* 进入临界区，保证读取一致 */
    seq = g_datahub.imu.seq;                      /* 读取最新 IMU 序号 */
    taskEXIT_CRITICAL();                          /* 退出临界区 */

    return seq;                                   /* 返回序号，RS485 线程可用来判断是否有新数据 */
}

void DataHub_SetImuReady(int ready)
{
    taskENTER_CRITICAL();                         /* 进入临界区 */
    g_datahub.imu.imu_ready = ready;              /* 更新 IMU ready 状态 */
    taskEXIT_CRITICAL();                          /* 退出临界区 */
}

void DataHub_SetImuReadResult(int read_rslt)
{
    taskENTER_CRITICAL();                         /* 进入临界区 */
    g_datahub.imu.last_read_rslt = read_rslt;     /* 更新最近一次 IMU 读取结果 */
    taskEXIT_CRITICAL();                          /* 退出临界区 */
}

void DataHub_PublishAd7606(const DataHubAd7606Data *ad7606)
{
    uint32_t next_seq;

    if (ad7606 == 0)
    {
        return;
    }

    taskENTER_CRITICAL();
    next_seq = g_datahub.ad7606.seq + 1U;
    g_datahub.ad7606 = *ad7606;
    g_datahub.ad7606.seq = next_seq;
    taskEXIT_CRITICAL();
}

void DataHub_GetAd7606(DataHubAd7606Data *ad7606)
{
    if (ad7606 == 0)
    {
        return;
    }

    taskENTER_CRITICAL();
    *ad7606 = g_datahub.ad7606;
    taskEXIT_CRITICAL();
}

uint32_t DataHub_GetAd7606Seq(void)
{
    uint32_t seq;

    taskENTER_CRITICAL();
    seq = g_datahub.ad7606.seq;
    taskEXIT_CRITICAL();

    return seq;
}

void DataHub_SetAd7606Ready(int ready)
{
    taskENTER_CRITICAL();
    g_datahub.ad7606.ad7606_ready = ready;
    taskEXIT_CRITICAL();
}

void DataHub_SetAd7606ReadResult(int read_ok)
{
    taskENTER_CRITICAL();
    g_datahub.ad7606.last_read_ok = read_ok;
    taskEXIT_CRITICAL();
}

void DataHub_UpdateAd7606Status(uint32_t sample_count,
                                uint32_t timeout_count,
                                int ready,
                                int read_ok)
{
    taskENTER_CRITICAL();
    g_datahub.ad7606.sample_count = sample_count;
    g_datahub.ad7606.timeout_count = timeout_count;
    g_datahub.ad7606.ad7606_ready = ready;
    g_datahub.ad7606.last_read_ok = read_ok;
    taskEXIT_CRITICAL();
}
