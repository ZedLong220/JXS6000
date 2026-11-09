#include "can_protocol.h"
#include "host_bridge.h"
#include "serial_cmd.h"
#include <math.h>
#include <stdio.h>
#include <string.h>

#define POSITION_QUERY_PERIOD_MS 20U
#define IDLE_QUERY_PERIOD_MS 200U
#define FEEDBACK_TIMEOUT_MS 750U
#define START_READY_TIMEOUT_MS 1500U
#define VERIFY_TIMEOUT_MS 5000U
#define POSITION_TOLERANCE_REV 0.01f
#define MIN_SEGMENT_SEC 0.05f
#define MAX_SEGMENT_SEC 120.0f
/* 51200 internal steps/revolution, signed int32 targets in the 57 firmware. */
#define MAX_TARGET_REV 40000.0f
#define RX_QUEUE_SIZE 64U

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
static uint32_t lastQuery, lastStatus, txErrors;
static uint8_t pathCount, pathIndex, pathPlaying, pathRepeat;
static uint8_t oneShot, movingMask, lastError;
static DualAxisState state;
/* Version 3: selected nodes 1..5 execute the buffered curve locally. */
typedef struct {
    uint8_t valid, state, count, index, error;
    uint16_t session;
    uint32_t tick;
} SplineFeedback;
static SplineFeedback splineFeedback[CAN_AXIS_COUNT];
static float splineStart[CAN_AXIS_COUNT], nextVelocity[CAN_AXIS_COUNT];
static uint8_t splineEnabled, velocityReady, uploadStage, uploadIndex;
static uint16_t splineSession;
static uint32_t uploadSince, lastSplineQuery;
static uint8_t requiredMask=CAN_AXIS_MASK, fiveAxisMode;
static uint8_t startMask, targetMask, velocityMask, stageOpen;
static TrajectoryPoint staged;
static uint32_t stageSince;

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

static void publish_status(void)
{
    uint8_t data[8] = {0};
    data[0] = (uint8_t)state;
    data[1] = lastError;
    data[2] = online_mask(HAL_GetTick());
    data[3] = movingMask;
    data[4] = pathCount;
    data[5] = pathPlaying ? pathIndex : 0xFFU;
    data[6]=requiredMask; data[7]=3U; /* bit0=J1 ... bit4=J5 */
    CAN_SendHostFrame(CAN_CMD_CONTROLLER_STATUS, data, sizeof(data));
    lastStatus = HAL_GetTick();
}

static void set_state(DualAxisState next, uint8_t error)
{
    state = next;
    lastError = error;
    publish_status();
}

static void stop_drives(void)
{
    uint8_t data[8] = {0};
    /* Stop every physical axis, including one with stale master feedback. */
    for (uint8_t i=0U;i<CAN_AXIS_COUNT;++i)
        command((uint8_t)(i+1U),CAN_CMD_STOP,data,sizeof(data));
}

static void fault(uint8_t error)
{
    SerialCmd_ResetPending();
    pathPlaying = pathRepeat = oneShot = movingMask = 0U;
    stop_drives();
    set_state(DUAL_AXIS_FAULT, error);
}

int CAN_DualAxisIsActive(void)
{
    return pathPlaying || oneShot;
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
    uploadStage=1U; uploadIndex=0U; uploadSince=HAL_GetTick();
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
                for (uint8_t i=0U;i<CAN_AXIS_COUNT;++i)
                    if ((requiredMask&(1U<<i)) && fabsf(feedback[i].positionRev-splineStart[i])>0.02f) { fault(6U); return; }
                if (reset_splines()!=0) fault(1U);
            } else if (now-readySince>=START_READY_TIMEOUT_MS) fault(2U);
            return;
        }
        for (uint8_t i=0U;i<CAN_AXIS_COUNT;++i) {
        if (!(requiredMask&(1U<<i))) continue;
            if (splineFeedback[i].valid && splineFeedback[i].session==splineSession && splineFeedback[i].error) {
                fault(5U); return;
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
            uint8_t sync[8]={0xFFU,0x66U,3U,0U,0U,0U,0U,0U};
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
    if ((online_mask(now)&requiredMask)!=requiredMask) { fault(2U); return; }
    for (uint8_t i=0U;i<CAN_AXIS_COUNT;++i) {
        if (!(requiredMask&(1U<<i))) continue;
        if (!splineFeedback[i].valid || splineFeedback[i].session!=splineSession || now-splineFeedback[i].tick>FEEDBACK_TIMEOUT_MS) {
            fault(4U); return;
        }
        if (splineFeedback[i].error || splineFeedback[i].state==4U) { fault(5U); return; }
        if (now-segmentStart>200U && splineFeedback[i].state!=2U && splineFeedback[i].state!=3U) {
            fault(5U); return;
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
    movingMask = lastError = 0U;
    lastQuery = lastStatus = txErrors = 0U;
    state = DUAL_AXIS_IDLE;
    splineEnabled=velocityReady=uploadStage=0U;
    splineSession=0U; lastSplineQuery=0U; requiredMask=CAN_AXIS_MASK;
    fiveAxisMode=startMask=targetMask=velocityMask=stageOpen=0U;
    memset(splineFeedback,0,sizeof(splineFeedback));
}

void CAN_DualAxisStop(void)
{
    SerialCmd_ResetPending();
    pathPlaying = pathRepeat = oneShot = movingMask = 0U;
    stop_drives();
    set_state(DUAL_AXIS_IDLE, 0U);
}

void CAN_DualAxisSetZero(void)
{
    uint8_t data[8] = {0};
    CAN_DualAxisStop();
    pathCount = pathIndex = 0U;
    for (uint8_t i=0U;i<CAN_AXIS_COUNT;++i) {
        if (command((uint8_t)(i+1U),CAN_CMD_SET_HOME,data,sizeof(data))!=0) { fault(1U); return; }
        feedback[i].valid=0U; activePoint.target[i]=0.0f;
    }
    CAN_TrajectoryClear();
    /* Existing drive 0x15 stores home in EEPROM; it is never sent automatically. */
}

int CAN_DualAxisMove(float j2, float j3, float duration)
{
    TrajectoryPoint point = {{0.0f,j2,j3,0.0f,0.0f}, duration, {0}};
    if (!CAN_ValidateTarget(j2, j3, duration) || CAN_DualAxisIsActive()) return -1;
    pathCount = pathIndex = 0U;
    requiredMask=0x06U; fiveAxisMode=0U; oneShot = 1U;
    if (begin_point(&point) != 0) { fault(1U); return -2; }
    return 0;
}

void CAN_TrajectoryClear(void)
{
    if (CAN_DualAxisIsActive()) CAN_DualAxisStop();
    pathCount = pathIndex = 0U;
    splineEnabled=velocityReady=0U;
    fiveAxisMode=startMask=targetMask=velocityMask=stageOpen=0U; requiredMask=CAN_AXIS_MASK;
    set_state(DUAL_AXIS_IDLE, 0U);
}

int CAN_TrajectorySetStart(float j2,float j3)
{
    if (CAN_DualAxisIsActive() || pathCount || !CAN_ValidateTarget(j2,j3,1.0f)) return -1;
    if (fiveAxisMode) return -1;
    requiredMask=0x06U; splineStart[1]=j2; splineStart[2]=j3; splineEnabled=1U; velocityReady=0U;
    return 0;
}
int CAN_TrajectorySetVelocity(float j2,float j3)
{
    velocityReady=0U;
    if (fiveAxisMode || !splineEnabled || CAN_DualAxisIsActive() || !isfinite(j2) || !isfinite(j3) ||
        fabsf(j2)>30.0f || fabsf(j3)>30.0f) return -1;
    nextVelocity[1]=j2; nextVelocity[2]=j3; velocityReady=1U;
    return 0;
}
void CAN_TrajectoryDiscardVelocity(void) { velocityReady=0U; stageOpen=0U; targetMask=velocityMask=0U; }

int CAN_MultiBegin(uint8_t version,uint8_t mask)
{
    if (version!=3U || !mask || (mask & ~CAN_AXIS_MASK) || CAN_DualAxisIsActive()) return -1;
    CAN_TrajectoryClear();
    requiredMask=mask; fiveAxisMode=splineEnabled=1U;
    memset(splineStart,0,sizeof(splineStart));
    set_state(DUAL_AXIS_BUFFERING,0U);
    return 0;
}

int CAN_MultiSetStart(uint8_t node,float position)
{
    uint8_t bit;
    if (node<1U || node>CAN_AXIS_COUNT) return -1;
    bit=(uint8_t)(1U<<(node-1U));
    if (!fiveAxisMode || CAN_DualAxisIsActive() || pathCount || stageOpen ||
        !(requiredMask&bit) || !CAN_ValidateTarget(position,position,1.0f)) return -1;
    splineStart[node-1U]=position; startMask|=bit;
    return 0;
}

int CAN_MultiStage(uint16_t sequence,float duration)
{
    CAN_TrajectoryDiscardVelocity();
    if (!fiveAxisMode || CAN_DualAxisIsActive() || startMask!=requiredMask ||
        sequence!=pathCount || pathCount>=CAN_TRAJECTORY_MAX_POINTS ||
        !CAN_ValidateTarget(0.0f,0.0f,duration)) return -1;
    memset(&staged,0,sizeof(staged));
    staged.duration=duration; stageOpen=1U; stageSince=HAL_GetTick();
    return 0;
}

int CAN_MultiValue(uint8_t node,uint8_t kind,uint16_t sequence,float value)
{
    uint8_t bit;
    if (!fiveAxisMode || CAN_DualAxisIsActive() || !stageOpen || sequence!=pathCount ||
        HAL_GetTick()-stageSince>=1500U || node<1U || node>CAN_AXIS_COUNT || kind>1U ||
        !isfinite(value)) { CAN_TrajectoryDiscardVelocity(); return -1; }
    bit=(uint8_t)(1U<<(node-1U));
    if (!(requiredMask&bit) || fabsf(value)>(kind?30.0f:MAX_TARGET_REV)) {
        CAN_TrajectoryDiscardVelocity(); return -1;
    }
    if (kind) { staged.velocity[node-1U]=value; velocityMask|=bit; }
    else { staged.target[node-1U]=value; targetMask|=bit; }
    return 0;
}

int CAN_MultiCommit(uint16_t sequence)
{
    uint8_t valid=fiveAxisMode && !CAN_DualAxisIsActive() && stageOpen && sequence==pathCount &&
        pathCount<CAN_TRAJECTORY_MAX_POINTS && targetMask==requiredMask && velocityMask==requiredMask &&
        HAL_GetTick()-stageSince<1500U;
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
    if (fiveAxisMode || CAN_DualAxisIsActive() || pathCount >= CAN_TRAJECTORY_MAX_POINTS ||
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
    if (pathCount == 0U || CAN_DualAxisIsActive() || (fiveAxisMode && (startMask!=requiredMask || stageOpen))) return -1;
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
            frame.data[0]==3U && (frame.data[5]&0x05U)==0x05U) {
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
    uint32_t now;
    process_rx();
    if (rxOverflow) {
        rxOverflow = 0U;
        rxTail = rxHead;
        SerialCmd_ResetPending();
        fault(1U);
        return;
    }
    now = HAL_GetTick();
    if (query_positions(now, CAN_DualAxisIsActive() ?
                        POSITION_QUERY_PERIOD_MS : IDLE_QUERY_PERIOD_MS) != 0 &&
        CAN_DualAxisIsActive()) {
        fault(1U);
        return;
    }
    if (now - lastStatus >= 500U) publish_status();
    if (!CAN_DualAxisIsActive()) return;
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
    if (target_reached()) {
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
