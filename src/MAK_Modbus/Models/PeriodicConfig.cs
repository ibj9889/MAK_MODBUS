namespace MAK_Modbus.Models;

public class PeriodicReadConfig
{
    public string TaskId { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public byte DeviceId { get; set; } = 1;
    public ushort StartAddress { get; set; }
    public ushort Count { get; set; } = 1;
    public ModbusFunctionCode FunctionCode { get; set; } = ModbusFunctionCode.ReadHoldingRegisters;
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(1);
}

public class PeriodicWriteConfig
{
    public string TaskId { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public byte DeviceId { get; set; } = 1;
    public ushort StartAddress { get; set; }
    public ushort[]? RegisterValues { get; set; }
    public bool[]? CoilValues { get; set; }
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(1);
    public bool ReadBackAfterWrite { get; set; } = false;
    public ushort ReadBackCount { get; set; } = 1;
}
