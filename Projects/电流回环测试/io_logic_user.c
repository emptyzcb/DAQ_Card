/* I1: 4-20 mA breathing, six-second cycle.
 * Relays R1-R4 and transistors T1-T4 alternate every second.
 * Switch each group off before enabling the other group.
 */
void IO_Mode_0_Callback(void)
{
    IO_AO_BreathCurrent(4000, 20000, 6000);

    IO_DO_Set(TRANSISTOR_1, 0);
    IO_DO_Set(TRANSISTOR_2, 0);
    IO_DO_Set(TRANSISTOR_3, 0);
    IO_DO_Set(TRANSISTOR_4, 0);
    IO_DO_Set(RELAY_1, 1);
    IO_DO_Set(RELAY_2, 1);
    IO_DO_Set(RELAY_3, 1);
    IO_DO_Set(RELAY_4, 1);
    IO_Delay(1000);

    IO_DO_Set(RELAY_1, 0);
    IO_DO_Set(RELAY_2, 0);
    IO_DO_Set(RELAY_3, 0);
    IO_DO_Set(RELAY_4, 0);
    IO_DO_Set(TRANSISTOR_1, 1);
    IO_DO_Set(TRANSISTOR_2, 1);
    IO_DO_Set(TRANSISTOR_3, 1);
    IO_DO_Set(TRANSISTOR_4, 1);
    IO_Delay(1000);

    IO_DO_Set(TRANSISTOR_1, 0);
    IO_DO_Set(TRANSISTOR_2, 0);
    IO_DO_Set(TRANSISTOR_3, 0);
    IO_DO_Set(TRANSISTOR_4, 0);
}

void IO_Logic_Main(void)
{
    IO_SetMode(0);
    while (1)
    {
        IO_RunCurrentMode();
        IO_Delay(10);
    }
}
