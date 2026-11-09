#include "motion_planner.h"
#include "math.h"


void MotionPlanner::CurrentTracker::Init()
{
    SetCurrentAcc(context->config->ratedCurrentAcc);
}


void MotionPlanner::CurrentTracker::NewTask(int32_t _realCurrent)
{
    currentIntegral = 0;
    trackCurrent = _realCurrent;
}


void MotionPlanner::CurrentTracker::CalcSoftGoal(int32_t _goalCurrent)
{
    int32_t deltaCurrent = _goalCurrent - trackCurrent;

    if (deltaCurrent == 0)
    {
        trackCurrent = _goalCurrent;
    } else if (deltaCurrent > 0)
    {
        if (trackCurrent >= 0)
        {
            CalcCurrentIntegral(currentAcc);
            if (trackCurrent >= _goalCurrent)
            {
                currentIntegral = 0;
                trackCurrent = _goalCurrent;
            }
        } else
        {
            CalcCurrentIntegral(currentAcc);
            if ((int32_t) trackCurrent >= 0)
            {
                currentIntegral = 0;
                trackCurrent = 0;
            }
        }
    } else if (deltaCurrent < 0)
    {
        if (trackCurrent <= 0)
        {
            CalcCurrentIntegral(-currentAcc);
            if ((int32_t) trackCurrent <= (int32_t) _goalCurrent)
            {
                currentIntegral = 0;
                trackCurrent = _goalCurrent;
            }
        } else
        {
            CalcCurrentIntegral(-currentAcc);
            if ((int32_t) trackCurrent <= 0)
            {
                currentIntegral = 0;
                trackCurrent = 0;
            }
        }
    }

    goCurrent = (int32_t) trackCurrent;
}


void MotionPlanner::CurrentTracker::CalcCurrentIntegral(int32_t _current)
{
    currentIntegral += _current;
    trackCurrent += currentIntegral / context->CONTROL_FREQUENCY;
    currentIntegral = currentIntegral % context->CONTROL_FREQUENCY;
}


void MotionPlanner::CurrentTracker::SetCurrentAcc(int32_t _currentAcc)
{
    currentAcc = _currentAcc;
}


void MotionPlanner::VelocityTracker::Init()
{
    SetVelocityAcc(context->config->ratedVelocityAcc);
    accelerationJerk = context->config->ratedVelocityJerk;
    if (accelerationJerk <= 0)
        accelerationJerk = velocityAcc * 20;
}


void MotionPlanner::VelocityTracker::SetVelocityAcc(int32_t _velocityAcc)
{
    velocityAcc = _velocityAcc;
}


void MotionPlanner::VelocityTracker::NewTask(int32_t _realVelocity)
{
    velocityIntegral = 0;
    accelerationIntegral = 0;
    accelerationNow = 0;
    trackVelocity = _realVelocity;
}


void MotionPlanner::VelocityTracker::CalcSoftGoal(int32_t _goalVelocity)
{
    int32_t velocityBefore = trackVelocity;
    int32_t deltaVelocity = _goalVelocity - trackVelocity;
    int32_t accelerationTarget = 0;

    if (deltaVelocity > 0)
        accelerationTarget = velocityAcc;
    else if (deltaVelocity < 0)
        accelerationTarget = -velocityAcc;

    // 先限制加速度变化率，再积分得到速度。
    CalcVelocityIntegral(accelerationTarget);

    // 目标速度被跨过时钳位，避免定点积分造成反复振荡。
    if ((velocityBefore < _goalVelocity && trackVelocity >= _goalVelocity) ||
        (velocityBefore > _goalVelocity && trackVelocity <= _goalVelocity))
    {
        trackVelocity = _goalVelocity;
        ResetMotionState();
    }

    goVelocity = (int32_t) trackVelocity;
}


void MotionPlanner::VelocityTracker::CalcVelocityIntegral(int32_t _acceleration)
{
    int32_t deltaAcceleration = _acceleration - accelerationNow;

    if (accelerationJerk <= 0)
    {
        accelerationNow = _acceleration;
        accelerationIntegral = 0;
    }
    else if (deltaAcceleration > 0)
    {
        accelerationIntegral += accelerationJerk;
        int32_t step = accelerationIntegral / context->CONTROL_FREQUENCY;
        accelerationIntegral %= context->CONTROL_FREQUENCY;
        if (step > deltaAcceleration)
        {
            step = deltaAcceleration;
            accelerationIntegral = 0;
        }
        accelerationNow += step;
    }
    else if (deltaAcceleration < 0)
    {
        accelerationIntegral -= accelerationJerk;
        int32_t step = accelerationIntegral / context->CONTROL_FREQUENCY;
        accelerationIntegral %= context->CONTROL_FREQUENCY;
        if (step < deltaAcceleration)
        {
            step = deltaAcceleration;
            accelerationIntegral = 0;
        }
        accelerationNow += step;
    }

    velocityIntegral += accelerationNow;
    trackVelocity += velocityIntegral / context->CONTROL_FREQUENCY;
    velocityIntegral = velocityIntegral % context->CONTROL_FREQUENCY;
}


void MotionPlanner::VelocityTracker::ResetMotionState()
{
    velocityIntegral = 0;
    accelerationIntegral = 0;
    accelerationNow = 0;
}


void MotionPlanner::PositionTracker::Init()
{
    SetVelocityAcc(context->config->ratedVelocityAcc);
    accelerationJerk = context->config->ratedVelocityJerk;
    if (accelerationJerk <= 0)
        accelerationJerk = velocityUpAcc * 20;

    /*
     *  Allow to locking-brake when velocity is lower than (speedLockingBrake).
     *  The best value should be (ratedMoveAcc/1000)
     */
    speedLockingBrake = context->config->ratedVelocityAcc / 1000;
}


void MotionPlanner::PositionTracker::SetVelocityAcc(int32_t value)
{
    velocityUpAcc = value;
    velocityDownAcc = value;
    quickVelocityDownAcc = 0.5f / (float) velocityDownAcc;
}


void MotionPlanner::PositionTracker::NewTask(int32_t real_location, int32_t real_speed)
{
    velocityIntegral = 0;
    accelerationIntegral = 0;
    accelerationNow = 0;
    trackVelocity = real_speed;
    positionIntegral = 0;
    trackPosition = real_location;
}


void MotionPlanner::PositionTracker::CalcSoftGoal(int32_t _goalPosition)
{
    int32_t deltaPosition = _goalPosition - trackPosition;

    if (deltaPosition == 0)
    {
        if ((trackVelocity >= -speedLockingBrake) && (trackVelocity <= speedLockingBrake))
        {
            velocityIntegral = 0;
            trackVelocity = 0;
            positionIntegral = 0;
            accelerationIntegral = 0;
            accelerationNow = 0;
        } else if (trackVelocity > 0)
        {
            CalcVelocityIntegral(-velocityDownAcc);
            if (trackVelocity <= 0)
            {
                velocityIntegral = 0;
                trackVelocity = 0;
                accelerationIntegral = 0;
                accelerationNow = 0;
            }
        } else if (trackVelocity < 0)
        {
            CalcVelocityIntegral(velocityDownAcc);
            if (trackVelocity >= 0)
            {
                velocityIntegral = 0;
                trackVelocity = 0;
                accelerationIntegral = 0;
                accelerationNow = 0;
            }
        }
    } else
    {
        if (trackVelocity == 0)
        {
            if (deltaPosition > 0)
            {
                CalcVelocityIntegral(velocityUpAcc);
            } else
            {
                CalcVelocityIntegral(-velocityUpAcc);
            }
        } else if ((deltaPosition > 0) && (trackVelocity > 0))
        {
            if (trackVelocity <= context->config->ratedVelocity)
            {
                auto need_down_location = GetBrakeDistance();
                if (abs(deltaPosition) > need_down_location)
                {
                    if (trackVelocity < context->config->ratedVelocity)
                    {
                        CalcVelocityIntegral(velocityUpAcc);
                        if (trackVelocity >= context->config->ratedVelocity)
                        {
                            velocityIntegral = 0;
                            trackVelocity = context->config->ratedVelocity;
                        }
                    } else if (trackVelocity > context->config->ratedVelocity)
                    {
                        CalcVelocityIntegral(-velocityDownAcc);
                    } else
                    {
                        // 正好达到最高速度时，平滑将加速度降为 0，进入匀速段。
                        CalcVelocityIntegral(0);
                    }
                } else
                {
                    CalcVelocityIntegral(-velocityDownAcc);
                        if (trackVelocity <= 0)
                        {
                            velocityIntegral = 0;
                            trackVelocity = 0;
                            accelerationIntegral = 0;
                            accelerationNow = 0;
                    }
                }
            } else
            {
                CalcVelocityIntegral(-velocityDownAcc);
                if (trackVelocity <= 0)
                {
                    velocityIntegral = 0;
                    trackVelocity = 0;
                    accelerationIntegral = 0;
                    accelerationNow = 0;
                }
            }
        } else if ((deltaPosition < 0) && (trackVelocity < 0))
        {
            if (trackVelocity >= -context->config->ratedVelocity)
            {
                auto need_down_location = GetBrakeDistance();
                if (abs(deltaPosition) > need_down_location)
                {
                    if (trackVelocity > -context->config->ratedVelocity)
                    {
                        CalcVelocityIntegral(-velocityUpAcc);
                        if (trackVelocity <= -context->config->ratedVelocity)
                        {
                            velocityIntegral = 0;
                            trackVelocity = -context->config->ratedVelocity;
                        }
                    } else if (trackVelocity < -context->config->ratedVelocity)
                    {
                        CalcVelocityIntegral(velocityDownAcc);
                    } else
                    {
                        // 正好达到负方向最高速度时，平滑将加速度降为 0。
                        CalcVelocityIntegral(0);
                    }
                } else
                {
                    CalcVelocityIntegral(velocityDownAcc);
                        if (trackVelocity >= 0)
                        {
                            velocityIntegral = 0;
                            trackVelocity = 0;
                            accelerationIntegral = 0;
                            accelerationNow = 0;
                    }
                }
            } else
            {
                CalcVelocityIntegral(velocityDownAcc);
                if (trackVelocity >= 0)
                {
                    velocityIntegral = 0;
                    trackVelocity = 0;
                    accelerationIntegral = 0;
                    accelerationNow = 0;
                }
            }
        } else if ((deltaPosition < 0) && (trackVelocity > 0))
        {
            CalcVelocityIntegral(-velocityDownAcc);
            if (trackVelocity <= 0)
            {
                velocityIntegral = 0;
                trackVelocity = 0;
                accelerationIntegral = 0;
                accelerationNow = 0;
            }
        } else if (((deltaPosition > 0) && (trackVelocity < 0)))
        {
            CalcVelocityIntegral(velocityDownAcc);
            if (trackVelocity >= 0)
            {
                velocityIntegral = 0;
                trackVelocity = 0;
                accelerationIntegral = 0;
                accelerationNow = 0;
            }
        }
    }

    CalcPositionIntegral(trackVelocity);

    go_location = (int32_t) trackPosition;
    go_velocity = (int32_t) trackVelocity;
}


void MotionPlanner::PositionTracker::CalcPositionIntegral(int32_t value)
{
    positionIntegral += value;
    trackPosition += positionIntegral / context->CONTROL_FREQUENCY;
    positionIntegral = positionIntegral % context->CONTROL_FREQUENCY;
}


void MotionPlanner::PositionTracker::CalcVelocityIntegral(int32_t value)
{
    int32_t deltaAcceleration = value - accelerationNow;

    if (accelerationJerk <= 0)
    {
        accelerationNow = value;
        accelerationIntegral = 0;
    }
    else if (deltaAcceleration > 0)
    {
        accelerationIntegral += accelerationJerk;
        int32_t step = accelerationIntegral / context->CONTROL_FREQUENCY;
        accelerationIntegral %= context->CONTROL_FREQUENCY;
        if (step > deltaAcceleration)
        {
            step = deltaAcceleration;
            accelerationIntegral = 0;
        }
        accelerationNow += step;
    }
    else if (deltaAcceleration < 0)
    {
        accelerationIntegral -= accelerationJerk;
        int32_t step = accelerationIntegral / context->CONTROL_FREQUENCY;
        accelerationIntegral %= context->CONTROL_FREQUENCY;
        if (step < deltaAcceleration)
        {
            step = deltaAcceleration;
            accelerationIntegral = 0;
        }
        accelerationNow += step;
    }

    velocityIntegral += accelerationNow;
    trackVelocity += velocityIntegral / context->CONTROL_FREQUENCY;
    velocityIntegral = velocityIntegral % context->CONTROL_FREQUENCY;
}


void MotionPlanner::PositionTracker::ResetMotionState()
{
    velocityIntegral = 0;
    accelerationIntegral = 0;
    accelerationNow = 0;
}


int32_t MotionPlanner::PositionTracker::GetBrakeDistance() const
{
    float speed = (float) abs(trackVelocity);
    float maxAcceleration = (float) abs(velocityDownAcc);
    float jerk = (float) abs(accelerationJerk);

    if (maxAcceleration <= 0.0f || jerk <= 0.0f)
        return 0;

    // Jerk 过渡期间仍会产生位移，因此要比 v^2/(2a) 提前刹车。
    float jerkTime = maxAcceleration / jerk;
    float brakeDistance = speed * speed / (2.0f * maxAcceleration);
    brakeDistance += speed * jerkTime;

    if (brakeDistance >= 2147483647.0f)
        return 2147483647;
    return (int32_t) brakeDistance;
}


void MotionPlanner::PositionInterpolator::Init()
{

}


void MotionPlanner::PositionInterpolator::NewTask(int32_t _realPosition, int32_t _realVelocity)
{
    recordPosition = _realPosition;
    recordPositionLast = _realPosition;
    estPosition = _realPosition;
    estVelocity = _realVelocity;
}


void MotionPlanner::PositionInterpolator::CalcSoftGoal(int32_t _goalPosition)
{
    recordPositionLast = recordPosition;
    recordPosition = _goalPosition;

    estPositionIntegral += (((recordPosition - recordPositionLast) * context->CONTROL_FREQUENCY)
                            + ((estVelocity << 6) - estVelocity));
    estVelocity = estPositionIntegral >> 6;
    estPositionIntegral -= (estVelocity << 6);

    estPosition = recordPosition;

    goPosition = estPosition;
    goVelocity = estVelocity;
}


void MotionPlanner::TrajectoryTracker::SetSlowDownVelocityAcc(int32_t value)
{
    velocityDownAcc = value;
}


void MotionPlanner::TrajectoryTracker::NewTask(int32_t real_location, int32_t real_speed)
{
    updateTime = 0;
    overtimeFlag = false;
    dynamicVelocityAccRemainder = 0;
    velocityNow = real_speed;
    velovityNowRemainder = 0;
    positionNow = real_location;
}


void MotionPlanner::TrajectoryTracker::CalcSoftGoal(int32_t _goalPosition, int32_t _goalVelocity)
{
    if (_goalVelocity != recordVelocity || _goalPosition != recordPosition)
    {
        updateTime = 0;
        recordVelocity = _goalVelocity;
        recordPosition = _goalPosition;

        dynamicVelocityAcc = (int32_t) ((float) (_goalVelocity + velocityNow) *
                                        (float) (_goalVelocity - velocityNow) /
                                        (float) (2 * (_goalPosition - positionNow)));

        overtimeFlag = false;
    } else
    {
        if (updateTime >= (updateTimeout * 1000))
            overtimeFlag = true;
        else
            updateTime += context->CONTROL_PERIOD;
    }

    if (overtimeFlag)
    {
        if (velocityNow == 0)
        {
            dynamicVelocityAccRemainder = 0;
        } else if (velocityNow > 0)
        {
            CalcVelocityIntegral(-velocityDownAcc);
            if (velocityNow <= 0)
            {
                dynamicVelocityAccRemainder = 0;
                velocityNow = 0;
            }
        } else
        {
            CalcVelocityIntegral(velocityDownAcc);
            if (velocityNow >= 0)
            {
                dynamicVelocityAccRemainder = 0;
                velocityNow = 0;
            }
        }
    } else
    {
        CalcVelocityIntegral(dynamicVelocityAcc);
    }

    CalcPositionIntegral(velocityNow);

    goPosition = positionNow;
    goVelocity = velocityNow;
}


void MotionPlanner::TrajectoryTracker::CalcVelocityIntegral(int32_t value)
{
    dynamicVelocityAccRemainder += value; // sum up last remainder
    velocityNow += dynamicVelocityAccRemainder / context->CONTROL_FREQUENCY;
    dynamicVelocityAccRemainder = dynamicVelocityAccRemainder % context->CONTROL_FREQUENCY; // calc remainder
}


void MotionPlanner::TrajectoryTracker::CalcPositionIntegral(int32_t value)
{
    velovityNowRemainder += value;
    positionNow += velovityNowRemainder / context->CONTROL_FREQUENCY;
    velovityNowRemainder = velovityNowRemainder % context->CONTROL_FREQUENCY;
}


void MotionPlanner::TrajectoryTracker::Init(int32_t _updateTimeout)
{
    //SetSlowDownVelocityAcc(context->config->ratedVelocityAcc / 10);
    SetSlowDownVelocityAcc(context->config->ratedVelocityAcc);
    updateTimeout = _updateTimeout;
}


void MotionPlanner::AttachConfig(MotionPlanner::Config_t* _config)
{
    config = _config;

    currentTracker.Init();
    velocityTracker.Init();
    positionTracker.Init();
    positionInterpolator.Init();
    trajectoryTracker.Init(200);
}
