namespace MAK_Modbus.Models;

public class ModbusReadEventArgs : EventArgs
{
    public byte DeviceId { get; }
    public ushort StartAddress { get; }
    public ModbusFunctionCode FunctionCode { get; }
    public ushort[]? RegisterValues { get; }
    public bool[]? CoilValues { get; }
    public DateTime Timestamp { get; }
    public string? TaskId { get; }

    public ModbusReadEventArgs(byte deviceId, ushort startAddress, ModbusFunctionCode fc,
        ushort[] registerValues, string? taskId = null)
    {
        DeviceId = deviceId;
        StartAddress = startAddress;
        FunctionCode = fc;
        RegisterValues = registerValues;
        Timestamp = DateTime.Now;
        TaskId = taskId;
    }

    public ModbusReadEventArgs(byte deviceId, ushort startAddress, ModbusFunctionCode fc,
        bool[] coilValues, string? taskId = null)
    {
        DeviceId = deviceId;
        StartAddress = startAddress;
        FunctionCode = fc;
        CoilValues = coilValues;
        Timestamp = DateTime.Now;
        TaskId = taskId;
    }
}

public class ModbusWriteEventArgs : EventArgs
{
    public byte DeviceId { get; }
    public ushort StartAddress { get; }
    public DateTime Timestamp { get; }
    public string? TaskId { get; }

    public ModbusWriteEventArgs(byte deviceId, ushort startAddress, string? taskId = null)
    {
        DeviceId = deviceId;
        StartAddress = startAddress;
        Timestamp = DateTime.Now;
        TaskId = taskId;
    }
}

public class ModbusErrorEventArgs : EventArgs
{
    public Exception Error { get; }
    public string? TaskId { get; }
    public DateTime Timestamp { get; }

    public ModbusErrorEventArgs(Exception error, string? taskId = null)
    {
        Error = error;
        TaskId = taskId;
        Timestamp = DateTime.Now;
    }
}

public class ModbusConnectionEventArgs : EventArgs
{
    public bool IsConnected { get; }
    public string Message { get; }

    public ModbusConnectionEventArgs(bool isConnected, string message)
    {
        IsConnected = isConnected;
        Message = message;
    }
}
