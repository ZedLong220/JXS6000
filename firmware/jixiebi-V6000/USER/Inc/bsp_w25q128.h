/**
 * @file    bsp_w25q128.h
 * @brief   板载W25Q128外部Flash驱动接口
 * @note    复用开发板示例的扇区读改写流程，使用工程现有HAL SPI1。
 *          PA5=SCK、PA6=MISO、PA7=MOSI、PC13=CS。
 */
#ifndef BSP_W25Q128_H
#define BSP_W25Q128_H

#include "main.h"

/* W25Q128总容量：128Mbit，即16MB。 */
#define W25Q128_TOTAL_SIZE       0x01000000U
/* 开发板预置GBK字库起始地址，驱动禁止向此地址及其后方写入。 */
#define W25Q128_FONT_START_ADDR  0x00A00000U
/* W25Q128最小擦除单位：4KB扇区。 */
#define W25Q128_SECTOR_SIZE      4096U

/**
 * @brief  初始化并读取Flash的JEDEC ID
 * @retval 1=检测到16MB Flash，0=通信失败或容量不匹配
 */
uint8_t W25Q128_Init(void);

/**
 * @brief  从W25Q128读取连续数据
 * @param  addr   24位字节地址
 * @param  data   接收缓冲区
 * @param  length 读取字节数
 * @retval 1=成功，0=设备未就绪、参数越界或SPI通信失败
 */
uint8_t W25Q128_ReadData(uint32_t addr, uint8_t *data, uint16_t length);

/**
 * @brief  向W25Q128写入连续数据，内部自动执行扇区读改写
 * @param  addr   24位字节地址，只允许位于字库区之前
 * @param  data   待写入数据
 * @param  length 写入字节数
 * @retval 1=成功，0=设备未就绪、参数越界或SPI通信失败
 * @note   函数不会写入0x00A00000开始的GBK字库区域。
 */
uint8_t W25Q128_WriteData(uint32_t addr, const uint8_t *data, uint16_t length);

/**
 * @brief  获取初始化时读到的24位JEDEC ID
 * @retval JEDEC ID，格式为“厂商ID|类型ID|容量ID”
 */
uint32_t W25Q128_GetJedecId(void);

#endif /* BSP_W25Q128_H */
