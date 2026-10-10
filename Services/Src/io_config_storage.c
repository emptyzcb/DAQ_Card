/**
 * @file    io_config_storage.c
 * @brief   IO 逻辑程序镜像存储（v2 字节码格式，A/B 双槽主备）
 *
 * 镜像布局（大端）：
 *   [0..3]    MAGIC = 0x494F4346
 *   [4..5]    格式版本 = 2
 *   [6..7]    头部长度 = 32
 *   [8..11]   generation（代次，上位机自增）
 *   [12..13]  scan_period_ms（扫描周期，默认 10）
 *   [14..15]  保留
 *   [16..19]  程序主体长度（16 模式入口表 + 字节码）
 *   [20..23]  主体 CRC32（自 BODY_OFFSET 起算）
 *   [24..27]  保留
 *   [28..31]  头部 CRC32（前 28 字节）
 *
 * 校验规则（工业安全）：
 *   - 主体长度必须满足 BODY_OFFSET + body_length == 镜像总长；
 *   - 主体 CRC 与头部 CRC 双层校验，任一不通过即拒绝激活；
 *   - 激活前回读新槽并完整校验，失败保持原槽运行。
 */

#include "io_config_storage.h"

#include <string.h>

#include "main.h"

/* 镜像槽区须位于 Keil scatter 文件应用程序镜像之外（0x081C0000~0x08200000）。 */
#define IO_CONFIG_SLOT_A_ADDRESS 0x081C0000UL
#define IO_CONFIG_SLOT_B_ADDRESS 0x081E0000UL
#define IO_CONFIG_SLOT_SIZE      0x00020000UL
#define IO_CONFIG_VALID_MARKER   0x56414C44UL

typedef struct
{
  uint8_t  active;             /* 下载会话进行中 */
  uint16_t session;            /* 会话号 */
  uint32_t expected_length;    /* 期望镜像总长 */
  uint32_t expected_crc32;     /* 期望整体 CRC32 */
  uint32_t received_length;    /* 已接收字节数 */
  uint8_t  verified;           /* 校验通过标志 */
} IO_CONFIG_Download;

static uint8_t  io_config_active_image[IO_CONFIG_MAX_IMAGE_SIZE];
static uint8_t  io_config_staging_image[IO_CONFIG_MAX_IMAGE_SIZE];
static uint32_t io_config_active_length;
static uint8_t  io_config_active_slot;
static uint8_t  io_config_running;
static uint16_t io_config_last_error;
static IO_CONFIG_Download io_config_download;

static uint16_t io_config_read_u16(const uint8_t *data)
{
  return (uint16_t)(((uint16_t)data[0] << 8) | data[1]);
}

static uint32_t io_config_read_u32(const uint8_t *data)
{
  return ((uint32_t)data[0] << 24) | ((uint32_t)data[1] << 16) |
         ((uint32_t)data[2] << 8) | data[3];
}

static void io_config_write_u32(uint8_t *data, uint32_t value)
{
  data[0] = (uint8_t)(value >> 24);
  data[1] = (uint8_t)(value >> 16);
  data[2] = (uint8_t)(value >> 8);
  data[3] = (uint8_t)value;
}

uint32_t IO_CONFIG_Crc32(const uint8_t *data, uint32_t length)
{
  uint32_t crc = 0xFFFFFFFFUL;
  if (data == 0)
  {
    return 0U;
  }
  for (uint32_t index = 0U; index < length; index++)
  {
    crc ^= data[index];
    for (uint8_t bit = 0U; bit < 8U; bit++)
    {
      crc = ((crc & 1U) != 0U) ? ((crc >> 1) ^ 0xEDB88320UL) : (crc >> 1);
    }
  }
  return ~crc;
}

/*
 * 校验 v2 镜像：MAGIC、格式版本、头部长度、主体长度关系、双层 CRC。
 * 通过返回 1，否则返回 0。
 */
static uint8_t io_config_validate_image(const uint8_t *image, uint32_t length)
{
  uint32_t body_length;

  if ((image == 0) || (length < IO_CONFIG_HEADER_LENGTH) ||
      (length > IO_CONFIG_MAX_IMAGE_SIZE) ||
      (io_config_read_u32(&image[IO_CONFIG_OFF_MAGIC]) != IO_CONFIG_MAGIC) ||
      (io_config_read_u16(&image[IO_CONFIG_OFF_VERSION]) != IO_CONFIG_FORMAT_VERSION) ||
      (io_config_read_u16(&image[IO_CONFIG_OFF_HEADER_LENGTH]) != IO_CONFIG_HEADER_LENGTH))
  {
    return 0U;
  }

  body_length = io_config_read_u32(&image[IO_CONFIG_OFF_BODY_LENGTH]);
  if ((IO_CONFIG_BODY_OFFSET + body_length != length) ||
      (IO_CONFIG_Crc32(&image[IO_CONFIG_BODY_OFFSET], body_length) !=
       io_config_read_u32(&image[IO_CONFIG_OFF_BODY_CRC])) ||
      (IO_CONFIG_Crc32(image, 28U) != io_config_read_u32(&image[IO_CONFIG_OFF_HEADER_CRC])))
  {
    return 0U;
  }

  return 1U;
}

/* 有效标记写入偏移：镜像长度向上对齐到 32 字节。 */
static uint32_t io_config_marker_offset(uint32_t image_length)
{
  return (image_length + 31U) & ~31UL;
}

/*
 * 校验指定槽区镜像并加载到活动缓冲区。
 * 通过返回 1 并更新活动长度/槽号，否则返回 0 且不改动当前状态。
 */
static uint8_t io_config_slot_valid(uint32_t address, uint8_t slot)
{
  const uint8_t *image = (const uint8_t *)address;
  uint32_t image_length;
  uint32_t marker_address;

  if (io_config_read_u32(image) != IO_CONFIG_MAGIC)
  {
    return 0U;
  }
  image_length = IO_CONFIG_BODY_OFFSET + io_config_read_u32(&image[IO_CONFIG_OFF_BODY_LENGTH]);
  marker_address = address + io_config_marker_offset(image_length);
  if ((image_length > IO_CONFIG_MAX_IMAGE_SIZE) ||
      (io_config_marker_offset(image_length) + 32U > IO_CONFIG_SLOT_SIZE) ||
      (io_config_read_u32((const uint8_t *)marker_address) != IO_CONFIG_VALID_MARKER) ||
      (io_config_validate_image(image, image_length) == 0U))
  {
    return 0U;
  }

  memcpy(io_config_active_image, image, image_length);
  io_config_active_length = image_length;
  io_config_active_slot = slot;
  return 1U;
}

static uint8_t io_config_erase_sector(uint32_t sector)
{
  FLASH_EraseInitTypeDef erase = {0};
  uint32_t error = 0U;

  erase.TypeErase = FLASH_TYPEERASE_SECTORS;
  erase.Banks = FLASH_BANK_2;
  erase.Sector = sector;
  erase.NbSectors = 1U;
  erase.VoltageRange = FLASH_VOLTAGE_RANGE_3;
  return (HAL_FLASHEx_Erase(&erase, &error) == HAL_OK) ? 1U : 0U;
}

/*
 * Flash 编程：32 字节对齐（STM32H7 双字编程约束）。
 * 协议处理为串行调用，使用静态缓冲安全。
 */
static uint8_t io_config_write_flash(uint32_t address, const uint8_t *data, uint32_t length)
{
  static uint8_t word[32] __ALIGNED(32);

  while (length > 0U)
  {
    uint32_t count = (length > sizeof(word)) ? sizeof(word) : length;
    memset(word, 0xFF, sizeof(word));
    memcpy(word, data, count);
    if (HAL_FLASH_Program(FLASH_TYPEPROGRAM_FLASHWORD, address, (uint32_t)word) != HAL_OK)
    {
      return 0U;
    }
    address += sizeof(word);
    data += count;
    length -= count;
  }
  return 1U;
}

/* 无有效镜像时的安全默认态：仅头部合法，引擎判定停止。 */
static void io_config_load_safe_default(void)
{
  memset(io_config_active_image, 0, sizeof(io_config_active_image));
  io_config_write_u32(&io_config_active_image[IO_CONFIG_OFF_MAGIC], IO_CONFIG_MAGIC);
  io_config_active_image[IO_CONFIG_OFF_VERSION + 1] = IO_CONFIG_FORMAT_VERSION;
  io_config_active_image[IO_CONFIG_OFF_HEADER_LENGTH + 1] = IO_CONFIG_HEADER_LENGTH;
  io_config_active_length = IO_CONFIG_HEADER_LENGTH;
  io_config_active_slot = 3U;
  io_config_running = 0U;
}

void IO_CONFIG_Init(void)
{
  uint8_t valid_a = io_config_slot_valid(IO_CONFIG_SLOT_A_ADDRESS, 1U);
  uint32_t generation_a = valid_a ? io_config_read_u32(&io_config_active_image[IO_CONFIG_OFF_GENERATION]) : 0U;
  uint8_t valid_b = io_config_slot_valid(IO_CONFIG_SLOT_B_ADDRESS, 2U);
  uint32_t generation_b = valid_b ? io_config_read_u32(&io_config_active_image[IO_CONFIG_OFF_GENERATION]) : 0U;

  memset(&io_config_download, 0, sizeof(io_config_download));
  memset(io_config_staging_image, 0xFF, sizeof(io_config_staging_image));
  io_config_last_error = 0U;

  if ((valid_a == 0U) && (valid_b == 0U))
  {
    io_config_load_safe_default();
    return;
  }

  /* 双槽均有效时加载代次更新者；校验第二槽会临时覆盖活动缓冲区，需重载一次。 */
  if ((valid_a != 0U) && ((valid_b == 0U) || (generation_a >= generation_b)))
  {
    (void)io_config_slot_valid(IO_CONFIG_SLOT_A_ADDRESS, 1U);
  }
  else
  {
    (void)io_config_slot_valid(IO_CONFIG_SLOT_B_ADDRESS, 2U);
  }
  io_config_running = 1U;
}

const uint8_t *IO_CONFIG_GetActiveImage(uint32_t *length)
{
  if (length != 0)
  {
    *length = io_config_active_length;
  }
  return io_config_active_image;
}

uint8_t IO_CONFIG_GetInfo(IO_CONFIG_Info *info)
{
  if (info == 0)
  {
    return 0U;
  }
  memset(info, 0, sizeof(*info));
  info->format_version = io_config_read_u16(&io_config_active_image[IO_CONFIG_OFF_VERSION]);
  info->run_state = (io_config_download.active != 0U) ? 2U : (io_config_running != 0U ? 1U : 0U);
  info->config_state = io_config_active_slot;
  info->rule_count = 0U; /* v2 无规则数概念 */
  info->image_length = io_config_active_length;
  info->image_crc32 = IO_CONFIG_Crc32(io_config_active_image, io_config_active_length);
  info->generation = io_config_read_u32(&io_config_active_image[IO_CONFIG_OFF_GENERATION]);
  info->scan_period_ms = io_config_read_u16(&io_config_active_image[IO_CONFIG_OFF_SCAN_PERIOD_MS]);
  info->last_error = io_config_last_error;
  info->active_slot = io_config_active_slot;
  return 1U;
}

uint8_t IO_CONFIG_IsRunning(void)
{
  return io_config_running;
}

void IO_CONFIG_SetRunning(uint8_t running)
{
  io_config_running = (running != 0U) ? 1U : 0U;
}

uint8_t IO_CONFIG_Begin(uint16_t session, uint16_t format_version, uint32_t image_length,
                        uint32_t image_crc32)
{
  if ((io_config_download.active != 0U) || (session == 0U))
  {
    return IO_CONFIG_STATUS_BUSY;
  }
  if (format_version != IO_CONFIG_FORMAT_VERSION)
  {
    return IO_CONFIG_STATUS_VERSION;
  }
  if ((image_length < IO_CONFIG_HEADER_LENGTH) || (image_length > IO_CONFIG_MAX_IMAGE_SIZE))
  {
    return IO_CONFIG_STATUS_SIZE;
  }

  memset(&io_config_download, 0, sizeof(io_config_download));
  memset(io_config_staging_image, 0xFF, sizeof(io_config_staging_image));
  io_config_download.active = 1U;
  io_config_download.session = session;
  io_config_download.expected_length = image_length;
  io_config_download.expected_crc32 = image_crc32;
  io_config_running = 1U;
  return IO_CONFIG_STATUS_OK;
}

uint8_t IO_CONFIG_WriteChunk(uint16_t session, uint32_t offset, const uint8_t *data, uint8_t length)
{
  if ((io_config_download.active == 0U) || (session != io_config_download.session))
  {
    return IO_CONFIG_STATUS_NO_SESSION;
  }
  if ((data == 0) || (length == 0U) || (length > IO_CONFIG_MAX_DATA_CHUNK))
  {
    return IO_CONFIG_STATUS_LENGTH;
  }
  if ((offset != io_config_download.received_length) ||
      (offset + length > io_config_download.expected_length))
  {
    return IO_CONFIG_STATUS_OFFSET;
  }

  memcpy(&io_config_staging_image[offset], data, length);
  io_config_download.received_length += length;
  return IO_CONFIG_STATUS_OK;
}

uint8_t IO_CONFIG_Verify(uint16_t session, uint32_t *received_length, uint32_t *calculated_crc32)
{
  uint32_t image_crc;

  if ((io_config_download.active == 0U) || (session != io_config_download.session))
  {
    return IO_CONFIG_STATUS_NO_SESSION;
  }

  image_crc = IO_CONFIG_Crc32(io_config_staging_image, io_config_download.received_length);
  if (received_length != 0)
  {
    *received_length = io_config_download.received_length;
  }
  if (calculated_crc32 != 0)
  {
    *calculated_crc32 = image_crc;
  }
  if ((io_config_download.received_length != io_config_download.expected_length) ||
      (image_crc != io_config_download.expected_crc32))
  {
    return IO_CONFIG_STATUS_CRC;
  }
  if (io_config_validate_image(io_config_staging_image, io_config_download.received_length) == 0U)
  {
    return IO_CONFIG_STATUS_RULE;
  }

  io_config_download.verified = 1U;
  return IO_CONFIG_STATUS_OK;
}

uint8_t IO_CONFIG_Activate(uint16_t session, uint32_t *generation, uint8_t *active_slot)
{
  uint8_t new_slot = (io_config_active_slot == 1U) ? 2U : 1U;
  uint32_t address = (new_slot == 1U) ? IO_CONFIG_SLOT_A_ADDRESS : IO_CONFIG_SLOT_B_ADDRESS;
  uint32_t sector = (new_slot == 1U) ? FLASH_SECTOR_6 : FLASH_SECTOR_7;
  uint32_t marker_offset = io_config_marker_offset(io_config_download.expected_length);
  static uint8_t marker[32] __ALIGNED(32);

  if ((io_config_download.active == 0U) || (session != io_config_download.session))
  {
    return IO_CONFIG_STATUS_NO_SESSION;
  }
  if (io_config_download.verified == 0U)
  {
    return IO_CONFIG_STATUS_CRC;
  }

  if (HAL_FLASH_Unlock() != HAL_OK)
  {
    return IO_CONFIG_STATUS_FLASH;
  }
  if (io_config_erase_sector(sector) == 0U)
  {
    HAL_FLASH_Lock();
    return IO_CONFIG_STATUS_FLASH;
  }
  if (io_config_write_flash(address, io_config_staging_image, io_config_download.expected_length) == 0U)
  {
    HAL_FLASH_Lock();
    return IO_CONFIG_STATUS_FLASH;
  }

  memset(marker, 0xFF, sizeof(marker));
  io_config_write_u32(marker, IO_CONFIG_VALID_MARKER);
  if (io_config_write_flash(address + marker_offset, marker, sizeof(marker)) == 0U)
  {
    HAL_FLASH_Lock();
    return IO_CONFIG_STATUS_FLASH;
  }
  HAL_FLASH_Lock();

  /* 回读新槽并完整校验，失败时保持原槽运行。 */
  if (io_config_slot_valid(address, new_slot) == 0U)
  {
    return IO_CONFIG_STATUS_READBACK;
  }
  if (generation != 0)
  {
    *generation = io_config_read_u32(&io_config_active_image[IO_CONFIG_OFF_GENERATION]);
  }
  if (active_slot != 0)
  {
    *active_slot = new_slot;
  }
  memset(&io_config_download, 0, sizeof(io_config_download));
  io_config_running = 1U;
  return IO_CONFIG_STATUS_OK;
}

uint8_t IO_CONFIG_Abort(uint16_t session)
{
  if ((io_config_download.active == 0U) || (session != io_config_download.session))
  {
    return IO_CONFIG_STATUS_NO_SESSION;
  }
  memset(&io_config_download, 0, sizeof(io_config_download));
  return IO_CONFIG_STATUS_OK;
}

uint8_t IO_CONFIG_Clear(void)
{
  uint8_t result = IO_CONFIG_STATUS_OK;

  if (HAL_FLASH_Unlock() != HAL_OK)
  {
    return IO_CONFIG_STATUS_FLASH;
  }
  if ((io_config_erase_sector(FLASH_SECTOR_6) == 0U) ||
      (io_config_erase_sector(FLASH_SECTOR_7) == 0U))
  {
    result = IO_CONFIG_STATUS_FLASH;
  }
  HAL_FLASH_Lock();

  if (result == IO_CONFIG_STATUS_OK)
  {
    memset(&io_config_download, 0, sizeof(io_config_download));
    io_config_load_safe_default();
    io_config_last_error = 0U;
  }
  return result;
}
