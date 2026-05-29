namespace SimulatorWpfApp;

public sealed class ModbusCommandSet
{
    public string Name { get; set; } = string.Empty;
    public List<ModbusCommand> Commands { get; set; } = new();
}

public sealed class ModbusCommand
{
    public string Name { get; set; } = string.Empty;
    public string Action { get; set; } = "Read";
    public byte SlaveID { get; set; } = 1;
    public string Address { get; set; } = "0x0000";
    public int Length { get; set; } = 1;
    public ushort Value { get; set; } = 0;
    public int DelayAfterMs { get; set; } = 0;
}

public class CommandResultRow
{
    public string Name    { get; set; } = "";
    public string Action  { get; set; } = "";
    public string Status  { get; set; } = "";
    public string Message { get; set; } = "";
}
