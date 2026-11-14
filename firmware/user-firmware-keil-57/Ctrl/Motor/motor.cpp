#include "configurations.h"
#include "motor.h"
#include "gpio.h"

#include <cmath>

/*
 * 电机闭环控制实现。
 *
 * 控制链路可以概括为：
 *   编码器 -> 位置/速度估计 -> 限位保护 -> 软目标规划
 *   -> DCE/PID/电流控制器 -> FOC 驱动输出。
 *
 * Motor::Tick20kHz() 由定时器中断周期调用，控制周期为 50 us。
 * 这里的 position、velocity 均使用驱动板内部的细分步单位：
 * 一圈 = MOTOR_ONE_CIRCLE_SUBDIVIDE_STEPS = 200 * 256 = 51200。
 */

/* PB0 左限位为低电平有效。 */
static bool IsLeftLimitTriggered()
{
    return HAL_GPIO_ReadPin(LIMIT_LEFT_GPIO_Port, LIMIT_LEFT_Pin) == GPIO_PIN_RESET;
}


/* PB1 右限位为低电平有效。 */
static bool IsRightLimitTriggered()
{
    return HAL_GPIO_ReadPin(LIMIT_RIGHT_GPIO_Port, LIMIT_RIGHT_Pin) == GPIO_PIN_RESET;
}


/* 将两个限位输入整理为位图：bit0=左限位，bit1=右限位。 */
static uint8_t ReadLimitInputState()
{
    uint8_t state = Motor::LIMIT_SIDE_NONE;

    if (IsLeftLimitTriggered())
        state |= Motor::LIMIT_SIDE_LEFT;
    if (IsRightLimitTriggered())
        state |= Motor::LIMIT_SIDE_RIGHT;

    return state;
}


void Motor::Tick20kHz()
{
    /* 第一步：读取并更新编码器角度。 */
    encoder->UpdateAngle();

    /* 第二步：执行一次完整的闭环控制计算。 */
    CloseLoopControlTick();
}


/* 绑定编码器对象，必须在开始运行控制循环前完成。 */
void Motor::AttachEncoder(EncoderBase* _encoder)
{
    encoder = _encoder;
}


/* 绑定 FOC 驱动对象，用于设置电流矢量、休眠或刹车。 */
void Motor::AttachDriver(DriverBase* _driver)
{
    driver = _driver;
}


/*
 * 20 kHz 闭环控制主流程。
 *
 * 注意：本函数前半段使用本周期的软目标计算控制输出，后半段更新下一周期
 * 使用的软目标。因此模式切换和新目标通常会在下一个控制周期完全生效。
 */
void Motor::CloseLoopControlTick()
{
    /************************************ 首次调用：建立连续位置基准 ************************************/
    static bool isFirstCalled = true;
    if (isFirstCalled)
    {
        int32_t angle;
        /*
         * 编码器输出是单圈角度。根据 home offset 把它映射到离当前零位最近
         * 的一圈，避免上电时把跨圈位置误判成大幅运动。
         */
        if (config.motionParams.encoderHomeOffset < MOTOR_ONE_CIRCLE_SUBDIVIDE_STEPS / 2)
        {
            angle =
                encoder->angleData.rectifiedAngle >
                config.motionParams.encoderHomeOffset + MOTOR_ONE_CIRCLE_SUBDIVIDE_STEPS / 2 ?
                encoder->angleData.rectifiedAngle - MOTOR_ONE_CIRCLE_SUBDIVIDE_STEPS :
                encoder->angleData.rectifiedAngle;
        } else
        {
            angle =
                encoder->angleData.rectifiedAngle <
                config.motionParams.encoderHomeOffset - MOTOR_ONE_CIRCLE_SUBDIVIDE_STEPS / 2 ?
                encoder->angleData.rectifiedAngle + MOTOR_ONE_CIRCLE_SUBDIVIDE_STEPS :
                encoder->angleData.rectifiedAngle;
        }

        /* 首次调用没有速度信息，因此实位置和上一拍位置都初始化为同一值。 */
        controller->realLapPosition = angle;
        controller->realLapPositionLast = angle;
        controller->realPosition = angle;
        controller->realPositionLast = angle;

        isFirstCalled = false;
        return;
    }

    /********************************* 更新编码器位置 *********************************/
    int32_t deltaLapPosition;

    /* 保存上一拍单圈角度，再读取本拍的编码器角度。 */
    controller->realLapPositionLast = controller->realLapPosition;
    controller->realLapPosition = encoder->angleData.rectifiedAngle;

    /*
     * 计算单圈位置差，并处理 0/满量程边界跳变。
     * 例如从 51199 变为 0，应被解释为 +1，而不是大幅反向运动。
     */
    deltaLapPosition = controller->realLapPosition - controller->realLapPositionLast;
    if (deltaLapPosition > MOTOR_ONE_CIRCLE_SUBDIVIDE_STEPS >> 1)
        deltaLapPosition -= MOTOR_ONE_CIRCLE_SUBDIVIDE_STEPS;
    else if (deltaLapPosition < -MOTOR_ONE_CIRCLE_SUBDIVIDE_STEPS >> 1)
        deltaLapPosition += MOTOR_ONE_CIRCLE_SUBDIVIDE_STEPS;

    /* 将单圈增量累加为连续多圈位置。 */
    controller->realPositionLast = controller->realPosition;
    controller->realPosition += deltaLapPosition;

    /********************************* 估计位置和速度 *********************************/
    /*
     * 速度估计采用带余数的离散积分和一阶平滑。
     * estVelocityIntegral 保存除以 2^5 后的余数，减少整数运算量化误差。
     */
    controller->estVelocityIntegral += (
        (controller->realPosition - controller->realPositionLast) * motionPlanner.CONTROL_FREQUENCY
        + ((controller->estVelocity << 5) - controller->estVelocity)
    );
    controller->estVelocity = controller->estVelocityIntegral >> 5;
    controller->estVelocityIntegral -= (controller->estVelocity << 5);

    /*
     * 根据当前速度补偿电机的电角度/位置滞后，得到用于控制的预测位置。
     * realPosition 是编码器实测位置，estPosition 是闭环控制使用的位置。
     */
    controller->estLeadPosition = Controller::CompensateAdvancedAngle(controller->estVelocity);
    controller->estPosition = controller->realPosition + controller->estLeadPosition;

    /* 记录软目标与估计位置之间的误差，便于调试和保护逻辑使用。 */
    controller->estError = controller->softPosition - controller->estPosition;

    // 限位光眼默认高电平，低电平触发；默认负方向为左，正方向为右。
    controller->ApplyLimitProtection();
    if (motionPlanner.bufferedPath.Active()) {
        uint8_t pathError = controller->BufferedPathSafetyError();
        if (pathError != BufferedPath::ErrorNone) {
            motionPlanner.bufferedPath.Fail(pathError);
            controller->requestMode = controller->modeRunning = MODE_STOP;
        }
    }

    /************************************ 控制器输出 ************************************/
    if (controller->isStalled ||
        controller->softDisable ||
        !encoder->IsCalibrated())
    {
        /* 失能、未校准或堵转时清除积分，关闭 FOC 输出。 */
        controller->ClearIntegral();
        controller->focPosition = 0;
        controller->focCurrent = 0;
        driver->Sleep();
    } else if (controller->softBrake)
    {
        /* 刹车模式同样不保留积分，避免解除刹车后突然释放累积电流。 */
        controller->ClearIntegral();
        controller->focPosition = 0;
        controller->focCurrent = 0;
        driver->Brake();
    } else
    {
        switch (controller->modeRunning)
        {
            case MODE_BUFFERED_PATH:
                controller->CalcDceToOutput(controller->softPosition, controller->softVelocity);
                break;
            case MODE_STEP_DIR:
                /* STEP/DIR 输入最终也转换为位置闭环控制。 */
                controller->CalcDceToOutput(controller->softPosition, controller->softVelocity);
                break;
            case MODE_STOP:
                driver->Sleep();
                break;
            case MODE_COMMAND_Trajectory:
                /* 轨迹模式同时使用软位置和软速度计算 DCE 输出。 */
                controller->CalcDceToOutput(controller->softPosition, controller->softVelocity);
                break;
            case MODE_COMMAND_CURRENT:
                /* 电流模式跳过位置环，直接输出目标电流矢量。 */
                controller->CalcCurrentToOutput(controller->softCurrent);
                break;
            case MODE_COMMAND_VELOCITY:
                /* 速度模式使用速度 PID，将速度误差转换为电流。 */
                controller->CalcPidToOutput(controller->softVelocity);
                break;
            case MODE_COMMAND_POSITION:
                /* 位置模式使用 DCE：位置误差 + 速度误差共同决定电流。 */
                controller->CalcDceToOutput(controller->softPosition, controller->softVelocity);
                break;
            case MODE_PWM_CURRENT:
                controller->CalcCurrentToOutput(controller->softCurrent);
                break;
            case MODE_PWM_VELOCITY:
                controller->CalcPidToOutput(controller->softVelocity);
                break;
            case MODE_PWM_POSITION:
                controller->CalcDceToOutput(controller->softPosition, controller->softVelocity);
                break;
            default:
                break;
        }
    }

    /******************************* 处理模式切换 ********************************/
    if (controller->modeRunning != controller->requestMode)
    {
        controller->modeRunning = controller->requestMode;
        controller->softNewCurve = true;
    }

    /******************************* 限制硬目标 ********************************/
    /* 上层命令可以直接写入 goalVelocity/goalCurrent，这里统一做幅值限幅。 */
    if (controller->goalVelocity > config.motionParams.ratedVelocity)
        controller->goalVelocity = config.motionParams.ratedVelocity;
    else if (controller->goalVelocity < -config.motionParams.ratedVelocity)
        controller->goalVelocity = -config.motionParams.ratedVelocity;
    if (controller->goalCurrent > config.motionParams.ratedCurrent)
        controller->goalCurrent = config.motionParams.ratedCurrent;
    else if (controller->goalCurrent < -config.motionParams.ratedCurrent)
        controller->goalCurrent = -config.motionParams.ratedCurrent;

    /******************************** 运动规划器 *********************************/
    if ((controller->softDisable && !controller->goalDisable) ||
        (controller->softBrake && !controller->goalBrake))
    {
        controller->softNewCurve = true;
    }

    if (controller->softNewCurve)
    {
        /*
         * 新任务开始时，以当前估计位置/速度作为规划器初始状态，
         * 同时清空控制器积分，防止上一段运动的残留影响本段运动。
         */
        controller->softNewCurve = false;
        controller->ClearIntegral();
        controller->ClearStallFlag();

        switch (controller->modeRunning)
        {
            case MODE_BUFFERED_PATH:
                controller->softPosition = motionPlanner.bufferedPath.position;
                controller->softVelocity = motionPlanner.bufferedPath.velocity;
                break;
            /* 停止模式不需要启动运动规划器，输出会保持关闭。 */
            case MODE_STOP:
                break;

            /*
             * 普通位置模式：
             * 以当前估计位置和速度作为规划器起点，避免切换模式时
             * 软目标突然跳变，随后由 positionTracker 生成 S 型位置/速度曲线。
             */
            case MODE_COMMAND_POSITION:
                motionPlanner.positionTracker.NewTask(controller->estPosition, controller->estVelocity);
                break;

            /*
             * 普通速度模式：
             * 以当前估计速度作为起点，避免速度指令切换时产生突变。
             * 后续由 velocityTracker 按加速度和 jerk 限制逐步改变软速度。
             */
            case MODE_COMMAND_VELOCITY:
                motionPlanner.velocityTracker.NewTask(controller->estVelocity);
                break;

            /*
             * 普通电流模式：
             * 以当前 FOC 电流作为起点，后续由 currentTracker 按电流加速度
             * 平滑过渡到新的目标电流，避免电流阶跃。
             */
            case MODE_COMMAND_CURRENT:
                motionPlanner.currentTracker.NewTask(controller->focCurrent);
                break;

            /*
             * 轨迹模式：
             * 记录当前估计位置和速度，作为后续轨迹点插补的初始状态。
             * 轨迹命令同时包含位置和速度信息。
             */
            case MODE_COMMAND_Trajectory:
                motionPlanner.trajectoryTracker.NewTask(controller->estPosition, controller->estVelocity);
                break;

            /*
             * PWM 位置模式：
             * 与普通位置模式共用 positionTracker，区别在于后续输出模式
             * 由 PWM 位置控制路径处理。
             */
            case MODE_PWM_POSITION:
                motionPlanner.positionTracker.NewTask(controller->estPosition, controller->estVelocity);
                break;

            /* PWM 速度模式与普通速度模式共用速度规划器。 */
            case MODE_PWM_VELOCITY:
                motionPlanner.velocityTracker.NewTask(controller->estVelocity);
                break;

            /* PWM 电流模式与普通电流模式共用电流规划器。 */
            case MODE_PWM_CURRENT:
                motionPlanner.currentTracker.NewTask(controller->focCurrent);
                break;

            /*
             * STEP/DIR 模式：
             * positionInterpolator 根据外部步进位置增量生成软位置和软速度。
             * 模式切换时把当前位置作为新的内部起点，避免把旧的绝对目标
             * 当成新的 STEP/DIR 增量而导致电机突然运动。
             */
            case MODE_STEP_DIR:
                motionPlanner.positionInterpolator.NewTask(controller->estPosition, controller->estVelocity);
                controller->goalPosition = controller->estPosition;
                break;

            /* 未知模式不初始化任何规划器，保持现有控制状态。 */
            default:
                break;
        }
    }

    /******************************* 更新软目标 ********************************/
    /*
     * 硬目标是上层命令给出的最终目标，软目标经过 S 型加减速规划，
     * 控制器只跟踪 softPosition/softVelocity/softCurrent。
     */
    switch (controller->modeRunning)
    {
        case MODE_BUFFERED_PATH:
            motionPlanner.bufferedPath.Tick();
            controller->softPosition = motionPlanner.bufferedPath.position;
            controller->softVelocity = motionPlanner.bufferedPath.velocity;
            controller->goalPosition = controller->softPosition;
            controller->goalVelocity = controller->softVelocity;
            if (motionPlanner.bufferedPath.state == 4U) {
                controller->requestMode = controller->modeRunning = MODE_STOP;
                driver->Sleep();
            }
            break;
        /*
         * 停止模式：
         * 不再更新软目标。控制输出部分会调用 driver->Sleep()，
         * 电机保持停止状态。
         */
        case MODE_STOP:
            break;

        /*
         * 普通位置模式：
         * positionTracker 根据硬目标 goalPosition 计算当前周期的软位置
         * 和软速度，形成带加速度/jerk 限制的 S 型运动曲线。
         */
        case MODE_COMMAND_POSITION:
            motionPlanner.positionTracker.CalcSoftGoal(controller->goalPosition);
            /* go_location 是本周期要跟踪的软位置。 */
            controller->softPosition = motionPlanner.positionTracker.go_location;
            /* go_velocity 是本周期的位置环速度前馈/阻尼目标。 */
            controller->softVelocity = motionPlanner.positionTracker.go_velocity;
            break;

        /*
         * 普通速度模式：
         * velocityTracker 将目标速度逐步逼近 goalVelocity，限制加速度和 jerk，
         * 防止速度指令瞬间变化导致电流突变。
         */
        case MODE_COMMAND_VELOCITY:
            motionPlanner.velocityTracker.CalcSoftGoal(controller->goalVelocity);
            /* 速度模式没有新的软位置，只更新软速度。 */
            controller->softVelocity = motionPlanner.velocityTracker.goVelocity;
            break;

        /*
         * 普通电流模式：
         * currentTracker 将当前 FOC 电流平滑过渡到 goalCurrent，
         * 避免直接改变电流目标造成电机冲击。
         */
        case MODE_COMMAND_CURRENT:
            motionPlanner.currentTracker.CalcSoftGoal(controller->goalCurrent);
            /* softCurrent 随后由 CalcCurrentToOutput() 转换为 FOC 电流矢量。 */
            controller->softCurrent = motionPlanner.currentTracker.goCurrent;
            break;

        /*
         * 轨迹模式：
         * trajectoryTracker 同时处理位置和速度目标，适用于连续轨迹点执行。
         */
        case MODE_COMMAND_Trajectory:
            motionPlanner.trajectoryTracker.CalcSoftGoal(controller->goalPosition, controller->goalVelocity);
            controller->softPosition = motionPlanner.trajectoryTracker.goPosition;
            controller->softVelocity = motionPlanner.trajectoryTracker.goVelocity;
            break;

        /*
         * PWM 位置模式：
         * 与普通位置模式使用同一个位置规划器，输出阶段再根据 PWM 模式选择
         * 对应的控制路径。
         */
        case MODE_PWM_POSITION:
            motionPlanner.positionTracker.CalcSoftGoal(controller->goalPosition);
            controller->softPosition = motionPlanner.positionTracker.go_location;
            controller->softVelocity = motionPlanner.positionTracker.go_velocity;
            break;

        /* PWM 速度模式：复用速度规划器，只更新软速度目标。 */
        case MODE_PWM_VELOCITY:
            motionPlanner.velocityTracker.CalcSoftGoal(controller->goalVelocity);
            controller->softVelocity = motionPlanner.velocityTracker.goVelocity;
            break;

        /* PWM 电流模式：复用电流规划器，只更新软电流目标。 */
        case MODE_PWM_CURRENT:
            motionPlanner.currentTracker.CalcSoftGoal(controller->goalCurrent);
            controller->softCurrent = motionPlanner.currentTracker.goCurrent;
            break;

        /*
         * STEP/DIR 模式：
         * positionInterpolator 根据 STEP/DIR 产生的位置目标进行插补，
         * 输出本周期的软位置和软速度。
         */
        case MODE_STEP_DIR:
            motionPlanner.positionInterpolator.CalcSoftGoal(controller->goalPosition);
            controller->softPosition = motionPlanner.positionInterpolator.goPosition;
            controller->softVelocity = motionPlanner.positionInterpolator.goVelocity;
            break;

        /* 未知模式不更新软目标，保持上一周期的软目标值。 */
        default:
            break;
    }

    controller->softDisable = controller->goalDisable;
    controller->softBrake = controller->goalBrake;

    /******************************** 堵转和过载检测 ********************************/
    int32_t current = abs(controller->focCurrent);

    /* 堵转保护：长时间高电流且速度很低时置位 isStalled。 */
    if (controller->config->stallProtectSwitch)
    {
        if (// Current Mode
            ((controller->modeRunning == MODE_COMMAND_CURRENT ||
              controller->modeRunning == MODE_PWM_CURRENT) &&
             (current != 0))
            || // Other Mode
            current == config.motionParams.ratedCurrent)
        {
            if (abs(controller->estVelocity) < MOTOR_ONE_CIRCLE_SUBDIVIDE_STEPS / 5)
            {
                if (controller->stalledTime >= 1000 * 1000)
                    controller->isStalled = true;
                else
                    controller->stalledTime += motionPlanner.CONTROL_PERIOD;
            }
        } else // can ONLY clear stall flag  MANUALLY
        {
            controller->stalledTime = 0;
        }
    }

    /* 过载检测：非电流模式下持续达到额定电流时置位 overloadFlag。 */
    if ((controller->modeRunning != MODE_COMMAND_CURRENT) &&
        (controller->modeRunning != MODE_PWM_CURRENT) &&
        (current == config.motionParams.ratedCurrent))
    {
        if (controller->overloadTime >= 1000 * 1000)
            controller->overloadFlag = true;
        else
            controller->overloadTime += motionPlanner.CONTROL_PERIOD;
    } else // auto clear overload flag when released
    {
        controller->overloadTime = 0;
        controller->overloadFlag = false;
    }

    /******************************** 更新对外状态 ********************************/
    if (!encoder->IsCalibrated())
        controller->state = STATE_NO_CALIB;
    else if (controller->isStalled)
        controller->state = STATE_STALL;
    else if (controller->limitProtectActive)
        controller->state = STATE_LIMIT;
    else if (controller->modeRunning == MODE_STOP)
        controller->state = STATE_STOP;
    else if (controller->overloadFlag)
        controller->state = STATE_OVERLOAD;
    else
    {
        if (controller->modeRunning == MODE_BUFFERED_PATH)
        {
            controller->state = motionPlanner.bufferedPath.state == 3U ? STATE_FINISH : STATE_RUNNING;
        }
        else if (controller->modeRunning == MODE_COMMAND_POSITION)
        {
            if ((controller->softPosition == controller->goalPosition)
                && (controller->softVelocity == 0))
                controller->state = STATE_FINISH;
            else
                controller->state = STATE_RUNNING;
        } else if (controller->modeRunning == MODE_COMMAND_VELOCITY)
        {
            if (controller->softVelocity == controller->goalVelocity)
                controller->state = STATE_FINISH;
            else
                controller->state = STATE_RUNNING;
        } else if (controller->modeRunning == MODE_COMMAND_CURRENT)
        {
            if (controller->softCurrent == controller->goalCurrent)
                controller->state = STATE_FINISH;
            else
                controller->state = STATE_RUNNING;
        } else
        {
            controller->state = STATE_FINISH;
        }
    }
}


void Motor::Controller::ApplyLimitProtection()
{
    /*
     * 限位保护优先级高于普通位置/速度/电流控制。
     * 规则：
     *   1. 任意限位刚变低时，立即清零所有运动目标并关闭驱动输出；
     *   2. 保持触发侧禁止继续向限位方向运动；
     *   3. 反方向运动允许执行，用于退出限位；
     *   4. 找限位命令触发时，通过待处理标志向 CAN 层发送 0x32 事件。
     */
    bool leftLimit = IsLeftLimitTriggered();
    bool rightLimit = IsRightLimitTriggered();
    uint8_t limitInputState = ReadLimitInputState();
    bool newlyTriggered = (limitInputState & (uint8_t)~limitInputStateLast) != 0;
    bool blockedByLimit = false;
    uint8_t blockedSide = LIMIT_SIDE_NONE;

    /* 只要有一个输入保持有效，就保持 STATE_LIMIT；反向退出仍然允许。 */
    limitProtectActive = limitInputState != LIMIT_SIDE_NONE;

    /*
     * 限位刚触发时不判断当前方向，统一立即停机一个控制周期。
     * 这样可以避免运动指令在触发瞬间绕过保护逻辑。
     */
    if (newlyTriggered)
    {
        bool fromCommand = limitMoveActive &&
                           ((limitInputState & limitMoveTargetSide) != 0);

        goalPosition = estPosition;
        softPosition = estPosition;
        goalVelocity = 0;
        softVelocity = 0;
        goalCurrent = 0;
        softCurrent = 0;
        requestMode = MODE_STOP;
        modeRunning = MODE_STOP;
        softNewCurve = true;
        limitMoveActive = false;
        ClearIntegral();
        /* 限位属于安全保护，立即关闭输出，不等待 S 型减速完成。 */
        context->driver->Sleep();
        RecordLimitReached(limitInputState, fromCommand);
        limitInputStateLast = limitInputState;
        return;
    }

    limitInputStateLast = limitInputState;

    /* 找限位命令执行期间，目标限位或双限位触发时立即停止并准备回报。 */
    if (limitMoveActive &&
        ((limitMoveTargetSide == LIMIT_SIDE_LEFT && leftLimit) ||
         (limitMoveTargetSide == LIMIT_SIDE_RIGHT && rightLimit) ||
         (leftLimit && rightLimit)))
    {
        uint8_t side = ReadLimitInputState();

        goalPosition = estPosition;
        softPosition = estPosition;
        goalVelocity = 0;
        softVelocity = 0;
        goalCurrent = 0;
        softCurrent = 0;
        requestMode = MODE_STOP;
        modeRunning = MODE_STOP;
        softNewCurve = true;
        limitMoveActive = false;
        limitProtectActive = true;
        ClearIntegral();
        context->driver->Sleep();
        RecordLimitReached(side, true);
        return;
    }

    /* 左限位对应负方向：禁止继续向负方向，允许向正方向退出。 */
    if (leftLimit)
    {
        if (goalPosition < estPosition)
        {
            goalPosition = estPosition;
            softNewCurve = true;
            limitProtectActive = true;
            blockedByLimit = true;
            blockedSide |= LIMIT_SIDE_LEFT;
        }
        if (softPosition < estPosition)
        {
            softPosition = estPosition;
            softNewCurve = true;
            limitProtectActive = true;
            blockedByLimit = true;
            blockedSide |= LIMIT_SIDE_LEFT;
        }
        if (goalVelocity < 0)
        {
            goalVelocity = 0;
            softNewCurve = true;
            limitProtectActive = true;
            blockedByLimit = true;
            blockedSide |= LIMIT_SIDE_LEFT;
        }
        if (softVelocity < 0)
        {
            softVelocity = 0;
            softNewCurve = true;
            limitProtectActive = true;
            blockedByLimit = true;
            blockedSide |= LIMIT_SIDE_LEFT;
        }
        if (goalCurrent < 0)
        {
            goalCurrent = 0;
            softNewCurve = true;
            limitProtectActive = true;
            blockedByLimit = true;
            blockedSide |= LIMIT_SIDE_LEFT;
        }
        if (softCurrent < 0)
        {
            softCurrent = 0;
            softNewCurve = true;
            limitProtectActive = true;
            blockedByLimit = true;
            blockedSide |= LIMIT_SIDE_LEFT;
        }
    }

    /* 右限位对应正方向：禁止继续向正方向，允许向负方向退出。 */
    if (rightLimit)
    {
        if (goalPosition > estPosition)
        {
            goalPosition = estPosition;
            softNewCurve = true;
            limitProtectActive = true;
            blockedByLimit = true;
            blockedSide |= LIMIT_SIDE_RIGHT;
        }
        if (softPosition > estPosition)
        {
            softPosition = estPosition;
            softNewCurve = true;
            limitProtectActive = true;
            blockedByLimit = true;
            blockedSide |= LIMIT_SIDE_RIGHT;
        }
        if (goalVelocity > 0)
        {
            goalVelocity = 0;
            softNewCurve = true;
            limitProtectActive = true;
            blockedByLimit = true;
            blockedSide |= LIMIT_SIDE_RIGHT;
        }
        if (softVelocity > 0)
        {
            softVelocity = 0;
            softNewCurve = true;
            limitProtectActive = true;
            blockedByLimit = true;
            blockedSide |= LIMIT_SIDE_RIGHT;
        }
        if (goalCurrent > 0)
        {
            goalCurrent = 0;
            softNewCurve = true;
            limitProtectActive = true;
            blockedByLimit = true;
            blockedSide |= LIMIT_SIDE_RIGHT;
        }
        if (softCurrent > 0)
        {
            softCurrent = 0;
            softNewCurve = true;
            limitProtectActive = true;
            blockedByLimit = true;
            blockedSide |= LIMIT_SIDE_RIGHT;
        }
    }

    if (blockedByLimit)
    {
        /* 普通运动撞到限位时也生成一次限位事件，供主控执行急停策略。 */
        bool fromCommand = limitMoveActive &&
                           ((blockedSide & limitMoveTargetSide) != 0);
        limitMoveActive = false;
        requestMode = MODE_STOP;
        modeRunning = MODE_STOP;
        context->driver->Sleep();
        RecordLimitReached(blockedSide, fromCommand);
    }
}


void Motor::Controller::RecordLimitReached(uint8_t _side, bool _fromCommand)
{
    /* 保存一次限位事件，主循环稍后通过 FetchLimitReachedReport() 读取。 */
    limitReachedSide = _side;
    limitReachedFromCommand = _fromCommand ? 1 : 0;
    limitReachedReportPending = true;
}


int32_t Motor::Controller::NormalizeLimitMoveVelocity(int32_t _vel) const
{
    /* 将找限位速度转换为正值，并限制在当前额定速度以内。 */
    int32_t speed = abs(_vel);
    int32_t maxSpeed = context->config.motionParams.ratedVelocity;

    if (maxSpeed <= 0)
        maxSpeed = context->MOTOR_ONE_CIRCLE_SUBDIVIDE_STEPS;
    if (speed <= 0)
        speed = maxSpeed / 5;
    if (speed <= 0)
        speed = context->MOTOR_ONE_CIRCLE_SUBDIVIDE_STEPS;
    if (speed > maxSpeed)
        speed = maxSpeed;

    return speed;
}


void Motor::Controller::MoveToLeftLimit(int32_t _vel)
{
    /* 向负方向连续运动，直到 PB0 左限位触发。 */
    int32_t speed = NormalizeLimitMoveVelocity(_vel);

    if (IsLeftLimitTriggered())
    {
        /* 已经在左限位时不再运动，直接生成找限位完成事件。 */
        SetCtrlMode(MODE_STOP);
        SetVelocitySetPoint(0);
        limitMoveActive = false;
        limitProtectActive = true;
        RecordLimitReached(ReadLimitInputState(), true);
        return;
    }

    limitMoveActive = true;
    limitMoveTargetSide = LIMIT_SIDE_LEFT;
    context->config.motionParams.ratedVelocity = boardConfig.velocityLimit;
    SetCtrlMode(MODE_COMMAND_VELOCITY);
    SetVelocitySetPoint(-speed);
}


void Motor::Controller::MoveToRightLimit(int32_t _vel)
{
    /* 向正方向连续运动，直到 PB1 右限位触发。 */
    int32_t speed = NormalizeLimitMoveVelocity(_vel);

    if (IsRightLimitTriggered())
    {
        /* 已经在右限位时不再运动，直接生成找限位完成事件。 */
        SetCtrlMode(MODE_STOP);
        SetVelocitySetPoint(0);
        limitMoveActive = false;
        limitProtectActive = true;
        RecordLimitReached(ReadLimitInputState(), true);
        return;
    }

    limitMoveActive = true;
    limitMoveTargetSide = LIMIT_SIDE_RIGHT;
    context->config.motionParams.ratedVelocity = boardConfig.velocityLimit;
    SetCtrlMode(MODE_COMMAND_VELOCITY);
    SetVelocitySetPoint(speed);
}


bool Motor::Controller::FetchLimitReachedReport(uint8_t* _side, uint8_t* _fromCommand)
{
    /*
     * 读取并消费待发送的限位事件。
     * 返回 true 只代表有一条新事件，不代表当前输入已经恢复高电平。
     */
    if (!limitReachedReportPending)
        return false;

    if (_side)
        *_side = limitReachedSide;
    if (_fromCommand)
        *_fromCommand = limitReachedFromCommand;
    limitReachedReportPending = false;
    return true;
}


uint8_t Motor::Controller::GetLimitInputState() const
{
    /* 返回当前 PB0/PB1 限位输入位图，供 CAN 0x35 查询使用。 */
    return ReadLimitInputState();
}


void Motor::Controller::CalcCurrentToOutput(int32_t current)
{
    /*
     * 将目标电流转换为 FOC 电流矢量。
     * 电流为正/负时，电角度分别提前/滞后约 90 度；电流为 0 时保持当前位置。
     */
    focCurrent = current;

    if (focCurrent > 0)
        focPosition = estPosition + context->SOFT_DIVIDE_NUM; // ahead phase of 90°
    else if (focCurrent < 0)
        focPosition = estPosition - context->SOFT_DIVIDE_NUM; // behind phase of 90°
    else focPosition = estPosition;

    context->driver->SetFocCurrentVector(focPosition, focCurrent);
}


void Motor::Controller::CalcPidToOutput(int32_t _speed)
{
    /*
     * 速度 PID 控制器：
     *   vError = 目标速度 - 估计速度
     *   输出 = Kp * 速度误差 + Ki * 积分 + Kd * 误差变化量
     * 最终输出限制在额定电流范围内。
     */
    config->pid.vErrorLast = config->pid.vError;
    config->pid.vError = _speed - estVelocity;
    if (config->pid.vError > (1024 * 1024)) config->pid.vError = (1024 * 1024);
    if (config->pid.vError < (-1024 * 1024)) config->pid.vError = (-1024 * 1024);
    config->pid.outputKp = ((config->pid.kp) * (config->pid.vError));

    config->pid.integralRound += (config->pid.ki * config->pid.vError);
    config->pid.integralRemainder = config->pid.integralRound >> 10;
    config->pid.integralRound -= (config->pid.integralRemainder << 10);
    config->pid.outputKi += config->pid.integralRemainder;
    /* 积分项使用额外的 10 bit 小数精度，并限制到额定电流范围。 */
    if (config->pid.outputKi > context->config.motionParams.ratedCurrent << 10)
        config->pid.outputKi = context->config.motionParams.ratedCurrent << 10;
    else if (config->pid.outputKi < -(context->config.motionParams.ratedCurrent << 10))
        config->pid.outputKi = -(context->config.motionParams.ratedCurrent << 10);

    config->pid.outputKd = config->pid.kd * (config->pid.vError - config->pid.vErrorLast);

    config->pid.output = (config->pid.outputKp + config->pid.outputKi + config->pid.outputKd) >> 10;
    if (config->pid.output > context->config.motionParams.ratedCurrent)
        config->pid.output = context->config.motionParams.ratedCurrent;
    else if (config->pid.output < -context->config.motionParams.ratedCurrent)
        config->pid.output = -context->config.motionParams.ratedCurrent;

    CalcCurrentToOutput(config->pid.output);
}


void Motor::Controller::CalcDceToOutput(int32_t _location, int32_t _speed)
{
    /*
     * DCE 位置控制器：
     *   pError = 目标位置 - 估计位置
     *   vError = (目标速度 - 估计速度) / 128
     *   outputKp = Kp * pError
     *   outputKi = 对 (Ki*pError + Kv*vError) 积分
     *   outputKd = Kd * vError
     *
     * 注意当前工程实现中 Kv 也参与积分，Kd 直接作用于速度误差，
     * 因此不能完全按照传统 PID 的参数含义理解这四个参数。
     */
    /* 限制位置误差，避免大目标位置瞬间产生过大的电流指令。 */
    config->dce.pError = _location - estPosition;
    if (config->dce.pError > (3200)) config->dce.pError = (3200);   // limited pError to 1/16r (51200/16)
    if (config->dce.pError < (-3200)) config->dce.pError = (-3200);
    /* 速度误差右移 7 位，降低定点数幅值。 */
    config->dce.vError = (_speed - estVelocity) >> 7;
    if (config->dce.vError > (4000)) config->dce.vError = (4000);   // limited vError
    if (config->dce.vError < (-4000)) config->dce.vError = (-4000);

    config->dce.outputKp = config->dce.kp * config->dce.pError;

    config->dce.integralRound += (config->dce.ki * config->dce.pError + config->dce.kv * config->dce.vError);
    config->dce.integralRemainder = config->dce.integralRound >> 7;
    config->dce.integralRound -= (config->dce.integralRemainder << 7);
    config->dce.outputKi += config->dce.integralRemainder;
    /* DCE 积分项限幅，防止持续误差导致积分饱和。 */
    if (config->dce.outputKi > context->config.motionParams.ratedCurrent << 10)
        config->dce.outputKi = context->config.motionParams.ratedCurrent << 10;
    else if (config->dce.outputKi < -(context->config.motionParams.ratedCurrent << 10))
        config->dce.outputKi = -(context->config.motionParams.ratedCurrent << 10);

    config->dce.outputKd = ((config->dce.kd) * (config->dce.vError));

    /* 合并三项并恢复 10 bit 定点缩放。 */
    config->dce.output = (config->dce.outputKp + config->dce.outputKi + config->dce.outputKd) >> 10;
    if (config->dce.output > context->config.motionParams.ratedCurrent)
        config->dce.output = context->config.motionParams.ratedCurrent;
    else if (config->dce.output < -context->config.motionParams.ratedCurrent)
        config->dce.output = -context->config.motionParams.ratedCurrent;

    CalcCurrentToOutput(config->dce.output);
}


void Motor::Controller::SetCtrlMode(Motor::Mode_t _mode)
{
    if (_mode != MODE_BUFFERED_PATH && context->motionPlanner.bufferedPath.count != 0U)
        context->motionPlanner.bufferedPath.Clear(context->motionPlanner.bufferedPath.session);
    /* 这里只修改请求模式，真正切换在下一次控制周期中完成。 */
    requestMode = _mode;
}

uint8_t Motor::Controller::BufferedPathSafetyError() const
{
    if (!context->encoder->IsCalibrated()) return BufferedPath::ErrorEncoderNotCalibrated;
    if (isStalled) return BufferedPath::ErrorMotorStalled;
    if (goalDisable) return BufferedPath::ErrorMotorDisabled;
    if (goalBrake) return BufferedPath::ErrorMotorBraked;
    if (GetLimitInputState()!=0U) return BufferedPath::ErrorLimitActive;
    return BufferedPath::ErrorNone;
}

uint8_t Motor::Controller::BufferedPathStartError(int32_t initialPosition) const
{
    uint8_t safetyError=BufferedPathSafetyError();
    if (safetyError!=BufferedPath::ErrorNone) return safetyError;
    int64_t delta=(int64_t)realPosition-initialPosition;
    return delta<=1024 && delta>=-1024 ? BufferedPath::ErrorNone :
        BufferedPath::ErrorStartPositionMismatch;
}


void Motor::Controller::AddTrajectorySetPoint(int32_t _pos, int32_t _vel)
{
    /* 设置轨迹模式的一组位置和速度目标。 */
    SetPositionSetPoint(_pos);
    SetVelocitySetPoint(_vel);
}


void Motor::Controller::SetPositionSetPoint(int32_t _pos)
{
    /* 外部位置以 home 为零点，这里加回编码器偏移转换成内部绝对位置。 */
    goalPosition = _pos + context->config.motionParams.encoderHomeOffset;
}


bool Motor::Controller::SetPositionSetPointWithTime(int32_t _pos, float _time)
{
    /*
     * 根据目标位移和指定时间反算本次运动允许的最高速度。
     * 位移过大、无法在指定时间内完成时，恢复默认额定速度并仍然接受目标。
     */
    int32_t deltaPos = abs(_pos - realPosition + context->config.motionParams.encoderHomeOffset);

    /* 对称加减速条件下，给定时间内可完成的最大位移。 */
    float pMax = (float) context->config.motionParams.ratedVelocityAcc * _time * _time / 4;
    if ((float) deltaPos > pMax)
    {
        /* 时间不足：不强行计算 sqrt，使用驱动板保存的速度限制。 */
        context->config.motionParams.ratedVelocity = boardConfig.velocityLimit;
        SetPositionSetPoint(_pos);

        return false;
    } else
    {
        /* 时间足够：根据梯形/S 型规划所需的加速度求本次速度上限。 */
        float vMax = _time * (float) context->config.motionParams.ratedVelocityAcc;
        vMax -= (float) context->config.motionParams.ratedVelocityAcc *
                (sqrtf(_time * _time - 4 * (float) deltaPos /
                                       (float) context->config.motionParams.ratedVelocityAcc));
        vMax /= 2;

        context->config.motionParams.ratedVelocity = (int32_t) vMax;
        SetPositionSetPoint(_pos);

        return true;
    }
}


void Motor::Controller::SetVelocitySetPoint(int32_t _vel)
{
    /* 速度目标只能在当前额定速度范围内更新，超出范围的命令被忽略。 */
    if ((_vel >= -context->config.motionParams.ratedVelocity) &&
        (_vel <= context->config.motionParams.ratedVelocity))
    {
        goalVelocity = _vel;
    }
}


float Motor::Controller::GetPosition(bool _isLap)
{
    /* 返回相对 home 的位置，单位为电机圈；_isLap=true 时返回连续多圈位置。 */
    return _isLap ?
           (float) (realLapPosition - context->config.motionParams.encoderHomeOffset) /
           (float) (context->MOTOR_ONE_CIRCLE_SUBDIVIDE_STEPS)
                  :
           (float) (realPosition - context->config.motionParams.encoderHomeOffset) /
           (float) (context->MOTOR_ONE_CIRCLE_SUBDIVIDE_STEPS);
}


float Motor::Controller::GetVelocity()
{
    /* 内部步/s 转换为电机圈/s。 */
    return (float) estVelocity / (float) context->MOTOR_ONE_CIRCLE_SUBDIVIDE_STEPS;
}


float Motor::Controller::GetFocCurrent()
{
    /* 内部 mA 转换为 A，供 CAN 查询和状态显示使用。 */
    return (float) focCurrent / 1000.f;
}


void Motor::Controller::SetCurrentSetPoint(int32_t _cur)
{
    /* 电流目标单位为 mA，并限制在额定电流范围内。 */
    if (_cur > context->config.motionParams.ratedCurrent)
        goalCurrent = context->config.motionParams.ratedCurrent;
    else if (_cur < -context->config.motionParams.ratedCurrent)
        goalCurrent = -context->config.motionParams.ratedCurrent;
    else
        goalCurrent = _cur;
}


void Motor::Controller::SetDisable(bool _disable)
{
    /* 设置软失能目标，实际输出会在控制周期中逐步/统一处理。 */
    goalDisable = _disable;
}


void Motor::Controller::SetBrake(bool _brake)
{
    /* 设置刹车目标；刹车时关闭 FOC 电流并调用驱动器 Brake()。 */
    goalBrake = _brake;

}


void Motor::Controller::ClearStallFlag()
{
    /* 堵转保护属于锁存状态，收到新任务时由此清除。 */
    stalledTime = 0;
    isStalled = false;
}


int32_t Motor::Controller::CompensateAdvancedAngle(int32_t _vel)
{
    /*
     * 根据速度估算位置超前量，用于补偿电机高速运行时的控制滞后。
     * 当前分段参数来自 DPS 系列传感器；使用 MT6816 时应重新标定。
     * 返回值单位仍为内部位置细分步，且限制在约 +/-430 步以内。
     */

    int32_t compensate;

    if (_vel < 0)
    {
        if (_vel > -100000) compensate = 0;
        else if (_vel > -1300000) compensate = (((_vel + 100000) * 262) >> 20) - 0;
        else if (_vel > -2200000) compensate = (((_vel + 1300000) * 105) >> 20) - 300;
        else compensate = (((_vel + 2200000) * 52) >> 20) - 390;

        if (compensate < -430) compensate = -430;
    } else
    {
        if (_vel < 100000) compensate = 0;
        else if (_vel < 1300000) compensate = (((_vel - 100000) * 262) >> 20) + 0;
        else if (_vel < 2200000) compensate = (((_vel - 1300000) * 105) >> 20) + 300;
        else compensate = (((_vel - 2200000) * 52) >> 20) + 390;

        if (compensate > 430) compensate = 430;
    }

    return compensate;
}


void Motor::Controller::Init()
{
    /*
     * 初始化控制器运行状态：
     * - 根据配置决定上电请求模式；
     * - 清零位置、速度、电流和软目标；
     * - 清除堵转、过载、限位事件及 PID/DCE 积分。
     */
    /* enableMotorOnBoot=1 时使用配置的默认模式，否则上电保持停止。 */
    requestMode = boardConfig.enableMotorOnBoot ? static_cast<Mode_t>(boardConfig.defaultMode) : MODE_STOP;

    modeRunning = MODE_STOP;
    state = STATE_STOP;

    realLapPosition = 0;
    realLapPositionLast = 0;
    realPosition = 0;
    realPositionLast = 0;

    estVelocityIntegral = 0;
    estVelocity = 0;
    estLeadPosition = 0;
    estPosition = 0;
    estError = 0;

    /* 内部绝对目标位置从编码器 home offset 开始。 */
    goalPosition = context->config.motionParams.encoderHomeOffset;
    goalVelocity = 0;
    goalCurrent = 0;
    goalDisable = false;
    goalBrake = false;

    softPosition = 0;
    softVelocity = 0;
    softCurrent = 0;
    softDisable = false;
    softBrake = false;
    softNewCurve = false;

    focPosition = 0;
    focCurrent = 0;

    stalledTime = 0;
    isStalled = false;

    overloadTime = 0;
    overloadFlag = false;

    limitProtectActive = false;
    limitMoveActive = false;
    limitMoveTargetSide = LIMIT_SIDE_NONE;
    limitInputStateLast = LIMIT_SIDE_NONE;
    limitReachedReportPending = false;
    limitReachedSide = LIMIT_SIDE_NONE;
    limitReachedFromCommand = 0;

    /* 清除速度 PID 的历史误差、积分和输出缓存。 */
    config->pid.vError = 0;
    config->pid.vErrorLast = 0;
    config->pid.outputKp = 0;
    config->pid.outputKi = 0;
    config->pid.outputKd = 0;
    config->pid.integralRound = 0;
    config->pid.integralRemainder = 0;
    config->pid.output = 0;

    /* 清除位置 DCE 的历史误差、积分和输出缓存。 */
    config->dce.pError = 0;
    config->dce.vError = 0;
    config->dce.outputKp = 0;
    config->dce.outputKi = 0;
    config->dce.outputKd = 0;
    config->dce.integralRound = 0;
    config->dce.integralRemainder = 0;
    config->dce.output = 0;
}


void Motor::Controller::ApplyPosAsHomeOffset()
{
    /*
     * 将当前编码器位置设为运行时零点，并把连续多圈坐标重新落到当前单圈。
     * 只修改运行时控制状态；是否写入EEPROM由CAN命令层单独决定。
     */
    int32_t homeOffset = realLapPosition % context->MOTOR_ONE_CIRCLE_SUBDIVIDE_STEPS;
    if (homeOffset < 0)
        homeOffset += context->MOTOR_ONE_CIRCLE_SUBDIVIDE_STEPS;

    context->config.motionParams.encoderHomeOffset = homeOffset;
    realLapPosition = homeOffset;
    realLapPositionLast = homeOffset;
    realPosition = homeOffset;
    realPositionLast = homeOffset;
    estPosition = homeOffset;
    estLeadPosition = 0;
    estVelocity = 0;
    estVelocityIntegral = 0;
    estError = 0;

    goalPosition = homeOffset;
    softPosition = homeOffset;
    goalVelocity = 0;
    softVelocity = 0;
    goalCurrent = 0;
    softCurrent = 0;
    requestMode = MODE_STOP;
    modeRunning = MODE_STOP;
    softNewCurve = true;
    limitMoveActive = false;
    ClearIntegral();
    context->driver->Sleep();
}


void Motor::Controller::AttachConfig(Motor::Controller::Config_t* _config)
{
    /* 绑定由 Motor 对象持有的 PID/DCE 配置结构。 */
    config = _config;
}


void Motor::Controller::ClearIntegral() const
{
    /*
     * 清除速度 PID 和位置 DCE 的积分历史。
     * 该函数会在模式切换、限位、刹车、失能和新任务开始时调用，
     * 用于避免上一段运动的误差积累影响下一段运动。
     */
    config->pid.integralRound = 0;
    config->pid.integralRemainder = 0;
    config->pid.outputKi = 0;

    config->dce.integralRound = 0;
    config->dce.integralRemainder = 0;
    config->dce.outputKi = 0;
}
