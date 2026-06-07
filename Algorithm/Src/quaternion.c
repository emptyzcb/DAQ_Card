#include "quaternion.h"         /* 引入四元数、欧拉角、三维向量等类型定义 */

#include <math.h>               /* 使用 sqrtf/sinf/cosf/atan2f/asinf 等数学函数 */

#ifndef M_PI                    /* 有些 C 库不会默认定义 M_PI */
#define M_PI 3.14159265358979323846f  /* 定义圆周率，用于角度和弧度换算 */
#endif

#define DEG_TO_RAD (M_PI / 180.0f)    /* 角度转弧度的比例系数 */
#define RAD_TO_DEG (180.0f / M_PI)    /* 弧度转角度的比例系数 */

/*
 * 函数：Quaternion_Init
 * 功能：初始化为单位四元数。
 * 含义：单位四元数表示“没有旋转”，也就是初始姿态。
 */
void Quaternion_Init(Quaternion *q)
{
    if (q == 0)                  /* 如果传入的四元数指针为空 */
    {
        return;                  /* 无法写入数据，直接返回 */
    }

    q->w = 1.0f;                 /* 实部设置为 1 */
    q->x = 0.0f;                 /* X 虚部设置为 0 */
    q->y = 0.0f;                 /* Y 虚部设置为 0 */
    q->z = 0.0f;                 /* Z 虚部设置为 0 */
}

/*
 * 函数：Quaternion_Normalize
 * 功能：四元数归一化。
 * 原因：姿态积分和浮点计算会产生误差，四元数长度会慢慢偏离 1。
 * 结果：归一化后保持为单位四元数，才能正确表示旋转。
 */
void Quaternion_Normalize(Quaternion *q)
{
    float norm;                  /* 保存四元数模长 */
    float inv_norm;              /* 保存模长倒数，用乘法替代除法 */

    if (q == 0)                  /* 如果四元数指针为空 */
    {
        return;                  /* 无法处理，直接返回 */
    }

    norm = sqrtf((q->w * q->w) + /* 计算 w^2 */
                 (q->x * q->x) + /* 加上 x^2 */
                 (q->y * q->y) + /* 加上 y^2 */
                 (q->z * q->z)); /* 加上 z^2 后开方，得到四元数长度 */
    if (norm <= 1.0e-6f)         /* 如果模长太小，说明四元数已经异常 */
    {
        Quaternion_Init(q);      /* 直接恢复成单位四元数 */
        return;                  /* 避免后面除以接近 0 的数 */
    }

    inv_norm = 1.0f / norm;      /* 计算模长倒数 */
    q->w *= inv_norm;            /* w 除以模长 */
    q->x *= inv_norm;            /* x 除以模长 */
    q->y *= inv_norm;            /* y 除以模长 */
    q->z *= inv_norm;            /* z 除以模长 */
}

/*
 * 函数：Quaternion_Multiply
 * 功能：四元数乘法。
 * 用途：组合两个旋转，结果等价于先执行 q2，再执行 q1。
 */
void Quaternion_Multiply(const Quaternion *q1, const Quaternion *q2, Quaternion *result)
{
    Quaternion out;              /* 临时保存乘法结果，避免 result 和输入指针重叠导致计算被覆盖 */

    if ((q1 == 0) || (q2 == 0) || (result == 0)) /* 任意输入或输出指针为空 */
    {
        return;                                  /* 无法计算，直接返回 */
    }

    out.w = (q1->w * q2->w) -    /* 实部：w1*w2 */
            (q1->x * q2->x) -    /* 减去 x1*x2 */
            (q1->y * q2->y) -    /* 减去 y1*y2 */
            (q1->z * q2->z);     /* 减去 z1*z2 */
    out.x = (q1->w * q2->x) +    /* X 虚部：w1*x2 */
            (q1->x * q2->w) +    /* 加 x1*w2 */
            (q1->y * q2->z) -    /* 加 y1*z2 */
            (q1->z * q2->y);     /* 减 z1*y2 */
    out.y = (q1->w * q2->y) -    /* Y 虚部：w1*y2 */
            (q1->x * q2->z) +    /* 减 x1*z2 */
            (q1->y * q2->w) +    /* 加 y1*w2 */
            (q1->z * q2->x);     /* 加 z1*x2 */
    out.z = (q1->w * q2->z) +    /* Z 虚部：w1*z2 */
            (q1->x * q2->y) -    /* 加 x1*y2 */
            (q1->y * q2->x) +    /* 减 y1*x2 */
            (q1->z * q2->w);     /* 加 z1*w2 */

    *result = out;               /* 把临时结果写入输出变量 */
}

/*
 * 函数：Quaternion_Conjugate
 * 功能：求四元数共轭。
 * 含义：对于单位四元数，共轭等价于反向旋转。
 */
void Quaternion_Conjugate(const Quaternion *q, Quaternion *result)
{
    if ((q == 0) || (result == 0)) /* 如果输入或输出指针为空 */
    {
        return;                    /* 无法计算，直接返回 */
    }

    result->w = q->w;             /* 共轭不改变实部 w */
    result->x = -q->x;            /* 共轭会把 X 虚部取反 */
    result->y = -q->y;            /* 共轭会把 Y 虚部取反 */
    result->z = -q->z;            /* 共轭会把 Z 虚部取反 */
}

/*
 * 函数：Quaternion_FromAxisAngle
 * 功能：由“旋转轴 + 旋转角度”生成四元数。
 * 用途：如果知道绕某个方向转了多少度，可以用这个函数构造姿态增量。
 */
void Quaternion_FromAxisAngle(const Vector3f *axis, float angle_deg, Quaternion *q)
{
    float norm;                  /* 保存旋转轴长度 */
    float inv_norm;              /* 保存旋转轴长度倒数，用于归一化旋转轴 */
    float half_angle;            /* 保存半角，四元数公式使用 angle / 2 */
    float sin_half;              /* 保存 sin(angle / 2) */

    if ((axis == 0) || (q == 0))  /* 如果旋转轴或输出四元数指针为空 */
    {
        return;                  /* 无法生成四元数，直接返回 */
    }

    norm = sqrtf((axis->x * axis->x) + /* 计算旋转轴 x^2 */
                 (axis->y * axis->y) + /* 加上旋转轴 y^2 */
                 (axis->z * axis->z)); /* 加上旋转轴 z^2 后开方，得到轴长度 */
    if (norm <= 1.0e-6f)         /* 如果旋转轴长度太小，方向无意义 */
    {
        Quaternion_Init(q);      /* 输出单位四元数，表示不旋转 */
        return;                  /* 避免除以接近 0 的数 */
    }

    inv_norm = 1.0f / norm;      /* 计算旋转轴长度倒数 */
    half_angle = angle_deg * DEG_TO_RAD * 0.5f; /* 把角度转弧度，并取一半 */
    sin_half = sinf(half_angle); /* 计算半角正弦，用于虚部 */

    q->w = cosf(half_angle);     /* 实部为 cos(angle / 2) */
    q->x = axis->x * inv_norm * sin_half; /* X 虚部 = 单位轴 X * sin(angle / 2) */
    q->y = axis->y * inv_norm * sin_half; /* Y 虚部 = 单位轴 Y * sin(angle / 2) */
    q->z = axis->z * inv_norm * sin_half; /* Z 虚部 = 单位轴 Z * sin(angle / 2) */
    Quaternion_Normalize(q);     /* 归一化，消除浮点计算误差 */
}

/*
 * 函数：Quaternion_UpdateFromGyro
 * 功能：根据陀螺仪角速度积分更新四元数。
 * 输入：gyro_dps 单位是 deg/s，dt 单位是秒。
 * 作用：这是姿态解算里“陀螺预测”的核心。
 */
void Quaternion_UpdateFromGyro(Quaternion *q, const Vector3f *gyro_dps, float dt)
{
    float gx;                    /* X 轴角速度，单位 rad/s */
    float gy;                    /* Y 轴角速度，单位 rad/s */
    float gz;                    /* Z 轴角速度，单位 rad/s */
    float qw;                    /* 暂存更新前的 w */
    float qx;                    /* 暂存更新前的 x */
    float qy;                    /* 暂存更新前的 y */
    float qz;                    /* 暂存更新前的 z */
    float half_dt;               /* 保存 0.5 * dt，四元数微分公式中会用到 */

    if ((q == 0) || (gyro_dps == 0) || (dt <= 0.0f)) /* 如果指针为空或 dt 非法 */
    {
        return;                                      /* 无法积分，直接返回 */
    }

    gx = gyro_dps->x * DEG_TO_RAD; /* 把 X 轴角速度从 deg/s 转成 rad/s */
    gy = gyro_dps->y * DEG_TO_RAD; /* 把 Y 轴角速度从 deg/s 转成 rad/s */
    gz = gyro_dps->z * DEG_TO_RAD; /* 把 Z 轴角速度从 deg/s 转成 rad/s */

    qw = q->w;                    /* 暂存原始 w，避免更新 q->w 后影响后续计算 */
    qx = q->x;                    /* 暂存原始 x */
    qy = q->y;                    /* 暂存原始 y */
    qz = q->z;                    /* 暂存原始 z */
    half_dt = 0.5f * dt;          /* 计算半个采样周期 */

    q->w += half_dt * ((-qx * gx) - (qy * gy) - (qz * gz)); /* 积分更新 w 分量 */
    q->x += half_dt * (( qw * gx) + (qy * gz) - (qz * gy)); /* 积分更新 x 分量 */
    q->y += half_dt * (( qw * gy) - (qx * gz) + (qz * gx)); /* 积分更新 y 分量 */
    q->z += half_dt * (( qw * gz) + (qx * gy) - (qy * gx)); /* 积分更新 z 分量 */

    Quaternion_Normalize(q);      /* 每次积分后归一化，防止四元数长度漂移 */
}

/*
 * 函数：Quaternion_ToEuler
 * 功能：四元数转换为欧拉角。
 * 输出：roll/pitch/yaw，单位是 deg。
 */
void Quaternion_ToEuler(const Quaternion *q, EulerAngle *euler)
{
    float qw;                    /* 暂存四元数 w */
    float qx;                    /* 暂存四元数 x */
    float qy;                    /* 暂存四元数 y */
    float qz;                    /* 暂存四元数 z */
    float sinr_cosp;             /* roll 公式中的分子项 */
    float cosr_cosp;             /* roll 公式中的分母项 */
    float sinp;                  /* pitch 公式中的正弦值 */
    float siny_cosp;             /* yaw 公式中的分子项 */
    float cosy_cosp;             /* yaw 公式中的分母项 */

    if ((q == 0) || (euler == 0)) /* 如果输入或输出指针为空 */
    {
        return;                  /* 无法转换，直接返回 */
    }

    qw = q->w;                   /* 读取 w 分量 */
    qx = q->x;                   /* 读取 x 分量 */
    qy = q->y;                   /* 读取 y 分量 */
    qz = q->z;                   /* 读取 z 分量 */

    sinr_cosp = 2.0f * ((qw * qx) + (qy * qz));          /* 计算 roll 的 atan2 分子 */
    cosr_cosp = 1.0f - (2.0f * ((qx * qx) + (qy * qy))); /* 计算 roll 的 atan2 分母 */
    euler->roll = atan2f(sinr_cosp, cosr_cosp) * RAD_TO_DEG; /* 计算 roll 并从弧度转成角度 */

    sinp = 2.0f * ((qw * qy) - (qz * qx)); /* 计算 pitch 的 sin 值 */
    if (sinp > 1.0f)                      /* 浮点误差可能让 sinp 略大于 1 */
    {
        sinp = 1.0f;                      /* 限制到 asin 合法输入范围 */
    }
    else if (sinp < -1.0f)                /* 浮点误差可能让 sinp 略小于 -1 */
    {
        sinp = -1.0f;                     /* 限制到 asin 合法输入范围 */
    }
    euler->pitch = asinf(sinp) * RAD_TO_DEG; /* 计算 pitch 并从弧度转成角度 */

    siny_cosp = 2.0f * ((qw * qz) + (qx * qy));          /* 计算 yaw 的 atan2 分子 */
    cosy_cosp = 1.0f - (2.0f * ((qy * qy) + (qz * qz))); /* 计算 yaw 的 atan2 分母 */
    euler->yaw = atan2f(siny_cosp, cosy_cosp) * RAD_TO_DEG; /* 计算 yaw 并从弧度转成角度 */
}

/*
 * 函数：Quaternion_FromEuler
 * 功能：欧拉角转换为四元数。
 * 输入：roll/pitch/yaw，单位是 deg。
 * 用途：互补滤波得到欧拉角后，用它重新生成一致的四元数。
 */
void Quaternion_FromEuler(float roll_deg, float pitch_deg, float yaw_deg, Quaternion *q)
{
    float roll;                  /* roll 半角，单位 rad */
    float pitch;                 /* pitch 半角，单位 rad */
    float yaw;                   /* yaw 半角，单位 rad */
    float cr;                    /* cos(roll / 2) */
    float sr;                    /* sin(roll / 2) */
    float cp;                    /* cos(pitch / 2) */
    float sp;                    /* sin(pitch / 2) */
    float cy;                    /* cos(yaw / 2) */
    float sy;                    /* sin(yaw / 2) */

    if (q == 0)                  /* 如果输出四元数指针为空 */
    {
        return;                  /* 无法写入结果，直接返回 */
    }

    roll = roll_deg * DEG_TO_RAD * 0.5f;   /* roll 从角度转成弧度并取半角 */
    pitch = pitch_deg * DEG_TO_RAD * 0.5f; /* pitch 从角度转成弧度并取半角 */
    yaw = yaw_deg * DEG_TO_RAD * 0.5f;     /* yaw 从角度转成弧度并取半角 */

    cr = cosf(roll);             /* 计算 cos(roll / 2) */
    sr = sinf(roll);             /* 计算 sin(roll / 2) */
    cp = cosf(pitch);            /* 计算 cos(pitch / 2) */
    sp = sinf(pitch);            /* 计算 sin(pitch / 2) */
    cy = cosf(yaw);              /* 计算 cos(yaw / 2) */
    sy = sinf(yaw);              /* 计算 sin(yaw / 2) */

    q->w = (cr * cp * cy) + (sr * sp * sy); /* 根据 ZYX 欧拉角顺序计算 w */
    q->x = (sr * cp * cy) - (cr * sp * sy); /* 根据 ZYX 欧拉角顺序计算 x */
    q->y = (cr * sp * cy) + (sr * cp * sy); /* 根据 ZYX 欧拉角顺序计算 y */
    q->z = (cr * cp * sy) - (sr * sp * cy); /* 根据 ZYX 欧拉角顺序计算 z */
    Quaternion_Normalize(q);      /* 归一化，消除三角函数和浮点计算误差 */
}

/*
 * 函数：Quaternion_RotateVector
 * 功能：使用四元数旋转一个三维向量。
 * 用途：后续可用于机体系/世界系坐标变换，例如把机体系加速度转到世界系。
 */
void Quaternion_RotateVector(const Quaternion *q, const Vector3f *v_in, Vector3f *v_out)
{
    float qw;                    /* 暂存四元数 w */
    float qx;                    /* 暂存四元数 x */
    float qy;                    /* 暂存四元数 y */
    float qz;                    /* 暂存四元数 z */
    float qx2;                   /* 保存 x^2，减少重复计算 */
    float qy2;                   /* 保存 y^2，减少重复计算 */
    float qz2;                   /* 保存 z^2，减少重复计算 */

    if ((q == 0) || (v_in == 0) || (v_out == 0)) /* 如果四元数、输入向量或输出向量为空 */
    {
        return;                                  /* 无法旋转，直接返回 */
    }

    qw = q->w;                   /* 读取 w 分量 */
    qx = q->x;                   /* 读取 x 分量 */
    qy = q->y;                   /* 读取 y 分量 */
    qz = q->z;                   /* 读取 z 分量 */
    qx2 = qx * qx;               /* 计算 x^2 */
    qy2 = qy * qy;               /* 计算 y^2 */
    qz2 = qz * qz;               /* 计算 z^2 */

    v_out->x = (v_in->x * (1.0f - (2.0f * (qy2 + qz2)))) +          /* 输入 X 对输出 X 的贡献 */
               (v_in->y * (2.0f * ((qx * qy) - (qw * qz)))) +      /* 输入 Y 对输出 X 的贡献 */
               (v_in->z * (2.0f * ((qx * qz) + (qw * qy))));       /* 输入 Z 对输出 X 的贡献 */

    v_out->y = (v_in->x * (2.0f * ((qx * qy) + (qw * qz)))) +      /* 输入 X 对输出 Y 的贡献 */
               (v_in->y * (1.0f - (2.0f * (qx2 + qz2)))) +         /* 输入 Y 对输出 Y 的贡献 */
               (v_in->z * (2.0f * ((qy * qz) - (qw * qx))));       /* 输入 Z 对输出 Y 的贡献 */

    v_out->z = (v_in->x * (2.0f * ((qx * qz) - (qw * qy)))) +      /* 输入 X 对输出 Z 的贡献 */
               (v_in->y * (2.0f * ((qy * qz) + (qw * qx)))) +      /* 输入 Y 对输出 Z 的贡献 */
               (v_in->z * (1.0f - (2.0f * (qx2 + qy2))));          /* 输入 Z 对输出 Z 的贡献 */
}
