#include "madgwick.h"

void Madgwick_Init(AttitudeEstState *state, const AttitudeEstConfig *config)
{
    AttitudeEst_Init(state, config);
}

void Madgwick_Update(AttitudeEstState *state, const AttitudeIMUData *imu)
{
    AttitudeEst_Update(state, imu);
}

void Madgwick_UpdateDt(AttitudeEstState *state, const AttitudeIMUData *imu, float dt)
{
    AttitudeEst_UpdateDt(state, imu, dt);
}
