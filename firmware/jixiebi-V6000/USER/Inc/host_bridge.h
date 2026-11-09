#ifndef HOST_BRIDGE_H
#define HOST_BRIDGE_H

#include <stdint.h>

void HostBridge_Init(void);
void HostBridge_SetSession(uint8_t enabled);
int HostBridge_IsSession(void);
void HostBridge_Process(void);
void HostBridge_OnCanRx(uint16_t id, const uint8_t *data, uint8_t length);

#endif
