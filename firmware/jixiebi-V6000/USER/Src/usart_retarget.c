/**
 * @file    usart_retarget.c
 * @brief   printf重定向到USART1(调试输出)
 * @note    放到 Core/Src/ 目录
 *          作用:让标准C库的printf函数通过USART1串口输出,
 *          方便用串口助手查看调试信息。
 *          使用前提:Keil需勾选 Use MicroLIB(微库),
 *          否则链接时会报错(缺少_sys_exit等符号)。
 */

#include "main.h"       /* HAL库头文件,包含uint8_t等类型 */
#include "usart.h"       /* USART句柄huart1声明 */
#include <stdio.h>       /* 标准IO,FILE类型和fputc声明 */

/* 不同编译器对printf重定向的函数签名不同:
 * - GCC编译器: 重定向 __io_putchar 函数
 * - ARMCC(Keil)编译器: 重定向 fputc 函数
 * 这里用宏自动适配,实际在Keil(ARMCC)下走 fputc 分支
 */
#ifdef __GNUC__
  #define PUTCHAR_PROTOTYPE int __io_putchar(int ch)
#else
  #define PUTCHAR_PROTOTYPE int fputc(int ch, FILE *f)
#endif

/**
 * @brief  printf字符输出底层函数(重定向)
 *         每次printf调用时,内部会逐个字符调用此函数
 * @param  ch  要输出的字符
 * @param  f   文件指针(Keil下不会用到,但签名需要)
 * @retval 输出的字符
 */
PUTCHAR_PROTOTYPE
{
    /* 调用HAL库阻塞发送一个字节到USART1
     * 参数3:发送长度=1字节
     * 参数4:超时=0xFFFF(约65秒,防止卡死)
     */
    HAL_UART_Transmit(&huart1, (uint8_t *)&ch, 1, 0xFFFF);
    return ch;
}
