#include "common_inc.h"
#include "configurations.h"
#include <can.h>
#include <cmath>
#include <cstring>


extern Motor motor;
extern EncoderCalibrator encoderCalibrator;

/*
 * 笛卡尔插补同步协议的单点缓存。
 * F407先发送0x08把本板目标位置和执行时间放入缓存，再用广播0x66
 * 通知所有驱动板同时提交。每块驱动板只需要缓存自己的一个电机目标。
 */
static volatile bool queuedTrajectoryValid = false;
static volatile int32_t queuedTrajectoryPosition = 0;
static volatile float queuedTrajectoryTime = 0.0f;
static float splinePosition[2], splineVelocity[2];
static uint8_t splineMask;
struct PendingSpline { float p0,p1,v0,v1,t; uint16_t seq,id; };
static PendingSpline pendingSpline;
static volatile bool pendingSplineValid=false;
extern CAN_TxHeaderTypeDef txHeader;

static float SplineFloat(const uint8_t *p) {
    float value; memcpy(&value,p,4U); return value;
}
static uint32_t EnterSplineCommit() { uint32_t key=__get_PRIMASK(); __disable_irq(); return key; }
static void LeaveSplineCommit(uint32_t key) { __set_PRIMASK(key); }
void PrepareSplineBlock() {
    uint32_t key=EnterSplineCommit();
    if(!pendingSplineValid) { LeaveSplineCommit(key); return; }
    PendingSpline pending=pendingSpline; pendingSplineValid=false;
    LeaveSplineCommit(key);
    BufferedPath &path=motor.motionPlanner.bufferedPath;
    if(path.session!=pending.id) return;
    float scale=(float)motor.MOTOR_ONE_CIRCLE_SUBDIVIDE_STEPS;
    int64_t p0=(int64_t)lroundf(pending.p0*scale)+motor.config.motionParams.encoderHomeOffset;
    int64_t p1=(int64_t)lroundf(pending.p1*scale)+motor.config.motionParams.encoderHomeOffset;
    if(p0<INT32_MIN || p0>INT32_MAX || p1<INT32_MIN || p1>INT32_MAX) { path.Fail(3); return; }
    path.Append((uint8_t)pending.seq,pending.id,(int32_t)p0,(int32_t)p1,
        (int32_t)lroundf(pending.v0*scale),(int32_t)lroundf(pending.v1*scale),
        pending.t,(float)boardConfig.velocityLimit,(float)boardConfig.velocityAcc,
        EnterSplineCommit,LeaveSplineCommit);
}
static void SendSplineStatus() {
    BufferedPath &path=motor.motionPlanner.bufferedPath;
    uint8_t data[8]={4U,path.state,path.count,path.index,path.error,0x05U,
                    (uint8_t)path.session,(uint8_t)(path.session>>8)};
    txHeader.StdId=(boardConfig.canNodeId<<7)|0x3CU;
    CAN_Send(&txHeader,data);
}

CAN_TxHeaderTypeDef txHeader =
    {
        .StdId = 0x00,
        .ExtId = 0x00,
        .IDE = CAN_ID_STD,
        .RTR = CAN_RTR_DATA,
        .DLC = 8,
        .TransmitGlobalTime = DISABLE
    };

void SendOne() {
    printf("send one begin\r\n");
    uint8_t TxData[8] = {0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08};
    CAN_TxHeaderTypeDef TxHeader;

    TxHeader.StdId = 0x123;
    TxHeader.ExtId = 0x00;
    TxHeader.RTR = CAN_RTR_DATA;
    TxHeader.IDE = CAN_ID_STD;
    TxHeader.DLC = 8;
    TxHeader.TransmitGlobalTime = DISABLE;

    if (HAL_CAN_AddTxMessage(&hcan, &TxHeader, TxData, &TxMailbox) != HAL_OK)
    {
        Error_Handler();
    }
    printf("send one \r\n");
}

static int32_t ParseLimitMoveVelocitySteps(uint8_t* _data, uint32_t _len)
{
    float speedRps = 0.0f;

    // data[0..3]可选传入找限位速度，单位沿用驱动协议的圈/s；0表示使用默认慢速。
    if (_len >= 4)
    {
        speedRps = *(float*) _data;
        if (speedRps < 0.0f)
            speedRps = -speedRps;
    }

    return (int32_t) (speedRps * (float) motor.MOTOR_ONE_CIRCLE_SUBDIVIDE_STEPS);
}


void SendLimitReached(uint8_t _side, uint8_t _fromCommand)
{
    uint8_t data[8] = {0};

    // 0x32回报给F407：data[0]=限位方向(1左/2右/3双边)，data[1]=1表示由找限位命令触发。
    data[0] = _side;
    data[1] = _fromCommand ? 1 : 0;
    data[2] = (uint8_t) motor.controller->state;
    data[3] = motor.controller->GetLimitInputState();
    txHeader.StdId = (boardConfig.canNodeId << 7) | 0x32;
    CAN_Send(&txHeader, data);
}


void SendLimitStatus(void)
{
    uint8_t data[8] = {0};

    /* 0x35回复当前状态：data[0]=0无/1左/2右/3双，data[1]=电机状态，data[2]=原始输入位图。 */
    data[0] = motor.controller->GetLimitInputState();
    data[1] = (uint8_t)motor.controller->state;
    data[2] = motor.controller->GetLimitInputState();
    txHeader.StdId = (boardConfig.canNodeId << 7) | 0x35;
    CAN_Send(&txHeader, data);
}


void OnCanCmd(uint8_t _cmd, uint8_t* _data, uint32_t _len)
{
    BufferedPath &path=motor.motionPlanner.bufferedPath;
    if (_cmd==0x3CU && _len==0U) {
        path.KeepAlive(); SendSplineStatus(); return;
    }
    if (_cmd==0x0DU) {
        if (_len!=2U) return;
        motor.controller->SetCtrlMode(Motor::MODE_STOP);
        path.Clear((uint16_t)(_data[0]|((uint16_t)_data[1]<<8)));
        splineMask=0; queuedTrajectoryValid=false;
        pendingSplineValid=false;
        return;
    }
    if (_cmd==0x0AU || _cmd==0x0BU) {
        if (_len!=8U || path.Active()) return;
        float a=SplineFloat(_data), b=SplineFloat(_data+4);
        if (!std::isfinite(a)||!std::isfinite(b)||fabsf(a)>40000||fabsf(b)>40000) {
            splineMask=0; path.Fail(3); return;
        }
        float *values=_cmd==0x0AU?splinePosition:splineVelocity;
        values[0]=a; values[1]=b;
        splineMask|=_cmd==0x0AU?1U:2U;
        return;
    }
    if (_cmd==0x0CU) {
        if (_len!=8U || path.Active()) return;
        uint16_t seq=(uint16_t)(_data[4]|((uint16_t)_data[5]<<8));
        uint16_t id=(uint16_t)(_data[6]|((uint16_t)_data[7]<<8));
        if (splineMask!=3U || seq>=BufferedPath::Capacity || pendingSplineValid) { path.Fail(3); return; }
        splineMask=0;
        pendingSpline=PendingSpline{splinePosition[0],splinePosition[1],splineVelocity[0],splineVelocity[1],SplineFloat(_data),seq,id};
        pendingSplineValid=true;
        return;
    }
    if (_cmd==0x66U && _len==8U && _data[0]==0xFFU && _data[1]==0x66U && _data[2]==4U) {
        if (boardConfig.canNodeId<1U || boardConfig.canNodeId>6U ||
            !(_data[3] & (1U<<(boardConfig.canNodeId-1U)))) return;
        uint16_t id=(uint16_t)(_data[6]|((uint16_t)_data[7]<<8));
        if ((_data[3]&~0x3FU)!=0U || !motor.controller->CanStartBufferedPath(path.blocks[0].p0)) {
            path.Fail(2); return;
        }
        if (path.Start(_data[4],id,_data[5]!=0U)) {
            queuedTrajectoryValid=false;
            motor.controller->SetCtrlMode(Motor::MODE_BUFFERED_PATH);
        }
        return;
    }
    float tmpF;
    int32_t tmpI;

    switch (_cmd)
    {
        // 0x00~0x0F No Memory CMDs
        case 0x01:  // Enable Motor
            motor.controller->requestMode = (*(uint32_t*) (RxData) == 1) ?
                                            Motor::MODE_COMMAND_VELOCITY : Motor::MODE_STOP;
            break;
        case 0x09:  // Emergency Stop: broadcast stop for all motors
            splineMask=0; queuedTrajectoryValid=false; pendingSplineValid=false;
            motor.controller->SetCtrlMode(Motor::MODE_STOP);
            path.Clear(path.session); // also invalidate an in-flight first block preparation
            motor.controller->SetVelocitySetPoint(0);
            motor.controller->SetCurrentSetPoint(0);
            motor.controller->modeRunning = Motor::MODE_STOP;
            break;
        case 0x02:  // Do Calibration
            encoderCalibrator.isTriggered = true;
            break;
        case 0x03:  // Set Current SetPoint
            if (motor.controller->modeRunning != Motor::MODE_COMMAND_CURRENT)
                motor.controller->SetCtrlMode(Motor::MODE_COMMAND_CURRENT);
            motor.controller->SetCurrentSetPoint((int32_t) (*(float*) RxData * 1000));
            break;
        case 0x04:  // Set Velocity SetPoint
            if (motor.controller->modeRunning != Motor::MODE_COMMAND_VELOCITY)
            {
                motor.config.motionParams.ratedVelocity = boardConfig.velocityLimit;
                motor.controller->SetCtrlMode(Motor::MODE_COMMAND_VELOCITY);
            }
            motor.controller->SetVelocitySetPoint(
                (int32_t) (*(float*) RxData *
                           (float) motor.MOTOR_ONE_CIRCLE_SUBDIVIDE_STEPS));
            //printf("vel: %d\n", (int32_t) (*(float*) RxData));
            break;
        case 0x05:  // Set Position SetPoint
            if (motor.controller->modeRunning != Motor::MODE_COMMAND_POSITION)
            {
                motor.config.motionParams.ratedVelocity = boardConfig.velocityLimit;
                motor.controller->SetCtrlMode(Motor::MODE_COMMAND_POSITION);
            }
            motor.controller->SetPositionSetPoint(
                (int32_t) (*(float*) RxData * (float) motor.MOTOR_ONE_CIRCLE_SUBDIVIDE_STEPS));
            //printf("SET MOTOR[0x05] POSITION[]\r\n");
            if (_data[4]) // Need Position & Finished ACK
            {
                tmpF = motor.controller->GetPosition();
                auto* b = (unsigned char*) &tmpF;
                for (int i = 0; i < 4; i++)
                    _data[i] = *(b + i);
                _data[4] = motor.controller->state == Motor::STATE_FINISH ? 1 : 0;
                txHeader.StdId = (boardConfig.canNodeId << 7) | 0x23;
                CAN_Send(&txHeader, _data);
            }
            break;
        case 0x06:  // Set Position with Time
            if (motor.controller->modeRunning != Motor::MODE_COMMAND_POSITION)
                motor.controller->SetCtrlMode(Motor::MODE_COMMAND_POSITION);
            motor.controller->SetPositionSetPointWithTime(
                (int32_t) (*(float*) RxData * (float) motor.MOTOR_ONE_CIRCLE_SUBDIVIDE_STEPS),
                *(float*) (RxData + 4));
            if (_data[4]) // Need Position & Finished ACK
            {
                tmpF = motor.controller->GetPosition();
                auto* b = (unsigned char*) &tmpF;
                for (int i = 0; i < 4; i++)
                    _data[i] = *(b + i);
                _data[4] = motor.controller->state == Motor::STATE_FINISH ? 1 : 0;
                txHeader.StdId = (boardConfig.canNodeId << 7) | 0x23;
                CAN_Send(&txHeader, _data);
            }
            break;
        case 0x07:  // Set Position with Velocity-Limit
        {
            if (motor.controller->modeRunning != Motor::MODE_COMMAND_POSITION)
            {
                motor.config.motionParams.ratedVelocity = boardConfig.velocityLimit;
                motor.controller->SetCtrlMode(Motor::MODE_COMMAND_POSITION);
            }
            motor.config.motionParams.ratedVelocity =
                (int32_t) (*(float*) (RxData + 4) * (float) motor.MOTOR_ONE_CIRCLE_SUBDIVIDE_STEPS);
            motor.controller->SetPositionSetPoint(
                (int32_t) (*(float*) RxData * (float) motor.MOTOR_ONE_CIRCLE_SUBDIVIDE_STEPS));
            // Always Need Position & Finished ACK
            tmpF = motor.controller->GetPosition();
            auto* b = (unsigned char*) &tmpF;
            for (int i = 0; i < 4; i++)
                _data[i] = *(b + i);
            _data[4] = motor.controller->state == Motor::STATE_FINISH ? 1 : 0;
            txHeader.StdId = (boardConfig.canNodeId << 7) | 0x23;
            CAN_Send(&txHeader, _data);
        }
            break;

        case 0x08:  // Queue Position with Time, commit after broadcast 0x66
        {
            /* 0x08数据布局与0x06一致：位置圈数(float)+时间秒(float)。 */
            if (_len < 8)
                break;

            float trajectoryTime = *(float*) (RxData + 4);
            /* 非法或过小时间不能交给sqrtf，避免位置规划器出现NaN。 */
            if (!(trajectoryTime > 0.0f))
                trajectoryTime = 0.01f;
            if (trajectoryTime < 0.01f)
                trajectoryTime = 0.01f;

            queuedTrajectoryPosition =
                (int32_t) (*(float*) RxData * (float) motor.MOTOR_ONE_CIRCLE_SUBDIVIDE_STEPS);
            queuedTrajectoryTime = trajectoryTime;
            queuedTrajectoryValid = true;
        }
            break;

        case 0x66:  // Broadcast trajectory sync: commit the cached point
            /* 只接受F407约定的0xFF,0x66同步标记，避免误触发。 */
            if (_len == 2 && _data[0] == 0xFF && _data[1] == 0x66 && queuedTrajectoryValid)
            {
                /* 提交时才切换位置模式和目标，保证各板等待同一个同步帧。 */
                if (motor.controller->modeRunning != Motor::MODE_COMMAND_POSITION)
                    motor.controller->SetCtrlMode(Motor::MODE_COMMAND_POSITION);
                motor.controller->SetPositionSetPointWithTime(
                    queuedTrajectoryPosition, queuedTrajectoryTime);
                queuedTrajectoryValid = false;
            }
            break;

        case 0x30:  // Move to left limit, stop and report 0x32 when LIMIT_LEFT(PB0) is low
            motor.controller->MoveToLeftLimit(ParseLimitMoveVelocitySteps(_data, _len));
            break;

        case 0x31:  // Move to right limit, stop and report 0x32 when LIMIT_RIGHT(PB1) is low
            motor.controller->MoveToRightLimit(ParseLimitMoveVelocitySteps(_data, _len));
            break;

        case 0x33:  // Apply current position as volatile home; do not write EEPROM
        {
            uint8_t data[8] = {0};
            uint8_t limitState = motor.controller->GetLimitInputState();
            if (limitState == Motor::LIMIT_SIDE_RIGHT)
            {
                motor.controller->ApplyPosAsHomeOffset();
                data[0] = 1;
            }
            data[1] = limitState;
            txHeader.StdId = (boardConfig.canNodeId << 7) | 0x33;
            CAN_Send(&txHeader, data);
        }
            break;

        case 0x39: // Buffered path protocol version + encoder/spline capabilities
        {
            if (_len!=0U) break;
            uint8_t data[8]={4U,0x05U,BufferedPath::Capacity,0U};
            txHeader.StdId=(boardConfig.canNodeId<<7)|0x39U;
            CAN_Send(&txHeader,data);
        }
            break;
        case 0x35:  // Get current left/right limit input state
            SendLimitStatus();
            break;

            // 0x10~0x1F CMDs with Memory
        case 0x11:  // Set Node-ID and Store to EEPROM
            boardConfig.canNodeId = *(uint32_t*) (RxData);
            if (_data[4])
                boardConfig.configStatus = CONFIG_COMMIT;
            break;
        case 0x12:  // Set Current-Limit and Store to EEPROM
            motor.config.motionParams.ratedCurrent = (int32_t) (*(float*) RxData * 1000);
            boardConfig.currentLimit = motor.config.motionParams.ratedCurrent;
            if (_data[4])
                boardConfig.configStatus = CONFIG_COMMIT;
            break;
        case 0x13:  // Set Velocity-Limit and Store to EEPROM
            motor.config.motionParams.ratedVelocity =
                (int32_t) (*(float*) RxData *
                           (float) motor.MOTOR_ONE_CIRCLE_SUBDIVIDE_STEPS);
            boardConfig.velocityLimit = motor.config.motionParams.ratedVelocity;
            if (_data[4])
                boardConfig.configStatus = CONFIG_COMMIT;
            break;
        case 0x14:  // Set Acceleration （and Store to EEPROM）
            tmpF = *(float*) RxData * (float) motor.MOTOR_ONE_CIRCLE_SUBDIVIDE_STEPS;

            motor.config.motionParams.ratedVelocityAcc = (int32_t) tmpF;
            motor.motionPlanner.velocityTracker.SetVelocityAcc((int32_t) tmpF);
            motor.motionPlanner.positionTracker.SetVelocityAcc((int32_t) tmpF);
            boardConfig.velocityAcc = motor.config.motionParams.ratedVelocityAcc;
            if (_data[4])
                boardConfig.configStatus = CONFIG_COMMIT;
            break;
        case 0x15:  // Apply Home-Position and Store to EEPROM
            motor.controller->ApplyPosAsHomeOffset();
            boardConfig.encoderHomeOffset = motor.config.motionParams.encoderHomeOffset %
                                            motor.MOTOR_ONE_CIRCLE_SUBDIVIDE_STEPS;
            boardConfig.configStatus = CONFIG_COMMIT;
            break;
        case 0x16:  // Set Auto-Enable and Store to EEPROM
            boardConfig.enableMotorOnBoot = (*(uint32_t*) (RxData) == 1);
            if (_data[4])
                boardConfig.configStatus = CONFIG_COMMIT;
            break;
        case 0x17:  // Set DCE Kp
            motor.config.ctrlParams.dce.kp = *(int32_t*) (RxData);
            boardConfig.dce_kp = motor.config.ctrlParams.dce.kp;
            if (_data[4])
                boardConfig.configStatus = CONFIG_COMMIT;
            break;
        case 0x18:  // Set DCE Kv
            motor.config.ctrlParams.dce.kv = *(int32_t*) (RxData);
            boardConfig.dce_kv = motor.config.ctrlParams.dce.kv;
            if (_data[4])
                boardConfig.configStatus = CONFIG_COMMIT;
            break;
        case 0x19:  // Set DCE Ki
            motor.config.ctrlParams.dce.ki = *(int32_t*) (RxData);
            boardConfig.dce_ki = motor.config.ctrlParams.dce.ki;
            if (_data[4])
                boardConfig.configStatus = CONFIG_COMMIT;
            break;
        case 0x1A:  // Set DCE Kd
            motor.config.ctrlParams.dce.kd = *(int32_t*) (RxData);
            boardConfig.dce_kd = motor.config.ctrlParams.dce.kd;
            if (_data[4])
                boardConfig.configStatus = CONFIG_COMMIT;
            break;
        case 0x1B:  // Set Enable Stall-Protect
            motor.config.ctrlParams.stallProtectSwitch = (*(uint32_t*) (RxData) == 1);
            boardConfig.enableStallProtect = motor.config.ctrlParams.stallProtectSwitch;
            if (_data[4])
                boardConfig.configStatus = CONFIG_COMMIT;
            break;


            // 0x20~0x2F Inquiry CMDs
        case 0x21: // Get Current
        {
            tmpF = motor.controller->GetFocCurrent();
            auto* b = (unsigned char*) &tmpF;
            for (int i = 0; i < 4; i++)
                _data[i] = *(b + i);
            _data[4] = (motor.controller->state == Motor::STATE_FINISH ? 1 : 0);

            txHeader.StdId = (boardConfig.canNodeId << 7) | 0x21;
            CAN_Send(&txHeader, _data);
        }
            break;
        case 0x22: // Get Velocity
        {
            tmpF = motor.controller->GetVelocity();
            auto* b = (unsigned char*) &tmpF;
            for (int i = 0; i < 4; i++)
                _data[i] = *(b + i);
            _data[4] = (motor.controller->state == Motor::STATE_FINISH ? 1 : 0);

            txHeader.StdId = (boardConfig.canNodeId << 7) | 0x22;
            CAN_Send(&txHeader, _data);
        }
            break;
        case 0x23: // Get Position
        {
            tmpF = motor.controller->GetPosition();
            auto* b = (unsigned char*) &tmpF;
            for (int i = 0; i < 4; i++)
                _data[i] = *(b + i);
            // Finished ACK
            _data[4] = motor.controller->state == Motor::STATE_FINISH ? 1 : 0;
            txHeader.StdId = (boardConfig.canNodeId << 7) | 0x23;
            CAN_Send(&txHeader, _data);
//            printf("CAN SEND BACK to NODE[%d]\n", boardConfig.canNodeId );
        }
            break;
        case 0x24: // Get Offset
        {
            tmpI = motor.config.motionParams.encoderHomeOffset;
            auto* b = (unsigned char*) &tmpI;
            for (int i = 0; i < 4; i++)
                _data[i] = *(b + i);
            txHeader.StdId = (boardConfig.canNodeId << 7) | 0x24;
            CAN_Send(&txHeader, _data);
        }
            break;

        case 0x25: // Get temperature
        {
            auto* b = (unsigned char*) &boardConfig.motor_temperature;
            for (int i = 0; i < 4; i++)
                _data[i] = *(b + i);
            _data[4] = 0;
            _data[5] = 0;
            _data[6] = 0;
            _data[7] = 0;
            txHeader.StdId = (boardConfig.canNodeId << 7) | 0x25;
            CAN_Send(&txHeader, _data);
        }
            break;


        case 0x26: // Get Limit-Current
        {
            float currentLimitFloat = (float)boardConfig.currentLimit;
            // 将浮点数转换为字节序列
            unsigned char* b = (unsigned char*)&currentLimitFloat;
            for (int i = 0; i < 4; i++)
                _data[i] = *(b + i);
            _data[4] = 0;
            _data[5] = 0;
            _data[6] = 0;
            _data[7] = 0;
            txHeader.StdId = (boardConfig.canNodeId << 7) | 0x26;
            CAN_Send(&txHeader, _data);
        }
            break;
        case 0x27: // Get Velocity-Limit
        {
            tmpF = (float)boardConfig.velocityLimit / (float)motor.MOTOR_ONE_CIRCLE_SUBDIVIDE_STEPS;
            auto* b = (unsigned char*) &tmpF;
            for (int i = 0; i < 4; i++)
                _data[i] = *(b + i);
            _data[4] = 0;
            _data[5] = 0;
            _data[6] = 0;
            _data[7] = 0;
            txHeader.StdId = (boardConfig.canNodeId << 7) | 0x27;
            CAN_Send(&txHeader, _data);
        }
            break;
        case 0x28: // Get DCE Kp
        {
            tmpF = (float)boardConfig.dce_kp;
            auto* b = (unsigned char*) &tmpF;
            for (int i = 0; i < 4; i++)
                _data[i] = *(b + i);
            _data[4] = 0;
            _data[5] = 0;
            _data[6] = 0;
            _data[7] = 0;
            txHeader.StdId = (boardConfig.canNodeId << 7) | 0x28;
            CAN_Send(&txHeader, _data);
        }
            break;
        case 0x29: // Get DCE Kv
        {
            tmpF = (float)boardConfig.dce_kv;
            auto* b = (unsigned char*) &tmpF;
            for (int i = 0; i < 4; i++)
                _data[i] = *(b + i);
            _data[4] = 0;
            _data[5] = 0;
            _data[6] = 0;
            _data[7] = 0;
            txHeader.StdId = (boardConfig.canNodeId << 7) | 0x29;
            CAN_Send(&txHeader, _data);
        }
            break;
        case 0x2A: // Get DCE Ki
        {
            tmpF = (float)boardConfig.dce_ki;
            auto* b = (unsigned char*) &tmpF;
            for (int i = 0; i < 4; i++)
                _data[i] = *(b + i);
            _data[4] = 0;
            _data[5] = 0;
            _data[6] = 0;
            _data[7] = 0;
            txHeader.StdId = (boardConfig.canNodeId << 7) | 0x2A;
            CAN_Send(&txHeader, _data);
        }
            break;
        case 0x2B: // Get DCE Kd
        {
            tmpF = (float)boardConfig.dce_kd;
            auto* b = (unsigned char*) &tmpF;
            for (int i = 0; i < 4; i++)
                _data[i] = *(b + i);
            _data[4] = 0;
            _data[5] = 0;
            _data[6] = 0;
            _data[7] = 0;
            txHeader.StdId = (boardConfig.canNodeId << 7) | 0x2B;
            CAN_Send(&txHeader, _data);
        }
            break;


        case 0x7d:  // enable motor temperature watch
            boardConfig.enableTempWatch = true;
            break;
        case 0x7e:  // Erase Configs
            boardConfig.configStatus = CONFIG_RESTORE;
            break;
        case 0x7f:  // Reboot
            HAL_NVIC_SystemReset();
            break;
        default:
            break;
    }

}
