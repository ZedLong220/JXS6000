#include "main.h"
#include "can.h"
#include "usart.h"
#include "gpio.h"
#include "can_protocol.h"
#include "arm_controller.h"
#include "serial_cmd.h"
#include "host_bridge.h"
#include "gripper_servo.h"
#include <stdio.h>

void SystemClock_Config(void);

int main(void)
{
    uint32_t lastLedTick = 0U;
    uint32_t lastPrintTick = 0U;

    HAL_Init();
    SystemClock_Config();
    MX_GPIO_Init();
    MX_CAN1_Init();
    MX_USART1_UART_Init();
    MX_USART2_UART_Init();
    Gripper_Init();

    CAN_Proto_Init();
    Arm_Init();
    SerialCmd_Init();

    printf("\r\n=== JXS6000 CAN42 Controller ===\r\n");
    printf("J1..J6: CAN closed-loop drives; PA0: gripper PWM\r\n");
    printf("42 steppers + user-firmware-keil-57: no brake, no STEP/DIR\r\n");
    printf("Commands: move:j2_rev,j3_rev,time_s | status | zero | stop\r\n");
    printf("SLCAN HMI: v4 path 0x50..0x54; gripper 0x55..0x57\r\n");

    while (1) {
        Arm_Update();
        SerialCmd_Process();

        if (HAL_GetTick() - lastLedTick >= 1000U) {
            lastLedTick = HAL_GetTick();
            HAL_GPIO_TogglePin(G_LED_GPIO_Port, G_LED_Pin);
            HAL_GPIO_TogglePin(B_LED_GPIO_Port, B_LED_Pin);
        }
        if (!HostBridge_IsSession() && HAL_GetTick() - lastPrintTick >= 1000U) {
            lastPrintTick = HAL_GetTick();
            Arm_PrintStatus();
        }
        HAL_Delay(1U);
    }
}

void SystemClock_Config(void)
{
    RCC_OscInitTypeDef oscillator = {0};
    RCC_ClkInitTypeDef clock = {0};

    __HAL_RCC_PWR_CLK_ENABLE();
    __HAL_PWR_VOLTAGESCALING_CONFIG(PWR_REGULATOR_VOLTAGE_SCALE1);
    oscillator.OscillatorType = RCC_OSCILLATORTYPE_HSI;
    oscillator.HSIState = RCC_HSI_ON;
    oscillator.HSICalibrationValue = RCC_HSICALIBRATION_DEFAULT;
    oscillator.PLL.PLLState = RCC_PLL_ON;
    oscillator.PLL.PLLSource = RCC_PLLSOURCE_HSI;
    oscillator.PLL.PLLM = 8U;
    oscillator.PLL.PLLN = 168U;
    oscillator.PLL.PLLP = RCC_PLLP_DIV2;
    oscillator.PLL.PLLQ = 4U;
    if (HAL_RCC_OscConfig(&oscillator) != HAL_OK) Error_Handler();

    clock.ClockType = RCC_CLOCKTYPE_HCLK | RCC_CLOCKTYPE_SYSCLK |
                      RCC_CLOCKTYPE_PCLK1 | RCC_CLOCKTYPE_PCLK2;
    clock.SYSCLKSource = RCC_SYSCLKSOURCE_PLLCLK;
    clock.AHBCLKDivider = RCC_SYSCLK_DIV1;
    clock.APB1CLKDivider = RCC_HCLK_DIV4;
    clock.APB2CLKDivider = RCC_HCLK_DIV2;
    if (HAL_RCC_ClockConfig(&clock, FLASH_LATENCY_5) != HAL_OK) Error_Handler();
}

void HAL_CAN_RxFifo0MsgPendingCallback(CAN_HandleTypeDef *hcan)
{
    CAN_RxHeaderTypeDef header;
    uint8_t data[8];
    while (HAL_CAN_GetRxFifoFillLevel(hcan, CAN_RX_FIFO0) != 0U) {
        if (HAL_CAN_GetRxMessage(hcan, CAN_RX_FIFO0, &header, data) != HAL_OK) break;
        CAN_RxCallback(&header, data);
    }
}

void HAL_UART_RxCpltCallback(UART_HandleTypeDef *huart)
{
    if (huart->Instance == USART1) SerialCmd_HandleRx();
}

void HAL_UART_ErrorCallback(UART_HandleTypeDef *huart)
{
    if (huart->Instance == USART1) SerialCmd_HandleError();
}

void Error_Handler(void)
{
    __disable_irq();
    while (1) { }
}
