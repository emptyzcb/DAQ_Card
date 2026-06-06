#include "sys.h"

static int imu_service_ready;

int IMU_SERVICE_Init(void)
{
  imu_service_ready = BSP_IMU660RC_Init(BSP_IMU660RC_ACCEL_RANGE_8G,
                                        BSP_IMU660RC_GYRO_RANGE_2000DPS);
  return imu_service_ready;
}

int IMU_SERVICE_IsReady(void)
{
  return imu_service_ready;
}

int IMU_SERVICE_Read6Axis(IMU_SERVICE_6AxisData *data)
{
  if ((data == NULL) || !imu_service_ready)
  {
    return -1;
  }
  return BSP_IMU660RC_Read6Axis(data);
}

uint8_t IMU_SERVICE_ReadChipId(void)
{
  return BSP_IMU660RC_ReadChipId();
}

void IMU_SERVICE_PrintDebug(void)
{
  BSP_IMU660RC_PrintDebug();
}
