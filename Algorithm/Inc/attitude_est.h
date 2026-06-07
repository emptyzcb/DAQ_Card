#ifndef ATTITUDE_EST_H
#define ATTITUDE_EST_H

#include "quaternion.h"

#ifdef __cplusplus
extern "C" {
#endif

typedef struct
{
    Vector3f accel_g;     /* acceleration in g */
    Vector3f gyro_dps;    /* angular rate in degrees/second */
} AttitudeIMUData;

typedef struct
{
    float sample_rate_hz;     /* fallback update rate when no dt is supplied */
    float accel_lpf_alpha;    /* 0..1, higher means smoother accel vector */
    float comp_alpha;         /* 0..1, higher trusts gyro more */
    float accel_min_g;        /* lower valid accel norm for correction */
    float accel_max_g;        /* upper valid accel norm for correction */
} AttitudeEstConfig;

typedef struct
{
    Quaternion q;
    EulerAngle euler;
    Vector3f accel_lpf_g;
    float dt;
    float accel_lpf_alpha;
    float comp_alpha;
    float accel_min_g;
    float accel_max_g;
    unsigned char accel_lpf_ready;
} AttitudeEstState;

void AttitudeEst_Init(AttitudeEstState *state, const AttitudeEstConfig *config);
void AttitudeEst_Reset(AttitudeEstState *state);
void AttitudeEst_Update(AttitudeEstState *state, const AttitudeIMUData *imu);
void AttitudeEst_UpdateDt(AttitudeEstState *state, const AttitudeIMUData *imu, float dt);
void AttitudeEst_GetEuler(const AttitudeEstState *state, EulerAngle *euler);
void AttitudeEst_GetQuaternion(const AttitudeEstState *state, Quaternion *q);

/* Backward-compatible alias for older code. */
typedef AttitudeIMUData CalibratedIMU;

#ifdef __cplusplus
}
#endif

#endif /* ATTITUDE_EST_H */
