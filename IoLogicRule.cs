namespace IndustrialFieldDataAcquisitionTerminal;

internal enum IoRuleConditionMode
{
    Level,
    RisingEdge,
    FallingEdge
}

internal enum IoRuleOperator
{
    And,
    Or
}

internal enum IoRuleAction
{
    Set,
    Reset,
    Toggle,
    Pulse
}

internal sealed class IoLogicRule
{
    public string Name { get; set; } = "新规则";
    public bool Enabled { get; set; } = true;
    public int Priority { get; set; } = 100;
    public IoRuleConditionMode ConditionMode { get; set; } = IoRuleConditionMode.Level;
    public IoRuleOperator Operator { get; set; } = IoRuleOperator.And;
    public ushort InputMask { get; set; } = 0x0001;
    public ushort InputExpected { get; set; } = 0x0001;
    public ushort OutputMask { get; set; } = 0x0001;
    public IoRuleAction Action { get; set; } = IoRuleAction.Set;
    public int DelayMs { get; set; }
    public int PulseMs { get; set; } = 1000;

    public override string ToString() => $"{(Enabled ? "●" : "○")}  {Name}";
}
