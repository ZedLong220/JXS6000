首先，强烈不建议转为keil工程
非常！非常！非常！非常的麻烦

注意：旧版上位机代码和程序源码是不带有PID参数修改功能。
配合上位机改动PID参数
长电机：Kp：200，Kv：80，Ki：200，Kd：250
短电机：Kp：200，Kv：80，Ki：200，Kd：500

电机4的PID参数：Kp：200，Kv：80，Ki：85，Kd：100

主要改动地方：
1.把Ctrl，Port，UserApp三个文件夹复制到MX生成的keil工程目录下
2.添加对应.c和.cpp文件到工程
3.添加对应.h文件路径到工程

4.retarget.h的#include <sys/stat.h>改为
#if defined(__ARMCC_VERSION)
    struct stat { int st_mode; };
    #define S_IFCHR 0x2000
#elif defined(__GNUC__)
    #include <sys/stat.h>
#endif

5.retarget.c文件删掉 #include <sys/time.h>
文件开头加 #include <stdio.h> 和 UART_HandleTypeDef *gHuart;(UART_HandleTypeDef *gHuart;必须添加#ifndef EBADF之前)
后续添加
#ifndef EBADF
#define EBADF  9
#endif
#ifndef EIO
#define EIO    5
#endif
后续添加
#if defined(__MICROLIB)
int fputc(int ch, FILE *f) {
    (void)f;
    if (gHuart) HAL_UART_Transmit(gHuart, (uint8_t*)&ch, 1, HAL_MAX_DELAY);
    return ch;
}
int fgetc(FILE *f) {
    (void)f;
    uint8_t ch = 0;
    if (gHuart) HAL_UART_Receive(gHuart, &ch, 1, HAL_MAX_DELAY);
    return (int)ch;
}
#endif

6.把sin_map.h的#include <cstdint> 和 #include <cmath> 移到 extern "C" { 之前。
7.把main.c  gpio.c  adc.c  can.c  dma.c  spi.c  tim.c  usart.c  stm32f1xx_it.c和对应的.h文件，都用clion原版的内容复制替换
8.所有#include <stdint-gcc.h>改为#include <stdint.h>
9.stm32f1xx_it.c里的#include <memory.h>改为#include <string.h>
10.注意MX每次重新生成keil工程后，需要把gpio.c里面HAL_NVIC_SetPriority(EXTI9_5_IRQn, 0, 0);和HAL_NVIC_EnableIRQ(EXTI9_5_IRQn);进行注释
11.keil编译器需要选择AC6
12.编译优化选择-O1
13.Language C 选择 c99
14.Language C++ 选择 gnu++ 14
15.Preprocessor Symbols  ---  Define  填写为USE_HAL_DRIVER,STM32F1,STM32F1xx,STM32F103xB
