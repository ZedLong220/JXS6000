/**
  ******************************************************************************
  * @file    can.c
  * @brief   This file provides code for the configuration
  *          of the CAN instances.
  ******************************************************************************
  * @attention
  *
  * <h2><center>&copy; Copyright (c) 2026 STMicroelectronics.
  * All rights reserved.</center></h2>
  *
  * This software component is licensed by ST under BSD 3-Clause license,
  * the "License"; You may not use this file except in compliance with the
  * License. You may obtain a copy of the License at:
  *                        opensource.org/licenses/BSD-3-Clause
  *
  ******************************************************************************
  */

/* Includes ------------------------------------------------------------------*/
#include "can.h"

/* USER CODE BEGIN 0 */
#include <stdio.h>
#include <string.h>

#include "common_inc.h"
#include "configurations.h"

CAN_TxHeaderTypeDef TxHeader;
CAN_RxHeaderTypeDef RxHeader;
uint8_t TxData[8];
uint8_t RxData[8];
uint32_t TxMailbox;
volatile uint32_t canTxDropCount = 0;
volatile uint32_t canTxDeferredCount = 0;
volatile uint32_t canTxRetryCount = 0;
volatile uint32_t canRxDropCount = 0;
volatile uint32_t canErrorCount = 0;
volatile uint32_t canLastError = HAL_CAN_ERROR_NONE;

#define CAN_TX_QUEUE_SIZE 16U
typedef struct
{
    CAN_TxHeaderTypeDef header;
    uint8_t data[8];
} PendingCanTx;

static PendingCanTx canTxQueue[CAN_TX_QUEUE_SIZE];
static volatile uint8_t canTxQueueHead = 0U;
static volatile uint8_t canTxQueueTail = 0U;

static uint32_t CAN_EnterQueueCritical(void)
{
    uint32_t key = __get_PRIMASK();
    __disable_irq();
    return key;
}

static void CAN_LeaveQueueCritical(uint32_t key)
{
    __set_PRIMASK(key);
}

static HAL_StatusTypeDef CAN_QueueTx(const CAN_TxHeaderTypeDef *header,
                                    const uint8_t *data)
{
    uint32_t key;
    uint8_t next;

    if (header == NULL || data == NULL || header->DLC > 8U)
        return HAL_ERROR;

    key = CAN_EnterQueueCritical();
    next = (uint8_t)((canTxQueueHead + 1U) % CAN_TX_QUEUE_SIZE);
    if (next == canTxQueueTail)
    {
        CAN_LeaveQueueCritical(key);
        ++canTxDropCount;
        return HAL_BUSY;
    }
    canTxQueue[canTxQueueHead].header = *header;
    memcpy(canTxQueue[canTxQueueHead].data, data, header->DLC);
    canTxQueueHead = next;
    ++canTxDeferredCount;
    CAN_LeaveQueueCritical(key);
    return HAL_OK;
}

/* USER CODE END 0 */

CAN_HandleTypeDef hcan;

/* CAN init function */
void MX_CAN_Init(void)
{

  /* USER CODE BEGIN CAN_Init 0 */
    //printf("can init begin\r\n");
  /* USER CODE END CAN_Init 0 */

  /* USER CODE BEGIN CAN_Init 1 */

  /* USER CODE END CAN_Init 1 */
  hcan.Instance = CAN1;
  hcan.Init.Prescaler = 4;
  hcan.Init.Mode = CAN_MODE_NORMAL;
  hcan.Init.SyncJumpWidth = CAN_SJW_1TQ;
  hcan.Init.TimeSeg1 = CAN_BS1_5TQ;
  hcan.Init.TimeSeg2 = CAN_BS2_3TQ;
  hcan.Init.TimeTriggeredMode = DISABLE;
  hcan.Init.AutoBusOff = ENABLE;
  hcan.Init.AutoWakeUp = DISABLE;
  hcan.Init.AutoRetransmission = ENABLE;
  hcan.Init.ReceiveFifoLocked = DISABLE;
  hcan.Init.TransmitFifoPriority = DISABLE;
  if (HAL_CAN_Init(&hcan) != HAL_OK)
  {
    Error_Handler();
  }
  /* USER CODE BEGIN CAN_Init 2 */
    CAN_FilterTypeDef filter = {0};
    filter.FilterActivation = ENABLE;
    filter.FilterBank = 0;
    filter.FilterFIFOAssignment = CAN_RX_FIFO0;
    filter.FilterIdHigh = 0;
    filter.FilterIdLow = 0;
    filter.FilterMaskIdHigh = 0;
    filter.FilterMaskIdLow = 0;
    filter.FilterMode = CAN_FILTERMODE_IDMASK;
    filter.FilterScale = CAN_FILTERSCALE_32BIT;
    HAL_CAN_ConfigFilter(&hcan, &filter);

    HAL_CAN_Stop(&hcan); // ��ֹͣ
    HAL_Delay(100);
    HAL_StatusTypeDef status = HAL_CAN_Start(&hcan);
    //printf("HAL_CAN_Start status: %d\r\n", status);

    HAL_CAN_ActivateNotification(&hcan,
                                 CAN_IT_TX_MAILBOX_EMPTY |
                                 CAN_IT_RX_FIFO0_MSG_PENDING | CAN_IT_RX_FIFO1_MSG_PENDING |
                                 /* we probably only want this */
                                 CAN_IT_RX_FIFO0_FULL | CAN_IT_RX_FIFO1_FULL |
                                 CAN_IT_RX_FIFO0_OVERRUN | CAN_IT_RX_FIFO1_OVERRUN |
                                 CAN_IT_WAKEUP | CAN_IT_SLEEP_ACK |
                                 CAN_IT_ERROR_WARNING | CAN_IT_ERROR_PASSIVE |
                                 CAN_IT_BUSOFF | CAN_IT_LAST_ERROR_CODE |
                                 CAN_IT_ERROR);

/* Configure Transmission process */
    TxHeader.StdId = boardConfig.canNodeId;
    TxHeader.ExtId = 0x00;
    TxHeader.RTR = CAN_RTR_DATA;
    TxHeader.IDE = CAN_ID_STD;
    TxHeader.DLC = 8;
    TxHeader.TransmitGlobalTime = DISABLE;
//    printf("AFIO->MAPR = 0x%08lX\r\n", AFIO->MAPR);
//    printf("can init end\r\n");
//    HAL_Delay(500); // �� CAN һ����������ʱ��
    //PrintCANBitRate(&hcan);
   // test_can_loopback_once();
  /* USER CODE END CAN_Init 2 */

}

void HAL_CAN_MspInit(CAN_HandleTypeDef* canHandle)
{

  GPIO_InitTypeDef GPIO_InitStruct = {0};
  if(canHandle->Instance==CAN1)
  {
  /* USER CODE BEGIN CAN1_MspInit 0 */

  /* USER CODE END CAN1_MspInit 0 */
    /* CAN1 clock enable */
    __HAL_RCC_CAN1_CLK_ENABLE();

    __HAL_RCC_GPIOA_CLK_ENABLE();
    /**CAN GPIO Configuration
    PA11     ------> CAN_RX
    PA12     ------> CAN_TX
    */
    GPIO_InitStruct.Pin = GPIO_PIN_11;
    GPIO_InitStruct.Mode = GPIO_MODE_INPUT;
    GPIO_InitStruct.Pull = GPIO_NOPULL;
    HAL_GPIO_Init(GPIOA, &GPIO_InitStruct);

    GPIO_InitStruct.Pin = GPIO_PIN_12;
    GPIO_InitStruct.Mode = GPIO_MODE_AF_PP;
    GPIO_InitStruct.Speed = GPIO_SPEED_FREQ_HIGH;
    HAL_GPIO_Init(GPIOA, &GPIO_InitStruct);

    /* CAN1 interrupt Init */
    HAL_NVIC_SetPriority(USB_HP_CAN1_TX_IRQn, 3, 0);
    HAL_NVIC_EnableIRQ(USB_HP_CAN1_TX_IRQn);
    HAL_NVIC_SetPriority(USB_LP_CAN1_RX0_IRQn, 3, 0);
    HAL_NVIC_EnableIRQ(USB_LP_CAN1_RX0_IRQn);
    HAL_NVIC_SetPriority(CAN1_RX1_IRQn, 3, 0);
    HAL_NVIC_EnableIRQ(CAN1_RX1_IRQn);
    HAL_NVIC_SetPriority(CAN1_SCE_IRQn, 3, 0);
    HAL_NVIC_EnableIRQ(CAN1_SCE_IRQn);
  /* USER CODE BEGIN CAN1_MspInit 1 */
    }
}

void HAL_CAN_MspInit1(CAN_HandleTypeDef* canHandle)
{
  //printf("can here\r\n");
  GPIO_InitTypeDef GPIO_InitStruct = {0};
  if(canHandle->Instance==CAN1)
  {
  /* USER CODE BEGIN CAN1_MspInit 0 */

  /* USER CODE END CAN1_MspInit 0 */
    /* CAN1 clock enable */
    __HAL_RCC_CAN1_CLK_ENABLE();

    __HAL_RCC_GPIOB_CLK_ENABLE();
    /**CAN GPIO Configuration
    PB8     ------> CAN_RX
    PB9     ------> CAN_TX
    */
    GPIO_InitStruct.Pin = GPIO_PIN_8;
    GPIO_InitStruct.Mode = GPIO_MODE_INPUT;
    GPIO_InitStruct.Pull = GPIO_NOPULL;
    HAL_GPIO_Init(GPIOB, &GPIO_InitStruct);

    GPIO_InitStruct.Pin = GPIO_PIN_9;
    GPIO_InitStruct.Mode = GPIO_MODE_AF_PP;
    GPIO_InitStruct.Speed = GPIO_SPEED_FREQ_HIGH;
    HAL_GPIO_Init(GPIOB, &GPIO_InitStruct);


    __HAL_AFIO_REMAP_CAN1_2();

    /* CAN1 interrupt Init */
      HAL_NVIC_SetPriority(USB_HP_CAN1_TX_IRQn, 3, 0);
      HAL_NVIC_EnableIRQ(USB_HP_CAN1_TX_IRQn);
      HAL_NVIC_SetPriority(USB_LP_CAN1_RX0_IRQn, 3, 0);
      HAL_NVIC_EnableIRQ(USB_LP_CAN1_RX0_IRQn);
      HAL_NVIC_SetPriority(CAN1_RX1_IRQn, 3, 0);
      HAL_NVIC_EnableIRQ(CAN1_RX1_IRQn);
      HAL_NVIC_SetPriority(CAN1_SCE_IRQn, 3, 0);
      HAL_NVIC_EnableIRQ(CAN1_SCE_IRQn);
  /* USER CODE BEGIN CAN1_MspInit 1 */

  /* USER CODE END CAN1_MspInit 1 */
  }
}

void HAL_CAN_MspDeInit(CAN_HandleTypeDef* canHandle)
{

  if(canHandle->Instance==CAN1)
  {
  /* USER CODE BEGIN CAN1_MspDeInit 0 */

  /* USER CODE END CAN1_MspDeInit 0 */
    /* Peripheral clock disable */
    __HAL_RCC_CAN1_CLK_DISABLE();

    /**CAN GPIO Configuration
    PA11     ------> CAN_RX
    PA12     ------> CAN_TX
    */
    HAL_GPIO_DeInit(GPIOA, GPIO_PIN_11|GPIO_PIN_12);

    /* CAN1 interrupt Deinit */
    HAL_NVIC_DisableIRQ(USB_HP_CAN1_TX_IRQn);
    HAL_NVIC_DisableIRQ(USB_LP_CAN1_RX0_IRQn);
    HAL_NVIC_DisableIRQ(CAN1_RX1_IRQn);
    HAL_NVIC_DisableIRQ(CAN1_SCE_IRQn);
  /* USER CODE BEGIN CAN1_MspDeInit 1 */

  /* USER CODE END CAN1_MspDeInit 1 */
  }
}

/* USER CODE BEGIN 1 */
HAL_StatusTypeDef CAN_Send(CAN_TxHeaderTypeDef* pHeader, uint8_t* data)
{
    HAL_StatusTypeDef status;
    uint32_t key;
    uint8_t queueEmpty;

    if (pHeader == NULL || data == NULL || pHeader->DLC > 8U)
        return HAL_ERROR;

    key = CAN_EnterQueueCritical();
    queueEmpty = canTxQueueHead == canTxQueueTail ? 1U : 0U;
    CAN_LeaveQueueCritical(key);

    if (queueEmpty && HAL_CAN_GetTxMailboxesFreeLevel(&hcan) != 0U)
    {
        status = HAL_CAN_AddTxMessage(&hcan, pHeader, data, &TxMailbox);
        if (status == HAL_OK)
            return HAL_OK;
    }

    /* CAN replies are often produced from the RX interrupt. Never block the
       motor loop there, but retain the complete frame for main-loop retry. */
    return CAN_QueueTx(pHeader, data);
}

void CAN_ProcessTxQueue(void)
{
    PendingCanTx pending;
    uint32_t key;
    uint32_t mailbox;

    while (HAL_CAN_GetTxMailboxesFreeLevel(&hcan) != 0U)
    {
        key = CAN_EnterQueueCritical();
        if (canTxQueueTail == canTxQueueHead)
        {
            CAN_LeaveQueueCritical(key);
            return;
        }
        pending = canTxQueue[canTxQueueTail];
        CAN_LeaveQueueCritical(key);

        if (HAL_CAN_AddTxMessage(&hcan, &pending.header,
                                pending.data, &mailbox) != HAL_OK)
            return;

        key = CAN_EnterQueueCritical();
        canTxQueueTail = (uint8_t)((canTxQueueTail + 1U) % CAN_TX_QUEUE_SIZE);
        ++canTxRetryCount;
        CAN_LeaveQueueCritical(key);
    }
}

//void HAL_CAN_RxFifo0MsgPendingCallback(CAN_HandleTypeDef* CanHandle)
//{
//    printf("CAN Frame Received!\r\n");
//    CAN_RxHeaderTypeDef RxHeader;
//    uint8_t RxData[8];
//
//    if (HAL_CAN_GetRxMessage(CanHandle, CAN_RX_FIFO0, &RxHeader, RxData) != HAL_OK)
//    {
//        Error_Handler();
//    }
//
//    printf("ID: 0x%X, DLC: %d\r\n", RxHeader.StdId, RxHeader.DLC);
//    for (int i = 0; i < RxHeader.DLC; i++)
//    {
//        printf("%02X ", RxData[i]);
//    }
//    printf("\r\n");
//}
 void HAL_CAN_RxFifo0MsgPendingCallback(CAN_HandleTypeDef* CanHandle)
 {
     /* Get RX message */
     if (HAL_CAN_GetRxMessage(CanHandle, CAN_RX_FIFO0, &RxHeader, RxData) != HAL_OK)
     {
         ++canRxDropCount;
         return;
     }

     uint8_t id = (RxHeader.StdId >> 7); // 4Bits ID & 7Bits Msg
     uint8_t cmd = RxHeader.StdId & 0x7F; // 4Bits ID & 7Bits Msg
     //printf("HAL_CAN_RxFifo0MsgPendingCallback id:%d ,cmd:%d RxData[0]:%d\n",id,cmd,RxData[0]);
     if (id == 0 || id == boardConfig.canNodeId)
     {
         OnCanCmd(cmd, RxData, RxHeader.DLC);
     }
 }

void HAL_CAN_ErrorCallback(CAN_HandleTypeDef *CanHandle)
{
    /* AutoBusOff and AutoRetransmission let bxCAN recover in hardware. Never
       stop/restart CAN or delay from this interrupt callback: the position
       controller must keep running while the bus is recovering. */
    canLastError = HAL_CAN_GetError(CanHandle);
    ++canErrorCount;
}
/* USER CODE END 1 */

/************************ (C) COPYRIGHT STMicroelectronics *****END OF FILE****/
