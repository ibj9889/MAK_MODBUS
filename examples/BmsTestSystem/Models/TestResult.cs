namespace BmsTestSystem.Models;

public sealed class CommandResult
{
    public string Name { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;

    /// <summary>Raw register values from Read command. Null for Write commands.</summary>
    public ushort[]? RawData { get; set; }

    public DateTime Timestamp { get; } = DateTime.Now;
}
