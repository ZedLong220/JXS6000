#include "arm_controller.h"
#include "can_protocol.h"
#include <stdio.h>

void Arm_Init(void)
{
    CAN_DualAxisInit();
    printf("Six-axis CAN path coordinator v4: nodes 1..6; gripper PWM PA0\r\n");
}

void Arm_Update(void)
{
    CAN_DualAxisUpdate();
}

void Arm_PrintStatus(void)
{
    CAN_DualAxisPrintStatus();
}
