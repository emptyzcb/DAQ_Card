#ifndef MADGWICK_H
#define MADGWICK_H

#include "attitude_est.h"

#ifdef __cplusplus
extern "C" {
#endif

/*
 * Compatibility wrapper kept for the existing Keil project group.
 * The active attitude estimator is implemented in attitude_est.c.
 */
void Madgwick_Init(AttitudeEstState *state, const AttitudeEstConfig *config);
void Madgwick_Update(AttitudeEstState *state, const AttitudeIMUData *imu);
void Madgwick_UpdateDt(AttitudeEstState *state, const AttitudeIMUData *imu, float dt);

#ifdef __cplusplus
}
#endif

#endif /* MADGWICK_H */
