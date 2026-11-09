#ifndef SERIAL_CMD_H
#define SERIAL_CMD_H
#include "main.h"
void SerialCmd_Init(void);
void SerialCmd_Process(void);
void SerialCmd_HandleRx(void);
void SerialCmd_HandleError(void);
void SerialCmd_OnRxByte(uint8_t byte);
void SerialCmd_ResetPending(void);
int SerialCmd_HandleCanFrame(uint16_t stdId, const uint8_t *data, uint8_t length);
#endif
