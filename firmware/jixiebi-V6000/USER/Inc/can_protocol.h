#ifndef CAN_PROTOCOL_H
#define CAN_PROTOCOL_H

#include "main.h"
#include "can.h"
#include <stdint.h>

#define NODE_ID_J2 2U
#define NODE_ID_J3 3U
#define CAN_AXIS_COUNT 6U
#define CAN_AXIS_MASK 0x3FU
#define CAN_PATH_PROTOCOL_VERSION 4U

#define CAN_CMD_POSITION_TIME 0x06U
#define CAN_CMD_TRAJECTORY_POINT 0x08U
#define CAN_CMD_STOP 0x09U
#define CAN_CMD_SET_HOME 0x15U
#define CAN_CMD_GET_POSITION 0x23U
#define CAN_CMD_CONTROLLER_STATUS 0x38U
#define CAN_CMD_TRAJECTORY_CONTROL 0x3AU
#define CAN_CMD_HOST_ACK 0x3BU
#define CAN_CMD_HOST_J2_POSITION_TIME 0x60U
#define CAN_CMD_HOST_J3_POSITION_TIME 0x61U
#define CAN_CMD_HOST_J2_TRAJECTORY_POINT 0x62U
#define CAN_CMD_HOST_J3_TRAJECTORY_POINT 0x63U
#define CAN_CMD_TRAJECTORY_SYNC 0x66U

#define CAN_TRAJECTORY_MAX_POINTS 32U

typedef enum
{
    DUAL_AXIS_IDLE = 0,
    DUAL_AXIS_BUFFERING = 1,
    DUAL_AXIS_STARTING = 2,
    DUAL_AXIS_BLEND_WAIT = 3,
    DUAL_AXIS_RUNNING = 4,
    DUAL_AXIS_VERIFY_POSITION = 5,
    DUAL_AXIS_STOPPING = 6,
    DUAL_AXIS_FAULT = 7
} DualAxisState;

void CAN_Proto_Init(void);
void CAN_DualAxisInit(void);
void CAN_DualAxisUpdate(void);
int CAN_DualAxisMove(float j2TargetRev, float j3TargetRev, float durationSec);
void CAN_DualAxisStop(void);
int CAN_DualAxisIsActive(void);
int CAN_ValidateTarget(float j2TargetRev, float j3TargetRev, float durationSec);
void CAN_DualAxisSetZero(void);
DualAxisState CAN_DualAxisGetState(void);
void CAN_DualAxisPrintStatus(void);

void CAN_TrajectoryClear(void);
int CAN_TrajectoryEnqueue(float j2TargetRev, float j3TargetRev, float durationSec);
int CAN_TrajectorySetStart(float j2, float j3);
int CAN_TrajectorySetVelocity(float j2, float j3);
void CAN_TrajectoryDiscardVelocity(void);
int CAN_MultiBegin(uint8_t version, uint8_t mask);
int CAN_MultiSetStart(uint8_t node, float position);
int CAN_MultiStage(uint16_t sequence, float duration);
int CAN_MultiValue(uint8_t node, uint8_t kind, uint16_t sequence, float value);
int CAN_MultiCommit(uint16_t sequence);
int CAN_TrajectoryStart(uint8_t repeat);
uint8_t CAN_TrajectoryCount(void);

int CAN_SendHostFrame(uint16_t stdId, const uint8_t *data, uint8_t length);
void CAN_RxCallback(CAN_RxHeaderTypeDef *header, uint8_t *data);

#endif
