#ifndef SCRIPT_FLASH_H
#define SCRIPT_FLASH_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/*
 * 脚本内部 Flash 存储（方案 B）
 *
 * 脚本区：内部 Flash 最后一个 128KB 扇区（bank2 扇区 15）
 *   地址范围 0x081E0000 ~ 0x081FFFFF，与代码区（bank1）物理隔离。
 *   H7 双 bank 模式下，bank1 执行代码的同时可对 bank2 擦写，不影响运行。
 *
 * 存储布局（扇区起始处）：
 *   [0..3]  magic "SCRP"
 *   [4..7]  uint32 文本长度 len（小端）
 *   [8..]   UTF-8 脚本文本（len 字节）
 *   末尾 2 字节 CRC16（Modbus 多项式，覆盖 [0 .. 8+len)）
 */

#define SCRIPT_FLASH_BASE      0x081E0000UL
#define SCRIPT_FLASH_SECTOR    FLASH_SECTOR_15
#define SCRIPT_FLASH_BANK      FLASH_BANK_2
#define SCRIPT_FLASH_SIZE      0x20000UL          /* 128 KB */
#define SCRIPT_MAX_TEXT        8192U              /* 脚本最大文本长度（字节） */

/* 返回值约定：0 = 成功，-1 = 参数错误，-2 = Flash 操作失败 */
int  SCRIPT_FLASH_Init(void);                     /* 上电校验已有内容 */
int  SCRIPT_FLASH_GetLength(void);                /* 有效脚本长度，无脚本返回 0 */
uint32_t SCRIPT_FLASH_Read(char *buf, uint32_t max_len); /* 拷贝脚本文本，返回实际长度 */
int  SCRIPT_FLASH_Erase(void);                    /* 擦除整个脚本扇区 */
int  SCRIPT_FLASH_EraseAndWrite(const char *text, uint32_t len); /* 擦除 + 写入 + 回读校验 */

#ifdef __cplusplus
}
#endif

#endif /* SCRIPT_FLASH_H */
