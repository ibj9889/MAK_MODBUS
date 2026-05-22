using MAK_Modbus.Models;

namespace MAK_Modbus.Core;

public interface IModbusClient : IDisposable
{
    bool IsConnected { get; }

    event EventHandler<ModbusConnectionEventArgs>? ConnectionChanged;

    Task ConnectAsync(CancellationToken cancellationToken = default);
    void Disconnect();

    // ─── Read Operations ────────────────────────────────────────────────────

    /// <summary>FC01 - Coil 읽기 (0x/비트 단위 출력)</summary>
    Task<bool[]> ReadCoilsAsync(byte deviceId, ushort startAddress, ushort count,
        CancellationToken cancellationToken = default);

    /// <summary>FC02 - Discrete Input 읽기 (1x/비트 단위 입력, 읽기전용)</summary>
    Task<bool[]> ReadDiscreteInputsAsync(byte deviceId, ushort startAddress, ushort count,
        CancellationToken cancellationToken = default);

    /// <summary>FC03 - Holding Register 읽기 (4x/16비트 읽기쓰기)</summary>
    Task<ushort[]> ReadHoldingRegistersAsync(byte deviceId, ushort startAddress, ushort count,
        CancellationToken cancellationToken = default);

    /// <summary>FC04 - Input Register 읽기 (3x/16비트 읽기전용)</summary>
    Task<ushort[]> ReadInputRegistersAsync(byte deviceId, ushort startAddress, ushort count,
        CancellationToken cancellationToken = default);

    // ─── Write Operations ────────────────────────────────────────────────────

    /// <summary>FC05 - 단일 Coil 쓰기</summary>
    Task WriteSingleCoilAsync(byte deviceId, ushort address, bool value,
        CancellationToken cancellationToken = default);

    /// <summary>FC06 - 단일 Register 쓰기</summary>
    Task WriteSingleRegisterAsync(byte deviceId, ushort address, ushort value,
        CancellationToken cancellationToken = default);

    /// <summary>FC0F - 다중 Coil 쓰기</summary>
    Task WriteMultipleCoilsAsync(byte deviceId, ushort startAddress, bool[] values,
        CancellationToken cancellationToken = default);

    /// <summary>FC10 - 다중 Register 쓰기</summary>
    Task WriteMultipleRegistersAsync(byte deviceId, ushort startAddress, ushort[] values,
        CancellationToken cancellationToken = default);

    // ─── Write then Read ─────────────────────────────────────────────────────

    /// <summary>Holding Register 쓰기 후 바로 읽기</summary>
    Task<ushort[]> WriteAndReadRegistersAsync(
        byte deviceId,
        ushort writeAddress, ushort[] writeValues,
        ushort readAddress, ushort readCount,
        CancellationToken cancellationToken = default);
}
