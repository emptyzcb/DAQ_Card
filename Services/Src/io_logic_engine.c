/**
 * @file    io_logic_engine.c
 * @brief   IO 逻辑字节码虚拟机（联合控制器运行时）
 *
 * 上位机将用户编写的 DSL 代码（16 个模式弱定义回调 + 主程序 while）编译为
 * v2 字节码镜像并下载到 Flash。本引擎按 10ms 时基逐条解释执行：
 *   - 主程序：从镜像 body 偏移 0 开始执行，遇到 OP_END 结束；
 *     包含 while 的程序通过条件跳转形成循环，永不触碰 OP_END；
 *   - 16 个模式回调：入口表 + 子程序调用栈，OP_RET 返回；
 *   - 切模式：先关闭全部输出，再切换到目标模式（规格要求）；
 *   - 鉴频：按程序内设置的阈值 + 迟滞状态机计算档位，供条件测试指令使用。
 *
 * 工业安全约束：
 *   - 每条指令执行前做边界检查，非法程序立即安全停止并关闭全部输出；
 *   - 每 tick 指令预算限制，防止恶意/异常循环独占任务；
 *   - 调用栈深度受限，超限按故障处理；
 *   - IO_Delay 在到期前暂停本 tick 后续指令，保证时序可预期。
 */

#include "io_logic_engine.h"

#include <string.h>

#include "bsp_analog_output.h"
#include "digital_io_service.h"
#include "io_config_storage.h"

/* ---------------- 字节码操作码定义（与上位机 IoLogicCompiler 一一对应） ---------------- */
#define OP_NOP            0x00U
#define OP_JMP            0x01U
#define OP_TEST_DI        0x10U
#define OP_TEST_DI_RISING 0x11U
#define OP_TEST_DI_FALLING 0x12U
#define OP_TEST_FREQ_CLASS 0x13U
#define OP_TEST_FREQ_GT   0x14U
#define OP_TEST_FREQ_LT   0x15U
#define OP_TEST_EXT_MODE_EQ 0x16U
#define OP_TEST_CUR_MODE_EQ 0x17U
#define OP_TEST_EXT_NEQ_CUR 0x18U
#define OP_DO_SET         0x20U
#define OP_DO_PULSE       0x21U
#define OP_AO_SET_VOLTAGE 0x22U
#define OP_AO_SET_CURRENT 0x23U
#define OP_AO_BREATH      0x24U
#define OP_DELAY          0x30U
#define OP_SET_MODE       0x31U
#define OP_RUN_CURRENT_MODE 0x32U
#define OP_MODE_CALL      0x33U
#define OP_RET            0x34U
#define OP_SET_MODE_EXT   0x35U /* 直接切换到外部编码模式（IO_SetMode(IO_GetExternalMode())） */
#define OP_SET_FREQ_TH    0x40U
#define OP_END            0xFFU

/* 鉴频阈值配置（由程序 OP_SET_FREQ_TH 指令写入） */
typedef struct
{
  uint16_t low_hz;
  uint16_t high_hz;
  uint8_t  hyst_pct;
  uint16_t timeout_ms;
  uint8_t  configured;
} IO_LOGIC_FreqThreshold;

/* 虚拟机完整状态 */
typedef struct
{
  const uint8_t *image;         /* 活动镜像指针 */
  uint32_t image_length;        /* 活动镜像总长 */
  const uint8_t *body;          /* 字节码主体指针 */
  uint32_t body_length;         /* 字节码主体长度 */
  uint32_t generation;          /* 已加载镜像代次 */
  uint16_t mode_entry[IO_LOGIC_MODE_COUNT]; /* 16 模式入口偏移 */
  uint16_t pc;                  /* 程序计数器 */
  uint16_t return_stack[IO_LOGIC_CALL_DEPTH];
  uint8_t  return_depth;
  uint8_t  current_mode;        /* 当前运行模式 */
  uint8_t  run_state;
  uint32_t delay_until_ms;      /* IO_Delay 到期时刻 */
  uint32_t step_budget;         /* 本 tick 指令预算 */
  uint16_t previous_input;
  uint16_t output_mask;
  uint32_t pulse_until[8];      /* 脉冲关断时刻 */
  IO_LOGIC_FreqThreshold freq_th[2];
  uint8_t  freq_class[2];
  uint16_t status_flags;
  uint16_t ao_voltage_mv;
  uint16_t ao_current_ua;
} IO_LOGIC_VM_State;

static IO_LOGIC_VM_State io_vm;

static uint16_t io_logic_read_u16(const uint8_t *data)
{
  return (uint16_t)(((uint16_t)data[0] << 8) | data[1]);
}

/* ---------------- 镜像加载 ---------------- */

/*
 * 从活动镜像装载字节码主体与 16 模式入口表。
 * 镜像总长不足 BODY_OFFSET 时判定非法，返回 0。
 */
static uint8_t io_logic_load_image(void)
{
  uint32_t image_length;
  const uint8_t *image;

  image = IO_CONFIG_GetActiveImage(&image_length);
  if ((image == 0) || (image_length < (uint32_t)IO_LOGIC_BODY_OFFSET))
  {
    return 0U;
  }

  io_vm.image = image;
  io_vm.image_length = image_length;
  io_vm.body = image + IO_LOGIC_BODY_OFFSET;
  io_vm.body_length = image_length - (uint32_t)IO_LOGIC_BODY_OFFSET;

  for (uint8_t index = 0U; index < IO_LOGIC_MODE_COUNT; index++)
  {
    io_vm.mode_entry[index] = io_logic_read_u16(&image[IO_LOGIC_IMAGE_HEADER_LENGTH + (index * 2U)]);
  }

  return 1U;
}

/* ---------------- 鉴频档位状态机（迟滞） ---------------- */

/*
 * 按程序阈值与当前脉冲频率更新指定通道档位。
 * 未配置阈值或阈值非法时档位恒为 NONE；超时无脉冲判定 NONE。
 * 迟滞区间防止频率在阈值附近抖动引起档位反复跳变。
 */
static void io_logic_update_freq_class(uint8_t channel, uint32_t now_ms)
{
  IO_LOGIC_FreqThreshold *threshold;
  uint32_t age_ms;
  uint32_t freq;
  uint32_t low;
  uint32_t high;
  uint32_t hysteresis;

  if (channel >= 2U)
  {
    return;
  }

  threshold = &io_vm.freq_th[channel];
  if ((threshold->configured == 0U) || (threshold->low_hz >= threshold->high_hz))
  {
    io_vm.freq_class[channel] = DIGITAL_IO_SERVICE_FREQ_CLASS_NONE;
    return;
  }

  age_ms = DIGITAL_IO_SERVICE_GetLastPulseAgeMs(channel, now_ms);
  if ((threshold->timeout_ms > 0U) && (age_ms > threshold->timeout_ms))
  {
    io_vm.freq_class[channel] = DIGITAL_IO_SERVICE_FREQ_CLASS_NONE;
    return;
  }

  freq = DIGITAL_IO_SERVICE_GetFrequencyHz(channel);
  low = threshold->low_hz;
  high = threshold->high_hz;
  hysteresis = threshold->hyst_pct;

  switch (io_vm.freq_class[channel])
  {
    case DIGITAL_IO_SERVICE_FREQ_CLASS_NONE:
      /* 从无信号恢复：按当前频率直接归位 */
      io_vm.freq_class[channel] = (freq >= low) ? DIGITAL_IO_SERVICE_FREQ_CLASS_NORMAL
                                                : DIGITAL_IO_SERVICE_FREQ_CLASS_LOW;
      break;
    case DIGITAL_IO_SERVICE_FREQ_CLASS_LOW:
      if (freq >= (low * (100U + hysteresis)) / 100U)
      {
        io_vm.freq_class[channel] = DIGITAL_IO_SERVICE_FREQ_CLASS_NORMAL;
      }
      break;
    case DIGITAL_IO_SERVICE_FREQ_CLASS_NORMAL:
      if (freq < low)
      {
        io_vm.freq_class[channel] = DIGITAL_IO_SERVICE_FREQ_CLASS_LOW;
      }
      else if (freq >= (high * (100U + hysteresis)) / 100U)
      {
        io_vm.freq_class[channel] = DIGITAL_IO_SERVICE_FREQ_CLASS_HIGH;
      }
      break;
    case DIGITAL_IO_SERVICE_FREQ_CLASS_HIGH:
      if (freq < high)
      {
        io_vm.freq_class[channel] = DIGITAL_IO_SERVICE_FREQ_CLASS_NORMAL;
      }
      else if (freq >= IO_LOGIC_SPEC_MAX_FREQ_HZ)
      {
        io_vm.freq_class[channel] = DIGITAL_IO_SERVICE_FREQ_CLASS_OVER;
      }
      break;
    case DIGITAL_IO_SERVICE_FREQ_CLASS_OVER:
      if (freq < (IO_LOGIC_SPEC_MAX_FREQ_HZ * (100U - hysteresis)) / 100U)
      {
        io_vm.freq_class[channel] = DIGITAL_IO_SERVICE_FREQ_CLASS_HIGH;
      }
      break;
    default:
      io_vm.freq_class[channel] = DIGITAL_IO_SERVICE_FREQ_CLASS_NONE;
      break;
  }
}

/* ---------------- 程序安全停止 ---------------- */

/* 虚拟机故障：安全停止并关闭全部输出，状态标志位留存供诊断。 */
static void io_logic_fault_stop(void)
{
  io_vm.run_state = IO_LOGIC_RUN_STATE_STOPPED;
  io_vm.status_flags |= IO_LOGIC_STATUS_VM_FAULT;
  io_vm.output_mask = 0U;
  memset(io_vm.pulse_until, 0, sizeof(io_vm.pulse_until));
  DIGITAL_IO_SERVICE_AllOutputsOff();
}

/* ---------------- 指令执行 ---------------- */

static uint8_t io_logic_read_di_level(uint8_t channel, uint16_t input_mask)
{
  if (channel >= 8U)
  {
    return 0U;
  }
  return ((input_mask & (uint16_t)(1U << channel)) != 0U) ? 1U : 0U;
}

/* 模式切换：规格要求先关闭全部输出，再启动目标模式。 */
static void io_logic_do_set_mode(uint8_t mode)
{
  if (mode >= IO_LOGIC_MODE_COUNT)
  {
    return;
  }
  if (mode == io_vm.current_mode)
  {
    return;
  }

  DIGITAL_IO_SERVICE_AllOutputsOff();
  io_vm.output_mask = 0U;
  memset(io_vm.pulse_until, 0, sizeof(io_vm.pulse_until));
  io_vm.current_mode = mode;
}

/* 调用模式子程序：入口为 0 表示弱定义空实现，直接返回。 */
static void io_logic_call_mode(uint8_t mode)
{
  uint16_t entry;

  if (mode >= IO_LOGIC_MODE_COUNT)
  {
    return;
  }
  entry = io_vm.mode_entry[mode];
  if (entry == 0U)
  {
    return;
  }
  if (io_vm.return_depth >= IO_LOGIC_CALL_DEPTH)
  {
    io_logic_fault_stop();
    return;
  }
  io_vm.return_stack[io_vm.return_depth] = io_vm.pc;
  io_vm.return_depth++;
  io_vm.pc = entry;
}

/* 取下一指令指针；越界视为非法程序并安全停止。 */
static uint16_t io_logic_fetch_next(void)
{
  uint16_t next = io_vm.pc;

  if (next >= io_vm.body_length)
  {
    io_logic_fault_stop();
    return 0xFFFFU;
  }
  return next;
}

static void io_logic_execute_one(uint16_t input_mask, uint16_t rising_mask,
                                 uint16_t falling_mask, uint32_t now_ms)
{
  uint8_t opcode;
  uint8_t channel;
  uint8_t operand8;
  uint16_t operand16;
  uint16_t jump_rel;
  uint16_t pc;

  pc = io_logic_fetch_next();
  if (pc == 0xFFFFU)
  {
    return;
  }
  opcode = io_vm.body[pc];
  io_vm.pc = (uint16_t)(pc + 1U);

  switch (opcode)
  {
    case OP_NOP:
      break;

    case OP_JMP:
      if ((io_vm.pc + 2U) > io_vm.body_length) { io_logic_fault_stop(); return; }
      jump_rel = io_logic_read_u16(&io_vm.body[io_vm.pc]);
      io_vm.pc = (uint16_t)(io_vm.pc + 2U + jump_rel);
      break;

    case OP_TEST_DI:
      if ((io_vm.pc + 4U) > io_vm.body_length) { io_logic_fault_stop(); return; }
      channel = io_vm.body[io_vm.pc];
      operand8 = io_vm.body[io_vm.pc + 1U];
      jump_rel = io_logic_read_u16(&io_vm.body[io_vm.pc + 2U]);
      io_vm.pc = (uint16_t)(io_vm.pc + 4U);
      if (io_logic_read_di_level(channel, input_mask) != operand8)
      {
        io_vm.pc = (uint16_t)(io_vm.pc + jump_rel);
      }
      break;

    case OP_TEST_DI_RISING:
      if ((io_vm.pc + 3U) > io_vm.body_length) { io_logic_fault_stop(); return; }
      channel = io_vm.body[io_vm.pc];
      jump_rel = io_logic_read_u16(&io_vm.body[io_vm.pc + 1U]);
      io_vm.pc = (uint16_t)(io_vm.pc + 3U);
      if ((channel >= 8U) || ((rising_mask & (uint16_t)(1U << channel)) == 0U))
      {
        io_vm.pc = (uint16_t)(io_vm.pc + jump_rel);
      }
      break;

    case OP_TEST_DI_FALLING:
      if ((io_vm.pc + 3U) > io_vm.body_length) { io_logic_fault_stop(); return; }
      channel = io_vm.body[io_vm.pc];
      jump_rel = io_logic_read_u16(&io_vm.body[io_vm.pc + 1U]);
      io_vm.pc = (uint16_t)(io_vm.pc + 3U);
      if ((channel >= 8U) || ((falling_mask & (uint16_t)(1U << channel)) == 0U))
      {
        io_vm.pc = (uint16_t)(io_vm.pc + jump_rel);
      }
      break;

    case OP_TEST_FREQ_CLASS:
      if ((io_vm.pc + 4U) > io_vm.body_length) { io_logic_fault_stop(); return; }
      channel = io_vm.body[io_vm.pc];
      operand8 = io_vm.body[io_vm.pc + 1U];
      jump_rel = io_logic_read_u16(&io_vm.body[io_vm.pc + 2U]);
      io_vm.pc = (uint16_t)(io_vm.pc + 4U);
      if (channel >= 2U)
      {
        io_logic_fault_stop();
        return;
      }
      if (io_vm.freq_class[channel] != operand8)
      {
        io_vm.pc = (uint16_t)(io_vm.pc + jump_rel);
      }
      break;

    case OP_TEST_FREQ_GT:
      if ((io_vm.pc + 5U) > io_vm.body_length) { io_logic_fault_stop(); return; }
      channel = io_vm.body[io_vm.pc];
      operand16 = io_logic_read_u16(&io_vm.body[io_vm.pc + 1U]);
      jump_rel = io_logic_read_u16(&io_vm.body[io_vm.pc + 3U]);
      io_vm.pc = (uint16_t)(io_vm.pc + 5U);
      if (channel >= 2U)
      {
        io_logic_fault_stop();
        return;
      }
      if (DIGITAL_IO_SERVICE_GetFrequencyHz(channel) <= operand16)
      {
        io_vm.pc = (uint16_t)(io_vm.pc + jump_rel);
      }
      break;

    case OP_TEST_FREQ_LT:
      if ((io_vm.pc + 5U) > io_vm.body_length) { io_logic_fault_stop(); return; }
      channel = io_vm.body[io_vm.pc];
      operand16 = io_logic_read_u16(&io_vm.body[io_vm.pc + 1U]);
      jump_rel = io_logic_read_u16(&io_vm.body[io_vm.pc + 3U]);
      io_vm.pc = (uint16_t)(io_vm.pc + 5U);
      if (channel >= 2U)
      {
        io_logic_fault_stop();
        return;
      }
      if (DIGITAL_IO_SERVICE_GetFrequencyHz(channel) >= operand16)
      {
        io_vm.pc = (uint16_t)(io_vm.pc + jump_rel);
      }
      break;

    case OP_TEST_EXT_MODE_EQ:
      if ((io_vm.pc + 3U) > io_vm.body_length) { io_logic_fault_stop(); return; }
      operand8 = io_vm.body[io_vm.pc];
      jump_rel = io_logic_read_u16(&io_vm.body[io_vm.pc + 1U]);
      io_vm.pc = (uint16_t)(io_vm.pc + 3U);
      if (DIGITAL_IO_SERVICE_GetExternalMode() != operand8)
      {
        io_vm.pc = (uint16_t)(io_vm.pc + jump_rel);
      }
      break;

    case OP_TEST_CUR_MODE_EQ:
      if ((io_vm.pc + 3U) > io_vm.body_length) { io_logic_fault_stop(); return; }
      operand8 = io_vm.body[io_vm.pc];
      jump_rel = io_logic_read_u16(&io_vm.body[io_vm.pc + 1U]);
      io_vm.pc = (uint16_t)(io_vm.pc + 3U);
      if (io_vm.current_mode != operand8)
      {
        io_vm.pc = (uint16_t)(io_vm.pc + jump_rel);
      }
      break;

    case OP_TEST_EXT_NEQ_CUR:
      if ((io_vm.pc + 2U) > io_vm.body_length) { io_logic_fault_stop(); return; }
      jump_rel = io_logic_read_u16(&io_vm.body[io_vm.pc]);
      io_vm.pc = (uint16_t)(io_vm.pc + 2U);
      if (DIGITAL_IO_SERVICE_GetExternalMode() == io_vm.current_mode)
      {
        io_vm.pc = (uint16_t)(io_vm.pc + jump_rel);
      }
      break;

    case OP_DO_SET:
      if ((io_vm.pc + 2U) > io_vm.body_length) { io_logic_fault_stop(); return; }
      channel = io_vm.body[io_vm.pc];
      operand8 = io_vm.body[io_vm.pc + 1U];
      io_vm.pc = (uint16_t)(io_vm.pc + 2U);
      if (channel >= 8U)
      {
        io_logic_fault_stop();
        return;
      }
      if (operand8 != 0U)
      {
        io_vm.output_mask |= (uint16_t)(1U << channel);
      }
      else
      {
        io_vm.output_mask &= (uint16_t)~(1U << channel);
      }
      io_vm.pulse_until[channel] = 0U;
      break;

    case OP_DO_PULSE:
      if ((io_vm.pc + 3U) > io_vm.body_length) { io_logic_fault_stop(); return; }
      channel = io_vm.body[io_vm.pc];
      operand16 = io_logic_read_u16(&io_vm.body[io_vm.pc + 1U]);
      io_vm.pc = (uint16_t)(io_vm.pc + 3U);
      if (channel >= 8U)
      {
        io_logic_fault_stop();
        return;
      }
      io_vm.output_mask |= (uint16_t)(1U << channel);
      io_vm.pulse_until[channel] = now_ms + operand16;
      break;

#if IO_LOGIC_ENABLE_ANALOG_OUTPUT
    case OP_AO_SET_VOLTAGE:
      if ((io_vm.pc + 2U) > io_vm.body_length) { io_logic_fault_stop(); return; }
      operand16 = io_logic_read_u16(&io_vm.body[io_vm.pc]);
      io_vm.pc = (uint16_t)(io_vm.pc + 2U);
      io_vm.ao_voltage_mv = operand16;
      /* 0~10V 电压输出：驱动 DAC1_CH2（PA5 -> LM358 -> 0~10V） */
      BSP_ANALOG_OUTPUT_SetVoltageMv(operand16);
      break;

    case OP_AO_SET_CURRENT:
      if ((io_vm.pc + 2U) > io_vm.body_length) { io_logic_fault_stop(); return; }
      operand16 = io_logic_read_u16(&io_vm.body[io_vm.pc]);
      io_vm.pc = (uint16_t)(io_vm.pc + 2U);
      io_vm.ao_current_ua = operand16;
      /* 0~20mA 电流输出：驱动 DAC1_CH1（PA4 -> LM358+SS8050 -> 4~20mA） */
      BSP_ANALOG_OUTPUT_SetCurrentUa(operand16);
      break;

    case OP_AO_BREATH:
      if ((io_vm.pc + 6U) > io_vm.body_length) { io_logic_fault_stop(); return; }
      {
        uint16_t lo = io_logic_read_u16(&io_vm.body[io_vm.pc]);
        uint16_t hi = io_logic_read_u16(&io_vm.body[io_vm.pc + 2U]);
        uint16_t period = io_logic_read_u16(&io_vm.body[io_vm.pc + 4U]);
        io_vm.pc = (uint16_t)(io_vm.pc + 6U);
        io_vm.ao_voltage_mv = hi;
        BSP_ANALOG_OUTPUT_StartVoltageBreath(lo, hi, period, now_ms);
      }
      break;
#else
    case OP_AO_SET_VOLTAGE:
      if ((io_vm.pc + 2U) > io_vm.body_length) { io_logic_fault_stop(); return; }
      io_vm.ao_voltage_mv = io_logic_read_u16(&io_vm.body[io_vm.pc]);
      io_vm.pc = (uint16_t)(io_vm.pc + 2U);
      break;

    case OP_AO_SET_CURRENT:
      if ((io_vm.pc + 2U) > io_vm.body_length) { io_logic_fault_stop(); return; }
      io_vm.ao_current_ua = io_logic_read_u16(&io_vm.body[io_vm.pc]);
      io_vm.pc = (uint16_t)(io_vm.pc + 2U);
      break;

    case OP_AO_BREATH:
      if ((io_vm.pc + 6U) > io_vm.body_length) { io_logic_fault_stop(); return; }
      io_vm.ao_voltage_mv = io_logic_read_u16(&io_vm.body[io_vm.pc + 2U]);
      io_vm.pc = (uint16_t)(io_vm.pc + 6U);
      break;
#endif

    case OP_DELAY:
      if ((io_vm.pc + 2U) > io_vm.body_length) { io_logic_fault_stop(); return; }
      operand16 = io_logic_read_u16(&io_vm.body[io_vm.pc]);
      io_vm.pc = (uint16_t)(io_vm.pc + 2U);
      if (operand16 > 0U)
      {
        io_vm.delay_until_ms = now_ms + operand16;
      }
      break;

    case OP_SET_MODE:
      if ((io_vm.pc + 1U) > io_vm.body_length) { io_logic_fault_stop(); return; }
      operand8 = io_vm.body[io_vm.pc];
      io_vm.pc = (uint16_t)(io_vm.pc + 1U);
      io_logic_do_set_mode(operand8);
      break;

    case OP_SET_MODE_EXT:
      /* 无条件切换到外部编码模式（DI3~DI6，已稳定），由主程序显式调用 */
      io_logic_do_set_mode(DIGITAL_IO_SERVICE_GetExternalMode());
      break;

    case OP_RUN_CURRENT_MODE:
      io_logic_call_mode(io_vm.current_mode);
      break;

    case OP_MODE_CALL:
      if ((io_vm.pc + 1U) > io_vm.body_length) { io_logic_fault_stop(); return; }
      operand8 = io_vm.body[io_vm.pc];
      io_vm.pc = (uint16_t)(io_vm.pc + 1U);
      io_logic_call_mode(operand8);
      break;

    case OP_RET:
      if (io_vm.return_depth > 0U)
      {
        io_vm.return_depth--;
        io_vm.pc = io_vm.return_stack[io_vm.return_depth];
      }
      else
      {
        /* 调用栈为空时 RET 视为主程序结束 */
        io_vm.run_state = IO_LOGIC_RUN_STATE_ENDED;
      }
      break;

    case OP_SET_FREQ_TH:
      if ((io_vm.pc + 7U) > io_vm.body_length) { io_logic_fault_stop(); return; }
      {
        uint8_t ch = io_vm.body[io_vm.pc];
        uint16_t low = io_logic_read_u16(&io_vm.body[io_vm.pc + 1U]);
        uint16_t high = io_logic_read_u16(&io_vm.body[io_vm.pc + 3U]);
        uint8_t hyst = io_vm.body[io_vm.pc + 5U];
        uint16_t timeout = io_logic_read_u16(&io_vm.body[io_vm.pc + 6U]);
        io_vm.pc = (uint16_t)(io_vm.pc + 8U);
        if ((ch >= 2U) || (hyst > 50U) || (low >= high) || (timeout == 0U))
        {
          /* 上位机已校验，固件防御性拒绝非法阈值 */
          io_logic_fault_stop();
          return;
        }
        io_vm.freq_th[ch].low_hz = low;
        io_vm.freq_th[ch].high_hz = high;
        io_vm.freq_th[ch].hyst_pct = hyst;
        io_vm.freq_th[ch].timeout_ms = timeout;
        io_vm.freq_th[ch].configured = 1U;
        io_vm.freq_class[ch] = DIGITAL_IO_SERVICE_FREQ_CLASS_NONE;
      }
      break;

    case OP_END:
      io_vm.run_state = IO_LOGIC_RUN_STATE_ENDED;
      break;

    default:
      /* 未定义操作码：程序非法，安全停止 */
      io_logic_fault_stop();
      break;
  }
}

/* ---------------- 对外接口 ---------------- */

void IO_LOGIC_ENGINE_Init(void)
{
  memset(&io_vm, 0, sizeof(io_vm));
  BSP_ANALOG_OUTPUT_Init(); /* 平台化工程在此初始化模拟量输出（默认 0V/0mA） */
  DIGITAL_IO_SERVICE_AllOutputsOff();
}

void IO_LOGIC_ENGINE_Step(uint16_t input_mask, uint32_t now_ms)
{
  uint32_t generation;
  uint16_t rising_mask;
  uint16_t falling_mask;

  if (IO_CONFIG_IsRunning() == 0U)
  {
    IO_LOGIC_ENGINE_Stop();
    io_vm.previous_input = input_mask;
    return;
  }

  if (io_logic_load_image() == 0U)
  {
    io_logic_fault_stop();
    io_vm.previous_input = input_mask;
    return;
  }

  /* 镜像代次变化：重置全部运行时状态（新模式程序首次执行）。 */
  generation = io_logic_read_u16(&io_vm.image[8]) |
               ((uint32_t)io_logic_read_u16(&io_vm.image[10]) << 16);
  if (generation != io_vm.generation)
  {
    memset(&io_vm.return_stack, 0, sizeof(io_vm.return_stack));
    io_vm.return_depth = 0U;
    io_vm.current_mode = 0U;
    io_vm.pc = 0U;
    io_vm.delay_until_ms = 0U;
    io_vm.run_state = IO_LOGIC_RUN_STATE_RUNNING;
    io_vm.output_mask = 0U;
    memset(io_vm.pulse_until, 0, sizeof(io_vm.pulse_until));
    memset(io_vm.freq_th, 0, sizeof(io_vm.freq_th));
    memset(io_vm.freq_class, 0, sizeof(io_vm.freq_class));
    io_vm.ao_voltage_mv = 0U;
    io_vm.ao_current_ua = 0U;
    io_vm.generation = generation;
    DIGITAL_IO_SERVICE_AllOutputsOff();
  }

  /* 单次执行结束（主程序无 while）：保持当前输出，不再继续执行。 */
  if (io_vm.run_state == IO_LOGIC_RUN_STATE_ENDED)
  {
    return;
  }
  if (io_vm.run_state != IO_LOGIC_RUN_STATE_RUNNING)
  {
    return;
  }

  /* 延时期：本 tick 不再执行指令。 */
  if ((io_vm.delay_until_ms != 0U) &&
      ((int32_t)(now_ms - io_vm.delay_until_ms) < 0))
  {
    return;
  }
  io_vm.delay_until_ms = 0U;

  /* 更新鉴频档位（程序配置了阈值才生效）。 */
  io_logic_update_freq_class(0U, now_ms);
  io_logic_update_freq_class(1U, now_ms);

  rising_mask = (uint16_t)(input_mask & (uint16_t)~io_vm.previous_input);
  falling_mask = (uint16_t)(io_vm.previous_input & (uint16_t)~input_mask);
  io_vm.previous_input = input_mask;

  /* 指令预算执行：防异常循环独占任务。 */
  io_vm.step_budget = IO_LOGIC_MAX_INSTR_PER_TICK;
  while ((io_vm.run_state == IO_LOGIC_RUN_STATE_RUNNING) && (io_vm.step_budget > 0U))
  {
    io_logic_execute_one(input_mask, rising_mask, falling_mask, now_ms);
    io_vm.step_budget--;
    /* IO_Delay 指令：本 tick 立即暂停，等待 delay_until 到期再继续，
     * 避免同一 tick 内穿透执行 delay 之后的指令（流水灯时序依赖此行为）。 */
    if ((io_vm.delay_until_ms != 0U) &&
        ((int32_t)(now_ms - io_vm.delay_until_ms) < 0))
    {
      break;
    }
  }

  /* 脉冲到期关断。 */
  for (uint8_t bit = 0U; bit < 8U; bit++)
  {
    uint16_t bit_mask = (uint16_t)(1U << bit);
    if ((io_vm.pulse_until[bit] != 0U) &&
        ((int32_t)(now_ms - io_vm.pulse_until[bit]) >= 0))
    {
      io_vm.output_mask &= (uint16_t)~bit_mask;
      io_vm.pulse_until[bit] = 0U;
    }
  }

  DIGITAL_IO_SERVICE_SetOutputMask(io_vm.output_mask);

  /* 模拟量输出波形推进（呼吸模式时更新 DAC，非呼吸模式下空操作）。 */
#if IO_LOGIC_ENABLE_ANALOG_OUTPUT
  BSP_ANALOG_OUTPUT_Process(now_ms);
#endif
}

void IO_LOGIC_ENGINE_Stop(void)
{
  io_vm.run_state = IO_LOGIC_RUN_STATE_STOPPED;
  io_vm.output_mask = 0U;
  memset(io_vm.pulse_until, 0, sizeof(io_vm.pulse_until));
  DIGITAL_IO_SERVICE_AllOutputsOff();
}

uint16_t IO_LOGIC_ENGINE_GetOutputMask(void)
{
  return io_vm.output_mask;
}

uint8_t IO_LOGIC_ENGINE_GetCurrentMode(void)
{
  return io_vm.current_mode;
}

uint8_t IO_LOGIC_ENGINE_GetRunState(void)
{
  return io_vm.run_state;
}

uint16_t IO_LOGIC_ENGINE_GetStatusFlags(void)
{
  return io_vm.status_flags;
}

uint8_t IO_LOGIC_ENGINE_GetFreqClass(uint8_t channel)
{
  if (channel >= 2U)
  {
    return 0U;
  }
  return io_vm.freq_class[channel];
}

uint16_t IO_LOGIC_ENGINE_GetAoVoltageMv(void)
{
  return io_vm.ao_voltage_mv;
}

uint16_t IO_LOGIC_ENGINE_GetAoCurrentUa(void)
{
  return io_vm.ao_current_ua;
}
