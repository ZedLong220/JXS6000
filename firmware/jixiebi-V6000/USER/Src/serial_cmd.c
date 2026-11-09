#include "serial_cmd.h"
#include "can_protocol.h"
#include "host_bridge.h"
#include "usart.h"
#include "gripper_servo.h"
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define RX_RING_SIZE 512U
#define LINE_SIZE 64U
#define PAIR_TIMEOUT_MS 1500U

static uint8_t rxByte, rxRing[RX_RING_SIZE];
static volatile uint16_t rxHead, rxTail;
static volatile uint8_t rxOverflow;
static char lineBuffer[LINE_SIZE];
static uint8_t lineLength, discardLine;
static float pendingTarget[2], pendingTime[2];
static float pathTarget[2], pathTime[2];
static uint8_t pendingMask, pathMask;
static uint32_t pendingTick, pathTick;
static uint16_t ackCounter;

void SerialCmd_ResetPending(void) { pendingMask = pathMask = 0U; CAN_TrajectoryDiscardVelocity(); }

static void reply(uint8_t byte) { HAL_UART_Transmit(&huart1, &byte, 1U, 50U); }

static int hex_value(char ch)
{
    if (ch >= '0' && ch <= '9') return ch - '0';
    if (ch >= 'A' && ch <= 'F') return ch - 'A' + 10;
    if (ch >= 'a' && ch <= 'f') return ch - 'a' + 10;
    return -1;
}

static void acknowledge(uint16_t id, const uint8_t *data, uint8_t length, int accepted)
{
    uint8_t ack[8] = {0};
    uint8_t checksum = (uint8_t)(id ^ (id >> 8) ^ length);
    for (uint8_t i = 0U; i < length; ++i) checksum ^= data[i];
    ++ackCounter;
    ack[0] = (uint8_t)ackCounter;
    ack[1] = (uint8_t)(ackCounter >> 8);
    ack[2] = (uint8_t)id;
    ack[3] = (uint8_t)(id >> 8);
    ack[4] = length;
    ack[5] = checksum;
    ack[6] = accepted ? 1U : 0U;
    CAN_SendHostFrame(CAN_CMD_HOST_ACK, ack, sizeof(ack));
}

static int process_control(uint8_t cmd, const uint8_t *data, uint8_t length)
{
    uint8_t index, isPath;
    float target, duration;
    uint32_t now = HAL_GetTick();
    if (cmd>=0x55U && cmd<=0x57U) {
        uint8_t status[8];
        if (cmd==0x55U) {
            float angle;
            if (length!=4U) return 0;
            memcpy(&angle,data,4U);
            if (Gripper_SetAngle(angle)!=0) return 0;
        } else if (cmd==0x56U) {
            if (length!=0U || Gripper_Disable()!=0) return 0;
        } else if (length!=0U) return 0;
        Gripper_GetStatus(status);
        return CAN_SendHostFrame(0x57U,status,8U)==0;
    }
    if (cmd>=0x50U && cmd<=0x54U) {
        float value; uint16_t sequence;
        static const uint8_t lengths[5]={2U,5U,6U,8U,2U};
        if (length!=lengths[cmd-0x50U]) {
            SerialCmd_ResetPending();
            return 0;
        }
        if (cmd==0x50U) {
            if (length!=2U) return 0;
            SerialCmd_ResetPending();
            return CAN_MultiBegin(data[0],data[1])==0;
        }
        if (cmd==0x51U) {
            if (length!=5U) return 0;
            memcpy(&value,data+1,4U);
            return CAN_MultiSetStart(data[0],value)==0;
        }
        if (cmd==0x52U) {
            if (length!=6U) return 0;
            memcpy(&value,data,4U); sequence=(uint16_t)(data[4]|((uint16_t)data[5]<<8));
            return CAN_MultiStage(sequence,value)==0;
        }
        if (cmd==0x53U) {
            if (length!=8U) return 0;
            sequence=(uint16_t)(data[2]|((uint16_t)data[3]<<8)); memcpy(&value,data+4,4U);
            return CAN_MultiValue(data[0],data[1],sequence,value)==0;
        }
        if (length!=2U) return 0;
        sequence=(uint16_t)(data[0]|((uint16_t)data[1]<<8));
        return CAN_MultiCommit(sequence)==0;
    }
    if (cmd==0x68U || cmd==0x64U) {
        float a,b;
        if (length!=8U) return 0;
        memcpy(&a,data,4U); memcpy(&b,data+4,4U);
        return (cmd==0x68U ? CAN_TrajectorySetStart(a,b) : CAN_TrajectorySetVelocity(a,b))==0;
    }
    if (cmd == CAN_CMD_STOP || (cmd == 0x04U && length >= 4U &&
        data[0] == 0U && data[1] == 0U && data[2] == 0U && data[3] == 0U)) {
        SerialCmd_ResetPending();
        CAN_DualAxisStop();
        return 1;
    }
    if (cmd == CAN_CMD_SET_HOME) {
        SerialCmd_ResetPending();
        CAN_DualAxisSetZero();
        return CAN_DualAxisGetState() != DUAL_AXIS_FAULT;
    }
    if (cmd == CAN_CMD_TRAJECTORY_CONTROL) {
        if (length != 1U || data[0] > 2U) return 0;
        pendingMask=pathMask=0U;
        if (data[0] == 0U) { SerialCmd_ResetPending(); CAN_TrajectoryClear(); return 1; }
        return CAN_TrajectoryStart(data[0] == 2U) == 0;
    }
    if (cmd == CAN_CMD_TRAJECTORY_SYNC) {
        if (length != 2U || data[0] != 0xFFU || data[1] != 0x66U ||
            pathMask != 3U || now - pathTick >= PAIR_TIMEOUT_MS) {
            pathMask = 0U;
            CAN_TrajectoryDiscardVelocity();
            return 0;
        }
        pathMask = 0U;
        return CAN_TrajectoryEnqueue(pathTarget[0], pathTarget[1],
                                    fmaxf(pathTime[0], pathTime[1])) == 0;
    }
    if (cmd != CAN_CMD_HOST_J2_POSITION_TIME && cmd != CAN_CMD_HOST_J3_POSITION_TIME &&
        cmd != CAN_CMD_HOST_J2_TRAJECTORY_POINT && cmd != CAN_CMD_HOST_J3_TRAJECTORY_POINT)
        return 0;
    isPath = cmd == CAN_CMD_HOST_J2_TRAJECTORY_POINT || cmd == CAN_CMD_HOST_J3_TRAJECTORY_POINT;
    index = (cmd == CAN_CMD_HOST_J3_POSITION_TIME || cmd == CAN_CMD_HOST_J3_TRAJECTORY_POINT) ? 1U : 0U;
    if (CAN_DualAxisIsActive() || length != 8U) { SerialCmd_ResetPending(); return 0; }
    memcpy(&target, data, 4U);
    memcpy(&duration, data + 4, 4U);
    if (!CAN_ValidateTarget(target, target, duration)) { SerialCmd_ResetPending(); return 0; }
    if (isPath) {
        if (now - pathTick >= PAIR_TIMEOUT_MS) { pathMask = 0U; CAN_TrajectoryDiscardVelocity(); }
        pathTarget[index] = target;
        pathTime[index] = duration;
        pathTick = now;
        pathMask |= (uint8_t)(1U << index);
    } else {
        if (now - pendingTick >= PAIR_TIMEOUT_MS) pendingMask = 0U;
        pendingTarget[index] = target;
        pendingTime[index] = duration;
        pendingTick = now;
        pendingMask |= (uint8_t)(1U << index);
        if (pendingMask == 3U) {
            pendingMask = 0U;
            return CAN_DualAxisMove(pendingTarget[0], pendingTarget[1],
                                   fmaxf(pendingTime[0], pendingTime[1])) == 0;
        }
    }
    return 1;
}

static int is_control(uint8_t cmd)
{
    return cmd == CAN_CMD_STOP || cmd == 0x04U || cmd == CAN_CMD_SET_HOME ||
           (cmd>=0x50U && cmd<=0x57U) ||
           cmd == CAN_CMD_TRAJECTORY_CONTROL || cmd == CAN_CMD_TRAJECTORY_SYNC ||
           cmd==0x68U || cmd==0x64U ||
           (cmd >= CAN_CMD_HOST_J2_POSITION_TIME && cmd <= CAN_CMD_HOST_J3_TRAJECTORY_POINT);
}

int SerialCmd_HandleCanFrame(uint16_t id, const uint8_t *data, uint8_t length)
{
    uint8_t node = (uint8_t)(id >> 7), cmd = (uint8_t)(id & 0x7FU);
    if (node == 0U && is_control(cmd)) {
        acknowledge(id, data, length, process_control(cmd, data, length));
        return 1;
    }
    /* The query has already reached the addressed drive on the shared bus.
       ACK it once for the HMI without retransmitting it or treating it as data. */
    if (node >= 1U && node <= 6U &&
        ((length == 0U && ((cmd >= 0x21U && cmd <= 0x2BU) || cmd == 0x35U)) ||
         ((node >= 1U && node <= CAN_AXIS_COUNT) && cmd == 0x12U && length >= 4U))) {
        acknowledge(id, data, length, 1);
        return 1;
    }
    return 0;
}

static void handle_slcan(const char *line)
{
    uint16_t id = 0U;
    uint8_t data[8] = {0}, length, node, cmd;
    int accepted = 0;
    size_t textLength = strlen(line);
    if (textLength < 5U) { reply('\a'); return; }
    for (uint8_t i = 1U; i <= 3U; ++i) {
        int value = hex_value(line[i]);
        if (value < 0) { reply('\a'); return; }
        id = (uint16_t)((id << 4) | (uint16_t)value);
    }
    {
        int value = hex_value(line[4]);
        if (value < 0 || value > 8) { reply('\a'); return; }
        length = (uint8_t)value;
    }
    if (id > 0x7FFU || textLength != (size_t)(5U + 2U * length)) { reply('\a'); return; }
    for (uint8_t i = 0U; i < length; ++i) {
        int hi = hex_value(line[5U + i * 2U]), lo = hex_value(line[6U + i * 2U]);
        if (hi < 0 || lo < 0) { reply('\a'); return; }
        data[i] = (uint8_t)((hi << 4) | lo);
    }
    node = (uint8_t)(id >> 7);
    cmd = (uint8_t)(id & 0x7FU);
    if (node == 0U && is_control(cmd)) accepted = process_control(cmd, data, length);
    else if (node >= 1U && node <= 6U && length == 0U &&
             ((cmd >= 0x21U && cmd <= 0x2BU) || cmd == 0x35U)) {
        accepted = (node < 1U || node > CAN_AXIS_COUNT) ||
                   CAN_SendHostFrame(id, data, length) == 0;
    } else if ((node >= 1U && node <= CAN_AXIS_COUNT) &&
               cmd == 0x12U && length >= 4U) {
        accepted = CAN_SendHostFrame(id, data, length) == 0;
    }
    acknowledge(id, data, length, accepted);
    reply(accepted ? '\r' : '\a');
}

static void handle_text(const char *line)
{
    if (strcmp(line, "stop") == 0) {
        SerialCmd_ResetPending();
        CAN_DualAxisStop();
    } else if (strcmp(line, "zero") == 0) {
        SerialCmd_ResetPending();
        CAN_DualAxisSetZero();
    } else if (strcmp(line, "status") == 0 || strcmp(line, "p") == 0) {
        CAN_DualAxisPrintStatus();
    } else if (strncmp(line, "move:", 5U) == 0) {
        char *end, *start;
        float j2, j3, duration;
        start = (char *)line + 5;
        j2 = strtof(start, &end);
        if (end == start || *end != ',') { printf("format: move:j2_rev,j3_rev,time_s\r\n"); return; }
        start = end + 1;
        j3 = strtof(start, &end);
        if (end == start || *end != ',') { printf("format: move:j2_rev,j3_rev,time_s\r\n"); return; }
        start = end + 1;
        duration = strtof(start, &end);
        if (end == start || *end != '\0' || CAN_DualAxisMove(j2, j3, duration) != 0)
            printf("[CAN42] move rejected\r\n");
    } else printf("commands: move:j2_rev,j3_rev,time_s | status | zero | stop\r\n");
}

static void handle_line(const char *line)
{
    if (strcmp(line, "S8") == 0 || strcmp(line, "O") == 0) {
        HostBridge_SetSession(1U);
        reply('\r');
    } else if (strcmp(line, "C") == 0) {
        SerialCmd_ResetPending();
        CAN_DualAxisStop();
        HostBridge_SetSession(0U);
        reply('\r');
    } else if (line[0] == 't') handle_slcan(line);
    else handle_text(line);
}

void SerialCmd_Init(void)
{
    rxHead = rxTail = 0U;
    rxOverflow = lineLength = discardLine = 0U;
    SerialCmd_ResetPending();
    ackCounter = 0U;
    HostBridge_Init();
    HAL_UART_Receive_IT(&huart1, &rxByte, 1U);
}

void SerialCmd_OnRxByte(uint8_t byte)
{
    uint16_t next = (uint16_t)((rxHead + 1U) % RX_RING_SIZE);
    if (next == rxTail) { rxOverflow = 1U; return; }
    rxRing[rxHead] = byte;
    __DMB();
    rxHead = next;
}

void SerialCmd_HandleRx(void)
{
    SerialCmd_OnRxByte(rxByte);
    HAL_UART_Receive_IT(&huart1, &rxByte, 1U);
}

void SerialCmd_HandleError(void)
{
    rxOverflow = 1U;
    HAL_UART_AbortReceive(&huart1);
    HAL_UART_Receive_IT(&huart1, &rxByte, 1U);
}

void SerialCmd_Process(void)
{
    if (rxOverflow) {
        rxOverflow = 0U;
        rxTail = rxHead;
        lineLength = 0U;
        discardLine = 1U;
        SerialCmd_ResetPending();
        CAN_DualAxisStop();
    }
    while (rxTail != rxHead) {
        uint8_t byte = rxRing[rxTail];
        rxTail = (uint16_t)((rxTail + 1U) % RX_RING_SIZE);
        if (byte == '\r' || byte == '\n') {
            if (!discardLine && lineLength != 0U) {
                lineBuffer[lineLength] = '\0';
                handle_line(lineBuffer);
            }
            lineLength = discardLine = 0U;
        } else if (!discardLine && lineLength + 1U < LINE_SIZE) {
            lineBuffer[lineLength++] = (char)byte;
        } else if (!discardLine) {
            discardLine = 1U;
            lineLength = 0U;
            SerialCmd_ResetPending();
            CAN_DualAxisStop();
        }
    }
    HostBridge_Process();
}
