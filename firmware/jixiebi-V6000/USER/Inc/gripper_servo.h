#ifndef GRIPPER_SERVO_H
#define GRIPPER_SERVO_H
#include <stdint.h>
#ifdef __cplusplus
extern "C" {
#endif
void Gripper_Init(void);
int Gripper_SetAngle(float degrees);
int Gripper_Disable(void);
void Gripper_GetStatus(uint8_t data[8]);
/* Board hooks: PA0 / TIM2_CH1, one microsecond per timer count. */
void GripperHardware_Init(void);
int GripperHardware_WritePulse(uint16_t microseconds);
#ifdef __cplusplus
}
#endif
#endif
