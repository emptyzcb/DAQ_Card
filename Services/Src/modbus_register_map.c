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

int MODBUS_REGISTER_ReadHolding(uint16_t address, uint16_t *value)
{
  DataHubImuData imu;
  DataHubAd7606Data ad7606;

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
      *value = DIGITAL_IO_SERVICE_ReadInputs();
      return 1;

    case 0x0002U:
      *value = DIGITAL_IO_SERVICE_GetOutputMask();
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
