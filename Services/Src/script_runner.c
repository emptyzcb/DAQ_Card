#include "script_runner.h"
#include "script_flash.h"
#include "modbus_slave.h"
#include "bsp_digital_io.h"
#include "FreeRTOS.h"   /* pdMS_TO_TICKS */
#include "task.h"       /* vTaskDelay */

#include <string.h>

/* ===== 限制（与上位机 500 万步保护一致） ===== */
#define SCRIPT_CMD_MAX     512U
#define SCRIPT_LOOP_DEPTH  32U
#define SCRIPT_MAX_STEPS   5000000UL

/* ===== 命令种类 ===== */
typedef enum
{
    SCRIPT_CMD_ON = 0,
    SCRIPT_CMD_OFF,
    SCRIPT_CMD_TOGGLE,
    SCRIPT_CMD_WRITE,
    SCRIPT_CMD_ALL_ON,
    SCRIPT_CMD_ALL_OFF,
    SCRIPT_CMD_DELAY,
    SCRIPT_CMD_READ,
    SCRIPT_CMD_LOOP,
    SCRIPT_CMD_ENDLOOP
} ScriptCmdKind;

typedef struct
{
    uint8_t  kind;
    uint16_t line;      /* 源行号，从 1 起 */
    int32_t  value;     /* bit / bits / ms / loop count（0=forever） */
} ScriptCmd;

/* ===== 运行时状态 ===== */
static char script_text[SCRIPT_MAX_TEXT];   /* Flash 脚本镜像 */
static ScriptCmd cmds[SCRIPT_CMD_MAX];
static uint16_t cmd_count;

static uint16_t loop_pc[SCRIPT_LOOP_DEPTH];
static int32_t  loop_cnt[SCRIPT_LOOP_DEPTH];
static uint8_t  loop_depth;

static volatile ScriptRunnerStatus status = SCRIPT_ST_IDLE;
static volatile uint8_t stop_request;
static uint16_t pc;
static uint16_t exec_line;
static uint16_t bits;          /* 输出位图（低 8 位有效） */
static uint32_t steps;

/* ===== 小工具 ===== */

/* 不区分大小写比较：a 与 b（b 为小写） */
static int ieq(const char *a, const char *b)
{
    char c;
    while (*b)
    {
        c = *a++;
        if (c >= 'A' && c <= 'Z')
        {
            c = (char)(c - 'A' + 'a');
        }
        if (c != *b)
        {
            return 0;
        }
        b++;
    }
    return *a == 0;
}

/* 通道名 → bit：ja1..ja4=0..3, jb1..jb4=4..7；未知返回 -1 */
static int channel_to_bit(const char *name)
{
    static const char *names[8] = {"ja1", "ja2", "ja3", "ja4",
                                   "jb1", "jb2", "jb3", "jb4"};
    int i;
    for (i = 0; i < 8; i++)
    {
        if (ieq(name, names[i]))
        {
            return i;
        }
    }
    return -1;
}

/* 无符号 10 进制解析（非负整数） */
static int parse_uint(const char *s, uint32_t *out)
{
    uint32_t v = 0;
    if (*s == 0)
    {
        return -1;
    }
    for (; *s; s++)
    {
        if (*s < '0' || *s > '9')
        {
            return -1;
        }
        v = v * 10U + (uint32_t)(*s - '0');
        if (v > 0x7FFFFFFFUL)
        {
            return -1;
        }
    }
    *out = v;
    return 0;
}

/* 16 进制解析：允许 0x 前缀；超出 16 位截断（与上位机 cast ushort 一致） */
static int parse_hex(const char *s, uint16_t *out)
{
    uint32_t v = 0;
    if (*s == 0)
    {
        return -1;
    }
    if (s[0] == '0' && (s[1] == 'x' || s[1] == 'X'))
    {
        s += 2;
    }
    if (*s == 0)
    {
        return -1;
    }
    for (; *s; s++)
    {
        char c = *s;
        uint32_t d;
        if (c >= '0' && c <= '9')
        {
            d = (uint32_t)(c - '0');
        }
        else if (c >= 'a' && c <= 'f')
        {
            d = (uint32_t)(c - 'a' + 10);
        }
        else if (c >= 'A' && c <= 'F')
        {
            d = (uint32_t)(c - 'A' + 10);
        }
        else
        {
            return -1;
        }
        v = (v << 4) | d;
        if (v > 0xFFFFUL)
        {
            v &= 0xFFFFUL;   /* 与上位机 ushort 截断一致 */
        }
    }
    *out = (uint16_t)v;
    return 0;
}

/* ===== 解析（语义与上位机 Parse 一致） ===== */

static void parse_error(uint16_t line_no)
{
    exec_line = line_no;
    status = SCRIPT_ST_ERROR;
}

/* 从一行文本提取一个命令；返回 0 成功 / -1 解析错误（已设置状态） */
static int parse_line(const char *line_start, const char *line_end, uint16_t line_no)
{
    const char *p = line_start;
    const char *q;
    char token[32];
    uint32_t tok_len;
    int kind = -1;
    int32_t value = 0;

    /* '# ' comment truncation; also treat NUL (padding byte from
     * register-aligned fc 0x10 staging) as end-of-line. */
    for (q = p; q < line_end; q++)
    {
        if (*q == '#' || *q == '\0')
        {
            line_end = q;
            break;
        }
    }

    /* 去首空白 */
    while (p < line_end && (*p == ' ' || *p == '\t'))
    {
        p++;
    }
    /* 去尾空白 */
    while (line_end > p && (line_end[-1] == ' ' || line_end[-1] == '\t'))
    {
        line_end--;
    }
    if (p >= line_end)
    {
        return 0;   /* 空行 */
    }

    /* 第一个 token：命令 */
    q = p;
    while (q < line_end && *q != ' ' && *q != '\t')
    {
        q++;
    }
    tok_len = (uint32_t)(q - p);
    if (tok_len >= sizeof(token))
    {
        tok_len = sizeof(token) - 1U;
    }
    memcpy(token, p, tok_len);
    token[tok_len] = 0;

    /* 剩余部分（去掉前导空白）即参数 */
    while (q < line_end && (*q == ' ' || *q == '\t'))
    {
        q++;
    }
    {
        const char *arg = q;
        const char *arg_end = line_end;
        char argbuf[64];
        uint32_t alen = (uint32_t)(arg_end - arg);
        if (alen >= sizeof(argbuf))
        {
            alen = sizeof(argbuf) - 1U;
        }
        memcpy(argbuf, arg, alen);
        argbuf[alen] = 0;

        if (ieq(token, "on"))
        {
            int bit;
            if (alen == 0)
            {
                parse_error(line_no); return -1;
            }
            bit = channel_to_bit(argbuf);
            if (bit < 0)
            {
                parse_error(line_no); return -1;
            }
            kind = SCRIPT_CMD_ON; value = bit;
        }
        else if (ieq(token, "off"))
        {
            int bit;
            if (alen == 0)
            {
                parse_error(line_no); return -1;
            }
            bit = channel_to_bit(argbuf);
            if (bit < 0)
            {
                parse_error(line_no); return -1;
            }
            kind = SCRIPT_CMD_OFF; value = bit;
        }
        else if (ieq(token, "toggle"))
        {
            int bit;
            if (alen == 0)
            {
                parse_error(line_no); return -1;
            }
            bit = channel_to_bit(argbuf);
            if (bit < 0)
            {
                parse_error(line_no); return -1;
            }
            kind = SCRIPT_CMD_TOGGLE; value = bit;
        }
        else if (ieq(token, "write"))
        {
            uint16_t v;
            if (alen == 0 || parse_hex(argbuf, &v) != 0)
            {
                parse_error(line_no); return -1;
            }
            kind = SCRIPT_CMD_WRITE; value = (int32_t)v;
        }
        else if (ieq(token, "all"))
        {
            if (ieq(argbuf, "on"))
            {
                kind = SCRIPT_CMD_ALL_ON;
            }
            else if (ieq(argbuf, "off"))
            {
                kind = SCRIPT_CMD_ALL_OFF;
            }
            else
            {
                parse_error(line_no); return -1;
            }
        }
        else if (ieq(token, "delay"))
        {
            uint32_t ms;
            if (alen == 0 || parse_uint(argbuf, &ms) != 0)
            {
                parse_error(line_no); return -1;
            }
            kind = SCRIPT_CMD_DELAY; value = (int32_t)ms;
        }
        else if (ieq(token, "read"))
        {
            if (alen != 0)
            {
                parse_error(line_no); return -1;
            }
            kind = SCRIPT_CMD_READ;
        }
        else if (ieq(token, "loop"))
        {
            uint32_t n;
            if (alen == 0)
            {
                parse_error(line_no); return -1;
            }
            if (ieq(argbuf, "forever"))
            {
                kind = SCRIPT_CMD_LOOP; value = 0;   /* 0 = 无限 */
            }
            else if (parse_uint(argbuf, &n) == 0 && n >= 1U)
            {
                kind = SCRIPT_CMD_LOOP; value = (int32_t)n;
            }
            else
            {
                parse_error(line_no); return -1;
            }
        }
        else if (ieq(token, "endloop"))
        {
            if (alen != 0)
            {
                parse_error(line_no); return -1;
            }
            kind = SCRIPT_CMD_ENDLOOP;
        }
        else
        {
            parse_error(line_no); return -1;
        }
    }

    if (cmd_count >= SCRIPT_CMD_MAX)
    {
        parse_error(line_no);
        return -1;
    }
    cmds[cmd_count].kind  = (uint8_t)kind;
    cmds[cmd_count].line  = line_no;
    cmds[cmd_count].value = value;
    cmd_count++;
    return 0;
}

static int parse_script(const char *text, uint32_t len)
{
    uint32_t pos = 0;
    uint16_t line_no = 1;
    cmd_count = 0;

    while (pos < len)
    {
        uint32_t line_start = pos;
        uint32_t line_end = pos;
        while (pos < len && text[pos] != '\n' && text[pos] != '\r')
        {
            pos++;
        }
        line_end = pos;
        /* 跳过行尾 \r\n */
        if (pos < len && text[pos] == '\r')
        {
            pos++;
        }
        if (pos < len && text[pos] == '\n')
        {
            pos++;
        }
        if (parse_line(text + line_start, text + line_end, line_no) != 0)
        {
            return -1;
        }
        line_no++;
    }
    return 0;
}

/* ===== 执行 ===== */

static void apply_bits(void)
{
    MODBUS_SLAVE_WriteOutputBits(bits);
}

void SCRIPT_RUNNER_Init(void)
{
    status = SCRIPT_ST_IDLE;
    stop_request = 0;
    cmd_count = 0;
    pc = 0;
    steps = 0;
    loop_depth = 0;
    exec_line = 0;
    bits = 0;
}

void SCRIPT_RUNNER_Start(void)
{
    uint32_t n;

    stop_request = 0;
    status = SCRIPT_ST_IDLE;

    n = SCRIPT_FLASH_Read(script_text, sizeof(script_text));
    if (n == 0)
    {
        exec_line = 0;
        status = SCRIPT_ST_IDLE;   /* no script stored: stay idle */
        return;
    }

    if (parse_script(script_text, n) != 0)
    {
        /* 错误行号已在 parse_error 记录 */
        status = SCRIPT_ST_ERROR;
        return;
    }

    bits = (uint16_t)(BSP_DIGITAL_IO_GetOutputMask() & 0x00FF);
    pc = 0;
    steps = 0;
    loop_depth = 0;
    status = SCRIPT_ST_RUNNING;
}

void SCRIPT_RUNNER_Stop(void)
{
    stop_request = 1;
}

void SCRIPT_RUNNER_Run(void)
{
    for (;;)
    {
        if (status != SCRIPT_ST_RUNNING)
        {
            if (stop_request)
            {
                stop_request = 0;
                status = SCRIPT_ST_STOPPED;
            }
            vTaskDelay(pdMS_TO_TICKS(20));
            continue;
        }

        if (stop_request)
        {
            stop_request = 0;
            status = SCRIPT_ST_STOPPED;
            continue;
        }

        if (pc >= cmd_count)
        {
            status = SCRIPT_ST_IDLE;   /* 自然执行完成 */
            continue;
        }

        {
            const ScriptCmd *cmd = &cmds[pc];
            exec_line = cmd->line;

            switch (cmd->kind)
            {
            case SCRIPT_CMD_ON:
                bits = (uint16_t)(bits | (uint16_t)(1U << cmd->value));
                apply_bits();
                pc++;
                break;
            case SCRIPT_CMD_OFF:
                bits = (uint16_t)(bits & (uint16_t)~(1U << cmd->value));
                apply_bits();
                pc++;
                break;
            case SCRIPT_CMD_TOGGLE:
                bits = (uint16_t)(bits ^ (uint16_t)(1U << cmd->value));
                apply_bits();
                pc++;
                break;
            case SCRIPT_CMD_WRITE:
                bits = (uint16_t)cmd->value;
                apply_bits();
                pc++;
                break;
            case SCRIPT_CMD_ALL_ON:
                bits = 0x00FF;
                apply_bits();
                pc++;
                break;
            case SCRIPT_CMD_ALL_OFF:
                bits = 0x0000;
                apply_bits();
                pc++;
                break;
            case SCRIPT_CMD_DELAY:
                vTaskDelay(pdMS_TO_TICKS((uint32_t)cmd->value));
                pc++;
                break;
            case SCRIPT_CMD_READ:
                /* 只记录，不产生 IO（当前位图经寄存器可查） */
                pc++;
                break;
            case SCRIPT_CMD_LOOP:
                if (loop_depth >= SCRIPT_LOOP_DEPTH)
                {
                    exec_line = cmd->line;
                    status = SCRIPT_ST_ERROR;
                    continue;
                }
                loop_pc[loop_depth] = (uint16_t)(pc + 1);
                loop_cnt[loop_depth] = cmd->value;
                loop_depth++;
                pc++;
                break;
            case SCRIPT_CMD_ENDLOOP:
                if (loop_depth == 0)
                {
                    exec_line = cmd->line;
                    status = SCRIPT_ST_ERROR;   /* endloop 没有配对的 loop */
                    continue;
                }
                {
                    uint16_t ret_pc = loop_pc[loop_depth - 1];
                    int32_t rem = loop_cnt[loop_depth - 1];
                    loop_depth--;
                    if (rem == 0)
                    {
                        /* forever：无限循环 */
                        loop_pc[loop_depth] = ret_pc;
                        loop_cnt[loop_depth] = 0;
                        loop_depth++;
                        pc = ret_pc;
                    }
                    else
                    {
                        rem--;
                        if (rem > 0)
                        {
                            loop_pc[loop_depth] = ret_pc;
                            loop_cnt[loop_depth] = rem;
                            loop_depth++;
                            pc = ret_pc;
                        }
                        else
                        {
                            pc++;
                        }
                    }
                }
                break;
            default:
                exec_line = cmd->line;
                status = SCRIPT_ST_ERROR;
                continue;
            }
        }

        if (++steps > SCRIPT_MAX_STEPS)
        {
            exec_line = cmds[pc >= cmd_count ? cmd_count - 1U : pc].line;
            status = SCRIPT_ST_ERROR;   /* 疑似死循环 */
        }
    }
}

void SCRIPT_RUNNER_GetInfo(ScriptRunnerInfo *info)
{
    if (info == 0)
    {
        return;
    }
    info->status = (ScriptRunnerStatus)status;
    info->line = exec_line;
    info->steps = steps;
}
