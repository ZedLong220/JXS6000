/**
 * @file    bsp_w25q128.c
 * @brief   W25Q128 HAL SPI驱动实现
 * @note    读、写使能、4KB扇区擦除和256字节页编程流程来自开发板示例。
 *          写入采用“读整扇区、修改RAM副本、擦除、逐页写回”，因此不会
 *          破坏同一扇区内未被本次请求覆盖的数据。
 */
#include "bsp_w25q128.h"
#include "spi.h"       /* 工程由CubeMX初始化的SPI1句柄hspi1 */
#include <string.h>      /* memcpy */

/* W25Q128 SPI指令码。 */
#define W25Q_CMD_WRITE_ENABLE   0x06U /* 写使能 */
#define W25Q_CMD_READ_STATUS1   0x05U /* 读取状态寄存器1 */
#define W25Q_CMD_READ_DATA      0x03U /* 普通速度连续读取 */
#define W25Q_CMD_PAGE_PROGRAM   0x02U /* 页编程，单次最多256字节 */
#define W25Q_CMD_SECTOR_ERASE   0x20U /* 擦除一个4KB扇区 */
#define W25Q_CMD_JEDEC_ID       0x9FU /* 读取厂商、类型和容量ID */

/* Flash物理页大小，页编程不能跨越页边界。 */
#define W25Q_PAGE_SIZE          256U
/* 单次HAL SPI收发的最大等待时间。 */
#define W25Q_SPI_TIMEOUT_MS     100U
/* 擦除或编程完成的最大等待时间。 */
#define W25Q_BUSY_TIMEOUT_MS    3000U

/* PC13片选控制：低电平开始一次SPI命令，高电平结束。 */
#define W25Q_CS_LOW()  HAL_GPIO_WritePin(W25Q_CS_GPIO_Port, W25Q_CS_Pin, GPIO_PIN_RESET)
#define W25Q_CS_HIGH() HAL_GPIO_WritePin(W25Q_CS_GPIO_Port, W25Q_CS_Pin, GPIO_PIN_SET)

static uint8_t w25qReady;       /* 1=初始化成功，允许后续读写 */
static uint32_t w25qJedecId;    /* 初始化时读取的24位JEDEC ID */
/* 扇区读改写缓冲，保护同一扇区中未被覆盖的原数据。 */
static uint8_t w25qSectorBuffer[W25Q128_SECTOR_SIZE];

/**
 * @brief  发送06h写使能指令
 * @retval 1=SPI发送成功，0=SPI发送失败
 * @note   每次页编程或扇区擦除之前都必须重新发送写使能。
 */
static uint8_t w25q_write_enable(void)
{
    uint8_t command = W25Q_CMD_WRITE_ENABLE; /* 本次发送的单字节指令 */
    HAL_StatusTypeDef status;                /* 保存HAL层通信结果 */

    /* 拉低CS发送指令，随后拉高CS使写使能命令生效。 */
    W25Q_CS_LOW();
    status = HAL_SPI_Transmit(&hspi1, &command, 1, W25Q_SPI_TIMEOUT_MS);
    W25Q_CS_HIGH();
    return (status == HAL_OK) ? 1U : 0U;
}

/**
 * @brief  轮询状态寄存器BUSY位，等待Flash内部操作结束
 * @retval 1=Flash已空闲，0=SPI失败或等待超时
 */
static uint8_t w25q_wait_ready(void)
{
    uint8_t command = W25Q_CMD_READ_STATUS1; /* 05h状态读取指令 */
    uint8_t statusReg = 0x01U;               /* bit0=BUSY，先按忙状态初始化 */
    uint32_t start = HAL_GetTick();           /* 用于3秒超时判断 */

    /* CS保持为低时，芯片会连续返回最新状态寄存器值。 */
    W25Q_CS_LOW();
    if (HAL_SPI_Transmit(&hspi1, &command, 1, W25Q_SPI_TIMEOUT_MS) != HAL_OK) {
        W25Q_CS_HIGH();
        return 0U;
    }

    do {
        /* 每接收一个字节就检查一次BUSY位。 */
        if (HAL_SPI_Receive(&hspi1, &statusReg, 1, W25Q_SPI_TIMEOUT_MS) != HAL_OK) {
            W25Q_CS_HIGH();
            return 0U;
        }
        if ((statusReg & 0x01U) == 0U) {
            /* BUSY=0，内部擦写已经结束。 */
            W25Q_CS_HIGH();
            return 1U;
        }
    } while ((HAL_GetTick() - start) < W25Q_BUSY_TIMEOUT_MS);

    /* 超时也必须释放CS，避免阻塞后续SPI通信。 */
    W25Q_CS_HIGH();
    return 0U;
}

/**
 * @brief  擦除addr所在的4KB扇区
 * @param  addr 扇区内任意地址；调用者通常传入4KB对齐地址
 * @retval 1=擦除成功，0=写使能、SPI通信或等待过程失败
 */
static uint8_t w25q_erase_sector(uint32_t addr)
{
    uint8_t command[4]; /* 1字节指令 + 3字节地址 */

    /* 按示例流程，先写使能并确认芯片处于空闲状态。 */
    if (!w25q_write_enable() || !w25q_wait_ready()) return 0U;

    /* W25Q128使用24位地址，按高字节在前发送。 */
    command[0] = W25Q_CMD_SECTOR_ERASE;
    command[1] = (uint8_t)(addr >> 16);
    command[2] = (uint8_t)(addr >> 8);
    command[3] = (uint8_t)addr;

    W25Q_CS_LOW();
    if (HAL_SPI_Transmit(&hspi1, command, sizeof(command), W25Q_SPI_TIMEOUT_MS) != HAL_OK) {
        W25Q_CS_HIGH();
        return 0U;
    }
    W25Q_CS_HIGH();
    /* 擦除命令返回后芯片仍在内部操作，必须等待BUSY清零。 */
    return w25q_wait_ready();
}

/**
 * @brief  向单个物理页写入数据
 * @param  addr   写入起始地址
 * @param  data   待写入数据缓冲区
 * @param  length 写入长度，范围1~256且不能跨页
 * @retval 1=编程成功，0=参数非法或Flash操作失败
 */
static uint8_t w25q_program_page(uint32_t addr, const uint8_t *data, uint16_t length)
{
    uint8_t command[4]; /* 1字节指令 + 3字节地址 */

    /* 页编程跨页会在当前页内回卷，因此必须提前拒绝。 */
    if (length == 0U || length > W25Q_PAGE_SIZE ||
        ((addr & (W25Q_PAGE_SIZE - 1U)) + length) > W25Q_PAGE_SIZE) {
        return 0U;
    }
    /* 每一页写入前都需要重新打开写使能锁存器。 */
    if (!w25q_write_enable() || !w25q_wait_ready()) return 0U;

    /* 组装02h页编程指令及24位起始地址。 */
    command[0] = W25Q_CMD_PAGE_PROGRAM;
    command[1] = (uint8_t)(addr >> 16);
    command[2] = (uint8_t)(addr >> 8);
    command[3] = (uint8_t)addr;

    W25Q_CS_LOW();
    if (HAL_SPI_Transmit(&hspi1, command, sizeof(command), W25Q_SPI_TIMEOUT_MS) != HAL_OK ||
        HAL_SPI_Transmit(&hspi1, (uint8_t *)data, length, W25Q_SPI_TIMEOUT_MS) != HAL_OK) {
        W25Q_CS_HIGH();
        return 0U;
    }
    W25Q_CS_HIGH();
    /* 等待本页数据真正写入Flash阵列。 */
    return w25q_wait_ready();
}

/**
 * @brief  把RAM中的完整4KB扇区逐页写回Flash
 * @param  addr 扇区起始地址，必须4KB对齐
 * @param  data 4KB扇区数据
 * @retval 1=16页全部写入成功，0=任意一页失败
 */
static uint8_t w25q_write_sector(uint32_t addr, const uint8_t *data)
{
    uint16_t offset; /* 当前256字节页在扇区内的偏移 */

    /* 一个4KB扇区包含16个256字节页。 */
    for (offset = 0U; offset < W25Q128_SECTOR_SIZE; offset += W25Q_PAGE_SIZE) {
        if (!w25q_program_page(addr + offset, data + offset, W25Q_PAGE_SIZE)) return 0U;
    }
    return 1U;
}

/**
 * @brief  初始化W25Q128并检查芯片容量
 * @retval 1=检测到容量码18h的16MB芯片，0=未检测到
 */
uint8_t W25Q128_Init(void)
{
    uint8_t command = W25Q_CMD_JEDEC_ID; /* 9Fh读取JEDEC ID */
    uint8_t id[3] = {0U};                /* 厂商ID、类型ID、容量ID */

    /* 初始化开始时先清除旧状态，避免失败后误认为设备可用。 */
    w25qReady = 0U;
    w25qJedecId = 0U;
    W25Q_CS_HIGH();
    /* 上电后留出芯片启动时间。 */
    HAL_Delay(5);

    /* 发送9Fh，并连续读取3字节JEDEC标识。 */
    W25Q_CS_LOW();
    if (HAL_SPI_Transmit(&hspi1, &command, 1, W25Q_SPI_TIMEOUT_MS) != HAL_OK ||
        HAL_SPI_Receive(&hspi1, id, sizeof(id), W25Q_SPI_TIMEOUT_MS) != HAL_OK) {
        W25Q_CS_HIGH();
        return 0U;
    }
    W25Q_CS_HIGH();

    /* 合并为便于打印和诊断的24位数值。 */
    w25qJedecId = ((uint32_t)id[0] << 16) | ((uint32_t)id[1] << 8) | id[2];
    /* 容量码18h代表128Mbit（16MB）；00/FF通常表示SPI未连通。 */
    if (id[0] == 0x00U || id[0] == 0xFFU || id[2] != 0x18U) return 0U;

    w25qReady = 1U;
    return 1U;
}

/**
 * @brief  从Flash指定地址读取连续数据
 * @param  addr   24位起始地址
 * @param  data   接收缓冲区
 * @param  length 读取字节数
 * @retval 1=读取成功，0=参数非法或SPI失败
 */
uint8_t W25Q128_ReadData(uint32_t addr, uint8_t *data, uint16_t length)
{
    uint8_t command[4];        /* 1字节指令 + 3字节地址 */
    HAL_StatusTypeDef status;  /* HAL SPI执行结果 */

    /* 拒绝未初始化、空指针、零长度和超出16MB容量的读取。 */
    if (!w25qReady || data == NULL || length == 0U ||
        addr >= W25Q128_TOTAL_SIZE || length > (W25Q128_TOTAL_SIZE - addr)) {
        return 0U;
    }

    /* 组装03h读取指令及24位地址。 */
    command[0] = W25Q_CMD_READ_DATA;
    command[1] = (uint8_t)(addr >> 16);
    command[2] = (uint8_t)(addr >> 8);
    command[3] = (uint8_t)addr;

    /* 一次CS低电平周期内先发地址，再接收连续数据。 */
    W25Q_CS_LOW();
    status = HAL_SPI_Transmit(&hspi1, command, sizeof(command), W25Q_SPI_TIMEOUT_MS);
    if (status == HAL_OK) {
        status = HAL_SPI_Receive(&hspi1, data, length, W25Q_SPI_TIMEOUT_MS);
    }
    W25Q_CS_HIGH();
    return (status == HAL_OK) ? 1U : 0U;
}

/**
 * @brief  向Flash写入任意长度的连续数据
 * @param  addr   写入起始地址
 * @param  data   待写入数据
 * @param  length 写入字节数
 * @retval 1=写入成功，0=参数越界或任一Flash操作失败
 * @note   采用扇区读改写方式，并强制保护0x00A00000后的字库。
 */
uint8_t W25Q128_WriteData(uint32_t addr, const uint8_t *data, uint16_t length)
{
    uint32_t sectorAddr;   /* 当前处理扇区的4KB对齐首地址 */
    uint16_t sectorOffset; /* 当前写入地址在扇区内的偏移 */
    uint16_t chunk;        /* 本轮在当前扇区中写入的字节数 */

    /* 上方6MB是开发板GBK字库，本驱动将其作为只读区保护。 */
    if (!w25qReady || data == NULL || length == 0U ||
        addr >= W25Q128_FONT_START_ADDR || length > (W25Q128_FONT_START_ADDR - addr)) {
        return 0U;
    }

    /* 写入可能跨越多个扇区，因此逐扇区执行读改写。 */
    while (length > 0U) {
        /* 计算当前地址对应的扇区、扇区内偏移和本轮长度。 */
        sectorAddr = addr & ~(W25Q128_SECTOR_SIZE - 1U);
        sectorOffset = (uint16_t)(addr - sectorAddr);
        chunk = (uint16_t)(W25Q128_SECTOR_SIZE - sectorOffset);
        if (chunk > length) chunk = length;

        /* 先读取完整旧扇区，再只覆盖调用者要求修改的区域。 */
        if (!W25Q128_ReadData(sectorAddr, w25qSectorBuffer, W25Q128_SECTOR_SIZE)) return 0U;
        memcpy(&w25qSectorBuffer[sectorOffset], data, chunk);
        /* 擦除当前扇区，然后按16页写回合并后的完整数据。 */
        if (!w25q_erase_sector(sectorAddr) ||
            !w25q_write_sector(sectorAddr, w25qSectorBuffer)) {
            return 0U;
        }

        /* 推进输入地址、数据指针和剩余长度，继续处理下一扇区。 */
        addr += chunk;
        data += chunk;
        length = (uint16_t)(length - chunk);
    }
    return 1U;
}

/**
 * @brief  返回初始化时缓存的JEDEC ID
 * @retval 24位JEDEC ID；初始化失败时可能为0、FFFFFF或异常值
 */
uint32_t W25Q128_GetJedecId(void)
{
    return w25qJedecId;
}
