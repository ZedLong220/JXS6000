/**
 * @file    kinematics.c
 * @brief   6轴机械臂正运动学(Forward Kinematics)实现
 * @note    关节参数来自URDF文件(jxs2000.urdf)
 *          放到 Core/Src/ 目录
 *
 * 正运动学(FK)的作用:
 *   已知6个关节的角度,计算末端执行器(夹爪)在空间中的位置和姿态。
 *   原理:每个关节产生一个旋转+平移变换,6个关节的变换逐级连乘,
 *   得到从基座到末端的总变换矩阵。
 *
 * 数学工具:4x4齐次变换矩阵
 *   [ R11 R12 R13 Tx ]   R:3x3旋转矩阵(描述姿态)
 *   [ R21 R22 R23 Ty ]   T:3x1平移向量(描述位置)
 *   [ R31 R32 R33 Tz ]
 *   [  0   0   0   1 ]   最后一行固定(齐次坐标)
 */
#include "kinematics.h"
#include <math.h>        /* sinf, cosf, sqrtf, atan2f 等数学函数 */

#ifndef M_PI
#define M_PI 3.14159265358979323846f   /* 圆周率(浮点版) */
#endif

/* ===== URDF关节参数(单位:米) ===== */
/* 每个关节由两部分描述:
 * - origin: 子连杆坐标系原点在父连杆坐标系中的位置(xyz偏移)
 * - axis:   关节旋转轴的方向向量(需归一化为单位向量)
 */
typedef struct {
    float ox, oy, oz;       /* origin xyz (米):关节平移量 */
    float ax, ay, az;       /* axis方向向量(需归一化):旋转轴 */
} JointParam;

/* 6个关节的参数表,直接从URDF的<joint>标签提取
 * 对应关系:
 *   <origin xyz="ox oy oz"/>  -> ox, oy, oz
 *   <axis xyz="ax ay az"/>    -> ax, ay, az
 */
static const JointParam jointParams[ARM_DOF] = {
    /* J1: 基座旋转,绕Z轴(竖直方向)
     * origin(0, 0.0022, 0.1745): link1原点在base上方0.1745m处 */
    { 0.0000f, 0.0022f, 0.1745f,   0.0f, 0.0f, 1.0f },
    /* J2: 肩部俯仰。当前用户角度正方向对应真实机构向上,
     * 因此在URDF +Y轴基础上按用户约定取反为-Y轴。 */
    { 0.0004f, -0.0975f, 0.0857f,  0.0f, -1.0f, 0.0f },
    /* J3: 肘部俯仰,绕Y轴
     * origin(0.0804, 0.0128, 0.4388): link3相对link2偏移 */
    { 0.0804f, 0.0128f, 0.4388f,  0.0f, 1.0f, 0.0f },
    /* J4: 腕部滚转,绕X轴 */
    { 0.1588f, 0.0818f, -0.4331f,  1.0f, 0.0f, 0.0f },
    /* J5: 腕部俯仰,绕Y轴
     * origin(0.0875, -0.0942, 0.0127): link5相对link4偏移 */
    { 0.0875f, -0.0942f, 0.0127f,   0.0f, 1.0f, 0.0f },
    /* J6: 末端滚转,绕X轴 */
    { 0.0883f, -0.0612f, 0.0062f,   1.0f, 0.0f, 0.0f },
};

/* ===== 矩阵运算 ===== */

/**
 * @brief  生成4x4单位矩阵(对角线为1,其余为0)
 *         单位矩阵乘任何矩阵=原矩阵本身,作为连乘的初始值
 */
void Matrix_Identity(Matrix4f *out)
{
    int i, j;
    for (i = 0; i < 4; i++)
        for (j = 0; j < 4; j++)
            out->m[i][j] = (i == j) ? 1.0f : 0.0f;  /* 对角线=1,其他=0 */
}

/**
 * @brief  4x4矩阵乘法: out = a * b
 *         注意:用临时变量tmp暂存结果,避免out和a/b重叠时出错
 */
void Matrix_Multiply(const Matrix4f *a, const Matrix4f *b, Matrix4f *out)
{
    Matrix4f tmp;   /* 临时变量,防止out==a或out==b时覆盖 */
    int i, j, k;
    for (i = 0; i < 4; i++) {
        for (j = 0; j < 4; j++) {
            tmp.m[i][j] = 0.0f;
            /* 矩阵乘法:行x列求和 */
            for (k = 0; k < 4; k++) {
                tmp.m[i][j] += a->m[i][k] * b->m[k][j];
            }
        }
    }
    *out = tmp;     /* 一次性拷贝结果 */
}

/**
 * @brief  用4x4变换矩阵变换一个3D点
 *         即: 点的新坐标 = 矩阵 * 点的齐次坐标
 *         用于将连杆本地坐标系的点转换到世界坐标系
 * @param  m    变换矩阵
 * @param  in   输入点(本地坐标)
 * @param  out  输出点(世界坐标)
 */
void Matrix_TransformPoint(const Matrix4f *m, const Vector3f *in, Vector3f *out)
{
    /* x' = m[0][0]*x + m[0][1]*y + m[0][2]*z + m[0][3](平移) */
    float x = m->m[0][0]*in->x + m->m[0][1]*in->y + m->m[0][2]*in->z + m->m[0][3];
    float y = m->m[1][0]*in->x + m->m[1][1]*in->y + m->m[1][2]*in->z + m->m[1][3];
    float z = m->m[2][0]*in->x + m->m[2][1]*in->y + m->m[2][2]*in->z + m->m[2][3];
    out->x = x;
    out->y = y;
    out->z = z;
}

/* ===== 正运动学 ===== */

/**
 * @brief  构造单个关节的4x4变换矩阵
 *         包含:绕任意轴旋转(Rodrigues公式) + origin平移
 * @param  jp    关节参数(origin + axis)
 * @param  angle 关节角度(弧度)
 * @param  out   输出的4x4变换矩阵
 *
 * Rodrigues旋转公式:
 *   绕单位向量 (ax,ay,az) 旋转 angle 弧度的旋转矩阵:
 *   R = I*cos + (1-cos)*[a*a^T] + sin*[a]_x
 *   其中 [a*a^T] 是外积矩阵, [a]_x 是反对称矩阵
 */
static void BuildJointTransform(const JointParam *jp, float angle, Matrix4f *out)
{
    float c = cosf(angle);   /* cos(theta) */
    float s = sinf(angle);   /* sin(theta) */
    float t = 1.0f - c;      /* 1 - cos(theta) */

    /* axis归一化(URDF的axis应该是归一化的,但保险起见再归一次) */
    float len = sqrtf(jp->ax*jp->ax + jp->ay*jp->ay + jp->az*jp->az);
    float ax = jp->ax / len;  /* 归一化后的轴向量分量 */
    float ay = jp->ay / len;
    float az = jp->az / len;

    /* 旋转部分(Rodrigues公式) + 平移部分(origin)
     * 矩阵布局:
     * [ R11 R12 R13 | ox ]
     * [ R21 R22 R23 | oy ]
     * [ R31 R32 R33 | oz ]
     * [  0   0   0  |  1 ]
     */
    out->m[0][0] = t*ax*ax + c;       /* R11 */
    out->m[0][1] = t*ax*ay - s*az;    /* R12 */
    out->m[0][2] = t*ax*az + s*ay;    /* R13 */
    out->m[0][3] = jp->ox;            /* 平移Tx */

    out->m[1][0] = t*ax*ay + s*az;    /* R21 */
    out->m[1][1] = t*ay*ay + c;       /* R22 */
    out->m[1][2] = t*ay*az - s*ax;    /* R23 */
    out->m[1][3] = jp->oy;            /* 平移Ty */

    out->m[2][0] = t*ax*az - s*ay;    /* R31 */
    out->m[2][1] = t*ay*az + s*ax;    /* R32 */
    out->m[2][2] = t*az*az + c;       /* R33 */
    out->m[2][3] = jp->oz;            /* 平移Tz */

    /* 齐次坐标最后一行固定 */
    out->m[3][0] = 0.0f;
    out->m[3][1] = 0.0f;
    out->m[3][2] = 0.0f;
    out->m[3][3] = 1.0f;
}

/**
 * @brief  正运动学求解:计算所有连杆的世界变换矩阵
 * @param  joints  6个关节角度(弧度)
 * @param  outT    输出:7个连杆的世界变换矩阵
 *                  outT[0] = base_link(单位阵,固定不动)
 *                  outT[1] = link1(J1转动后的位置)
 *                  ...
 *                  outT[6] = link6(末端,6个关节全转完)
 *
 * 原理:逐级连乘
 *   outT[0] = 单位矩阵(基座)
 *   outT[1] = outT[0] * T_J1   (J1的变换)
 *   outT[2] = outT[1] * T_J2   (J1+J2的累积变换)
 *   ...以此类推
 */
void FK_Solve(const Joint6D *joints, Matrix4f outT[7])
{
    Matrix4f jointT;   /* 单个关节的变换矩阵 */
    int i;

    /* base_link = 单位矩阵(基座不动,是世界坐标系原点) */
    Matrix_Identity(&outT[0]);

    /* 逐级连乘: outT[i+1] = outT[i] * jointTransform[i]
     * 每次连乘叠加一个关节的旋转+平移 */
    for (i = 0; i < ARM_DOF; i++) {
        BuildJointTransform(&jointParams[i], joints->j[i], &jointT);
        Matrix_Multiply(&outT[i], &jointT, &outT[i+1]);
    }
}

/**
 * @brief  计算末端(link6)的位姿(位置+姿态)
 * @param  joints  6个关节角度(弧度)
 * @param  pose    输出:末端位姿(位置XYZ + 欧拉角RPY)
 *
 * 位置:从末端变换矩阵outT[6]的第4列(平移部分)直接读取
 * 姿态:从outT[6]的3x3旋转矩阵转换为RPY欧拉角(Roll-Pitch-Yaw)
 */
void FK_GetEndPose(const Joint6D *joints, Pose6D *pose)
{
    Matrix4f T[7];
    FK_Solve(joints, T);

    /* 末端位置 = T[6]的第4列(平移部分) */
    pose->position.x = T[6].m[0][3];
    pose->position.y = T[6].m[1][3];
    pose->position.z = T[6].m[2][3];

    /* 提取旋转矩阵的9个元素 */
    float r00 = T[6].m[0][0], r01 = T[6].m[0][1];
    float r10 = T[6].m[1][0], r11 = T[6].m[1][1];
    float r20 = T[6].m[2][0], r21 = T[6].m[2][1], r22 = T[6].m[2][2];

    /* 旋转矩阵 -> 欧拉角(RPY, XYZ内旋顺序)
     * pitch = atan2(-r20, sqrt(r00^2 + r10^2))
     * 当pitch=+/-90度时会出现万向锁奇异,需特殊处理 */
    float pitch = atan2f(-r20, sqrtf(r00*r00 + r10*r10));

    float roll;
    if (fabsf(pitch) > 1.5707f) {
        /* 奇异情况(pitch接近+/-90度),用r01和r11计算roll */
        roll = atan2f(-r01, r11);
    } else {
        /* 正常情况 */
        roll = atan2f(r21, r22);
    }

    /* yaw = atan2(r10, r00) */
    float yaw = atan2f(r10, r00);

    /* 弧度转度 */
    pose->rpy.x = roll  * 180.0f / M_PI;
    pose->rpy.y = pitch * 180.0f / M_PI;
    pose->rpy.z = yaw   * 180.0f / M_PI;
}

/**
 * @brief  获取指定连杆原点在世界坐标系的坐标
 * @param  linkT   FK_Solve输出的变换矩阵数组
 * @param  linkIdx 连杆索引(0=base, 1=link1, ..., 6=link6/末端)
 * @retval 连杆原点的世界坐标
 * @note   用于穿地检查时获取各连杆的空间位置
 */
Vector3f FK_GetLinkOrigin(const Matrix4f linkT[7], int linkIdx)
{
    Vector3f origin;
    if (linkIdx < 0 || linkIdx > 6) linkIdx = 6;  /* 越界保护 */
    /* 原点坐标 = 变换矩阵第4列(平移部分) */
    origin.x = linkT[linkIdx].m[0][3];
    origin.y = linkT[linkIdx].m[1][3];
    origin.z = linkT[linkIdx].m[2][3];
    return origin;
}
