#ifndef SCRIPT_RUNNER_H
#define SCRIPT_RUNNER_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/*
 * 轻量脚本解释器（方案 B）
 *
 * 命令集与上位机 ScriptConsoleForm.cs 的 Parse/RunAsync 语义完全一致：
 *   on <ch> / off <ch> / toggle <ch>    ch = Ja1-Ja4, Jb1-Jb4（不区分大小写）
 *   write 0xXXXX                       整体覆盖输出位图（低 8 位）
 *   all on / all off                    0x00FF / 0x0000
 *   delay <非负整数毫秒>
 *   read                               只记录当前位图，不产生 IO
 *   loop <正整数|forever> ... endloop  循环（forever 即无限，count=0）
 *   '#' 开头为注释，空白行跳过，命令不区分大小写
 *
 * 执行语义：输出位图从"启动时刻当前输出低 8 位"出发逐步修改；
 * 步骤数超过上限（500 万，与上位机一致）视为死循环并报错。
 */

typedef enum
{
    SCRIPT_ST_IDLE    = 0,   /* 未运行 / 自然执行完成 */
    SCRIPT_ST_RUNNING = 1,
    SCRIPT_ST_STOPPED = 2,   /* 被停止命令终止 */
    SCRIPT_ST_ERROR   = 3    /* 解析或运行错误 */
} ScriptRunnerStatus;

typedef struct
{
    ScriptRunnerStatus status;
    uint16_t line;       /* 当前执行行（error 时为错误行），0 = 无 */
    uint32_t steps;      /* 已执行步骤数 */
} ScriptRunnerInfo;

void SCRIPT_RUNNER_Init(void);
void SCRIPT_RUNNER_Start(void);   /* 从 Flash 加载并开始执行 */
void SCRIPT_RUNNER_Stop(void);    /* 请求停止（任务内生效） */
void SCRIPT_RUNNER_Run(void);     /* 任务主循环，阻塞运行 */
void SCRIPT_RUNNER_GetInfo(ScriptRunnerInfo *info);

#ifdef __cplusplus
}
#endif

#endif /* SCRIPT_RUNNER_H */
