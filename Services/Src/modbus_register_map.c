#include "modbus_register_map.h"

#include "main.h"
#include "datahub.h"
#include "digital_io_service.h"
#include "ad7606_service.h"

#if MODBUS_REGISTER_TEST_MODE
static uint16_t modbus_test_output_mask;

static uint16_t modbus_test_counter(void)
{
  return (uint16_t)((HAL_GetTick() / 100U) & 0x00FFU);
}
#endif

#if !MODBUS_REGISTER_TEST_MODE
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
#endif

static uint16_t modbus_as_u16(int16_t value)
{
  return (uint16_t)value;
}

int MODBUS_REGISTER_ReadHolding(uint16_t address, uint16_t *value)
{
#if !MODBUS_REGISTER_TEST_MODE
  DataHubImuData imu;
  DataHubAd7606Data ad7606;
#endif
#if MODBUS_REGISTER_TEST_MODE
  uint16_t counter = modbus_test_counter();
#endif

  if (value == 0)
  {
    return 0;
  }

  switch (address)
  {
    case 0x0000U:
#if MODBUS_REGISTER_TEST_MODE
      *value = 0x000FU;
#else
      DataHub_GetImu(&imu);
      DataHub_GetAd7606(&ad7606);
      *value = (uint16_t)((imu.imu_ready ? 0x0001U : 0U) |
                          (ad7606.ad7606_ready ? 0x0002U : 0U) |
                          ((imu.imu_ready && (imu.last_read_rslt == 0)) ? 0x0004U : 0U) |
                          (ad7606.last_read_ok ? 0x0008U : 0U));
#endif
      return 1;

    case 0x0001U:
#if MODBUS_REGISTER_TEST_MODE
      *value = (uint16_t)(0x005AU ^ ((counter & 0x01U) != 0U ? 0x00A5U : 0U));
#else
      *value = DIGITAL_IO_SERVICE_ReadInputs();
#endif
      return 1;

    case 0x0002U:
#if MODBUS_REGISTER_TEST_MODE
      *value = modbus_test_output_mask;
#else
      *value = DIGITAL_IO_SERVICE_GetOutputMask();
#endif
      return 1;

    case 0x0003U:
#if MODBUS_REGISTER_TEST_MODE
      *value = (uint16_t)BSP_AD7606_TEST_PASS;
#else
      {
        AD7606_SERVICE_Diagnostics diagnostics;
        AD7606_SERVICE_GetDiagnostics(&diagnostics);
        *value = (uint16_t)diagnostics.self_test;
      }
#endif
      return 1;

    default:
      break;
  }

  if ((address >= 0x0010U) && (address < 0x0018U))
  {
#if MODBUS_REGISTER_TEST_MODE
    *value = (uint16_t)(1000U + ((address - 0x0010U) * 10U) + counter);
#else
    DataHub_GetAd7606(&ad7606);
    *value = modbus_as_u16(ad7606.raw[address - 0x0010U]);
#endif
    return 1;
  }

  if ((address >= 0x0020U) && (address < 0x0026U))
  {
#if MODBUS_REGISTER_TEST_MODE
    *value = (uint16_t)(200U + ((address - 0x0020U) * 20U) + counter);
#else
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
#endif
    return 1;
  }

  if ((address >= 0x0030U) && (address < 0x0033U))
  {
#if MODBUS_REGISTER_TEST_MODE
    switch (address - 0x0030U)
    {
      case 0U: *value = modbus_as_u16((int16_t)(1200 + counter)); break;
      case 1U: *value = modbus_as_u16((int16_t)(-450 + counter)); break;
      default: *value = modbus_as_u16((int16_t)(890 + counter)); break;
    }
#else
    DataHub_GetImu(&imu);
    switch (address - 0x0030U)
    {
      case 0U: *value = modbus_as_u16(modbus_scale_angle(imu.euler.roll)); break;
      case 1U: *value = modbus_as_u16(modbus_scale_angle(imu.euler.pitch)); break;
      default: *value = modbus_as_u16(modbus_scale_angle(imu.euler.yaw)); break;
    }
#endif
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
  const uint16_t valid_output_mask = 0x03FFU;

  return MODBUS_REGISTER_IsWritable(address) &&
         ((value & (uint16_t)~valid_output_mask) == 0U);
}

int MODBUS_REGISTER_WriteHolding(uint16_t address, uint16_t value)
{
  if (!MODBUS_REGISTER_IsValueValid(address, value))
  {
    return 0;
  }

#if MODBUS_REGISTER_TEST_MODE
  modbus_test_output_mask = value;
#else
  DIGITAL_IO_SERVICE_SetOutputMask(value);
#endif
  return 1;
}
