#include "modbus_slave.h"

#include "sys.h"
#include "script_flash.h"
#include "script_runner.h"

#include <string.h>

/* Modbus RTU frame gap in ms used to delimit frames.
 * Theoretical t3.5 at 115200 is ~0.33 ms, which is shorter than one
 * FreeRTOS tick; 3 ms is conservative and works with the host's
 * request/response pattern. */
#define MODBUS_FRAME_GAP_MS     3U

/* Max RX frame bytes held before forced processing.
 * Must fit a full fc 0x10 request (9 + 246 data = 255 bytes). */
#define MODBUS_RX_BUF_SIZE      256U

/* RX interrupt ring size (power of two). */
#define MODBUS_RX_RING_SIZE     512U
#define MODBUS_RX_RING_MASK     (MODBUS_RX_RING_SIZE - 1U)

/* Function codes. */
#define MODBUS_FC_READ_HOLDING  0x03U
#define MODBUS_FC_WRITE_SINGLE  0x06U
#define MODBUS_FC_WRITE_MULTI   0x10U

/* Exception codes. */
#define MODBUS_EXC_ILLEGAL_FUNCTION  0x01U
#define MODBUS_EXC_ILLEGAL_ADDRESS   0x02U
#define MODBUS_EXC_ILLEGAL_VALUE     0x03U

#define MODBUS_MIN_FRAME_LEN    8U

/* Staged script text area (bytes), matching SCRIPT_MAX_TEXT. */
#define MODBUS_SCRIPT_STAGE_SIZE  SCRIPT_MAX_TEXT

typedef struct
{
  volatile uint16_t head;
  volatile uint16_t tail;
  uint8_t buffer[MODBUS_RX_RING_SIZE];
} MODBUS_SLAVE_Ring;

static MODBUS_SLAVE_Ring rx_ring;
static uint8_t rx_frame[MODBUS_RX_BUF_SIZE];
static uint16_t rx_frame_len;
static volatile uint32_t last_rx_tick;
static uint8_t rx_overflow;

/* Staged script text received via fc 0x10 / 0x06 before "commit". */
static uint8_t script_stage[MODBUS_SCRIPT_STAGE_SIZE];
static uint16_t script_stage_len;

static MODBUS_SLAVE_Diagnostics diag;

/* ------------------------------------------------------------------ */
/* CRC16 / Modbus (poly 0xA001, init 0xFFFF), low byte transmitted first. */
static uint16_t modbus_crc16(const uint8_t *data, uint16_t length)
{
  uint16_t crc = 0xFFFFU;

  while (length-- > 0U)
  {
    crc ^= (uint16_t)*data++;
    for (uint8_t bit = 0U; bit < 8U; bit++)
    {
      if ((crc & 0x0001U) != 0U)
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

static void modbus_write_u16(uint8_t *buffer, uint16_t value)
{
  buffer[0] = (uint8_t)(value >> 8);
  buffer[1] = (uint8_t)(value & 0xFFU);
}

static void modbus_append_crc(uint8_t *frame, uint16_t length)
{
  uint16_t crc = modbus_crc16(frame, (uint16_t)(length - 2U));

  frame[length - 2U] = (uint8_t)(crc & 0xFFU);
  frame[length - 1U] = (uint8_t)(crc >> 8);
}

/* ------------------------------------------------------------------ */
/* RS485 half-duplex transmit: raise DIR, send, wait TC, drop DIR.     */
static void modbus_transmit(const uint8_t *data, uint16_t length)
{
  HAL_GPIO_WritePin(MODBUS_SLAVE_DIR_GPIO_Port, MODBUS_SLAVE_DIR_Pin, MODBUS_SLAVE_DIR_TX);

  if (HAL_UART_Transmit(&huart1, (uint8_t *)data, length, 50U) == HAL_OK)
  {
    diag.tx_frames++;
  }

  HAL_GPIO_WritePin(MODBUS_SLAVE_DIR_GPIO_Port, MODBUS_SLAVE_DIR_Pin, MODBUS_SLAVE_DIR_RX);
}

/* ------------------------------------------------------------------ */
/* Register map implementation.                                        */
static int modbus_is_valid_address(uint16_t address)
{
  if (address <= MODBUS_REG_DIGITAL_OUT)
  {
    return 1;
  }

  if ((address >= MODBUS_REG_AD7606) && (address <= (MODBUS_REG_AD7606 + 7U)))
  {
    return 1;
  }

  if ((address >= MODBUS_REG_IMU) && (address <= (MODBUS_REG_IMU + 5U)))
  {
    return 1;
  }

  if ((address >= MODBUS_REG_ANGLE) && (address <= (MODBUS_REG_ANGLE + 2U)))
  {
    return 1;
  }

  /* Script engine status/control + staged text area. */
  if ((address >= MODBUS_REG_SCRIPT_STATUS) && (address <= MODBUS_REG_SCRIPT_LEN))
  {
    return 1;
  }

  if ((address >= MODBUS_REG_SCRIPT_DATA) && (address <= MODBUS_SCRIPT_DATA_END))
  {
    return 1;
  }

  return 0;
}

static int modbus_read_script_status(uint16_t *value)
{
  ScriptRunnerInfo info;
  SCRIPT_RUNNER_GetInfo(&info);
  *value = (uint16_t)info.status;
  return 1;
}

static int modbus_read_script_stage(uint16_t address, uint16_t *value)
{
  uint32_t offset = ((uint32_t)address - MODBUS_REG_SCRIPT_DATA) * 2U;

  if (offset + 2U > MODBUS_SCRIPT_STAGE_SIZE)
  {
    return 0;
  }

  /* Beyond the staged length reads back as zero (not yet written). */
  if ((offset + 2U) > script_stage_len)
  {
    *value = 0U;
  }
  else
  {
    *value = (uint16_t)(((uint16_t)script_stage[offset] << 8) |
                         (uint16_t)script_stage[offset + 1U]);
  }
  return 1;
}

static int modbus_read_register(uint16_t address, uint16_t *value)
{
  DataHubImuData imu;
  DataHubAd7606Data ad;
  int32_t centi;

  if (value == NULL)
  {
    return 0;
  }

  switch (address)
  {
    case MODBUS_REG_STATUS:
    {
      uint16_t status = 0U;

      DataHub_GetImu(&imu);
      DataHub_GetAd7606(&ad);

      if (imu.imu_ready != 0)
      {
        status |= MODBUS_STATUS_BIT_IMU_READY;
      }

      if (ad.ad7606_ready != 0)
      {
        status |= MODBUS_STATUS_BIT_AD_READY;
      }

      if (imu.last_read_rslt == 0)
      {
        status |= MODBUS_STATUS_BIT_IMU_READ_OK;
      }

      if (ad.last_read_ok != 0)
      {
        status |= MODBUS_STATUS_BIT_AD_READ_OK;
      }

      *value = status;
      return 1;
    }

    case MODBUS_REG_DIGITAL_IN:
      *value = (uint16_t)(BSP_DIGITAL_IO_ReadInputMask() & 0x00FFU);
      return 1;

    case MODBUS_REG_DIGITAL_OUT:
      *value = (uint16_t)(BSP_DIGITAL_IO_GetOutputMask() & 0x00FFU);
      return 1;

    default:
      break;
  }

  if ((address >= MODBUS_REG_AD7606) && (address <= (MODBUS_REG_AD7606 + 7U)))
  {
    DataHub_GetAd7606(&ad);
    *value = (uint16_t)ad.raw[address - MODBUS_REG_AD7606];
    return 1;
  }

  if ((address >= MODBUS_REG_IMU) && (address <= (MODBUS_REG_IMU + 5U)))
  {
    DataHub_GetImu(&imu);
    switch (address - MODBUS_REG_IMU)
    {
      case 0U: *value = (uint16_t)imu.accel_raw_x; return 1;
      case 1U: *value = (uint16_t)imu.accel_raw_y; return 1;
      case 2U: *value = (uint16_t)imu.accel_raw_z; return 1;
      case 3U: *value = (uint16_t)imu.gyro_raw_x;  return 1;
      case 4U: *value = (uint16_t)imu.gyro_raw_y;  return 1;
      default: *value = (uint16_t)imu.gyro_raw_z;  return 1;
    }
  }

  if ((address >= MODBUS_REG_ANGLE) && (address <= (MODBUS_REG_ANGLE + 2U)))
  {
    DataHub_GetImu(&imu);

    switch (address - MODBUS_REG_ANGLE)
    {
      case 0U: centi = (int32_t)(imu.euler.roll  * 100.0f); break;
      case 1U: centi = (int32_t)(imu.euler.pitch * 100.0f); break;
      default: centi = (int32_t)(imu.euler.yaw   * 100.0f); break;
    }

    *value = (uint16_t)((int16_t)centi);
    return 1;
  }

  switch (address)
  {
    case MODBUS_REG_SCRIPT_STATUS:
      return modbus_read_script_status(value);

    case MODBUS_REG_SCRIPT_LINE:
    {
      ScriptRunnerInfo info;
      SCRIPT_RUNNER_GetInfo(&info);
      *value = info.line;
      return 1;
    }

    case MODBUS_REG_SCRIPT_STEPS:
    {
      ScriptRunnerInfo info;
      SCRIPT_RUNNER_GetInfo(&info);
      *value = (uint16_t)(info.steps & 0xFFFFU);
      return 1;
    }

    case MODBUS_REG_SCRIPT_LEN:
    {
      int len = SCRIPT_FLASH_GetLength();
      *value = (len > 0) ? (uint16_t)len : 0U;
      return 1;
    }

    default:
      break;
  }

  if ((address >= MODBUS_REG_SCRIPT_DATA) && (address <= MODBUS_SCRIPT_DATA_END))
  {
    return modbus_read_script_stage(address, value);
  }

  return 0;
}

/* Public output-bitmap write shared by fc 0x06 and the script runner.
 * Only the low 8 bits change; bit8/9 (DO_I / DO_U) are preserved. */
void MODBUS_SLAVE_WriteOutputBits(uint16_t bits)
{
  uint16_t current = BSP_DIGITAL_IO_GetOutputMask();
  uint16_t next = (uint16_t)((current & 0xFF00U) | (bits & 0x00FFU));
  BSP_DIGITAL_IO_SetOutputMask(next);
}

/* Write single holding register.
 * Return: 1 = success, 0 = illegal address (exc 02), -1 = illegal value (exc 03). */
static int modbus_write_register(uint16_t address, uint16_t value)
{
  if (address == MODBUS_REG_DIGITAL_OUT)
  {
    MODBUS_SLAVE_WriteOutputBits(value);
    return 1;
  }

  if (address == MODBUS_REG_SCRIPT_CTRL)
  {
    switch (value)
    {
      case MODBUS_SCRIPT_CTRL_ERASE:
        script_stage_len = 0U;
        return (SCRIPT_FLASH_Erase() == 0) ? 1 : -1;

      case MODBUS_SCRIPT_CTRL_COMMIT:
      {
        int ret;
        if (script_stage_len == 0U)
        {
          return -1;   /* nothing staged */
        }
        ret = SCRIPT_FLASH_EraseAndWrite((const char *)script_stage, script_stage_len);
        return (ret == 0) ? 1 : -1;
      }

      case MODBUS_SCRIPT_CTRL_START:
        SCRIPT_RUNNER_Start();
        return 1;

      case MODBUS_SCRIPT_CTRL_STOP:
        SCRIPT_RUNNER_Stop();
        return 1;

      default:
        return -1;
    }
  }

  if ((address >= MODBUS_REG_SCRIPT_DATA) && (address <= MODBUS_SCRIPT_DATA_END))
  {
    /* fc 0x06 single-register write into the staged text area. */
    uint32_t offset = ((uint32_t)address - MODBUS_REG_SCRIPT_DATA) * 2U;

    if (offset + 2U > MODBUS_SCRIPT_STAGE_SIZE)
    {
      return -1;
    }

    script_stage[offset] = (uint8_t)(value >> 8);
    script_stage[offset + 1U] = (uint8_t)(value & 0xFFU);

    if ((offset + 2U) > script_stage_len)
    {
      script_stage_len = (uint16_t)(offset + 2U);
    }
    return 1;
  }

  return 0;
}

/* ------------------------------------------------------------------ */
/* Request handling. Returns response length, or 0 on exception.       */
static uint16_t modbus_build_response(const uint8_t *req, uint16_t req_len,
                                      uint8_t *resp, uint16_t resp_size)
{
  uint8_t function = req[1];

  if (function == MODBUS_FC_READ_HOLDING)
  {
    uint16_t start;
    uint16_t quantity;
    uint16_t data_len;
    uint8_t *cursor;

    if (req_len < MODBUS_MIN_FRAME_LEN)
    {
      return 0U;
    }

    start = (uint16_t)(((uint16_t)req[2] << 8) | req[3]);
    quantity = (uint16_t)(((uint16_t)req[4] << 8) | req[5]);

    if ((quantity == 0U) || (quantity > 125U))
    {
      diag.exceptions_sent++;
      resp[0] = req[0];
      resp[1] = (uint8_t)(MODBUS_FC_READ_HOLDING | 0x80U);
      resp[2] = MODBUS_EXC_ILLEGAL_VALUE;
      modbus_append_crc(resp, 5U);
      return 5U;
    }

    for (uint16_t index = 0U; index < quantity; index++)
    {
      if (!modbus_is_valid_address((uint16_t)(start + index)))
      {
        diag.exceptions_sent++;
        resp[0] = req[0];
        resp[1] = (uint8_t)(MODBUS_FC_READ_HOLDING | 0x80U);
        resp[2] = MODBUS_EXC_ILLEGAL_ADDRESS;
        modbus_append_crc(resp, 5U);
        return 5U;
      }
    }

    data_len = (uint16_t)(quantity * 2U);
    if ((5U + data_len + 2U) > resp_size)
    {
      return 0U;
    }

    resp[0] = req[0];
    resp[1] = MODBUS_FC_READ_HOLDING;
    resp[2] = (uint8_t)data_len;
    cursor = &resp[3];

    for (uint16_t index = 0U; index < quantity; index++)
    {
      uint16_t value = 0U;

      (void)modbus_read_register((uint16_t)(start + index), &value);
      modbus_write_u16(cursor, value);
      cursor += 2U;
    }

    modbus_append_crc(resp, (uint16_t)(3U + data_len + 2U));
    return (uint16_t)(3U + data_len + 2U);
  }

  if (function == MODBUS_FC_WRITE_SINGLE)
  {
    uint16_t address;
    uint16_t value;
    int write_result;

    if (req_len < MODBUS_MIN_FRAME_LEN)
    {
      return 0U;
    }

    address = (uint16_t)(((uint16_t)req[2] << 8) | req[3]);
    value = (uint16_t)(((uint16_t)req[4] << 8) | req[5]);

    write_result = modbus_write_register(address, value);
    if (write_result == 0)
    {
      diag.exceptions_sent++;
      resp[0] = req[0];
      resp[1] = (uint8_t)(MODBUS_FC_WRITE_SINGLE | 0x80U);
      resp[2] = MODBUS_EXC_ILLEGAL_ADDRESS;
      modbus_append_crc(resp, 5U);
      return 5U;
    }

    if (write_result < 0)
    {
      diag.exceptions_sent++;
      resp[0] = req[0];
      resp[1] = (uint8_t)(MODBUS_FC_WRITE_SINGLE | 0x80U);
      resp[2] = MODBUS_EXC_ILLEGAL_VALUE;
      modbus_append_crc(resp, 5U);
      return 5U;
    }

    if (req_len > resp_size)
    {
      return 0U;
    }

    memcpy(resp, req, req_len);
    return req_len;
  }

  if (function == MODBUS_FC_WRITE_MULTI)
  {
    uint16_t start;
    uint16_t quantity;
    uint16_t byte_count;
    uint16_t data_off;

    if (req_len < 9U)
    {
      return 0U;
    }

    start = (uint16_t)(((uint16_t)req[2] << 8) | req[3]);
    quantity = (uint16_t)(((uint16_t)req[4] << 8) | req[5]);
    byte_count = req[6];
    data_off = 7U;

    /* Sanity checks (Modbus spec: 1..123 registers). */
    if ((quantity == 0U) || (quantity > 123U) ||
        (byte_count != (uint16_t)(quantity * 2U)))
    {
      diag.exceptions_sent++;
      resp[0] = req[0];
      resp[1] = (uint8_t)(MODBUS_FC_WRITE_MULTI | 0x80U);
      resp[2] = MODBUS_EXC_ILLEGAL_VALUE;
      modbus_append_crc(resp, 5U);
      return 5U;
    }

    /* Only the staged script text area accepts fc 0x10. */
    if ((start < MODBUS_REG_SCRIPT_DATA) ||
        (start + quantity - 1U > MODBUS_SCRIPT_DATA_END))
    {
      diag.exceptions_sent++;
      resp[0] = req[0];
      resp[1] = (uint8_t)(MODBUS_FC_WRITE_MULTI | 0x80U);
      resp[2] = MODBUS_EXC_ILLEGAL_ADDRESS;
      modbus_append_crc(resp, 5U);
      return 5U;
    }

    if ((uint16_t)(data_off + byte_count + 2U) > req_len)
    {
      return 0U;   /* frame truncated */
    }

    {
      uint32_t offset = ((uint32_t)start - MODBUS_REG_SCRIPT_DATA) * 2U;
      uint32_t bytes = (uint32_t)quantity * 2U;
      uint16_t index;

      if ((offset + bytes) > MODBUS_SCRIPT_STAGE_SIZE)
      {
        diag.exceptions_sent++;
        resp[0] = req[0];
        resp[1] = (uint8_t)(MODBUS_FC_WRITE_MULTI | 0x80U);
        resp[2] = MODBUS_EXC_ILLEGAL_ADDRESS;
        modbus_append_crc(resp, 5U);
        return 5U;
      }

      for (index = 0U; index < quantity; index++)
      {
        script_stage[offset + (uint32_t)index * 2U] =
            (uint8_t)(req[data_off + (uint16_t)(index * 2U)]);
        script_stage[offset + (uint32_t)index * 2U + 1U] =
            (uint8_t)(req[data_off + (uint16_t)(index * 2U) + 1U]);
      }

      if ((offset + bytes) > script_stage_len)
      {
        script_stage_len = (uint16_t)(offset + bytes);
      }
    }

    /* Response: addr, fc, start, quantity, crc (8 bytes). */
    resp[0] = req[0];
    resp[1] = MODBUS_FC_WRITE_MULTI;
    resp[2] = req[2];
    resp[3] = req[3];
    resp[4] = req[4];
    resp[5] = req[5];
    modbus_append_crc(resp, 8U);
    return 8U;
  }

  /* Unknown function code. */
  diag.exceptions_sent++;
  resp[0] = req[0];
  resp[1] = (uint8_t)(function | 0x80U);
  resp[2] = MODBUS_EXC_ILLEGAL_FUNCTION;
  modbus_append_crc(resp, 5U);
  return 5U;
}

/* ------------------------------------------------------------------ */
/* Public API.                                                        */
void MODBUS_SLAVE_Init(void)
{
  GPIO_InitTypeDef gpio = {0};

  rx_ring.head = 0U;
  rx_ring.tail = 0U;
  rx_frame_len = 0U;
  last_rx_tick = 0U;
  rx_overflow = 0U;
  script_stage_len = 0U;

  memset(&diag, 0, sizeof(diag));

  /* Configure the RS485 DIR pin as push-pull output (active level as
   * defined by MODBUS_SLAVE_DIR_* macros). */
  __HAL_RCC_GPIOA_CLK_ENABLE();
  HAL_GPIO_WritePin(MODBUS_SLAVE_DIR_GPIO_Port, MODBUS_SLAVE_DIR_Pin, MODBUS_SLAVE_DIR_RX);

  gpio.Pin = MODBUS_SLAVE_DIR_Pin;
  gpio.Mode = GPIO_MODE_OUTPUT_PP;
  gpio.Pull = GPIO_NOPULL;
  gpio.Speed = GPIO_SPEED_FREQ_LOW;
  HAL_GPIO_Init(MODBUS_SLAVE_DIR_GPIO_Port, &gpio);

  /* Enable USART1 RX interrupt and NVIC line. */
  HAL_NVIC_SetPriority(USART1_IRQn, 5, 0);
  HAL_NVIC_EnableIRQ(USART1_IRQn);
  __HAL_UART_ENABLE_IT(&huart1, UART_IT_RXNE);
}

void MODBUS_SLAVE_OnByteReceived(uint8_t byte)
{
  uint16_t next_tail = (uint16_t)((rx_ring.tail + 1U) & MODBUS_RX_RING_MASK);

  if (next_tail == rx_ring.head)
  {
    rx_overflow = 1U;
    return;
  }

  rx_ring.buffer[rx_ring.tail] = byte;
  rx_ring.tail = next_tail;
  last_rx_tick = HAL_GetTick();
  diag.rx_bytes++;
}

void MODBUS_SLAVE_Pump(void)
{
  while ((rx_ring.head != rx_ring.tail) && (rx_frame_len < MODBUS_RX_BUF_SIZE))
  {
    rx_frame[rx_frame_len++] = rx_ring.buffer[rx_ring.head];
    rx_ring.head = (uint16_t)((rx_ring.head + 1U) & MODBUS_RX_RING_MASK);
  }
}

void MODBUS_SLAVE_Process(void)
{
  uint8_t response[MODBUS_RX_BUF_SIZE];
  uint16_t response_len;

  if (rx_frame_len == 0U)
  {
    return;
  }

  /* Wait for the bus to go quiet before treating the buffer as a frame. */
  if ((HAL_GetTick() - last_rx_tick) < MODBUS_FRAME_GAP_MS)
  {
    return;
  }

  /* Try to consume one frame; slide one byte on CRC/address failure. */
  for (uint8_t attempt = 0U; (attempt < 8U) && (rx_frame_len >= MODBUS_MIN_FRAME_LEN); attempt++)
  {
    uint16_t crc;
    uint16_t expected_crc;
    uint16_t frame_len;

    if (rx_frame[0] != (uint8_t)MODBUS_SLAVE_ADDR)
    {
      diag.addr_errors++;
      memmove(rx_frame, &rx_frame[1], (size_t)(rx_frame_len - 1U));
      rx_frame_len--;
      continue;
    }

    /* Frame length depends on the function code:
     *   03 / 06 -> fixed 8 bytes
     *   10      -> 7 header bytes + byte_count + 2 crc
     */
    switch (rx_frame[1])
    {
      case MODBUS_FC_WRITE_MULTI:
        if (rx_frame_len < 9U)
        {
          break;
        }
        frame_len = (uint16_t)(9U + rx_frame[6]);
        break;
      case MODBUS_FC_READ_HOLDING:
      case MODBUS_FC_WRITE_SINGLE:
        frame_len = MODBUS_MIN_FRAME_LEN;
        break;
      default:
        frame_len = MODBUS_MIN_FRAME_LEN;   /* will fail as illegal function */
        break;
    }

    if (rx_frame_len < frame_len)
    {
      break;
    }

    expected_crc = (uint16_t)((uint16_t)rx_frame[frame_len - 2U] |
                              ((uint16_t)rx_frame[frame_len - 1U] << 8));
    crc = modbus_crc16(rx_frame, (uint16_t)(frame_len - 2U));

    if (crc != expected_crc)
    {
      diag.crc_errors++;
      memmove(rx_frame, &rx_frame[1], (size_t)(rx_frame_len - 1U));
      rx_frame_len--;
      continue;
    }

    response_len = modbus_build_response(rx_frame, frame_len, response,
                                         (uint16_t)sizeof(response));
    if (response_len > 0U)
    {
      modbus_transmit(response, response_len);
      diag.rx_frames++;
    }

    memmove(rx_frame, &rx_frame[frame_len], (size_t)(rx_frame_len - frame_len));
    rx_frame_len = (uint16_t)(rx_frame_len - frame_len);
    break;
  }

  /* Drop leftover garbage so the next frame starts clean. */
  if (rx_frame_len > 0U)
  {
    rx_frame_len = 0U;
  }
}

void MODBUS_SLAVE_GetDiagnostics(MODBUS_SLAVE_Diagnostics *diagnostics)
{
  if (diagnostics == NULL)
  {
    return;
  }

  *diagnostics = diag;
}
