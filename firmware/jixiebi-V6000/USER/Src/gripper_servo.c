#include "gripper_servo.h"
#include <math.h>
#include <string.h>

static float commandedAngle;
static uint16_t pulse;
static uint8_t enabled;

void Gripper_Init(void)
{
    commandedAngle=90.0f; pulse=0U; enabled=0U;
    GripperHardware_Init();
    (void)GripperHardware_WritePulse(0U);
}

int Gripper_SetAngle(float degrees)
{
    uint16_t requested;
    if (!isfinite(degrees) || degrees<0.0f || degrees>180.0f) return -1;
    requested=(uint16_t)(500.0f+degrees*(2000.0f/180.0f)+0.5f);
    if (GripperHardware_WritePulse(requested)!=0) return -2;
    commandedAngle=degrees; pulse=requested; enabled=1U;
    return 0;
}

int Gripper_Disable(void)
{
    if (GripperHardware_WritePulse(0U)!=0) return -1;
    pulse=0U; enabled=0U;
    return 0;
}

void Gripper_GetStatus(uint8_t data[8])
{
    /* Commanded angle only: an ordinary PWM servo has no position feedback. */
    memcpy(data,&commandedAngle,4U);
    data[4]=(uint8_t)pulse; data[5]=(uint8_t)(pulse>>8);
    data[6]=enabled; data[7]=1U;
}
