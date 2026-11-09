/**
 * @file    kinematics.h
 * @brief   6轴机械臂正运动学(Forward Kinematics)
 * @note    参数来自URDF(jxs2000.urdf),单位:米/弧度
 *          放到 Core/Inc/ 目录
 */
#ifndef KINEMATICS_H
#define KINEMATICS_H

#include <stdint.h>

/* 关节数量 */
#define ARM_DOF 6

/* 4x4齐次变换矩阵(行主序) */
typedef struct {
    float m[4][4];
} Matrix4f;

/* 3D点 */
typedef struct {
    float x, y, z;
} Vector3f;

/* 位姿:位置+RPY欧拉角(度) */
typedef struct {
    Vector3f position;
    Vector3f rpy;   /* roll, pitch, yaw (度) */
} Pose6D;

/* 6关节角度(弧度) */
typedef struct {
    float j[ARM_DOF];
} Joint6D;

/* ===== 矩阵运算 ===== */
void Matrix_Identity(Matrix4f *out);
void Matrix_Multiply(const Matrix4f *a, const Matrix4f *b, Matrix4f *out);
void Matrix_TransformPoint(const Matrix4f *m, const Vector3f *in, Vector3f *out);

/* ===== 正运动学 ===== */

/**
 * @brief  计算6关节的正运动学
 * @param  joints  6个关节角度(弧度)
 * @param  outT    输出:7个连杆的世界变换矩阵(base_link~link6)
 *                  outT[0]=base_link(单位阵), outT[1]=link1, ..., outT[6]=link6
 */
void FK_Solve(const Joint6D *joints, Matrix4f outT[7]);

/**
 * @brief  计算末端(link6)位姿
 * @param  joints  6个关节角度(弧度)
 * @param  pose    输出:末端位姿
 */
void FK_GetEndPose(const Joint6D *joints, Pose6D *pose);

/**
 * @brief  获取指定连杆的原点在世界坐标系的坐标
 * @param  linkT   FK_Solve输出的变换矩阵数组
 * @param  linkIdx 连杆索引(0=base, 6=link6/末端)
 * @retval 连杆原点世界坐标
 */
Vector3f FK_GetLinkOrigin(const Matrix4f linkT[7], int linkIdx);

#endif /* KINEMATICS_H */
