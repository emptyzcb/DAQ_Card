#include "sys.h"                /* 引入系统总头文件，里面包含 FreeRTOS、IMU 服务、姿态算法等头文件 */

/*
 * 函数：Task_att_est
 * 功能：姿态估计线程。
 * 线程职责：
 *   1. 初始化 BMI270。
 *   2. 周期读取加速度和陀螺仪。
 *   3. 把传感器数据送入姿态解算算法。
 *   4. 在算法状态中持续更新欧拉角和四元数。
 */
void Task_att_est(void *arg)
{
    IMU_SERVICE_6AxisData imu;   /* 保存 IMU 服务层读取到的 6 轴数据，包含 raw/g/dps */
    AttitudeEstConfig att_cfg;   /* 姿态解算配置参数，例如采样率和滤波系数 */
    AttitudeEstState att_state;  /* 姿态解算内部状态，例如四元数、欧拉角、低通滤波历史 */
    AttitudeIMUData att_imu;     /* 算法层输入数据，只保留平台无关的 accel_g 和 gyro_dps */
    EulerAngle euler;            /* 保存当前欧拉角，随后写入调试/共享缓存 */
    Quaternion quat;             /* 保存当前四元数，随后写入调试/共享缓存 */
    DataHubImuData hub_imu;      /* 发布到 DataHub 的 IMU/姿态快照 */
    int read_rslt;               /* 保存每次读取 IMU 的返回值 */
    TickType_t last_update_tick; /* 上一次姿态算法更新时的 FreeRTOS tick */
    TickType_t now_tick;         /* 当前 FreeRTOS tick */
    float dt;                    /* 两次姿态更新之间的时间间隔，单位 s */

    (void)arg;                   /* 当前线程暂时不使用传入参数，避免编译器未使用警告 */
    (void)IMU_SERVICE_Init();    /* 初始化 IMU 服务层，内部会初始化 BMI270 */

    last_update_tick = xTaskGetTickCount(); /* 姿态更新 tick 从当前时间开始 */

    att_cfg.sample_rate_hz = 100.0f;        /* 设置默认采样率 100Hz，用于 dt 异常时回退 */
    att_cfg.accel_lpf_alpha = 0.85f;        /* 设置加速度低通系数，越大越平滑 */
    att_cfg.comp_alpha = 0.98f;             /* 设置互补滤波系数，越大越信任陀螺 */
    att_cfg.accel_min_g = 0.60f;            /* 加速度模长低于 0.60g 时不参与姿态修正 */
    att_cfg.accel_max_g = 1.40f;            /* 加速度模长高于 1.40g 时不参与姿态修正 */
    AttitudeEst_Init(&att_state, &att_cfg); /* 初始化姿态解算器状态 */

    while (1)                               /* FreeRTOS 任务主循环，线程不会主动退出 */
    {
        if (IMU_SERVICE_IsReady() != 0)     /* 如果 IMU 服务已经初始化成功，可以读取数据 */
        {
            DataHub_SetImuReady(1);         /* 记录 IMU 当前处于 ready 状态 */
            read_rslt = IMU_SERVICE_Read6Axis(&imu); /* 读取一帧加速度和陀螺仪数据 */
            DataHub_SetImuReadResult(read_rslt); /* 保存最近一次读取结果，方便调试观察 */
            if (read_rslt == 0)             /* 返回 0 表示读取成功 */
            {
                now_tick = xTaskGetTickCount(); /* 获取当前 FreeRTOS tick */

                if (now_tick != last_update_tick) /* tick 前进后再更新姿态，避免 dt=0 导致积分时间异常 */
                {
                    dt = (float)(now_tick - last_update_tick) / (float)configTICK_RATE_HZ; /* tick 差值换算成秒 */
                    last_update_tick = now_tick;    /* 更新上一帧 tick，为下一次计算 dt 做准备 */

                    att_imu.accel_g.x = imu.accel.x_g;   /* 把 BSP 换算好的 X 轴加速度 g 填给算法层 */
                    att_imu.accel_g.y = imu.accel.y_g;   /* 把 BSP 换算好的 Y 轴加速度 g 填给算法层 */
                    att_imu.accel_g.z = imu.accel.z_g;   /* 把 BSP 换算好的 Z 轴加速度 g 填给算法层 */
                    att_imu.gyro_dps.x = imu.gyro.x_dps; /* 把 BSP 换算好的 X 轴角速度 deg/s 填给算法层 */
                    att_imu.gyro_dps.y = imu.gyro.y_dps; /* 把 BSP 换算好的 Y 轴角速度 deg/s 填给算法层 */
                    att_imu.gyro_dps.z = imu.gyro.z_dps; /* 把 BSP 换算好的 Z 轴角速度 deg/s 填给算法层 */
                    AttitudeEst_UpdateDt(&att_state, &att_imu, dt); /* 用当前 IMU 数据和 dt 更新姿态 */

                    AttitudeEst_GetEuler(&att_state, &euler);       /* 取出当前欧拉角 */
                    AttitudeEst_GetQuaternion(&att_state, &quat);   /* 取出当前四元数 */

                    hub_imu.timestamp_tick = (uint32_t)now_tick;    /* 保存当前数据对应的 tick */
                    hub_imu.dt = dt;                                /* 保存本次姿态更新使用的 dt */
                    hub_imu.last_read_rslt = read_rslt;             /* 保存最近一次读取结果 */
                    hub_imu.imu_ready = 1;                          /* 保存 IMU ready 状态 */
                    
                    hub_imu.accel_raw_x = imu.accel.x_raw;          /* 保存 X 轴加速度原始值 */
                    hub_imu.accel_raw_y = imu.accel.y_raw;          /* 保存 Y 轴加速度原始值 */
                    hub_imu.accel_raw_z = imu.accel.z_raw;          /* 保存 Z 轴加速度原始值 */
                    hub_imu.gyro_raw_x = imu.gyro.x_raw;            /* 保存 X 轴陀螺仪原始值 */
                    hub_imu.gyro_raw_y = imu.gyro.y_raw;            /* 保存 Y 轴陀螺仪原始值 */
                    hub_imu.gyro_raw_z = imu.gyro.z_raw;            /* 保存 Z 轴陀螺仪原始值 */
                    
                    hub_imu.accel_g_x = imu.accel.x_g;              /* 保存 X 轴加速度物理量 */
                    hub_imu.accel_g_y = imu.accel.y_g;              /* 保存 Y 轴加速度物理量 */
                    hub_imu.accel_g_z = imu.accel.z_g;              /* 保存 Z 轴加速度物理量 */
                    hub_imu.gyro_dps_x = imu.gyro.x_dps;            /* 保存 X 轴陀螺仪物理量 */
                    hub_imu.gyro_dps_y = imu.gyro.y_dps;            /* 保存 Y 轴陀螺仪物理量 */
                    hub_imu.gyro_dps_z = imu.gyro.z_dps;            /* 保存 Z 轴陀螺仪物理量 */
                    
                    hub_imu.euler = euler;                          /* 保存欧拉角，方便调试显示 */
                    hub_imu.quat = quat;                            /* 保存四元数，供后续 RS485/SLAM 使用 */
                    DataHub_PublishImu(&hub_imu);                   /* 发布完整 IMU/姿态快照到 DataHub */
                }
            }
        }
        else
        {
            DataHub_SetImuReady(0);         /* 记录 IMU 当前未 ready */
        }

        vTaskDelay(pdMS_TO_TICKS(1)); /* 线程延时 1ms，让出 CPU，同时控制读取循环频率 */
    }
}
