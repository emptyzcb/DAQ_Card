namespace IndustrialFieldDataAcquisitionTerminal;

internal readonly record struct TrendPoint(DateTime Timestamp, double Value, bool IsValid = true);
