#include "modbus_register_map.h"

#include "datahub.h"
#include "digital_io_service.h"
#include "ad7606_service.h"

static int16_t modbus_scale_angle(float degrees)
{
  float scaled = degrees * 100.0f;

  if (scaled > 32767.0f)
  {
    return 32767;
  }

  if (scaled < -32768.0f)
  {
    return -32768;
  }

  if (scaled >= 0.0f)
  {
    scaled += 0.5f;
  }
  else
  {
    scaled -= 0.5f;
  }

  return (int16_t)scaled;
}

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
  DataHubImuData imu;
  DataHubAd7606Data ad7606;
  DataHubDigitalIoData digital_io;

  if (value == 0)
  {
    return 0;
  }

  switch (address)
  {
    case 0x0000U:
      DataHub_GetImu(&imu);
      DataHub_GetAd7606(&ad7606);
      *value = (uint16_t)((imu.imu_ready ? 0x0001U : 0U) |
                          (ad7606.ad7606_ready ? 0x0002U : 0U) |
                          ((imu.imu_ready && (imu.last_read_rslt == 0)) ? 0x0004U : 0U) |
                          (ad7606.last_read_ok ? 0x0008U : 0U));
      return 1;

    case 0x0001U:
      DataHub_GetDigitalIo(&digital_io);
      *value = digital_io.input_mask;
      return 1;

    case 0x0002U:
      DataHub_GetDigitalIo(&digital_io);
      *value = digital_io.output_mask;
      return 1;

    case 0x0003U:
      {
        AD7606_SERVICE_Diagnostics diagnostics;
        AD7606_SERVICE_GetDiagnostics(&diagnostics);
        *value = (uint16_t)diagnostics.health;
      }
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

  if ((address >= 0x0020U) && (address < 0x0026U))
  {
    DataHub_GetImu(&imu);
    switch (address - 0x0020U)
    {
      case 0U: *value = modbus_as_u16(imu.accel_raw_x); break;
      case 1U: *value = modbus_as_u16(imu.accel_raw_y); break;
      case 2U: *value = modbus_as_u16(imu.accel_raw_z); break;
      case 3U: *value = modbus_as_u16(imu.gyro_raw_x); break;
      case 4U: *value = modbus_as_u16(imu.gyro_raw_y); break;
      default: *value = modbus_as_u16(imu.gyro_raw_z); break;
    }
    return 1;
  }

  if ((address >= 0x0030U) && (address < 0x0033U))
  {
    DataHub_GetImu(&imu);
    switch (address - 0x0030U)
    {
      case 0U: *value = modbus_as_u16(modbus_scale_angle(imu.euler.roll)); break;
      case 1U: *value = modbus_as_u16(modbus_scale_angle(imu.euler.pitch)); break;
      default: *value = modbus_as_u16(modbus_scale_angle(imu.euler.yaw)); break;
    }
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

  return 0;
}

int MODBUS_REGISTER_IsWritable(uint16_t address)
{
  return (address == 0x0002U) ? 1 : 0;
}

int MODBUS_REGISTER_IsValueValid(uint16_t address, uint16_t value)
{
  const uint16_t valid_output_mask = 0x00FFU;

  return MODBUS_REGISTER_IsWritable(address) &&
         ((value & (uint16_t)~valid_output_mask) == 0U);
}

int MODBUS_REGISTER_WriteHolding(uint16_t address, uint16_t value)
{
  if (!MODBUS_REGISTER_IsValueValid(address, value))
  {
    return 0;
  }

  DIGITAL_IO_SERVICE_SetOutputMask(value);
  return 1;
}
