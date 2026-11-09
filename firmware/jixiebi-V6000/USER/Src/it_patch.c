/**
 * @file    it_patch.c
 * @brief   stm32f4xx_it.c需要添加的代码片段(CAN中断相关)
 * @note    不要直接编译此文件,把内容粘贴到stm32f4xx_it.c对应位置
 *
 * stm32f4xx_it.c 是中断服务程序文件,CubeMX会自动生成
 * 各外设的中断处理函数(ISR)。
 */

/* ============================================================
 * 在 stm32f4xx_it.c 的 USER CODE BEGIN Includes 区域添加:
 * (位于文件顶部include区)
 * ============================================================ */
#include "can_protocol.h"  /* 需要访问CAN_RxCallback声明 */

/* ============================================================
 * 重要说明:
 *
 * CAN1_RX0_IRQHandler 和 TIM2_IRQHandler 这两个中断处理函数
 * 已经由CubeMX自动生成,不需要手动修改它们本身。
 *
 * CubeMX生成的ISR内部会调用HAL库的回调函数:
 *   CAN1_RX0_IRQHandler -> HAL_CAN_IRQHandler -> HAL_CAN_RxFifo0MsgPendingCallback
 *   TIM2_IRQHandler     -> HAL_TIM_IRQHandler  -> HAL_TIM_PeriodElapsedCallback
 *
 * 我们只需要在 main.c 的 USER CODE BEGIN 4 区域实现这两个回调函数即可,
 * 回调函数的具体代码在 main_patch.c 中已经提供。
 *
 * 这样设计的好处:
 *   - 不破坏CubeMX生成的ISR代码
 *   - 重新生成工程时不会丢失用户代码
 *   - 逻辑清晰: ISR由CubeMX管,业务逻辑由用户管
 * ============================================================ */
