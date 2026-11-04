namespace IndustrialFieldDataAcquisitionTerminal;

internal static class IoLogicExamples
{
    public const string Default = """
/* IOCF v2 example: R1-R4, then T1-T4, one output at a time.
 * Mode 0 also runs 4-20 mA current and 0-10 V voltage breaths with 6-second cycles.
 * Mode 0 is defined below. Undefined modes 1-15 compile as empty callbacks.
 * This is DSL bytecode behavior, not a C __weak function definition.
 */

void IO_Mode_0_Callback(void)
{
    IO_AO_BreathCurrent(4000, 20000, 6000);
    IO_AO_Breath(0, 10000, 6000);

    IO_DO_Set(RELAY_1, 1);
    IO_Delay(500);
    IO_DO_Set(RELAY_1, 0);

    IO_DO_Set(RELAY_2, 1);
    IO_Delay(500);
    IO_DO_Set(RELAY_2, 0);

    IO_DO_Set(RELAY_3, 1);
    IO_Delay(500);
    IO_DO_Set(RELAY_3, 0);

    IO_DO_Set(RELAY_4, 1);
    IO_Delay(500);
    IO_DO_Set(RELAY_4, 0);

    IO_DO_Set(TRANSISTOR_1, 1);
    IO_Delay(500);
    IO_DO_Set(TRANSISTOR_1, 0);

    IO_DO_Set(TRANSISTOR_2, 1);
    IO_Delay(500);
    IO_DO_Set(TRANSISTOR_2, 0);

    IO_DO_Set(TRANSISTOR_3, 1);
    IO_Delay(500);
    IO_DO_Set(TRANSISTOR_3, 0);

    IO_DO_Set(TRANSISTOR_4, 1);
    IO_Delay(500);
    IO_DO_Set(TRANSISTOR_4, 0);
}

void IO_Logic_Main(void)
{
    IO_SetMode(0);
    while (1)
    {
        /* DI3..DI6 select the mode; mode 0 runs the chase. */
        if (IO_GetExternalMode() != IO_GetCurrentMode())
        {
            IO_SetMode(IO_GetExternalMode());
        }
        IO_RunCurrentMode();
        IO_Delay(10);
    }
}
""";
}
