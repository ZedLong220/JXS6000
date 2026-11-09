/**
 * @file    main_patch.c
 * @brief   main.c需要添加的代码片段(复制到对应USER CODE区域)
 * @note    不要直接编译此文件,把内容按区域粘贴到main.c对应位置
 *
 * main.c由CubeMX生成,有若干 USER CODE BEGIN/END 区域,
 * 在这些区域内的代码不会被CubeMX重新生成时覆盖。
 */

/* ============================================================
 * 在 main.c 的 USER CODE BEGIN Includes 区域添加:
 * (位于main.c顶部include区)
 * ============================================================ */
#include "can_protocol.h"      /* CAN通信协议 */
#include "arm_controller.h"    /* 机械臂控制器 */
#include "kinematics.h"        /* 正运动学 */
#include "serial_cmd.h"        /* 串口命令解析 */
#include <stdio.h>             /* printf */

/* ============================================================
 * 在 main.c 的 USER CODE BEGIN PV 区域添加:
 * (位于main.c私有变量区)
 * ============================================================ */
/* 控制周期计数器(TIM2中断里自增,1kHz) */
static uint32_t ctrlTickCount = 0;
/* 上次状态打印的时间戳(用于每秒打印一次) */
static uint32_t lastPrintTick = 0;

/* ============================================================
 * 在 main.c 的 USER CODE BEGIN 2 区域添加:
 * (位于main函数的初始化部分,while循环之前)
 * ============================================================ */
  /* 初始化CAN通信(配置过滤器+启动CAN+开中断) */
  CAN_Proto_Init();

  /* 初始化机械臂控制器(清零角度+设置状态) */
  Arm_Init();

  /* 启动TIM2定时器中断(1kHz控制周期) */
  HAL_TIM_Base_Start_IT(&htim2);

  /* 使能所有6个电机(通电锁定) */
  Arm_EnableAll();

  /* 初始化串口命令接收 */
  SerialCmd_Init();

  /* 打印启动信息和使用说明 */
  printf("\r\n=== JXS2000 Arm Controller ===\r\n");
  printf("STM32F407VET6 + 6xCtrlStepDriver\r\n");
  printf("CAN 1Mbps, Control 1kHz\r\n");
  printf("Commands:\r\n");
  printf("  j1:val,j2:val,...,j6:val[,speed:v]\\n  (move to angles)\r\n");
  printf("  speed:deg_s  (set default move speed; speed queries it)\r\n");
  printf("  point:set/save/get/list/move/clear/store/load\r\n");
  printf("  point edits RAM; point:store commits all changes to Flash\r\n");
  printf("  loop:first,last,count[,speed]\r\n");
  printf("  home\\n  stop\\n  sethome\\n  status\\n\r\n");
  printf("  enable:id[,0/1]  calib:id  pos1:id\r\n");
  printf("  current:id,A  pos:id,rev  velocity:id,rad_s  (id=0 all)\r\n");
  printf("================================\r\n");

/* ============================================================
 * 在 main.c 的 USER CODE BEGIN 3 区域添加:
 * (位于main函数的while(1)循环体内)
 * ============================================================ */
    Arm_Update();  /* 运动状态更新 + 编码器实际位置动态穿地检测 */

    /* 每1秒打印一次状态(关节角度+末端位姿+穿地检查) */
    if (HAL_GetTick() - lastPrintTick >= 1000) {
        lastPrintTick = HAL_GetTick();
        if (Arm_GetState() != ARM_STATE_MOVING) {
            Arm_PrintStatus();
        }
    }

    /* 处理USART1接收的上位机命令 */
    SerialCmd_Process();

    HAL_Delay(10);  /* 主循环延时10ms,降低CPU占用 */

/* ============================================================
 * 在 main.c 的 USER CODE BEGIN 4 区域添加:
 * (位于main.c文件末尾,回调函数区)
 * ============================================================ */

/**
 * @brief  TIM2周期回调(1kHz中断)
 *         每毫秒触发一次,用于实时控制任务
 *         目前控制逻辑在主循环处理,这里留作高频任务扩展
 * @param  htim 定时器句柄
 */
void HAL_TIM_PeriodElapsedCallback(TIM_HandleTypeDef *htim)
{
    if (htim->Instance == TIM2) {
        ctrlTickCount++;  /* 计数器自增 */

        /* 1kHz实时任务占位 */
        /* 这里可以放高频控制代码,如: */
        /* - 实时穿地检查 */
        /* - 轨迹插值 */
        /* - 电机状态轮询 */
        /* 目前控制逻辑在主循环中处理 */
    }
}

/**
 * @brief  CAN接收回调(CAN接收中断触发)
 *         从FIFO0取出帧,转发给协议解析函数
 * @param  hcan CAN句柄
 */
void HAL_CAN_RxFifo0MsgPendingCallback(CAN_HandleTypeDef *hcan)
{
    CAN_RxHeaderTypeDef header;
    uint8_t data[8];

    /* 从FIFO0读取接收到的CAN帧 */
    if (HAL_CAN_GetRxMessage(hcan, CAN_RX_FIFO0, &header, data) == HAL_OK) {
        /* 转发给协议层解析(按节点ID和命令字分发) */
        CAN_RxCallback(&header, data);
    }
}

/**
 * @brief  USART1接收完成回调(上位机命令)
 *         逐字节收取PC上位机的运动指令
 * @param  huart UART句柄
 */
void HAL_UART_RxCpltCallback(UART_HandleTypeDef *huart)
{
    if (huart->Instance == USART1) {
        SerialCmd_HandleRx();
    }
}
