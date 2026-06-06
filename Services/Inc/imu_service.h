#ifndef IMU_SERVICE_H
#define IMU_SERVICE_H

#include "bsp_imu660rc.h"

typedef BSP_IMU660RC_6AxisData IMU_SERVICE_6AxisData;

int IMU_SERVICE_Init(void);
int IMU_SERVICE_IsReady(void);
int IMU_SERVICE_Read6Axis(IMU_SERVICE_6AxisData *data);
uint8_t IMU_SERVICE_ReadChipId(void);
void IMU_SERVICE_PrintDebug(void);

#endif /* IMU_SERVICE_H */
