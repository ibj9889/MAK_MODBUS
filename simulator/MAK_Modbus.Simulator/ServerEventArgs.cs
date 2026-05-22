namespace MAK_Modbus.Simulator;

public class ServerRequestEventArgs : EventArgs
{
    public byte UnitId { get; }
    public byte FunctionCode { get; }
    public ushort Address { get; }
    public ushort Count { get; }
    public string Direction { get; }
    public DateTime Timestamp { get; } = DateTime.Now;

    public ServerRequestEventArgs(byte unitId, byte fc, ushort address, ushort count, string direction)
    {
        UnitId = unitId;
        FunctionCode = fc;
        Address = address;
        Count = count;
        Direction = direction;
    }

    public override string ToString() =>
        $"[{Timestamp:HH:mm:ss.fff}] FC{FunctionCode:X2} DevID={UnitId} Addr={Address} Count={Count} ({Direction})";
}

public class RegisterChangedEventArgs : EventArgs
{
    public string MemoryType { get; }
    public ushort Address { get; }
    public ushort Count { get; }

    public RegisterChangedEventArgs(string memoryType, ushort address, ushort count)
    {
        MemoryType = memoryType;
        Address = address;
        Count = count;
    }
}
