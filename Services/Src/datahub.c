#include "sys.h"

#include <string.h>

DataHubState g_datahub; /* 全局最新数据中心，供调试观察和任务间读取 */

void DataHub_Init(void)
{
    /* No tasks exist yet; avoid masking peripheral interrupts before scheduler start. */
    memset(&g_datahub, 0, sizeof(g_datahub));
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

void DataHub_PublishDigitalIo(const DataHubDigitalIoData *digital_io)
{
    uint32_t next_seq;

    if (digital_io == 0)
    {
        return;
    }

    /* 整包更新，保证读取者拿到同一扫描时刻的输入和输出状态。 */
    taskENTER_CRITICAL();
    next_seq = g_datahub.digital_io.seq + 1U;
    g_datahub.digital_io = *digital_io;
    g_datahub.digital_io.seq = next_seq;
    taskEXIT_CRITICAL();
}

void DataHub_GetDigitalIo(DataHubDigitalIoData *digital_io)
{
    if (digital_io == 0)
    {
        return;
    }

    taskENTER_CRITICAL();
    *digital_io = g_datahub.digital_io;
    taskEXIT_CRITICAL();
}

void DataHub_ClearRs485Sensors(void)
{
    uint8_t index;

    /* Keep each interrupt-disabled copy short instead of clearing the full array at once. */
    for (index = 0U; index < DATAHUB_RS485_MAX_ITEMS; index++)
    {
        taskENTER_CRITICAL();
        memset(&g_datahub.rs485_sensors[index], 0, sizeof(g_datahub.rs485_sensors[index]));
        taskEXIT_CRITICAL();
    }
}

void DataHub_PublishRs485Sensor(uint8_t index, const DataHubRs485SensorData *sensor)
{
    uint32_t next_seq;

    if ((index >= DATAHUB_RS485_MAX_ITEMS) || (sensor == 0))
    {
        return;
    }

    taskENTER_CRITICAL();
    next_seq = g_datahub.rs485_sensors[index].seq + 1U;
    g_datahub.rs485_sensors[index] = *sensor;
    g_datahub.rs485_sensors[index].seq = next_seq;
    taskEXIT_CRITICAL();
}

void DataHub_GetRs485Sensor(uint8_t index, DataHubRs485SensorData *sensor)
{
    if (sensor == 0)
    {
        return;
    }
    if (index >= DATAHUB_RS485_MAX_ITEMS)
    {
        memset(sensor, 0, sizeof(*sensor));
        return;
    }

    taskENTER_CRITICAL();
    *sensor = g_datahub.rs485_sensors[index];
    taskEXIT_CRITICAL();
}
