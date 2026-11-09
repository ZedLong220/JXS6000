#include "arm_controller.h"
#include "can_protocol.h"
#include <stdio.h>

void Arm_Init(void)
{
    CAN_DualAxisInit();
    printf("Five-axis CAN path coordinator v3: nodes 1..5, virtual J6 fixed\r\n");
}

void Arm_Update(void)
{
    CAN_DualAxisUpdate();
}

void Arm_PrintStatus(void)
{
    CAN_DualAxisPrintStatus();
}
