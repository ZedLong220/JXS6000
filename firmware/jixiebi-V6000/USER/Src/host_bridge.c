#include "host_bridge.h"
#include "usart.h"
#include <string.h>

typedef struct
{
    uint16_t id;
    uint8_t length;
    uint8_t data[8];
} HostFrame;

#define HOST_QUEUE_SIZE 32U
static HostFrame queue[HOST_QUEUE_SIZE];
static volatile uint8_t head;
static volatile uint8_t tail;
static volatile uint8_t session;

void HostBridge_Init(void)
{
    head = tail = session = 0U;
}

void HostBridge_SetSession(uint8_t enabled)
{
    session = enabled ? 1U : 0U;
    if (!session) tail = head;
}

int HostBridge_IsSession(void)
{
    return session != 0U;
}

void HostBridge_OnCanRx(uint16_t id, const uint8_t *data, uint8_t length)
{
    uint8_t next;
    if (!session || data == NULL || length > 8U) return;
    next = (uint8_t)((head + 1U) % HOST_QUEUE_SIZE);
    if (next == tail) return;
    queue[head].id = id;
    queue[head].length = length;
    memcpy(queue[head].data, data, length);
    __DMB();
    head = next;
}

void HostBridge_Process(void)
{
    static const char digits[] = "0123456789ABCDEF";
    while (session && tail != head) {
        HostFrame frame = queue[tail];
        char text[23];
        uint16_t length;
        tail = (uint8_t)((tail + 1U) % HOST_QUEUE_SIZE);
        text[0] = 't';
        text[1] = digits[(frame.id >> 8) & 0x0FU];
        text[2] = digits[(frame.id >> 4) & 0x0FU];
        text[3] = digits[frame.id & 0x0FU];
        text[4] = digits[frame.length];
        for (uint8_t i = 0U; i < frame.length; ++i) {
            text[5U + i * 2U] = digits[frame.data[i] >> 4];
            text[6U + i * 2U] = digits[frame.data[i] & 0x0FU];
        }
        length = (uint16_t)(6U + frame.length * 2U);
        text[length - 1U] = '\r';
        HAL_UART_Transmit(&huart1, (uint8_t *)text, length, 50U);
    }
}
