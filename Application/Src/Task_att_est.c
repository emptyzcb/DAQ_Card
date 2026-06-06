#include "sys.h"

void Task_att_est(void *arg)
{
    IMU_SERVICE_6AxisData imu;
    int init_ok;
    int read_rslt;
    uint8_t chip_id;
    uint32_t sample_count = 0U;
    TickType_t last_print_tick;

    (void)arg;

    printf("[IMU] Task_att_est start\r\n");
    printf("[IMU] Init BMI270...\r\n");

    init_ok = IMU_SERVICE_Init();
    chip_id = IMU_SERVICE_ReadChipId();
    printf("[IMU] Init result=%d, chip_id=0x%02X\r\n", init_ok, chip_id);

    if (chip_id != 0x24U)
    {
        printf("[IMU] Warning: BMI270 chip id should be 0x24\r\n");
    }

    last_print_tick = xTaskGetTickCount();

    for (;;)
    {
        if (IMU_SERVICE_IsReady() != 0)
        {
            read_rslt = IMU_SERVICE_Read6Axis(&imu);
            if (read_rslt == 0)
            {
                sample_count++;

                if ((xTaskGetTickCount() - last_print_tick) >= pdMS_TO_TICKS(500))
                {
                    last_print_tick = xTaskGetTickCount();
                    printf("[IMU] cnt=%lu acc_raw=%d,%d,%d gyro_raw=%d,%d,%d gyro_dps=%ld,%ld,%ld\r\n",
                           (unsigned long)sample_count,
                           imu.accel.x_raw,
                           imu.accel.y_raw,
                           imu.accel.z_raw,
                           imu.gyro.x_raw,
                           imu.gyro.y_raw,
                           imu.gyro.z_raw,
                           (long)imu.gyro.x_dps,
                           (long)imu.gyro.y_dps,
                           (long)imu.gyro.z_dps);
                }
            }
            else if ((xTaskGetTickCount() - last_print_tick) >= pdMS_TO_TICKS(500))
            {
                last_print_tick = xTaskGetTickCount();
                chip_id = IMU_SERVICE_ReadChipId();
                printf("[IMU] Read failed, rslt=%d, chip_id=0x%02X\r\n", read_rslt, chip_id);
            }
        }
        else if ((xTaskGetTickCount() - last_print_tick) >= pdMS_TO_TICKS(1000))
        {
            last_print_tick = xTaskGetTickCount();
            chip_id = IMU_SERVICE_ReadChipId();
            printf("[IMU] Not ready, chip_id=0x%02X\r\n", chip_id);
        }

        vTaskDelay(pdMS_TO_TICKS(1));
    }
}
