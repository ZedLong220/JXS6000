#include "can_protocol.h"
#include "host_bridge.h"
#include "serial_cmd.h"
#include "gripper_servo.h"
#include <math.h>
#include <stdio.h>
#include <string.h>

/* Poll one participating drive per slot. Five-axis motion therefore refreshes
   every encoder in 50 ms without creating a synchronized ten-frame burst. */
#define POSITION_QUERY_PERIOD_MS 10U
#define FIXED_WAIT_QUERY_PERIOD_MS 20U
#define POSITION_QUERY_FAILURE_TIMEOUT_MS 250U
#define IDLE_QUERY_PERIOD_MS 200U
#define FEEDBACK_TIMEOUT_MS 750U
#define START_READY_TIMEOUT_MS 1500U
#define VERIFY_TIMEOUT_MS 5000U
#define POSITION_TOLERANCE_REV 0.01f
#define MIN_SEGMENT_SEC 0.05f
#define MAX_SEGMENT_SEC 120.0f
#define MULTI_STAGE_TIMEOUT_MS 5000U
#define SYNCHRONIZED_FINISH_LEAD_MS 2U
/* 51200 internal steps/revolution, signed int32 targets in the 57 firmware. */
#define MAX_TARGET_REV 40000.0f
#define RX_QUEUE_SIZE 128U
#define FIXED_AXIS_MASK 0x1FU
#define FIXED_J1_MASK 0x01U
#define FIXED_HOME_TOLERANCE_REV 0.02f
#define FIXED_FIRST_DURATION_SEC 10.0f
#define FIXED_J1_TO_MINUS_50_DURATION_SEC 2.0f
#define FIXED_RETURN_HOME_DURATION_SEC 10.0f
static uint8_t fixedReturnGripperClosed;
#define FIXED_RETURN_GRIPPER_CLOSE_DELAY_MS \
    ((uint32_t)(FIXED_RETURN_HOME_DURATION_SEC * 500.0f))
#define FIXED_SERVO_WAIT_MS 1000U
#define FIXED_J3_GRIPPER_OPEN_THRESHOLD_REV (-66.666667f) /* J3 -240 deg. */

/* A path is planned from an encoder sample taken before collision checking.
   Permit at most 0.5 joint degree of drift and rebase the first block to the
   newest sample immediately before loading. Values are motor revolutions for
   the installed 50/100/100/30/30/1 reductions. */
static const float startRebaseToleranceRev[CAN_AXIS_COUNT] = {
    0.06945f, 0.13889f, 0.13889f, 0.04167f, 0.04167f, 0.00139f
};

typedef struct {
    float positionRev;
    uint32_t positionTick;
    uint32_t version;
    uint8_t valid;
    uint8_t finished;
} AxisFeedback;

typedef struct {
    float target[CAN_AXIS_COUNT];
    float duration;
    float velocity[CAN_AXIS_COUNT];
} TrajectoryPoint;

typedef struct {
    uint16_t id;
    uint8_t length;
    uint8_t data[8];
} RxFrame;

static RxFrame rxQueue[RX_QUEUE_SIZE];
static volatile uint8_t rxHead, rxTail, rxOverflow;
static AxisFeedback feedback[CAN_AXIS_COUNT];
static TrajectoryPoint path[CAN_TRAJECTORY_MAX_POINTS];
static TrajectoryPoint activePoint;
static uint32_t dispatchVersion[CAN_AXIS_COUNT];
static uint32_t readySince, segmentStart, segmentDuration;
static uint32_t lastQuery, lastStatus, txErrors, queryFailureSince;
static uint8_t queryFailureActive;
static uint8_t positionQueryCursor;
static uint8_t pathCount, pathIndex, pathPlaying, pathRepeat;
static uint8_t oneShot, movingMask, lastError;
static uint8_t faultNode, faultDetail;
static DualAxisState state;
/* Version 4: selected nodes 1..6 execute the buffered curve locally. */
typedef struct {
    uint8_t valid, state, count, index, error;
    uint16_t session;
    uint32_t tick;
} SplineFeedback;
static SplineFeedback splineFeedback[CAN_AXIS_COUNT];
static float splineStart[CAN_AXIS_COUNT], nextVelocity[CAN_AXIS_COUNT];
static uint8_t splineEnabled, velocityReady, uploadStage, uploadIndex, finishBroadcast;
static uint16_t splineSession;
static uint32_t uploadSince, lastSplineQuery;
static uint8_t requiredMask=CAN_AXIS_MASK, multiAxisMode;
static uint8_t startMask, targetMask, velocityMask, stageOpen;
static TrajectoryPoint staged;
static uint32_t stageSince;

typedef enum {
    FIXED_SEQUENCE_OFF = 0,
    FIXED_SEQUENCE_WAIT_HOME,
    FIXED_SEQUENCE_MOVE_FIRST,
    FIXED_SEQUENCE_WAIT_SERVO_0,
    FIXED_SEQUENCE_MOVE_J1_TO_MINUS_50,
    FIXED_SEQUENCE_WAIT_SERVO_90_FINAL,
    FIXED_SEQUENCE_MOVE_HOME
} FixedSequenceState;

static FixedSequenceState fixedSequence;
static uint32_t fixedServoSince;
static uint8_t fixedGripperOpened;

static int send_frame(uint16_t id, const uint8_t *data, uint8_t length)
{
    CAN_TxHeaderTypeDef header = {0};
    uint32_t mailbox, start = HAL_GetTick();
    uint8_t empty[8] = {0};
    if (id > 0x7FFU || length > 8U || (length != 0U && data == NULL)) return -1;
    header.StdId = id;
    header.IDE = CAN_ID_STD;
    header.RTR = CAN_RTR_DATA;
    header.DLC = length;
    while (HAL_CAN_GetTxMailboxesFreeLevel(&hcan1) == 0U) {
        if (HAL_GetTick() - start >= 20U) { ++txErrors; return -1; }
    }
    if (HAL_CAN_AddTxMessage(&hcan1, &header,
            (uint8_t *)(data != NULL ? data : empty), &mailbox) != HAL_OK) {
        ++txErrors;
        return -1;
    }
    /* Wait in the main loop, never in RX IRQ. SYNC must follow BOTH targets
       on the wire even though SYNC has a numerically lower CAN identifier. */
    while (HAL_CAN_IsTxMessagePending(&hcan1, mailbox)) {
        if (HAL_GetTick() - start >= 20U) {
            HAL_CAN_AbortTxRequest(&hcan1, mailbox);
            ++txErrors;
            return -1;
        }
    }
    {
        uint32_t ok = mailbox == CAN_TX_MAILBOX0 ? CAN_TSR_TXOK0 :
                      mailbox == CAN_TX_MAILBOX1 ? CAN_TSR_TXOK1 : CAN_TSR_TXOK2;
        if ((hcan1.Instance->TSR & ok) == 0U) { ++txErrors; return -1; }
    }
    return 0;
}

static int command(uint8_t node, uint8_t cmd, const void *data, uint8_t length)
{
    return send_frame(((uint16_t)node << 7) | cmd, (const uint8_t *)data, length);
}

int CAN_SendHostFrame(uint16_t id, const uint8_t *data, uint8_t length)
{
    int result = send_frame(id, data, length);
    /* HAL does not loop our own transmitted CAN frames back to UART. */
    HostBridge_OnCanRx(id, data, length);
    return result;
}

static uint8_t online_mask(uint32_t now)
{
    uint8_t mask = 0U;
    for (uint8_t i = 0U; i < CAN_AXIS_COUNT; ++i)
        if (feedback[i].valid && now - feedback[i].positionTick <= FEEDBACK_TIMEOUT_MS)
            mask |= (uint8_t)(1U << i);
    return mask;
}

static uint8_t status_online_mask(uint32_t now)
{
    uint8_t mask=online_mask(now);
    if (pathPlaying && splineEnabled) {
        for (uint8_t i=0U;i<CAN_AXIS_COUNT;++i)
            if ((requiredMask&(1U<<i)) && splineFeedback[i].valid &&
                splineFeedback[i].session==splineSession &&
                now-splineFeedback[i].tick<=FEEDBACK_TIMEOUT_MS)
                mask|=(uint8_t)(1U<<i);
    }
    return mask;
}

static void publish_status(void)
{
    uint8_t data[8] = {0};
    data[0] = (uint8_t)state;
    data[1] = lastError;
    data[2] = status_online_mask(HAL_GetTick());
    data[3] = movingMask;
    data[4] = state==DUAL_AXIS_FAULT && lastError==5U ? faultNode : pathCount;
    data[5] = state==DUAL_AXIS_FAULT && lastError==5U ? faultDetail :
              (pathPlaying ? pathIndex : 0xFFU);
    data[6]=requiredMask; data[7]=CAN_PATH_PROTOCOL_VERSION; /* bit0=J1 ... bit5=J6 */
    CAN_SendHostFrame(CAN_CMD_CONTROLLER_STATUS, data, sizeof(data));
    lastStatus = HAL_GetTick();
}

static void set_state(DualAxisState next, uint8_t error)
{
    state = next;
    lastError = error;
    publish_status();
}

static void disable_drives(void)
{
    uint8_t data[8] = {0};
    /* Stop every physical axis, including one with stale master feedback. */
    for (uint8_t i=0U;i<CAN_AXIS_COUNT;++i)
        command((uint8_t)(i+1U),CAN_CMD_STOP,data,sizeof(data));
}

static void hold_drives(void)
{
    uint8_t data[8] = {0};
    for (uint8_t i=0U;i<CAN_AXIS_COUNT;++i)
        command((uint8_t)(i+1U),CAN_CMD_HOLD_POSITION,data,sizeof(data));
}

static void fault(uint8_t error)
{
    if (error != 5U) faultNode = faultDetail = 0U;
    SerialCmd_ResetPending();
    fixedSequence = FIXED_SEQUENCE_OFF;
    queryFailureActive = 0U;
    pathPlaying = pathRepeat = oneShot = movingMask = 0U;
    /* A controller/path fault must not remove gravity holding torque from
       every healthy joint. The faulty drive still enforces its own encoder,
       stall and limit interlocks. Only the explicit emergency command uses
       disable_drives(). */
    hold_drives();
    set_state(DUAL_AXIS_FAULT, error);
}

static void drive_fault(uint8_t node, uint8_t detail)
{
    faultNode = node;
    faultDetail = detail;
    fault(5U);
}

int CAN_DualAxisIsActive(void)
{
    return pathPlaying || oneShot || fixedSequence != FIXED_SEQUENCE_OFF;
}

int CAN_ValidateTarget(float j2, float j3, float duration)
{
    return isfinite(j2) && isfinite(j3) && isfinite(duration) &&
           fabsf(j2) <= MAX_TARGET_REV && fabsf(j3) <= MAX_TARGET_REV &&
           duration >= MIN_SEGMENT_SEC && duration <= MAX_SEGMENT_SEC;
}

static int query_positions(uint32_t now, uint32_t period)
{
    int result=0;
    if (now - lastQuery < period) return 0;
    lastQuery = now;

    /* Immediate calls are used at startup/segment preparation and deliberately
       take one complete snapshot. Periodic active polling is staggered so a
       fixed five-axis path cannot repeatedly congest the same response slot. */
    if (CAN_DualAxisIsActive() && period != 0U) {
        for (uint8_t attempt=0U; attempt<CAN_AXIS_COUNT; ++attempt) {
            uint8_t i=positionQueryCursor;
            uint8_t next=(uint8_t)((i+1U)%CAN_AXIS_COUNT);
            if (!(requiredMask&(1U<<i))) {
                positionQueryCursor=next;
                continue;
            }
            result=command((uint8_t)(i+1U),CAN_CMD_GET_POSITION,NULL,0U);
            if (result==0) positionQueryCursor=next;
            return result;
        }
        return 0;
    }
    for (uint8_t i=0U;i<CAN_AXIS_COUNT;++i)
        if ((!CAN_DualAxisIsActive() || (requiredMask&(1U<<i))) &&
            command((uint8_t)(i+1U),CAN_CMD_GET_POSITION,NULL,0U)!=0) result=-1;
    return result;
}

static int commit_point(void)
{
    uint8_t payload[8], sync[2] = {0xFFU, 0x66U};
    for (uint8_t i = 0U; i < CAN_AXIS_COUNT; ++i) {
        if (!(requiredMask&(1U<<i))) continue;
        memcpy(payload, &activePoint.target[i], 4U);
        memcpy(payload + 4, &activePoint.duration, 4U);
        if (command((uint8_t)(i + 1U), CAN_CMD_TRAJECTORY_POINT,
                    payload, sizeof(payload)) != 0) return -1;
    }
    if (command(0U, CAN_CMD_TRAJECTORY_SYNC, sync, sizeof(sync)) != 0) return -1;
    for (uint8_t i = 0U; i < CAN_AXIS_COUNT; ++i) {
        if (!(requiredMask&(1U<<i))) continue;
        dispatchVersion[i] = feedback[i].version;
        feedback[i].finished = 0U;
    }
    segmentStart = HAL_GetTick();
    segmentDuration = (uint32_t)(activePoint.duration * 1000.0f + 0.5f);
    set_state(DUAL_AXIS_RUNNING, 0U);
    return 0;
}

static int begin_point(const TrajectoryPoint *point)
{
    activePoint = *point;
    movingMask = requiredMask;
    readySince = HAL_GetTick();
    set_state(DUAL_AXIS_STARTING, 0U);
    return query_positions(HAL_GetTick(), 0U);
}

static void begin_fixed_point(const TrajectoryPoint *point)
{
    /* CAN_DualAxisUpdate already polls the participating axes before running
       the fixed-sequence state machine. Do not generate a second five-frame
       query burst while changing segments. */
    activePoint = *point;
    movingMask = requiredMask;
    readySince = HAL_GetTick();
    set_state(DUAL_AXIS_STARTING, 0U);
}
static int queue_fixed_home(TrajectoryPoint *point)
{
    memset(point, 0, sizeof(*point));
    point->duration = FIXED_RETURN_HOME_DURATION_SEC;
    return 0;
}

static int set_gripper_angle(float angle)
{
    uint8_t gripperStatus[8];
    if (Gripper_SetAngle(angle) != 0) return -1;
    Gripper_GetStatus(gripperStatus);
    (void)CAN_SendHostFrame(0x57U, gripperStatus, sizeof(gripperStatus));
    return 0;
}

static void fixed_sequence_update(uint32_t now)
{
    TrajectoryPoint point;
    if (fixedSequence == FIXED_SEQUENCE_WAIT_HOME) {
        if ((online_mask(now) & FIXED_AXIS_MASK) != FIXED_AXIS_MASK) {
            if (now - readySince >= START_READY_TIMEOUT_MS) fault(2U);
            return;
        }
        for (uint8_t i = 0U; i < CAN_AXIS_COUNT; ++i) {
            if ((FIXED_AXIS_MASK & (1U << i)) &&
                fabsf(feedback[i].positionRev) > FIXED_HOME_TOLERANCE_REV) {
                fault(7U);
                return;
            }
        }
        memset(&point, 0, sizeof(point));
        point.target[0] = -2.083333f;  /* J1 -15 deg, ratio 50. */
        point.target[1] = -19.444444f; /* J2 +70 deg, ratio 100, reversed. */
        point.target[2] = -83.333333f; /* J3 -300 deg, ratio 100. */
        point.target[3] = -7.500000f;  /* J4 +90 deg, ratio 30, reversed. */
        point.target[4] = 7.500000f;   /* J5 -90 deg, ratio 30, reversed. */
        point.duration = FIXED_FIRST_DURATION_SEC;
        fixedSequence = FIXED_SEQUENCE_MOVE_FIRST;
        oneShot = 1U;
        begin_fixed_point(&point);
        return;
    }
    if (fixedSequence == FIXED_SEQUENCE_WAIT_SERVO_0 &&
        now - fixedServoSince >= FIXED_SERVO_WAIT_MS) {
        memset(&point, 0, sizeof(point));
        point.target[0] = -6.944444f; /* J1 -50 deg, ratio 50. */
        point.duration = FIXED_J1_TO_MINUS_50_DURATION_SEC;

        /* J2-J5 already hold the requested pose. Do not restart them with
           near-zero moves, which can round their planned speed down to zero. */
        requiredMask = FIXED_J1_MASK;
        fixedSequence = FIXED_SEQUENCE_MOVE_J1_TO_MINUS_50;
        oneShot = 1U;
        begin_fixed_point(&point);
        return;
    }
    if (fixedSequence == FIXED_SEQUENCE_WAIT_SERVO_90_FINAL &&
        now - fixedServoSince >= FIXED_SERVO_WAIT_MS) {
        queue_fixed_home(&point);
        requiredMask = FIXED_AXIS_MASK;
        fixedReturnGripperClosed = 0U;
        fixedSequence = FIXED_SEQUENCE_MOVE_HOME;
        oneShot = 1U;
        begin_fixed_point(&point);
    }
}

int CAN_FixedPickSequenceStart(void)
{
    if (state != DUAL_AXIS_IDLE || CAN_DualAxisIsActive()) return -1;
    if (set_gripper_angle(0.0f) != 0) return -2;
    pathCount = pathIndex = pathPlaying = pathRepeat = oneShot = movingMask = 0U;
    splineEnabled = velocityReady = uploadStage = finishBroadcast = 0U;
    multiAxisMode = startMask = targetMask = velocityMask = stageOpen = 0U;
    requiredMask = FIXED_AXIS_MASK;
    positionQueryCursor = 0U;
    queryFailureActive = 0U;
    fixedGripperOpened = 0U;
    fixedSequence = FIXED_SEQUENCE_WAIT_HOME;
    readySince = HAL_GetTick();
    set_state(DUAL_AXIS_STARTING, 0U);
    if (query_positions(HAL_GetTick(), 0U) != 0) {
        queryFailureActive = 1U;
        queryFailureSince = HAL_GetTick();
    }
    return 0;
}

static uint8_t target_reached(void)
{
    for (uint8_t i = 0U; i < CAN_AXIS_COUNT; ++i)
        if ((requiredMask&(1U<<i)) && (!feedback[i].valid || !feedback[i].finished ||
            feedback[i].version == dispatchVersion[i] ||
            fabsf(activePoint.target[i] - feedback[i].positionRev) > POSITION_TOLERANCE_REV))
            return 0U;
    return 1U;
}

static int query_splines(uint32_t now, uint32_t period)
{
    if (now - lastSplineQuery < period) return 0;
    lastSplineQuery = now;
    for (uint8_t i=0U;i<CAN_AXIS_COUNT;++i)
        if ((requiredMask&(1U<<i)) && command((uint8_t)(i+1U),0x3CU,NULL,0U)!=0) return -1;
    return 0;
}

static int reset_splines(void)
{
    uint8_t data[2];
    ++splineSession;
    data[0] = (uint8_t)splineSession; data[1] = (uint8_t)(splineSession >> 8);
    memset(splineFeedback, 0, sizeof(splineFeedback));
    for (uint8_t i=0U; i<CAN_AXIS_COUNT; ++i)
        if ((requiredMask&(1U<<i)) && command((uint8_t)(i+1U), 0x0DU, data, 2U) != 0) return -1;
    uploadStage=1U; uploadIndex=0U; finishBroadcast=0U; uploadSince=HAL_GetTick();
    return query_splines(HAL_GetTick(),0U);
}

static int upload_block(void)
{
    uint8_t data[8];
    for (uint8_t i=0U; i<CAN_AXIS_COUNT; ++i) {
        if (!(requiredMask&(1U<<i))) continue;
        float p0=uploadIndex ? path[uploadIndex-1U].target[i] : splineStart[i];
        float v0=uploadIndex ? path[uploadIndex-1U].velocity[i] :
                              (pathRepeat ? path[pathCount-1U].velocity[i] : 0.0f);
        memcpy(data,&p0,4U); memcpy(data+4,&path[uploadIndex].target[i],4U);
        if (command((uint8_t)(i+1U),0x0AU,data,8U)!=0) return -1;
        memcpy(data,&v0,4U); memcpy(data+4,&path[uploadIndex].velocity[i],4U);
        if (command((uint8_t)(i+1U),0x0BU,data,8U)!=0) return -1;
        memcpy(data,&path[uploadIndex].duration,4U);
        data[4]=uploadIndex; data[5]=0U;
        data[6]=(uint8_t)splineSession; data[7]=(uint8_t)(splineSession>>8);
        if (command((uint8_t)(i+1U),0x0CU,data,8U)!=0) return -1;
    }
    uploadStage=2U; uploadSince=HAL_GetTick();
    return query_splines(HAL_GetTick(),0U);
}

static void update_spline(uint32_t now)
{
    if (query_splines(now,20U)!=0) { fault(1U); return; }
    if (state==DUAL_AXIS_STARTING) {
        if (uploadStage==0U) {
            if ((online_mask(now)&requiredMask)==requiredMask) {
                for (uint8_t i=0U;i<CAN_AXIS_COUNT;++i) {
                    if (!(requiredMask&(1U<<i))) continue;
                    if (fabsf(feedback[i].positionRev-splineStart[i])>
                        startRebaseToleranceRev[i]) { fault(6U); return; }
                    splineStart[i]=feedback[i].positionRev;
                }
                if (reset_splines()!=0) fault(1U);
            } else if (now-readySince>=START_READY_TIMEOUT_MS) fault(2U);
            return;
        }
        for (uint8_t i=0U;i<CAN_AXIS_COUNT;++i) {
        if (!(requiredMask&(1U<<i))) continue;
            if (splineFeedback[i].valid && splineFeedback[i].session==splineSession && splineFeedback[i].error) {
                drive_fault((uint8_t)(i+1U),splineFeedback[i].error); return;
            }
            if (!splineFeedback[i].valid || splineFeedback[i].session!=splineSession ||
                splineFeedback[i].count!=(uploadStage==1U ? 0U : uploadIndex+1U)) {
                if (now-uploadSince>=START_READY_TIMEOUT_MS) fault(4U);
                return;
            }
        }
        if (uploadStage==2U) ++uploadIndex;
        if (uploadIndex<pathCount) {
            if (upload_block()!=0) fault(1U);
        } else {
            uint8_t sync[8]={0xFFU,0x66U,CAN_PATH_PROTOCOL_VERSION,0U,0U,0U,0U,0U};
            for (uint8_t i=0U;i<CAN_AXIS_COUNT;++i)
                if ((requiredMask&(1U<<i)) && fabsf(feedback[i].positionRev-splineStart[i])>0.02f) { fault(6U); return; }
            sync[3]=requiredMask; sync[4]=pathCount; sync[5]=pathRepeat;
            sync[6]=(uint8_t)splineSession; sync[7]=(uint8_t)(splineSession>>8);
            if (command(0U,0x66U,sync,8U)!=0) { fault(1U); return; }
            activePoint=path[pathCount-1U];
            segmentDuration=20U;
            for (uint8_t i=0U;i<pathCount;++i) segmentDuration+=(uint32_t)(path[i].duration*1000.0f+0.5f);
            for (uint8_t i=0U;i<CAN_AXIS_COUNT;++i) {
                if (!(requiredMask&(1U<<i))) continue;
                dispatchVersion[i]=feedback[i].version; feedback[i].finished=0U;
            }
            segmentStart=HAL_GetTick();
            set_state(DUAL_AXIS_RUNNING,0U);
        }
        return;
    }
    if (!pathRepeat && !finishBroadcast &&
        now-segmentStart>=segmentDuration-SYNCHRONIZED_FINISH_LEAD_MS) {
        uint8_t finish[8]={0xFFU,CAN_CMD_TRAJECTORY_FINISH,CAN_PATH_PROTOCOL_VERSION,
                           requiredMask,pathCount,0U,(uint8_t)splineSession,
                           (uint8_t)(splineSession>>8)};
        if (command(0U,CAN_CMD_TRAJECTORY_FINISH,finish,8U)!=0) { fault(1U); return; }
        finishBroadcast=1U;
        set_state(DUAL_AXIS_VERIFY_POSITION,0U);
    }
    for (uint8_t i=0U;i<CAN_AXIS_COUNT;++i) {
        if (!(requiredMask&(1U<<i))) continue;
        if (!splineFeedback[i].valid || splineFeedback[i].session!=splineSession || now-splineFeedback[i].tick>FEEDBACK_TIMEOUT_MS) {
            /* During a buffered path the drive heartbeat proves that its local
               encoder controller is alive. Do not abort only because a 0x23
               telemetry reply was briefly delayed. If both channels are stale,
               retain the encoder-timeout diagnosis; otherwise report the path
               heartbeat/handshake failure. */
            if (!feedback[i].valid || now-feedback[i].positionTick>FEEDBACK_TIMEOUT_MS)
                fault(2U);
            else
                fault(4U);
            return;
        }
        if (splineFeedback[i].error || splineFeedback[i].state==4U) {
            drive_fault((uint8_t)(i+1U),splineFeedback[i].error); return;
        }
        if (now-segmentStart>200U && splineFeedback[i].state!=2U && splineFeedback[i].state!=3U) {
            drive_fault((uint8_t)(i+1U),0xFFU); return;
        }
    }
    pathIndex=0U;
    for (uint8_t i=0U;i<CAN_AXIS_COUNT;++i) if (requiredMask&(1U<<i)) { pathIndex=splineFeedback[i].index; break; }
    uint8_t ended=1U;
    for (uint8_t i=0U;i<CAN_AXIS_COUNT;++i) if ((requiredMask&(1U<<i)) && splineFeedback[i].state!=3U) ended=0U;
    if (ended && target_reached()) {
        pathPlaying=movingMask=0U;
        set_state(DUAL_AXIS_IDLE,0U);
    } else if (!pathRepeat && now-segmentStart>=segmentDuration+VERIFY_TIMEOUT_MS) fault(3U);
    else if (!pathRepeat && now-segmentStart>=segmentDuration && state!=DUAL_AXIS_VERIFY_POSITION)
        set_state(DUAL_AXIS_VERIFY_POSITION,0U);
}

void CAN_Proto_Init(void)
{
    CAN_FilterTypeDef filter = {0};
    filter.FilterBank = 0U;
    filter.FilterMode = CAN_FILTERMODE_IDMASK;
    filter.FilterScale = CAN_FILTERSCALE_32BIT;
    filter.FilterFIFOAssignment = CAN_RX_FIFO0;
    filter.FilterActivation = ENABLE;
    filter.SlaveStartFilterBank = 14U;
    if (HAL_CAN_ConfigFilter(&hcan1, &filter) != HAL_OK ||
        HAL_CAN_Start(&hcan1) != HAL_OK ||
        HAL_CAN_ActivateNotification(&hcan1, CAN_IT_RX_FIFO0_MSG_PENDING) != HAL_OK)
        Error_Handler();
}

void CAN_DualAxisInit(void)
{
    rxHead = rxTail = rxOverflow = 0U;
    memset(feedback, 0, sizeof(feedback));
    memset(path, 0, sizeof(path));
    memset(&activePoint, 0, sizeof(activePoint));
    pathCount = pathIndex = pathPlaying = pathRepeat = oneShot = 0U;
    movingMask = lastError = faultNode = faultDetail = 0U;
    lastQuery = lastStatus = txErrors = queryFailureSince = 0U;
    queryFailureActive = 0U;
    positionQueryCursor = 0U;
    state = DUAL_AXIS_IDLE;
    splineEnabled=velocityReady=uploadStage=finishBroadcast=0U;
    splineSession=0U; lastSplineQuery=0U; requiredMask=CAN_AXIS_MASK;
    multiAxisMode=startMask=targetMask=velocityMask=stageOpen=0U;
    fixedSequence=FIXED_SEQUENCE_OFF; fixedServoSince=0U; fixedGripperOpened=0U;
    fixedReturnGripperClosed=0U;
    memset(splineFeedback,0,sizeof(splineFeedback));
}

void CAN_DualAxisStop(void)
{
    SerialCmd_ResetPending();
    fixedSequence = FIXED_SEQUENCE_OFF;
    queryFailureActive = 0U;
    pathPlaying = pathRepeat = oneShot = movingMask = 0U;
    hold_drives();
    set_state(DUAL_AXIS_IDLE, 0U);
}

void CAN_DualAxisEmergencyDisable(void)
{
    SerialCmd_ResetPending();
    fixedSequence = FIXED_SEQUENCE_OFF;
    pathPlaying = pathRepeat = oneShot = movingMask = 0U;
    disable_drives();
    set_state(DUAL_AXIS_IDLE, 0U);
}

int CAN_DualAxisSetZero(void)
{
    uint8_t data[8] = {0};
    uint32_t now = HAL_GetTick();
    uint8_t mask;

    /* Setting a coordinate origin is not a stop command. Refuse the request
       while any path is active instead of cancelling motion and dropping into
       a different control state underneath the drives. */
    if (state != DUAL_AXIS_IDLE || CAN_DualAxisIsActive()) return -1;
    mask = online_mask(now);
    if (mask == 0U) return -1;
    for (uint8_t i=0U;i<CAN_AXIS_COUNT;++i)
        if ((mask & (1U << i)) && !feedback[i].finished) return -1;

    SerialCmd_ResetPending();
    pathCount = pathIndex = 0U;
    splineEnabled = velocityReady = uploadStage = finishBroadcast = 0U;
    multiAxisMode = startMask = targetMask = velocityMask = stageOpen = 0U;
    requiredMask = mask;
    for (uint8_t i=0U;i<CAN_AXIS_COUNT;++i) {
        /* Only address drives that have fresh encoder feedback. An absent axis
           must not make a safe zeroing operation hold the CAN bus in retry. */
        if (!(mask & (1U << i))) continue;
        if (command((uint8_t)(i+1U),CAN_CMD_SET_HOME,data,sizeof(data))!=0) {
            fault(1U);
            return -2;
        }
        feedback[i].valid=0U; activePoint.target[i]=0.0f;
    }
    movingMask = 0U;
    set_state(DUAL_AXIS_IDLE, 0U);
    /* Drive 0x15 stores home in EEPROM while preserving position torque. */
    return 0;
}

int CAN_DualAxisMove(float j2, float j3, float duration)
{
    TrajectoryPoint point = {{0.0f,j2,j3,0.0f,0.0f}, duration, {0}};
    if (!CAN_ValidateTarget(j2, j3, duration) || CAN_DualAxisIsActive()) return -1;
    pathCount = pathIndex = 0U;
    requiredMask=0x06U; multiAxisMode=0U; oneShot = 1U;
    if (begin_point(&point) != 0) { fault(1U); return -2; }
    return 0;
}

void CAN_TrajectoryClear(void)
{
    if (CAN_DualAxisIsActive()) CAN_DualAxisStop();
    pathCount = pathIndex = 0U;
    splineEnabled=velocityReady=0U;
    multiAxisMode=startMask=targetMask=velocityMask=stageOpen=0U; requiredMask=CAN_AXIS_MASK;
    set_state(DUAL_AXIS_IDLE, 0U);
}

int CAN_TrajectorySetStart(float j2,float j3)
{
    if (CAN_DualAxisIsActive() || pathCount || !CAN_ValidateTarget(j2,j3,1.0f)) return -1;
    if (multiAxisMode) return -1;
    requiredMask=0x06U; splineStart[1]=j2; splineStart[2]=j3; splineEnabled=1U; velocityReady=0U;
    return 0;
}
int CAN_TrajectorySetVelocity(float j2,float j3)
{
    velocityReady=0U;
    if (multiAxisMode || !splineEnabled || CAN_DualAxisIsActive() || !isfinite(j2) || !isfinite(j3) ||
        fabsf(j2)>30.0f || fabsf(j3)>30.0f) return -1;
    nextVelocity[1]=j2; nextVelocity[2]=j3; velocityReady=1U;
    return 0;
}
void CAN_TrajectoryDiscardVelocity(void) { velocityReady=0U; stageOpen=0U; targetMask=velocityMask=0U; }

int CAN_MultiBegin(uint8_t version,uint8_t mask)
{
    if (version!=CAN_PATH_PROTOCOL_VERSION || !mask || (mask & ~CAN_AXIS_MASK) || CAN_DualAxisIsActive()) return -1;
    CAN_TrajectoryClear();
    requiredMask=mask; multiAxisMode=splineEnabled=1U;
    memset(splineStart,0,sizeof(splineStart));
    set_state(DUAL_AXIS_BUFFERING,0U);
    return 0;
}

int CAN_MultiSetStart(uint8_t node,float position)
{
    uint8_t bit;
    if (node<1U || node>CAN_AXIS_COUNT) return -1;
    bit=(uint8_t)(1U<<(node-1U));
    if (!multiAxisMode || CAN_DualAxisIsActive() || pathCount || stageOpen ||
        !(requiredMask&bit) || !CAN_ValidateTarget(position,position,1.0f)) return -1;
    splineStart[node-1U]=position; startMask|=bit;
    return 0;
}

int CAN_MultiStage(uint16_t sequence,float duration)
{
    CAN_TrajectoryDiscardVelocity();
    if (!multiAxisMode || CAN_DualAxisIsActive() || startMask!=requiredMask ||
        sequence!=pathCount || pathCount>=CAN_TRAJECTORY_MAX_POINTS ||
        !CAN_ValidateTarget(0.0f,0.0f,duration)) return -1;
    memset(&staged,0,sizeof(staged));
    staged.duration=duration; stageOpen=1U; stageSince=HAL_GetTick();
    return 0;
}

int CAN_MultiValue(uint8_t node,uint8_t kind,uint16_t sequence,float value)
{
    uint8_t bit;
    if (!multiAxisMode || CAN_DualAxisIsActive() || !stageOpen || sequence!=pathCount ||
        HAL_GetTick()-stageSince>=MULTI_STAGE_TIMEOUT_MS || node<1U || node>CAN_AXIS_COUNT || kind>1U ||
        !isfinite(value)) { CAN_TrajectoryDiscardVelocity(); return -1; }
    bit=(uint8_t)(1U<<(node-1U));
    if (!(requiredMask&bit) || fabsf(value)>(kind?30.0f:MAX_TARGET_REV)) {
        CAN_TrajectoryDiscardVelocity(); return -1;
    }
    if (kind) { staged.velocity[node-1U]=value; velocityMask|=bit; }
    else { staged.target[node-1U]=value; targetMask|=bit; }
    stageSince=HAL_GetTick();
    return 0;
}

int CAN_MultiCommit(uint16_t sequence)
{
    uint8_t valid=multiAxisMode && !CAN_DualAxisIsActive() && stageOpen && sequence==pathCount &&
        pathCount<CAN_TRAJECTORY_MAX_POINTS && targetMask==requiredMask && velocityMask==requiredMask &&
        HAL_GetTick()-stageSince<MULTI_STAGE_TIMEOUT_MS;
    CAN_TrajectoryDiscardVelocity();
    if (!valid) return -1;
    path[pathCount++]=staged;
    set_state(DUAL_AXIS_BUFFERING,0U);
    return 0;
}

int CAN_TrajectoryEnqueue(float j2, float j3, float duration)
{
    uint8_t hadVelocity=velocityReady;
    velocityReady=0U;
    if (multiAxisMode || CAN_DualAxisIsActive() || pathCount >= CAN_TRAJECTORY_MAX_POINTS ||
        !CAN_ValidateTarget(j2, j3, duration) || (splineEnabled && !hadVelocity)) return -1;
    requiredMask=0x06U;
    path[pathCount].target[1] = j2;
    path[pathCount].target[2] = j3;
    path[pathCount].duration = duration;
    path[pathCount].velocity[1]=splineEnabled ? nextVelocity[1] : 0.0f;
    path[pathCount++].velocity[2]=splineEnabled ? nextVelocity[2] : 0.0f;
    velocityReady=0U;
    set_state(DUAL_AXIS_BUFFERING, 0U);
    return 0;
}

int CAN_TrajectoryStart(uint8_t repeat)
{
    if (pathCount == 0U || CAN_DualAxisIsActive() || (multiAxisMode && (startMask!=requiredMask || stageOpen))) return -1;
    if (splineEnabled) {
        for (uint8_t i=0U;i<CAN_AXIS_COUNT;++i)
            if ((requiredMask&(1U<<i)) && ((!repeat && path[pathCount-1U].velocity[i]!=0.0f) ||
                (repeat && path[pathCount-1U].target[i]!=splineStart[i]))) return -1;
    }
    pathPlaying = 1U;
    pathRepeat = repeat ? 1U : 0U;
    pathIndex = 0U;
    if (splineEnabled) {
        uploadStage=0U; readySince=HAL_GetTick(); movingMask=requiredMask;
        set_state(DUAL_AXIS_STARTING,0U);
        if (query_positions(HAL_GetTick(),0U)!=0) { fault(1U); return -2; }
        return 0;
    }
    if (begin_point(&path[0]) != 0) { fault(1U); return -2; }
    return 0;
}

uint8_t CAN_TrajectoryCount(void) { return pathCount; }
DualAxisState CAN_DualAxisGetState(void) { return state; }

/* IRQ producer only: all parsing, acknowledgements and TX run in the main loop. */
void CAN_RxCallback(CAN_RxHeaderTypeDef *header, uint8_t *data)
{
    uint8_t next;
    if (header == NULL || data == NULL || header->IDE != CAN_ID_STD ||
        header->RTR != CAN_RTR_DATA || header->StdId > 0x7FFU || header->DLC > 8U) return;
    next = (uint8_t)((rxHead + 1U) % RX_QUEUE_SIZE);
    if (next == rxTail) { rxOverflow = 1U; return; }
    rxQueue[rxHead].id = (uint16_t)header->StdId;
    rxQueue[rxHead].length = (uint8_t)header->DLC;
    memcpy(rxQueue[rxHead].data, data, header->DLC);
    __DMB();
    rxHead = next;
}

static void process_rx(void)
{
    while (rxTail != rxHead) {
        RxFrame frame = rxQueue[rxTail];
        uint8_t node = (uint8_t)(frame.id >> 7);
        uint8_t cmd = (uint8_t)(frame.id & 0x7FU);
        __DMB();
        rxTail = (uint8_t)((rxTail + 1U) % RX_QUEUE_SIZE);
        if ((node>=1U && node<=CAN_AXIS_COUNT) && cmd==0x3CU && frame.length==8U &&
            frame.data[0]==CAN_PATH_PROTOCOL_VERSION && (frame.data[5]&0x05U)==0x05U) {
            SplineFeedback *f=&splineFeedback[node-1U];
            f->valid=1U; f->state=frame.data[1]; f->count=frame.data[2];
            f->index=frame.data[3]; f->error=frame.data[4];
            f->session=(uint16_t)(frame.data[6]|((uint16_t)frame.data[7]<<8));
            f->tick=HAL_GetTick();
            continue; /* Internal status/heartbeat is not UART telemetry. */
        }
        if ((node >= 1U && node <= CAN_AXIS_COUNT) &&
            cmd == CAN_CMD_GET_POSITION && frame.length >= 5U) {
            float value;
            uint8_t i = (uint8_t)(node - 1U);
            memcpy(&value, frame.data, sizeof(value));
            if (isfinite(value)) {
                feedback[i].positionRev = value;
                feedback[i].finished = frame.data[4] ? 1U : 0U;
                feedback[i].positionTick = HAL_GetTick();
                feedback[i].valid = 1U;
                ++feedback[i].version;
            }
        }
        if (SerialCmd_HandleCanFrame(frame.id, frame.data, frame.length)) continue;
        /* Query requests on a shared CAN bus are not encoder samples. */
        if (frame.length != 0U) HostBridge_OnCanRx(frame.id, frame.data, frame.length);
    }
}

void CAN_DualAxisUpdate(void)
{
    uint32_t now, queryPeriod;
    process_rx();
    if (rxOverflow) {
        rxOverflow = 0U;
        rxTail = rxHead;
        SerialCmd_ResetPending();
        fault(1U);
        return;
    }
    now = HAL_GetTick();
    queryPeriod = CAN_DualAxisIsActive() ? POSITION_QUERY_PERIOD_MS : IDLE_QUERY_PERIOD_MS;
    if (fixedSequence == FIXED_SEQUENCE_WAIT_SERVO_0 ||
        fixedSequence == FIXED_SEQUENCE_WAIT_SERVO_90_FINAL)
        queryPeriod = FIXED_WAIT_QUERY_PERIOD_MS;
    if (now - lastQuery >= queryPeriod) {
        if (query_positions(now, queryPeriod) != 0) {
            if (!queryFailureActive) {
                queryFailureActive = 1U;
                queryFailureSince = now;
            } else if (now - queryFailureSince >= POSITION_QUERY_FAILURE_TIMEOUT_MS) {
                fault(1U);
            }
            /* Retry on the next polling interval. A single busy mailbox must
               not abort an otherwise healthy multi-axis sequence. */
            return;
        }
        queryFailureActive = 0U;
    }
    if (queryFailureActive) return;
    if (now - lastStatus >= 500U) publish_status();
    if (!CAN_DualAxisIsActive()) return;
    if (fixedSequence == FIXED_SEQUENCE_WAIT_HOME ||
        fixedSequence == FIXED_SEQUENCE_WAIT_SERVO_0 ||
        fixedSequence == FIXED_SEQUENCE_WAIT_SERVO_90_FINAL) {
        fixed_sequence_update(now);
        return;
    }
    if (pathPlaying && splineEnabled) { update_spline(now); return; }

    if (state == DUAL_AXIS_STARTING) {
        if ((online_mask(now)&requiredMask) == requiredMask) {
            if (commit_point() != 0) fault(1U);
        } else if (now - readySince >= START_READY_TIMEOUT_MS) {
            fault(2U);
        }
        return;
    }
    if ((online_mask(now)&requiredMask) != requiredMask) { fault(2U); return; }
    /* Never complete from stale finish flags returned before the new goal. */
    if (now - segmentStart < 20U) return;
    if (fixedSequence == FIXED_SEQUENCE_MOVE_HOME && !fixedReturnGripperClosed &&
        now - segmentStart >= FIXED_RETURN_GRIPPER_CLOSE_DELAY_MS) {
        if (set_gripper_angle(0.0f) != 0) { fault(8U); return; }
        fixedReturnGripperClosed = 1U;
    }
    if (fixedSequence == FIXED_SEQUENCE_MOVE_FIRST && !fixedGripperOpened &&
        feedback[2].valid &&
        feedback[2].positionRev <= FIXED_J3_GRIPPER_OPEN_THRESHOLD_REV) {
        if (set_gripper_angle(90.0f) != 0) { fault(8U); return; }
        fixedGripperOpened = 1U;
        return;
    }
    if (target_reached()) {
        if (fixedSequence == FIXED_SEQUENCE_MOVE_FIRST) {
            if (set_gripper_angle(0.0f) != 0) { fault(8U); return; }
            oneShot = movingMask = 0U;
            fixedSequence = FIXED_SEQUENCE_WAIT_SERVO_0;
            fixedServoSince = now;
            set_state(DUAL_AXIS_VERIFY_POSITION, 0U);
            return;
        }
        if (fixedSequence == FIXED_SEQUENCE_MOVE_J1_TO_MINUS_50) {
            if (set_gripper_angle(90.0f) != 0) { fault(8U); return; }
            oneShot = movingMask = 0U;
            requiredMask = FIXED_AXIS_MASK;
            fixedSequence = FIXED_SEQUENCE_WAIT_SERVO_90_FINAL;
            fixedServoSince = now;
            set_state(DUAL_AXIS_VERIFY_POSITION, 0U);
            return;
        }
        if (fixedSequence == FIXED_SEQUENCE_MOVE_HOME) {
            if (!fixedReturnGripperClosed && set_gripper_angle(0.0f) != 0) {
                fault(8U); return;
            }
            fixedReturnGripperClosed = 1U;
            fixedSequence = FIXED_SEQUENCE_OFF;
            oneShot = movingMask = 0U;
            set_state(DUAL_AXIS_IDLE, 0U);
            return;
        }
        if (pathPlaying) {
            uint8_t next = (uint8_t)(pathIndex + 1U);
            if (next == pathCount && pathRepeat) next = 0U;
            if (next < pathCount) {
                pathIndex = next;
                if (begin_point(&path[next]) != 0) fault(1U);
                return;
            }
        }
        pathPlaying = oneShot = movingMask = 0U;
        set_state(DUAL_AXIS_IDLE, 0U);
    } else if (now - segmentStart >= segmentDuration + VERIFY_TIMEOUT_MS) {
        fault(3U);
    } else if (now - segmentStart >= segmentDuration && state != DUAL_AXIS_VERIFY_POSITION) {
        set_state(DUAL_AXIS_VERIFY_POSITION, 0U);
    }
}

void CAN_DualAxisPrintStatus(void)
{
    printf("[JXS6000 CAN42] state=%u error=%u online=0x%02X moving=0x%02X path=%u index=%u target=(%.4f,%.4f) actual=(%.4f,%.4f) txerr=%lu\r\n",
           (unsigned)state, lastError, online_mask(HAL_GetTick()), movingMask,
           pathCount, pathIndex, activePoint.target[1], activePoint.target[2],
           feedback[1].positionRev, feedback[2].positionRev, (unsigned long)txErrors);
}
