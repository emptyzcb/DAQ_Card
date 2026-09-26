#include "script_flash.h"
#include "stm32h7xx_hal.h"

#include <string.h>

/* ===== 存储布局常量 ===== */
#define SCRIPT_MAGIC0   'S'
#define SCRIPT_MAGIC1   'C'
#define SCRIPT_MAGIC2   'R'
#define SCRIPT_MAGIC3   'P'

#define SCRIPT_HDR_MAGIC_OFF  0U   /* 4 字节 magic */
#define SCRIPT_HDR_LEN_OFF    4U   /* 4 字节长度（小端） */
#define SCRIPT_HDR_TEXT_OFF   8U   /* 脚本文本起点 */

/* 编程粒度：H7 按 FLASHWORD（32 字节 = 8 个 32 位字）编程 */
#define FLASH_WORD_BYTES  32U
#define FLASH_WORD_WORDS  8U

__align(32) static uint32_t flash_word[FLASH_WORD_WORDS];

/* ===== 本地 CRC16（Modbus 多项式 0xA001，与从站一致） ===== */
static uint16_t crc16_block(const uint8_t *data, uint32_t len)
{
    uint16_t crc = 0xFFFFU;
    uint32_t i;
    for (i = 0; i < len; i++)
    {
        uint8_t b = data[i];
        uint8_t bit;
        crc ^= b;
        for (bit = 0; bit < 8; bit++)
        {
            if (crc & 1U)
            {
                crc = (uint16_t)((crc >> 1) ^ 0xA001U);
            }
            else
            {
                crc >>= 1;
            }
        }
    }
    return crc;
}

/* ===== 读辅助 ===== */
static uint32_t flash_read_u32(uint32_t off)
{
    const uint8_t *p = (const uint8_t *)(SCRIPT_FLASH_BASE + off);
    return (uint32_t)p[0] | ((uint32_t)p[1] << 8) | ((uint32_t)p[2] << 16) | ((uint32_t)p[3] << 24);
}

static int flash_magic_valid(void)
{
    const uint8_t *p = (const uint8_t *)SCRIPT_FLASH_BASE;
    return p[0] == SCRIPT_MAGIC0 && p[1] == SCRIPT_MAGIC1 &&
           p[2] == SCRIPT_MAGIC2 && p[3] == SCRIPT_MAGIC3;
}

/* 已存脚本是否完整有效（magic + 长度合理 + CRC 通过） */
static int flash_content_valid(void)
{
    const uint8_t *p;
    uint32_t len;
    uint16_t stored_crc, calc_crc;

    if (!flash_magic_valid())
    {
        return 0;
    }

    len = flash_read_u32(SCRIPT_HDR_LEN_OFF);
    if (len == 0 || len > SCRIPT_MAX_TEXT)
    {
        return 0;
    }

    p = (const uint8_t *)SCRIPT_FLASH_BASE;
    stored_crc = (uint16_t)(p[SCRIPT_HDR_TEXT_OFF + len] |
                            ((uint16_t)p[SCRIPT_HDR_TEXT_OFF + len + 1] << 8));
    calc_crc = crc16_block(p, SCRIPT_HDR_TEXT_OFF + len);
    return stored_crc == calc_crc;
}

/* ===== 擦除整个脚本扇区 ===== */
static int flash_erase_sector(void)
{
    FLASH_EraseInitTypeDef erase;
    uint32_t sector_error = 0U;
    HAL_StatusTypeDef status;

    erase.TypeErase    = FLASH_TYPEERASE_SECTORS;
    erase.Banks        = SCRIPT_FLASH_BANK;
    erase.Sector       = SCRIPT_FLASH_SECTOR;
    erase.NbSectors    = 1U;
    erase.VoltageRange = FLASH_VOLTAGE_RANGE_3;

    HAL_FLASH_Unlock();
    status = HAL_FLASHEx_Erase(&erase, &sector_error);
    HAL_FLASH_Lock();
    return status == HAL_OK ? 0 : -2;
}

/* ===== 编程一页（32 字节，DataAddress 必须 32 字节对齐） ===== */
static int flash_program_page(uint32_t offset, const uint8_t *data, uint32_t len)
{
    uint32_t i;
    HAL_StatusTypeDef status;

    memset(flash_word, 0xFF, sizeof(flash_word));
    for (i = 0; i < len && i < FLASH_WORD_BYTES; i++)
    {
        flash_word[i / 4U] &= ~(0xFFU << ((i % 4U) * 8U));
        flash_word[i / 4U] |= ((uint32_t)data[i]) << ((i % 4U) * 8U);
    }

    HAL_FLASH_Unlock();
    status = HAL_FLASH_Program(FLASH_TYPEPROGRAM_FLASHWORD,
                               SCRIPT_FLASH_BASE + offset,
                               (uint32_t)flash_word);
    HAL_FLASH_Lock();
    return status == HAL_OK ? 0 : -2;
}

/* ===== 对外接口 ===== */

int SCRIPT_FLASH_Init(void)
{
    /* 仅做存在性校验；无效内容视为"无脚本" */
    return flash_content_valid() ? 0 : -1;
}

int SCRIPT_FLASH_GetLength(void)
{
    uint32_t len;
    if (!flash_content_valid())
    {
        return 0;
    }
    len = flash_read_u32(SCRIPT_HDR_LEN_OFF);
    return (len <= SCRIPT_MAX_TEXT) ? (int)len : 0;
}

uint32_t SCRIPT_FLASH_Read(char *buf, uint32_t max_len)
{
    const uint8_t *p;
    uint32_t len;

    if (buf == 0 || max_len == 0 || !flash_content_valid())
    {
        return 0;
    }

    len = flash_read_u32(SCRIPT_HDR_LEN_OFF);
    if (len > max_len || len == 0 || len > SCRIPT_MAX_TEXT)
    {
        return 0;
    }

    p = (const uint8_t *)SCRIPT_FLASH_BASE + SCRIPT_HDR_TEXT_OFF;
    memcpy(buf, p, len);
    return len;
}

int SCRIPT_FLASH_Erase(void)
{
    int ret = flash_erase_sector();
    if (ret == 0)
    {
        /* 擦除后扇区全 0xFF，magic 自然失效 */
    }
    return ret;
}

int SCRIPT_FLASH_EraseAndWrite(const char *text, uint32_t len)
{
    uint32_t total, off, chunk;
    int ret;

    if (text == 0 || len == 0 || len > SCRIPT_MAX_TEXT)
    {
        return -1;
    }

    /* 总数据 = magic(4) + len(4) + text(len) + crc(2) */
    total = SCRIPT_HDR_TEXT_OFF + len + 2U;

    /* 构造一次完整的扇区镜像，再逐页写入 */
    {
        /* 镜像按 32 字节对齐向上取整，剩余填 0xFF */
        uint32_t image_size = (total + FLASH_WORD_BYTES - 1U) & ~(FLASH_WORD_BYTES - 1U);
        /* 使用静态缓冲避免大栈；8192 + 头尾 < 9KB */
        static __align(32) uint8_t image[SCRIPT_MAX_TEXT + 16U];
        if (image_size > sizeof(image))
        {
            return -1;
        }

        memset(image, 0xFF, sizeof(image));
        image[0] = SCRIPT_MAGIC0;
        image[1] = SCRIPT_MAGIC1;
        image[2] = SCRIPT_MAGIC2;
        image[3] = SCRIPT_MAGIC3;
        image[4] = (uint8_t)(len & 0xFFU);
        image[5] = (uint8_t)((len >> 8) & 0xFFU);
        image[6] = (uint8_t)((len >> 16) & 0xFFU);
        image[7] = (uint8_t)((len >> 24) & 0xFFU);
        memcpy(image + SCRIPT_HDR_TEXT_OFF, text, len);

        {
            uint16_t crc = crc16_block(image, SCRIPT_HDR_TEXT_OFF + len);
            image[SCRIPT_HDR_TEXT_OFF + len]     = (uint8_t)(crc & 0xFFU);
            image[SCRIPT_HDR_TEXT_OFF + len + 1] = (uint8_t)(crc >> 8);
        }

        ret = flash_erase_sector();
        if (ret != 0)
        {
            return ret;
        }

        for (off = 0; off < image_size; off += FLASH_WORD_BYTES)
        {
            chunk = image_size - off;
            if (chunk > FLASH_WORD_BYTES)
            {
                chunk = FLASH_WORD_BYTES;
            }
            ret = flash_program_page(off, image + off, chunk);
            if (ret != 0)
            {
                return ret;
            }
        }
    }

    /* 回读校验 */
    return flash_content_valid() ? 0 : -2;
}
