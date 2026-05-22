using System.Net.Sockets;
using MAK_Modbus.Models;

namespace MAK_Modbus.Core;

/// <summary>
/// Modbus TCP 클라이언트 (MBAP 헤더 + PDU 방식)
/// </summary>
public sealed class ModbusTcpClient : IModbusClient
{
    private readonly string _host;
    private readonly int _port;
    private readonly int _timeoutMs;
    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private ushort _transactionId;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public bool IsConnected => _tcp?.Connected ?? false;
    public event EventHandler<ModbusConnectionEventArgs>? ConnectionChanged;

    /// <param name="host">PLC/장치 IP 주소</param>
    /// <param name="port">Modbus TCP 포트 (기본값 502)</param>
    /// <param name="timeoutMs">통신 타임아웃 (ms)</param>
    public ModbusTcpClient(string host, int port = 502, int timeoutMs = 3000)
    {
        _host = host;
        _port = port;
        _timeoutMs = timeoutMs;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        _tcp = new TcpClient { SendTimeout = _timeoutMs, ReceiveTimeout = _timeoutMs };
        await _tcp.ConnectAsync(_host, _port, cancellationToken);
        _stream = _tcp.GetStream();
        ConnectionChanged?.Invoke(this, new ModbusConnectionEventArgs(true, $"TCP 연결 성공: {_host}:{_port}"));
    }

    public void Disconnect()
    {
        _stream?.Close();
        _tcp?.Close();
        _stream = null;
        _tcp = null;
        ConnectionChanged?.Invoke(this, new ModbusConnectionEventArgs(false, "TCP 연결 종료"));
    }

    // ─── Read ───────────────────────────────────────────────────────────────

    public async Task<bool[]> ReadCoilsAsync(byte deviceId, ushort startAddress, ushort count,
        CancellationToken cancellationToken = default)
    {
        var response = await SendReceiveAsync(deviceId, BuildReadPdu(0x01, startAddress, count), cancellationToken);
        return ParseCoilResponse(response, count);
    }

    public async Task<bool[]> ReadDiscreteInputsAsync(byte deviceId, ushort startAddress, ushort count,
        CancellationToken cancellationToken = default)
    {
        var response = await SendReceiveAsync(deviceId, BuildReadPdu(0x02, startAddress, count), cancellationToken);
        return ParseCoilResponse(response, count);
    }

    public async Task<ushort[]> ReadHoldingRegistersAsync(byte deviceId, ushort startAddress, ushort count,
        CancellationToken cancellationToken = default)
    {
        var response = await SendReceiveAsync(deviceId, BuildReadPdu(0x03, startAddress, count), cancellationToken);
        return ParseRegisterResponse(response, count);
    }

    public async Task<ushort[]> ReadInputRegistersAsync(byte deviceId, ushort startAddress, ushort count,
        CancellationToken cancellationToken = default)
    {
        var response = await SendReceiveAsync(deviceId, BuildReadPdu(0x04, startAddress, count), cancellationToken);
        return ParseRegisterResponse(response, count);
    }

    // ─── Write ──────────────────────────────────────────────────────────────

    public async Task WriteSingleCoilAsync(byte deviceId, ushort address, bool value,
        CancellationToken cancellationToken = default)
    {
        ushort coilValue = value ? (ushort)0xFF00 : (ushort)0x0000;
        var pdu = new byte[] { 0x05, Hi(address), Lo(address), Hi(coilValue), Lo(coilValue) };
        await SendReceiveAsync(deviceId, pdu, cancellationToken);
    }

    public async Task WriteSingleRegisterAsync(byte deviceId, ushort address, ushort value,
        CancellationToken cancellationToken = default)
    {
        var pdu = new byte[] { 0x06, Hi(address), Lo(address), Hi(value), Lo(value) };
        await SendReceiveAsync(deviceId, pdu, cancellationToken);
    }

    public async Task WriteMultipleCoilsAsync(byte deviceId, ushort startAddress, bool[] values,
        CancellationToken cancellationToken = default)
    {
        var byteCount = (byte)((values.Length + 7) / 8);
        var coilBytes = new byte[byteCount];
        for (int i = 0; i < values.Length; i++)
            if (values[i]) coilBytes[i / 8] |= (byte)(1 << (i % 8));

        var pdu = new byte[6 + byteCount];
        pdu[0] = 0x0F;
        pdu[1] = Hi(startAddress); pdu[2] = Lo(startAddress);
        pdu[3] = Hi((ushort)values.Length); pdu[4] = Lo((ushort)values.Length);
        pdu[5] = byteCount;
        Array.Copy(coilBytes, 0, pdu, 6, byteCount);

        await SendReceiveAsync(deviceId, pdu, cancellationToken);
    }

    public async Task WriteMultipleRegistersAsync(byte deviceId, ushort startAddress, ushort[] values,
        CancellationToken cancellationToken = default)
    {
        var byteCount = (byte)(values.Length * 2);
        var pdu = new byte[6 + byteCount];
        pdu[0] = 0x10;
        pdu[1] = Hi(startAddress); pdu[2] = Lo(startAddress);
        pdu[3] = Hi((ushort)values.Length); pdu[4] = Lo((ushort)values.Length);
        pdu[5] = byteCount;
        for (int i = 0; i < values.Length; i++)
        {
            pdu[6 + i * 2] = Hi(values[i]);
            pdu[6 + i * 2 + 1] = Lo(values[i]);
        }
        await SendReceiveAsync(deviceId, pdu, cancellationToken);
    }

    public async Task<ushort[]> WriteAndReadRegistersAsync(
        byte deviceId,
        ushort writeAddress, ushort[] writeValues,
        ushort readAddress, ushort readCount,
        CancellationToken cancellationToken = default)
    {
        await WriteMultipleRegistersAsync(deviceId, writeAddress, writeValues, cancellationToken);
        return await ReadHoldingRegistersAsync(deviceId, readAddress, readCount, cancellationToken);
    }

    // ─── Internal ───────────────────────────────────────────────────────────

    private async Task<byte[]> SendReceiveAsync(byte unitId, byte[] pdu, CancellationToken ct)
    {
        if (_stream == null) throw new InvalidOperationException("연결되지 않았습니다. ConnectAsync()를 먼저 호출하세요.");

        await _lock.WaitAsync(ct);
        try
        {
            var frame = BuildMbapFrame(unitId, pdu);
            await _stream.WriteAsync(frame, ct);

            // MBAP header 7 bytes (TransID 2 + ProtocolID 2 + Length 2 + UnitID 1)
            var mbap = new byte[7];
            await ReadExactAsync(_stream, mbap, ct);

            var dataLength = (mbap[4] << 8) | mbap[5]; // Length 필드 (UnitID 포함)
            var body = new byte[dataLength - 1]; // UnitID는 이미 읽음
            await ReadExactAsync(_stream, body, ct);

            // 예외 응답 처리 (FC | 0x80)
            if (body.Length > 0 && (body[0] & 0x80) != 0)
                throw new ModbusException(body.Length > 1 ? body[1] : (byte)0xFF);

            return body;
        }
        finally
        {
            _lock.Release();
        }
    }

    private byte[] BuildMbapFrame(byte unitId, byte[] pdu)
    {
        var tid = ++_transactionId;
        var length = (ushort)(1 + pdu.Length);
        var frame = new byte[7 + pdu.Length];
        frame[0] = Hi(tid); frame[1] = Lo(tid);
        frame[2] = 0x00; frame[3] = 0x00; // Protocol ID
        frame[4] = Hi(length); frame[5] = Lo(length);
        frame[6] = unitId;
        Array.Copy(pdu, 0, frame, 7, pdu.Length);
        return frame;
    }

    private static byte[] BuildReadPdu(byte fc, ushort startAddr, ushort count) =>
        new byte[] { fc, Hi(startAddr), Lo(startAddr), Hi(count), Lo(count) };

    private static bool[] ParseCoilResponse(byte[] response, ushort count)
    {
        var result = new bool[count];
        for (int i = 0; i < count; i++)
            result[i] = (response[2 + i / 8] & (1 << (i % 8))) != 0;
        return result;
    }

    private static ushort[] ParseRegisterResponse(byte[] response, ushort count)
    {
        var result = new ushort[count];
        for (int i = 0; i < count; i++)
            result[i] = (ushort)((response[2 + i * 2] << 8) | response[2 + i * 2 + 1]);
        return result;
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), ct);
            if (read == 0) throw new IOException("연결이 종료되었습니다.");
            offset += read;
        }
    }

    /// <summary>
    /// Socket.Poll로 실제 TCP 연결 상태를 확인합니다.
    /// Poll + Available == 0 이면 서버가 연결을 끊은 상태입니다.
    /// </summary>
    public bool CheckConnectionHealth()
    {
        if (_tcp == null || !_tcp.Connected) return false;
        try
        {
            var socket = _tcp.Client;
            return !(socket.Poll(1, System.Net.Sockets.SelectMode.SelectRead)
                     && socket.Available == 0);
        }
        catch { return false; }
    }

    private static byte Hi(ushort v) => (byte)(v >> 8);
    private static byte Lo(ushort v) => (byte)(v & 0xFF);

    public void Dispose()
    {
        Disconnect();
        _lock.Dispose();
    }
}
