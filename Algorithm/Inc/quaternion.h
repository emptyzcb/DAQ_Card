#ifndef QUATERNION_H
#define QUATERNION_H

#ifdef __cplusplus
extern "C" {
#endif

//四元数结构体
typedef struct
{
    float w;
    float x;
    float y;
    float z;
} Quaternion;

//欧拉角
typedef struct
{
    float roll;   /* degrees */
    float pitch;  /* degrees */
    float yaw;    /* degrees */
} EulerAngle;


typedef struct
{
    float x;
    float y;
    float z;
} Vector3f;

void Quaternion_Init(Quaternion *q);
void Quaternion_Normalize(Quaternion *q);
void Quaternion_Multiply(const Quaternion *q1, const Quaternion *q2, Quaternion *result);
void Quaternion_Conjugate(const Quaternion *q, Quaternion *result);
void Quaternion_FromAxisAngle(const Vector3f *axis, float angle_deg, Quaternion *q);
void Quaternion_UpdateFromGyro(Quaternion *q, const Vector3f *gyro_dps, float dt);
void Quaternion_ToEuler(const Quaternion *q, EulerAngle *euler);
void Quaternion_FromEuler(float roll_deg, float pitch_deg, float yaw_deg, Quaternion *q);
void Quaternion_RotateVector(const Quaternion *q, const Vector3f *v_in, Vector3f *v_out);

#ifdef __cplusplus
}
#endif

#endif /* QUATERNION_H */
