#include "attitude_est.h"       /* 引入姿态解算模块的公开类型和函数声明 */

#include <math.h>               /* 使用 sqrtf/atan2f 等数学函数 */
#include <string.h>             /* 使用 memset 清空结构体 */

#ifndef M_PI                    /* 有些编译器的 math.h 不一定定义 M_PI */
#define M_PI 3.14159265358979323846f  /* 定义圆周率，后面用于角度/弧度换算 */
#endif

#define RAD_TO_DEG              (180.0f / M_PI)  /* 弧度转角度的比例系数 */
#define DEFAULT_SAMPLE_RATE_HZ  (100.0f)         /* 默认采样率，单位 Hz */
#define DEFAULT_ACCEL_LPF_ALPHA (0.85f)          /* 默认加速度低通系数，越大越平滑 */
#define DEFAULT_COMP_ALPHA      (0.98f)          /* 默认互补滤波系数，越大越信任陀螺 */
#define DEFAULT_ACCEL_MIN_G     (0.60f)          /* 允许参与修正的最小加速度模长，单位 g */
#define DEFAULT_ACCEL_MAX_G     (1.40f)          /* 允许参与修正的最大加速度模长，单位 g */

/*
 * 函数：clampf
 * 功能：把 value 限制在 min_value 和 max_value 之间。
 * 用途：保护外部配置参数，避免滤波参数超出合理范围。
 */
static float clampf(float value, float min_value, float max_value)
{
    if (value < min_value)       /* 如果输入值小于允许的最小值 */
    {
        return min_value;        /* 返回最小值，防止参数过小 */
    }

    if (value > max_value)       /* 如果输入值大于允许的最大值 */
    {
        return max_value;        /* 返回最大值，防止参数过大 */
    }

    return value;                /* 输入值本来就在范围内，直接返回 */
}

/*
 * 函数：normalize_angle_deg
 * 功能：把角度整理到 -180 deg ~ +180 deg。
 * 原因：姿态角跨越 180/-180 边界时，如果不归一化会出现大跳变。
 */
static float normalize_angle_deg(float angle)
{
    while (angle > 180.0f)       /* 只要角度大于 +180 deg */
    {
        angle -= 360.0f;         /* 减去一圈，使它回到常用角度范围 */
    }

    while (angle < -180.0f)      /* 只要角度小于 -180 deg */
    {
        angle += 360.0f;         /* 加上一圈，使它回到常用角度范围 */
    }

    return angle;                /* 返回归一化后的角度 */
}

/*
 * 函数：angle_error_deg
 * 功能：计算 target 相对 current 的最短角度差。
 * 用途：互补滤波修正 roll/pitch 时，避免 180/-180 附近修正方向错误。
 */
static float angle_error_deg(float target, float current)
{
    return normalize_angle_deg(target - current); /* 先相减，再把误差限制到 -180 ~ +180 */
}

/*
 * 函数：accel_is_valid
 * 功能：判断当前加速度是否接近 1g。
 * 用途：只有加速度主要代表重力时，才用它修正 roll/pitch。
 */
static int accel_is_valid(const AttitudeEstState *state, const Vector3f *accel_g)
{
    float norm;                  /* 保存三轴加速度的模长 */

    norm = sqrtf((accel_g->x * accel_g->x) +  /* 计算 ax^2 */
                 (accel_g->y * accel_g->y) +  /* 加上 ay^2 */
                 (accel_g->z * accel_g->z));  /* 加上 az^2 后开方，得到总加速度模长 */

    return ((norm >= state->accel_min_g) &&   /* 模长不能低于可信下限 */
            (norm <= state->accel_max_g));    /* 模长不能高于可信上限 */
}

/*
 * 函数：accel_lpf_update
 * 功能：对加速度做一阶低通滤波。
 * 公式：y[n] = alpha * y[n-1] + (1 - alpha) * x[n]
 * 用途：减小加速度噪声，让 roll/pitch 修正更平滑。
 */
static void accel_lpf_update(AttitudeEstState *state, const Vector3f *accel_g)
{
    float alpha;                 /* 低通滤波系数，越大越平滑 */
    float one_minus_alpha;       /* 1 - alpha，用于当前新数据的权重 */

    if (state->accel_lpf_ready == 0U)  /* 如果这是第一次进入低通滤波 */
    {
        state->accel_lpf_g = *accel_g; /* 直接把当前加速度作为低通初值 */
        state->accel_lpf_ready = 1U;   /* 标记低通滤波器已经有历史值 */
        return;                        /* 第一次不做滤波计算，直接返回 */
    }

    alpha = state->accel_lpf_alpha;    /* 从状态结构体读取低通系数 */
    one_minus_alpha = 1.0f - alpha;    /* 计算当前采样值的权重 */

    state->accel_lpf_g.x = (alpha * state->accel_lpf_g.x) +       /* X 轴保留上一帧低通值的一部分 */
                           (one_minus_alpha * accel_g->x);        /* X 轴加入当前新采样的一部分 */
    state->accel_lpf_g.y = (alpha * state->accel_lpf_g.y) +       /* Y 轴保留上一帧低通值的一部分 */
                           (one_minus_alpha * accel_g->y);        /* Y 轴加入当前新采样的一部分 */
    state->accel_lpf_g.z = (alpha * state->accel_lpf_g.z) +       /* Z 轴保留上一帧低通值的一部分 */
                           (one_minus_alpha * accel_g->z);        /* Z 轴加入当前新采样的一部分 */
}

/*
 * 函数：accel_to_roll_pitch
 * 功能：根据加速度计测到的重力方向估算 roll 和 pitch。
 * 注意：加速度计无法提供 yaw 修正，因为绕重力方向旋转时重力投影不变。
 */
static void accel_to_roll_pitch(const Vector3f *accel_g, float *roll_deg, float *pitch_deg)
{
    float ay;                    /* 保存 Y 轴加速度，便于公式阅读 */
    float az;                    /* 保存 Z 轴加速度，便于公式阅读 */
    float ax;                    /* 保存 X 轴加速度，便于公式阅读 */

    ax = accel_g->x;             /* 读取 X 轴加速度，单位 g */
    ay = accel_g->y;             /* 读取 Y 轴加速度，单位 g */
    az = accel_g->z;             /* 读取 Z 轴加速度，单位 g */

    *roll_deg = atan2f(ay, az) * RAD_TO_DEG;                         /* 由 Y/Z 重力投影计算 roll，结果转成度 */
    *pitch_deg = atan2f(-ax, sqrtf((ay * ay) + (az * az))) * RAD_TO_DEG; /* 由 X 与 YZ 平面投影计算 pitch，结果转成度 */
}

/*
 * 函数：AttitudeEst_Init
 * 功能：初始化姿态解算状态和滤波参数。
 * 调用位置：线程启动后，进入循环读取 IMU 之前调用一次。
 */
void AttitudeEst_Init(AttitudeEstState *state, const AttitudeEstConfig *config)
{
    float sample_rate_hz;        /* 保存最终使用的采样率 */

    if (state == 0)              /* 如果状态指针为空 */
    {
        return;                  /* 无法初始化，直接返回 */
    }

    memset(state, 0, sizeof(AttitudeEstState)); /* 清空整个状态结构体，避免残留旧数据 */
    Quaternion_Init(&state->q);                 /* 初始化四元数为单位四元数，表示零姿态 */

    sample_rate_hz = DEFAULT_SAMPLE_RATE_HZ;    /* 先使用默认采样率 */
    state->accel_lpf_alpha = DEFAULT_ACCEL_LPF_ALPHA; /* 设置默认加速度低通系数 */
    state->comp_alpha = DEFAULT_COMP_ALPHA;           /* 设置默认互补滤波系数 */
    state->accel_min_g = DEFAULT_ACCEL_MIN_G;         /* 设置默认加速度可信下限 */
    state->accel_max_g = DEFAULT_ACCEL_MAX_G;         /* 设置默认加速度可信上限 */

    if (config != 0)             /* 如果用户传入了配置结构体 */
    {
        if (config->sample_rate_hz > 0.0f)      /* 如果配置的采样率是有效正数 */
        {
            sample_rate_hz = config->sample_rate_hz; /* 使用用户配置的采样率 */
        }

        state->accel_lpf_alpha = clampf(config->accel_lpf_alpha, 0.0f, 0.999f); /* 限制低通系数范围 */
        state->comp_alpha = clampf(config->comp_alpha, 0.0f, 1.0f);             /* 限制互补滤波系数范围 */

        if ((config->accel_min_g > 0.0f) &&                         /* 加速度下限必须大于 0 */
            (config->accel_max_g > config->accel_min_g))            /* 加速度上限必须大于下限 */
        {
            state->accel_min_g = config->accel_min_g;               /* 使用用户配置的可信下限 */
            state->accel_max_g = config->accel_max_g;               /* 使用用户配置的可信上限 */
        }
    }

    state->dt = 1.0f / sample_rate_hz; /* 根据采样率计算默认采样周期，单位秒 */
}

/*
 * 函数：AttitudeEst_Reset
 * 功能：重置当前姿态，但保留滤波参数。
 * 用途：后续可以接按键、串口命令或上位机命令来重新置零姿态。
 */
void AttitudeEst_Reset(AttitudeEstState *state)
{
    float dt;                    /* 暂存原来的采样周期 */
    float accel_lpf_alpha;       /* 暂存原来的加速度低通系数 */
    float comp_alpha;            /* 暂存原来的互补滤波系数 */
    float accel_min_g;           /* 暂存原来的加速度可信下限 */
    float accel_max_g;           /* 暂存原来的加速度可信上限 */

    if (state == 0)              /* 如果状态指针为空 */
    {
        return;                  /* 无法重置，直接返回 */
    }

    dt = state->dt;                         /* 保存当前采样周期 */
    accel_lpf_alpha = state->accel_lpf_alpha; /* 保存当前加速度低通系数 */
    comp_alpha = state->comp_alpha;           /* 保存当前互补滤波系数 */
    accel_min_g = state->accel_min_g;         /* 保存当前加速度可信下限 */
    accel_max_g = state->accel_max_g;         /* 保存当前加速度可信上限 */

    memset(state, 0, sizeof(AttitudeEstState)); /* 清空姿态、欧拉角、低通历史等状态 */
    Quaternion_Init(&state->q);                 /* 重新设置四元数为单位四元数 */

    state->dt = dt;                         /* 恢复采样周期 */
    state->accel_lpf_alpha = accel_lpf_alpha; /* 恢复加速度低通系数 */
    state->comp_alpha = comp_alpha;           /* 恢复互补滤波系数 */
    state->accel_min_g = accel_min_g;         /* 恢复加速度可信下限 */
    state->accel_max_g = accel_max_g;         /* 恢复加速度可信上限 */
}

/*
 * 函数：AttitudeEst_Update
 * 功能：使用 state->dt 作为固定周期更新姿态。
 * 说明：如果采样周期稳定，可以用这个函数；当前线程更推荐用 UpdateDt。
 */
void AttitudeEst_Update(AttitudeEstState *state, const AttitudeIMUData *imu)
{
    if (state == 0)              /* 如果状态指针为空 */
    {
        return;                  /* 无法更新，直接返回 */
    }

    AttitudeEst_UpdateDt(state, imu, state->dt); /* 调用带 dt 的主更新函数，dt 使用状态里的默认值 */
}

/*
 * 函数：AttitudeEst_UpdateDt
 * 功能：姿态解算主函数，每读取一帧 IMU 数据调用一次。
 * 输入：加速度 accel_g，单位 g；陀螺 gyro_dps，单位 deg/s；dt，单位秒。
 */
void AttitudeEst_UpdateDt(AttitudeEstState *state, const AttitudeIMUData *imu, float dt)
{
    EulerAngle gyro_euler;       /* 保存纯陀螺积分得到的欧拉角 */
    EulerAngle fused_euler;      /* 保存互补滤波融合后的欧拉角 */
    float accel_roll;            /* 保存加速度估算的 roll */
    float accel_pitch;           /* 保存加速度估算的 pitch */
    float correction_gain;       /* 保存加速度修正权重，等于 1 - comp_alpha */

    if ((state == 0) || (imu == 0)) /* 如果状态指针或 IMU 数据指针为空 */
    {
        return;                     /* 数据无效，直接返回 */
    }

    if ((dt <= 0.0f) || (dt > 0.1f)) /* 如果 dt 为 0、负数，或者异常大于 100ms */
    {
        dt = state->dt;             /* 使用上一次有效 dt，避免积分突变 */
    }
    else                            /* 如果 dt 是合理的 */
    {
        state->dt = dt;             /* 保存本次有效 dt，供后续回退或固定周期更新使用 */
    }

    Quaternion_UpdateFromGyro(&state->q, &imu->gyro_dps, dt); /* 用三轴角速度积分更新四元数 */
    Quaternion_ToEuler(&state->q, &gyro_euler);               /* 把陀螺积分后的四元数转成欧拉角 */
    fused_euler = gyro_euler;                                 /* 默认融合结果先等于陀螺结果 */

    accel_lpf_update(state, &imu->accel_g);                   /* 对当前加速度做低通滤波 */

    if (accel_is_valid(state, &state->accel_lpf_g) != 0)      /* 如果低通后的加速度模长接近 1g */
    {
        accel_to_roll_pitch(&state->accel_lpf_g,              /* 使用低通后的加速度 */
                            &accel_roll,                     /* 输出加速度估算的 roll */
                            &accel_pitch);                   /* 输出加速度估算的 pitch */

        correction_gain = 1.0f - state->comp_alpha;           /* 计算加速度修正权重 */
        fused_euler.roll = gyro_euler.roll +                  /* 以陀螺积分 roll 为基础 */
                           (correction_gain *                 /* 加上一小部分加速度修正量 */
                            angle_error_deg(accel_roll, gyro_euler.roll)); /* 修正量是加速度 roll 与陀螺 roll 的最短差值 */
        fused_euler.pitch = gyro_euler.pitch +                /* 以陀螺积分 pitch 为基础 */
                            (correction_gain *                /* 加上一小部分加速度修正量 */
                             angle_error_deg(accel_pitch, gyro_euler.pitch)); /* 修正量是加速度 pitch 与陀螺 pitch 的最短差值 */
        fused_euler.yaw = gyro_euler.yaw;                     /* yaw 无法由加速度修正，因此保持陀螺积分值 */

        fused_euler.roll = normalize_angle_deg(fused_euler.roll);   /* 把融合后的 roll 限制到 -180 ~ +180 */
        fused_euler.pitch = normalize_angle_deg(fused_euler.pitch); /* 把融合后的 pitch 限制到 -180 ~ +180 */
        fused_euler.yaw = normalize_angle_deg(fused_euler.yaw);     /* 把融合后的 yaw 限制到 -180 ~ +180 */

        Quaternion_FromEuler(fused_euler.roll,                /* 使用融合后的 roll */
                             fused_euler.pitch,               /* 使用融合后的 pitch */
                             fused_euler.yaw,                 /* 使用陀螺积分得到的 yaw */
                             &state->q);                      /* 重新生成四元数，保证 q 与欧拉角一致 */
    }

    Quaternion_ToEuler(&state->q, &state->euler);             /* 把最终四元数转换成对外输出的欧拉角 */
}

/*
 * 函数：AttitudeEst_GetEuler
 * 功能：获取当前欧拉角输出。
 * 单位：roll/pitch/yaw 都是度。
 */
void AttitudeEst_GetEuler(const AttitudeEstState *state, EulerAngle *euler)
{
    if ((state == 0) || (euler == 0)) /* 如果输入或输出指针为空 */
    {
        return;                       /* 无法拷贝数据，直接返回 */
    }

    *euler = state->euler;            /* 把内部保存的欧拉角拷贝给调用者 */
}

/*
 * 函数：AttitudeEst_GetQuaternion
 * 功能：获取当前四元数输出。
 * 格式：q = w + xi + yj + zk。
 */
void AttitudeEst_GetQuaternion(const AttitudeEstState *state, Quaternion *q)
{
    if ((state == 0) || (q == 0))     /* 如果输入或输出指针为空 */
    {
        return;                       /* 无法拷贝数据，直接返回 */
    }

    *q = state->q;                    /* 把内部保存的四元数拷贝给调用者 */
}
