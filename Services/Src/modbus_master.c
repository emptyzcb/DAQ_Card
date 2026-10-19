#include "modbus_master.h"

#include <string.h>

#include "FreeRTOS.h"
#include "task.h"

#include "datahub.h"
#include "main.h"
#include "modbus_rtu.h"
#include "rs485_uart.h"

#define MODBUS_MASTER_RESPONSE_TIMEOUT_MS 200U
#define MODBUS_MASTER_REQ_SIZE              8U
#define MODBUS_MASTER_RESP_MAX             64U

static ModbusMasterPollEntry g_staging_table[MODBUS_MASTER_MAX_ITEMS];
static ModbusMasterPollEntry g_active_table[MODBUS_MASTER_MAX_ITEMS];
static uint32_t g_next_due_ms[MODBUS_MASTER_MAX_ITEMS];
static uint32_t g_inflight_generation[MODBUS_MASTER_MAX_ITEMS];
static uint32_t g_config_generation;
static uint8_t g_staging_count;
static uint8_t g_active_count;
static uint8_t g_schedule_cursor;

static uint16_t master_read_u16(const uint8_t *data)
{
  return (uint16_t)(((uint16_t)data[0] << 8) | data[1]);
}

static uint8_t master_entry_valid(const ModbusMasterPollEntry *entry)
{
  if (entry == 0)
  {
    return 0U;
  }
  if (entry->enabled == 0U)
  {
    return 1U;
  }
  if ((entry->slave_addr == 0U) || (entry->slave_addr > 247U))
  {
    return 0U;
  }
  if ((entry->function != MODBUS_MASTER_FUNCTION_HOLDING) &&
      (entry->function != MODBUS_MASTER_FUNCTION_INPUT))
  {
    return 0U;
  }
  if ((entry->quantity == 0U) ||
      (entry->quantity > MODBUS_MASTER_REGS_PER_ITEM))
  {
    return 0U;
  }
  if ((entry->interval_ms < MODBUS_MASTER_MIN_INTERVAL_MS) ||
      (entry->interval_ms > MODBUS_MASTER_MAX_INTERVAL_MS))
  {
    return 0U;
  }
  return 1U;
}

static void master_publish_configuration(void)
{
  uint8_t index;

  DataHub_ClearRs485Sensors();
  for (index = 0U; index < g_active_count; index++)
  {
    DataHubRs485SensorData sensor;
    ModbusMasterPollEntry entry;

    if (MODBUS_MASTER_GetActiveEntry(index, &entry) == 0)
    {
      continue;
    }
    memset(&sensor, 0, sizeof(sensor));
    sensor.configured = 1U;
    sensor.enabled = entry.enabled;
    sensor.slave_addr = entry.slave_addr;
    sensor.function = entry.function;
    sensor.reg_start = entry.reg_start;
    sensor.quantity = (uint8_t)entry.quantity;
    sensor.interval_ms = entry.interval_ms;
    DataHub_PublishRs485Sensor(index, &sensor);
  }
}

void MODBUS_MASTER_Init(void)
{
  taskENTER_CRITICAL();
  memset(g_staging_table, 0, sizeof(g_staging_table));
  memset(g_active_table, 0, sizeof(g_active_table));
  memset(g_next_due_ms, 0, sizeof(g_next_due_ms));
  memset(g_inflight_generation, 0, sizeof(g_inflight_generation));
  g_config_generation = 0U;
  g_staging_count = 0U;
  g_active_count = 0U;
  g_schedule_cursor = 0U;
  taskEXIT_CRITICAL();
  DataHub_ClearRs485Sensors();
}

int MODBUS_MASTER_StageEntry(uint8_t index, const ModbusMasterPollEntry *entry)
{
  if ((index >= MODBUS_MASTER_MAX_ITEMS) || (entry == 0))
  {
    return 0;
  }

  taskENTER_CRITICAL();
  g_staging_table[index] = *entry;
  taskEXIT_CRITICAL();
  return 1;
}

int MODBUS_MASTER_GetStagedEntry(uint8_t index, ModbusMasterPollEntry *entry)
{
  if ((index >= MODBUS_MASTER_MAX_ITEMS) || (entry == 0))
  {
    return 0;
  }

  taskENTER_CRITICAL();
  *entry = g_staging_table[index];
  taskEXIT_CRITICAL();
  return 1;
}

int MODBUS_MASTER_SetStagedCount(uint8_t count)
{
  if (count > MODBUS_MASTER_MAX_ITEMS)
  {
    return 0;
  }

  taskENTER_CRITICAL();
  g_staging_count = count;
  taskEXIT_CRITICAL();
  return 1;
}

uint8_t MODBUS_MASTER_GetStagedCount(void)
{
  uint8_t count;

  taskENTER_CRITICAL();
  count = g_staging_count;
  taskEXIT_CRITICAL();
  return count;
}

int MODBUS_MASTER_ApplyStaged(void)
{
  ModbusMasterPollEntry candidate[MODBUS_MASTER_MAX_ITEMS];
  uint8_t count;
  uint8_t index;

  taskENTER_CRITICAL();
  count = g_staging_count;
  memcpy(candidate, g_staging_table, sizeof(candidate));
  taskEXIT_CRITICAL();

  if (count > MODBUS_MASTER_MAX_ITEMS)
  {
    return 0;
  }
  for (index = 0U; index < count; index++)
  {
    if (master_entry_valid(&candidate[index]) == 0U)
    {
      return 0;
    }
  }

  taskENTER_CRITICAL();
  memset(g_active_table, 0, sizeof(g_active_table));
  memcpy(g_active_table, candidate, (uint32_t)count * sizeof(candidate[0]));
  memset(g_next_due_ms, 0, sizeof(g_next_due_ms));
  memset(g_inflight_generation, 0, sizeof(g_inflight_generation));
  g_active_count = count;
  g_schedule_cursor = 0U;
  g_config_generation++;
  taskEXIT_CRITICAL();

  master_publish_configuration();
  return 1;
}

uint8_t MODBUS_MASTER_GetActiveCount(void)
{
  uint8_t count;

  taskENTER_CRITICAL();
  count = g_active_count;
  taskEXIT_CRITICAL();
  return count;
}

int MODBUS_MASTER_GetActiveEntry(uint8_t index, ModbusMasterPollEntry *entry)
{
  if ((entry == 0) || (index >= MODBUS_MASTER_MAX_ITEMS))
  {
    return 0;
  }

  taskENTER_CRITICAL();
  if (index >= g_active_count)
  {
    taskEXIT_CRITICAL();
    return 0;
  }
  *entry = g_active_table[index];
  taskEXIT_CRITICAL();
  return 1;
}

uint32_t MODBUS_MASTER_GetConfigGeneration(void)
{
  uint32_t generation;

  taskENTER_CRITICAL();
  generation = g_config_generation;
  taskEXIT_CRITICAL();
  return generation;
}

int MODBUS_MASTER_TakeDue(uint32_t now_ms,
                          uint8_t *slot,
                          ModbusMasterPollEntry *entry)
{
  uint8_t offset;

  if ((slot == 0) || (entry == 0))
  {
    return 0;
  }

  taskENTER_CRITICAL();
  for (offset = 0U; offset < g_active_count; offset++)
  {
    uint8_t index = (uint8_t)((g_schedule_cursor + offset) % g_active_count);
    if ((g_active_table[index].enabled != 0U) &&
        ((int32_t)(now_ms - g_next_due_ms[index]) >= 0))
    {
      *slot = index;
      *entry = g_active_table[index];
      g_inflight_generation[index] = g_config_generation;
      g_schedule_cursor = (uint8_t)((index + 1U) % g_active_count);
      taskEXIT_CRITICAL();
      return 1;
    }
  }
  taskEXIT_CRITICAL();
  return 0;
}

void MODBUS_MASTER_Complete(uint8_t slot,
                            uint8_t success,
                            const uint16_t *values,
                            uint16_t quantity,
                            uint32_t now_ms)
{
  DataHubRs485SensorData sensor;
  ModbusMasterPollEntry entry;
  uint16_t copy_count;

  if (slot >= MODBUS_MASTER_MAX_ITEMS)
  {
    return;
  }

  taskENTER_CRITICAL();
  if ((slot >= g_active_count) ||
      (g_inflight_generation[slot] != g_config_generation))
  {
    taskEXIT_CRITICAL();
    return;
  }
  entry = g_active_table[slot];
  g_next_due_ms[slot] = now_ms + entry.interval_ms;
  g_inflight_generation[slot] = 0U;
  taskEXIT_CRITICAL();

  DataHub_GetRs485Sensor(slot, &sensor);
  sensor.configured = 1U;
  sensor.enabled = entry.enabled;
  sensor.slave_addr = entry.slave_addr;
  sensor.function = entry.function;
  sensor.reg_start = entry.reg_start;
  sensor.quantity = (uint8_t)entry.quantity;
  sensor.interval_ms = entry.interval_ms;
  sensor.timestamp_ms = now_ms;

  if ((success != 0U) && (values != 0))
  {
    copy_count = (quantity < entry.quantity) ? quantity : entry.quantity;
    if (copy_count > MODBUS_MASTER_REGS_PER_ITEM)
    {
      copy_count = MODBUS_MASTER_REGS_PER_ITEM;
    }
    memcpy(sensor.values, values, (uint32_t)copy_count * sizeof(values[0]));
    sensor.last_ok = 1U;
  }
  else
  {
    sensor.last_ok = 0U;
    sensor.error_count++;
  }
  DataHub_PublishRs485Sensor(slot, &sensor);
}

static void master_flush_rx(void)
{
  uint8_t buffer[RS485_UART_FRAME_MAX_SIZE];
  uint16_t length;

  while (RS485_UART_TryReceiveFrame(buffer, (uint16_t)sizeof(buffer), &length) > 0)
  {
    /* Discard late data before starting a new request. */
  }
}

static uint8_t master_poll_one(const ModbusMasterPollEntry *entry,
                               uint16_t *values)
{
  uint8_t request[MODBUS_MASTER_REQ_SIZE];
  uint8_t response[MODBUS_MASTER_RESP_MAX];
  uint16_t response_length;
  uint16_t expected_length;
  uint16_t received_crc;
  uint16_t calculated_crc;
  uint32_t start_tick;
  uint16_t index;

  request[0] = entry->slave_addr;
  request[1] = entry->function;
  request[2] = (uint8_t)(entry->reg_start >> 8);
  request[3] = (uint8_t)entry->reg_start;
  request[4] = (uint8_t)(entry->quantity >> 8);
  request[5] = (uint8_t)entry->quantity;
  calculated_crc = MODBUS_RTU_Crc16(request, 6U);
  request[6] = (uint8_t)calculated_crc;
  request[7] = (uint8_t)(calculated_crc >> 8);

  master_flush_rx();
  if (RS485_UART_Send(request, sizeof(request), 100U) == 0)
  {
    return 0U;
  }

  expected_length = (uint16_t)(5U + (2U * entry->quantity));
  start_tick = HAL_GetTick();
  for (;;)
  {
    int receive_result = RS485_UART_TryReceiveFrame(response,
                                                     (uint16_t)sizeof(response),
                                                     &response_length);
    if (receive_result > 0)
    {
      break;
    }
    if ((uint32_t)(HAL_GetTick() - start_tick) >= MODBUS_MASTER_RESPONSE_TIMEOUT_MS)
    {
      return 0U;
    }
    vTaskDelay(pdMS_TO_TICKS(1U));
  }

  if ((response_length != expected_length) ||
      (response[0] != entry->slave_addr) ||
      (response[1] != entry->function) ||
      (response[2] != (uint8_t)(entry->quantity * 2U)))
  {
    RS485_UART_DiagnosticMarkFrame(0U);
    return 0U;
  }

  received_crc = (uint16_t)(response[response_length - 2U] |
                            ((uint16_t)response[response_length - 1U] << 8));
  calculated_crc = MODBUS_RTU_Crc16(response, (uint16_t)(response_length - 2U));
  if (received_crc != calculated_crc)
  {
    RS485_UART_DiagnosticMarkFrame(0U);
    return 0U;
  }

  for (index = 0U; index < entry->quantity; index++)
  {
    values[index] = master_read_u16(&response[3U + (2U * index)]);
  }
  RS485_UART_DiagnosticMarkFrame(1U);
  return 1U;
}

void MODBUS_MASTER_Poll(uint32_t now_ms)
{
  ModbusMasterPollEntry entry;
  uint16_t values[MODBUS_MASTER_REGS_PER_ITEM];
  uint8_t slot;
  uint8_t success;

  if (MODBUS_MASTER_TakeDue(now_ms, &slot, &entry) == 0)
  {
    return;
  }

  memset(values, 0, sizeof(values));
  success = master_poll_one(&entry, values);
  MODBUS_MASTER_Complete(slot,
                          success,
                          values,
                          entry.quantity,
                          HAL_GetTick());
}
