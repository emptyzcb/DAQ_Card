using IndustrialFieldDataAcquisitionTerminal;

static IoLogicCompiledImage Compile(string source)
{
    var result = new IoLogicCompiler().Compile(source);
    if (!result.Success || result.Image is null)
        throw new InvalidOperationException(string.Join("; ", result.Errors));
    return result.Image;
}

var chase = IoLogicPreview.Build(Compile(IoLogicExamples.Default), 9000);
if (chase.At(0).DigitalOutputs != 0x01 || chase.At(490).DigitalOutputs != 0x01 ||
    chase.At(500).DigitalOutputs != 0x02 || chase.At(4000).DigitalOutputs != 0x01)
    throw new InvalidOperationException("The output chase must follow MCU delay timing.");
if (chase.At(0).CurrentUa != 4000 || chase.At(1500).CurrentUa < 19000 ||
    chase.At(3000).CurrentUa > 5000 || chase.At(1500).VoltageMv < 9000)
    throw new InvalidOperationException("Analog breaths must advance with simulated time.");

var conditional = Compile("""
void IO_Logic_Main(void) {
    while (1) {
        if (IO_DI_Level(INPUT_1) == 1) { IO_DO_Set(RELAY_2, 1); }
        else { IO_DO_Set(RELAY_2, 0); }
        IO_Delay(10);
    }
}
""");
var events = new[] { new IoPreviewInputEvent(0, 0), new IoPreviewInputEvent(200, 1) };
var driven = IoLogicPreview.Build(conditional, 500, events);
if (driven.At(190).DigitalOutputs != 0 || driven.At(200).DigitalOutputs != 0x02 ||
    driven.At(200).DigitalInputs != 1 || driven.At(199).TimeMs != 190)
    throw new InvalidOperationException("Seeking and digital input events must be deterministic.");

var pulse = IoLogicPreview.Build(Compile("""
void IO_Logic_Main(void) { IO_DO_Pulse(TRANSISTOR_1, 120); }
"""), 300);
if (pulse.At(0).DigitalOutputs != 0x10 || pulse.At(120).DigitalOutputs != 0)
    throw new InvalidOperationException("Pulse outputs must expire at their configured time.");

Console.WriteLine("Logic preview smoke passed.");
