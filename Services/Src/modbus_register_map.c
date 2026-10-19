#include "modbus_register_map.h"

#include "datahub.h"
#include "digital_io_service.h"
#include "ad7606_service.h"
#include "rs485_uart.h"
#include "io_logic_engine.h"
#include "modbus_master.h"

#define MODBUS_RS485_DATA_BASE             0x0080U
#define MODBUS_RS485_DATA_END              0x027FU
#define MODBUS_RS485_ACTIVE_COUNT_REG      0x0280U
#define MODBUS_RS485_CAPACITY_REG          0x0281U
#define MODBUS_RS485_GENERATION_HIGH_REG   0x0282U
#define MODBUS_RS485_GENERATION_LOW_REG    0x0283U
#define MODBUS_RS485_STAGED_COUNT_REG      0x0284U
#define MODBUS_RS485_APPLY_REG             0x0285U
#define MODBUS_RS485_APPLY_MAGIC           0xA55AU
#define MODBUS_RS485_METADATA_BASE         0x0300U
#define MODBUS_RS485_METADATA_END          0x03FFU
#define MODBUS_RS485_METADATA_REGS         8U
#define MODBUS_RS485_STAGING_BASE          0x0400U
#define MODBUS_RS485_STAGING_END           0x04BFU
#define MODBUS_RS485_STAGING_REGS          6U

static uint16_t modbus_as_u16(int16_t value)
{
  return (uint16_t)value;
}

static uint16_t modbus_u32_high(uint32_t value)
{
  return (uint16_t)(value >> 16);
}

static uint16_t modbus_u32_low(uint32_t value)
{
  return (uint16_t)value;
}

static uint16_t modbus_u64_word(uint64_t value, uint8_t word_index)
{
  return (uint16_t)(value >> ((3U - word_index) * 16U));
}

int MODBUS_REGISTER_ReadHolding(uint16_t address, uint16_t *value)
{
  DataHubAd7606Data ad7606;
  DataHubDigitalIoData digital_io;

  if (value == 0)
  {
    return 0;
  }

  switch (address)
  {
    case 0x0000U:
      DataHub_GetAd7606(&ad7606);
      *value = (uint16_t)((ad7606.ad7606_ready ? 0x0002U : 0U) |
                          (ad7606.last_read_ok ? 0x0008U : 0U));
      return 1;

    case 0x0001U:
      DataHub_GetDigitalIo(&digital_io);
      *value = digital_io.input_mask;
      return 1;

    case 0x0002U:
      /*
       * 输出掩码只读回读：板卡 8 路物理输出完全由 IO 规则引擎驱动，
       * 上位机写本寄存器不再直接拉继电器/晶体管。要影响输出请下载
       * IOCF 规则或写 0x0005 远程命令字。
       */
      *value = IO_LOGIC_ENGINE_GetOutputMask();
      return 1;

    case 0x0003U:
      {
        AD7606_SERVICE_Diagnostics diagnostics;
        AD7606_SERVICE_GetDiagnostics(&diagnostics);
        *value = (uint16_t)diagnostics.health;
      }
      return 1;

    case 0x0004U:
      /* 外部传感器链路状态：bit0..15 = item0..15 最近一次成功。 */
      {
        uint16_t word = 0U;
        uint8_t i;
        for (i = 0U; i < 16U; i++)
        {
          DataHubRs485SensorData sensor;
          DataHub_GetRs485Sensor(i, &sensor);
          if ((sensor.configured != 0U) && (sensor.last_ok != 0U))
          {
            word |= (uint16_t)(1U << i);
          }
        }
        *value = word;
      }
      return 1;

    case 0x0006U:
      /* bit0: snapshot inputs; bit1: current-breath opcode. */
      *value = 0x0003U;
      return 1;

    default:
      break;
  }

  if ((address >= 0x0010U) && (address < 0x0018U))
  {
    DataHub_GetAd7606(&ad7606);
    *value = modbus_as_u16(ad7606.raw[address - 0x0010U]);
    return 1;
  }

  if ((address >= 0x0040U) && (address <= 0x0058U))
  {
    uint32_t encoded_value;

    DataHub_GetDigitalIo(&digital_io);
    switch (address)
    {
      case 0x0040U: *value = modbus_u64_word(digital_io.pulse_count[0], 0U); break;
      case 0x0041U: *value = modbus_u64_word(digital_io.pulse_count[0], 1U); break;
      case 0x0042U: *value = modbus_u64_word(digital_io.pulse_count[0], 2U); break;
      case 0x0043U: *value = modbus_u64_word(digital_io.pulse_count[0], 3U); break;
      case 0x0044U: *value = modbus_u64_word(digital_io.pulse_count[1], 0U); break;
      case 0x0045U: *value = modbus_u64_word(digital_io.pulse_count[1], 1U); break;
      case 0x0046U: *value = modbus_u64_word(digital_io.pulse_count[1], 2U); break;
      case 0x0047U: *value = modbus_u64_word(digital_io.pulse_count[1], 3U); break;
      case 0x0048U: *value = modbus_u32_high(digital_io.pulse_frequency_hz[0]); break;
      case 0x0049U: *value = modbus_u32_low(digital_io.pulse_frequency_hz[0]); break;
      case 0x004AU: *value = modbus_u32_high(digital_io.pulse_frequency_hz[1]); break;
      case 0x004BU: *value = modbus_u32_low(digital_io.pulse_frequency_hz[1]); break;
      case 0x004CU:
        encoded_value = (uint32_t)digital_io.encoder_position;
        *value = modbus_u32_high(encoded_value);
        break;
      case 0x004DU:
        encoded_value = (uint32_t)digital_io.encoder_position;
        *value = modbus_u32_low(encoded_value);
        break;
      case 0x004EU:
        encoded_value = (uint32_t)digital_io.encoder_speed_cps;
        *value = modbus_u32_high(encoded_value);
        break;
      case 0x004FU:
        encoded_value = (uint32_t)digital_io.encoder_speed_cps;
        *value = modbus_u32_low(encoded_value);
        break;
      case 0x0050U:
        encoded_value = (uint32_t)digital_io.encoder_index_position;
        *value = modbus_u32_high(encoded_value);
        break;
      case 0x0051U:
        encoded_value = (uint32_t)digital_io.encoder_index_position;
        *value = modbus_u32_low(encoded_value);
        break;
      case 0x0052U: *value = modbus_u32_high(digital_io.encoder_index_count); break;
      case 0x0053U: *value = modbus_u32_low(digital_io.encoder_index_count); break;
      case 0x0054U: *value = modbus_u32_high(digital_io.encoder_error_count); break;
      case 0x0055U: *value = modbus_u32_low(digital_io.encoder_error_count); break;
      case 0x0056U: *value = digital_io.status_flags; break;
      case 0x0057U: *value = digital_io.normal_input_mask; break;
      default:
        *value = (uint16_t)(digital_io.encoder_ab_state & 0x03U);
        if (digital_io.encoder_direction > 0) *value |= 0x0100U;
        if (digital_io.encoder_direction < 0) *value |= 0x0200U;
        break;
    }
    return 1;
  }

  /*
   * RS485 link statistics (read-only), 0x0060..0x0063.
   * Cumulative counters maintained by the RS485 service since startup.
   * This block is independent of the digital IO counters at 0x0040..0x0058
   * and reports the physical Modbus link health to the host.
   */
  if ((address >= 0x0060U) && (address < 0x0064U))
  {
    RS485_UART_Stats stats;

    RS485_UART_GetStats(&stats);
    switch (address - 0x0060U)
    {
      case 0U: *value = (uint16_t)(stats.rx_frames & 0xFFFFU); break;
      case 1U: *value = (uint16_t)(stats.bad_frames & 0xFFFFU); break;
      case 2U: *value = (uint16_t)(stats.dropped & 0xFFFFU); break;
      default: *value = (uint16_t)(stats.tx_frames & 0xFFFFU); break;
    }
    return 1;
  }

  /*
   * 联合控制器实时数据 (read-only), 0x0070..0x007B.
   * 由字节码虚拟机与数字IO服务联合提供，上位机联合控制器观测页面读取。
   *  0x0070/0x0071 : DI1 累计脉冲数（低/高 16 位）
   *  0x0072/0x0073 : DI2 累计脉冲数（低/高 16 位）
   *  0x0074        : DI1 实时频率（Hz）
   *  0x0075        : DI2 实时频率（Hz）
   *  0x0076        : 鉴频档位（高 8 位=DI2，低 8 位=DI1）
   *  0x0077        : 运行状态(高8) | 外部模式(bit4~7) | 当前模式(低4位)
   *  0x0078        : 输入掩码
   *  0x0079        : 状态标志（VM 故障 | 数字IO 状态）
   *  0x007A        : 当前模拟电压输出指令（mV，非端子实测值）
   *  0x007B        : 模拟量输出电流（uA）
   */
  if ((address >= 0x0070U) && (address <= 0x007BU))
  {
    DataHub_GetDigitalIo(&digital_io);
    switch (address)
    {
      case 0x0070U: *value = (uint16_t)(digital_io.pulse_count[0] & 0xFFFFU); break;
      case 0x0071U: *value = (uint16_t)((digital_io.pulse_count[0] >> 16) & 0xFFFFU); break;
      case 0x0072U: *value = (uint16_t)(digital_io.pulse_count[1] & 0xFFFFU); break;
      case 0x0073U: *value = (uint16_t)((digital_io.pulse_count[1] >> 16) & 0xFFFFU); break;
      case 0x0074U: *value = (uint16_t)digital_io.pulse_frequency_hz[0]; break;
      case 0x0075U: *value = (uint16_t)digital_io.pulse_frequency_hz[1]; break;
      case 0x0076U:
        *value = (uint16_t)((IO_LOGIC_ENGINE_GetFreqClass(1U) << 8) |
                            IO_LOGIC_ENGINE_GetFreqClass(0U));
        break;
      case 0x0077U:
        *value = (uint16_t)((IO_LOGIC_ENGINE_GetRunState() << 8) |
                            (DIGITAL_IO_SERVICE_GetExternalMode() << 4) |
                            IO_LOGIC_ENGINE_GetCurrentMode());
        break;
      case 0x0078U: *value = digital_io.input_mask; break;
      case 0x0079U: *value = (uint16_t)(IO_LOGIC_ENGINE_GetStatusFlags() | digital_io.status_flags); break;
      case 0x007AU: *value = IO_LOGIC_ENGINE_GetAoVoltageMv(); break;
      default:      *value = IO_LOGIC_ENGINE_GetAoCurrentUa(); break;
    }
    return 1;
  }

  /* 32 个 RS485 采集项的最新数据，每项 16 个寄存器。 */
  if ((address >= MODBUS_RS485_DATA_BASE) &&
      (address <= MODBUS_RS485_DATA_END))
  {
    DataHubRs485SensorData sensor;
    uint16_t item_index = (uint16_t)((address - MODBUS_RS485_DATA_BASE) /
                                     MODBUS_MASTER_REGS_PER_ITEM);
    uint16_t value_index = (uint16_t)((address - MODBUS_RS485_DATA_BASE) %
                                      MODBUS_MASTER_REGS_PER_ITEM);

    if (item_index >= MODBUS_MASTER_MAX_ITEMS)
    {
      return 0;
    }
    DataHub_GetRs485Sensor((uint8_t)item_index, &sensor);
    *value = sensor.values[value_index];
    return 1;
  }

  if ((address >= MODBUS_RS485_ACTIVE_COUNT_REG) &&
      (address <= MODBUS_RS485_APPLY_REG))
  {
    uint32_t generation = MODBUS_MASTER_GetConfigGeneration();

    switch (address)
    {
      case MODBUS_RS485_ACTIVE_COUNT_REG:
        *value = MODBUS_MASTER_GetActiveCount();
        break;
      case MODBUS_RS485_CAPACITY_REG:
        *value = MODBUS_MASTER_MAX_ITEMS;
        break;
      case MODBUS_RS485_GENERATION_HIGH_REG:
        *value = modbus_u32_high(generation);
        break;
      case MODBUS_RS485_GENERATION_LOW_REG:
        *value = modbus_u32_low(generation);
        break;
      case MODBUS_RS485_STAGED_COUNT_REG:
        *value = MODBUS_MASTER_GetStagedCount();
        break;
      default:
        *value = 0U;
        break;
    }
    return 1;
  }

  /* 每个采集项 8 个状态寄存器：身份、数量、状态、错误数和时间戳。 */
  if ((address >= MODBUS_RS485_METADATA_BASE) &&
      (address <= MODBUS_RS485_METADATA_END))
  {
    DataHubRs485SensorData sensor;
    uint16_t item_index = (uint16_t)((address - MODBUS_RS485_METADATA_BASE) /
                                     MODBUS_RS485_METADATA_REGS);
    uint16_t field = (uint16_t)((address - MODBUS_RS485_METADATA_BASE) %
                                MODBUS_RS485_METADATA_REGS);

    if (item_index >= MODBUS_MASTER_MAX_ITEMS)
    {
      return 0;
    }
    DataHub_GetRs485Sensor((uint8_t)item_index, &sensor);
    switch (field)
    {
      case 0U: *value = sensor.slave_addr; break;
      case 1U: *value = sensor.function; break;
      case 2U: *value = sensor.quantity; break;
      case 3U:
        *value = (uint16_t)((sensor.configured ? 0x0001U : 0U) |
                            (sensor.enabled ? 0x0002U : 0U) |
                            (sensor.last_ok ? 0x0004U : 0U));
        break;
      case 4U: *value = modbus_u32_high(sensor.error_count); break;
      case 5U: *value = modbus_u32_low(sensor.error_count); break;
      case 6U: *value = modbus_u32_high(sensor.timestamp_ms); break;
      default: *value = modbus_u32_low(sensor.timestamp_ms); break;
    }
    return 1;
  }

  /* 上位机下载用的暂存采集表，每项 6 个寄存器。 */
  if ((address >= MODBUS_RS485_STAGING_BASE) &&
      (address <= MODBUS_RS485_STAGING_END))
  {
    ModbusMasterPollEntry entry;
    uint16_t item_index = (uint16_t)((address - MODBUS_RS485_STAGING_BASE) /
                                     MODBUS_RS485_STAGING_REGS);
    uint16_t field = (uint16_t)((address - MODBUS_RS485_STAGING_BASE) %
                                MODBUS_RS485_STAGING_REGS);

    if ((item_index >= MODBUS_MASTER_MAX_ITEMS) ||
        (MODBUS_MASTER_GetStagedEntry((uint8_t)item_index, &entry) == 0))
    {
      return 0;
    }
    switch (field)
    {
      case 0U: *value = entry.enabled; break;
      case 1U:
        *value = (uint16_t)(((uint16_t)entry.function << 8) |
                            entry.slave_addr);
        break;
      case 2U: *value = entry.reg_start; break;
      case 3U: *value = entry.quantity; break;
      case 4U: *value = entry.interval_ms; break;
      default: *value = entry.reserved; break;
    }
    return 1;
  }

  return 0;
}

int MODBUS_REGISTER_IsWritable(uint16_t address)
{
  /*
   * 0x0002 数字输出掩码改为只读（物理输出完全由 IO 规则引擎驱动）。
   * 0x0005 远程命令字：上位机写值后由引擎字节码决定如何响应，
   *        本寄存器本身不直接动任何硬件输出。
   */
  if ((address == 0x0005U) ||
      (address == MODBUS_RS485_STAGED_COUNT_REG) ||
      (address == MODBUS_RS485_APPLY_REG))
  {
    return 1;
  }

  return ((address >= MODBUS_RS485_STAGING_BASE) &&
          (address <= MODBUS_RS485_STAGING_END)) ? 1 : 0;
}

int MODBUS_REGISTER_IsValueValid(uint16_t address, uint16_t value)
{
  uint16_t field;

  if (address == 0x0005U)
  {
    return 1;
  }
  if (address == MODBUS_RS485_STAGED_COUNT_REG)
  {
    return (value <= MODBUS_MASTER_MAX_ITEMS) ? 1 : 0;
  }
  if (address == MODBUS_RS485_APPLY_REG)
  {
    return (value == MODBUS_RS485_APPLY_MAGIC) ? 1 : 0;
  }
  if ((address < MODBUS_RS485_STAGING_BASE) ||
      (address > MODBUS_RS485_STAGING_END))
  {
    return 0;
  }

  field = (uint16_t)((address - MODBUS_RS485_STAGING_BASE) %
                     MODBUS_RS485_STAGING_REGS);
  switch (field)
  {
    case 0U:
      return (value <= 1U) ? 1 : 0;
    case 1U:
      return ((((value >> 8) == MODBUS_MASTER_FUNCTION_HOLDING) ||
               ((value >> 8) == MODBUS_MASTER_FUNCTION_INPUT)) &&
              ((value & 0x00FFU) >= 1U) &&
              ((value & 0x00FFU) <= 247U)) ? 1 : 0;
    case 3U:
      return ((value >= 1U) &&
              (value <= MODBUS_MASTER_REGS_PER_ITEM)) ? 1 : 0;
    case 4U:
      return ((value >= MODBUS_MASTER_MIN_INTERVAL_MS) &&
              (value <= MODBUS_MASTER_MAX_INTERVAL_MS)) ? 1 : 0;
    case 5U:
      return (value == 0U) ? 1 : 0;
    default:
      return 1;
  }
}

int MODBUS_REGISTER_WriteHolding(uint16_t address, uint16_t value)
{
  ModbusMasterPollEntry entry;
  uint16_t item_index;
  uint16_t field;

  if (!MODBUS_REGISTER_IsValueValid(address, value))
  {
    return 0;
  }

  if (address == 0x0005U)
  {
    IO_LOGIC_ENGINE_SetRemoteCommand(value);
    return 1;
  }

  if (address == MODBUS_RS485_STAGED_COUNT_REG)
  {
    return MODBUS_MASTER_SetStagedCount((uint8_t)value);
  }

  if (address == MODBUS_RS485_APPLY_REG)
  {
    return MODBUS_MASTER_ApplyStaged();
  }

  item_index = (uint16_t)((address - MODBUS_RS485_STAGING_BASE) /
                          MODBUS_RS485_STAGING_REGS);
  field = (uint16_t)((address - MODBUS_RS485_STAGING_BASE) %
                     MODBUS_RS485_STAGING_REGS);
  if ((item_index >= MODBUS_MASTER_MAX_ITEMS) ||
      (MODBUS_MASTER_GetStagedEntry((uint8_t)item_index, &entry) == 0))
  {
    return 0;
  }

  switch (field)
  {
    case 0U:
      entry.enabled = (uint8_t)value;
      break;
    case 1U:
      entry.function = (uint8_t)(value >> 8);
      entry.slave_addr = (uint8_t)value;
      break;
    case 2U:
      entry.reg_start = value;
      break;
    case 3U:
      entry.quantity = value;
      break;
    case 4U:
      entry.interval_ms = value;
      break;
    default:
      entry.reserved = (uint8_t)value;
      break;
  }
  return MODBUS_MASTER_StageEntry((uint8_t)item_index, &entry);

}
