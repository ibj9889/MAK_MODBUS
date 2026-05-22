using System.IO.Ports;
using MAK_Modbus.Helpers;
using MAK_Modbus.Models;

namespace MAK_Modbus.Core;

/// <summary>
/// Modbus RTU 클라이언트 (시리얼 포트 통신)
/// </summary>
public sealed class ModbusRtuClient : IModbusClient
{
    private readonly string _portName;
    private readonly int _baudRate;
    private readonly Parity _parity;
    private readonly int _dataBits;
    private readonly StopBits _stopBits;
    private readonly int _timeoutMs;
    private SerialPort? _serialPort;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public bool IsConnected => _serialPort?.IsOpen ?? false;
    public event EventHandler<ModbusConnectionEventArgs>? ConnectionChanged;

    /// <param name="portName">시리얼 포트 이름 (예: "COM3" 또는 "/dev/ttyUSB0")</param>
    /// <param name="baudRate">통신 속도 (기본값 9600)</param>
    /// <param name="parity">패리티 (기본값 None)</param>
    /// <param name="dataBits">데이터 비트 (기본값 8)</param>
    /// <param name="stopBits">스톱 비트 (기본값 One)</param>
    /// <param name="timeoutMs">응답 타임아웃 (ms)</param>
    public ModbusRtuClient(
        string portName,
        int baudRate = 9600,
        Parity parity = Parity.None,
        int dataBits = 8,
        StopBits stopBits = StopBits.One,
        int timeoutMs = 1000)
    {
        _portName = portName;
        _baudRate = baudRate;
        _parity = parity;
        _dataBits = dataBits;
        _stopBits = stopBits;
        _timeoutMs = timeoutMs;
    }

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        _serialPort = new SerialPort(_portName, _baudRate, _parity, _dataBits, _stopBits)
        {
            ReadTimeout = _timeoutMs,
            WriteTimeout = _timeoutMs
        };
        _serialPort.Open();
        ConnectionChanged?.Invoke(this, new ModbusConnectionEventArgs(true,
            $"RTU 연결 성공: {_portName} @ {_baudRate}bps"));
        return Task.CompletedTask;
    }

    public void Disconnect()
    {
        _serialPort?.Close();
        _serialPort?.Dispose();
        _serialPort = null;
        ConnectionChanged?.Invoke(this, new ModbusConnectionEventArgs(false, "RTU 연결 종료"));
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

    private async Task<byte[]> SendReceiveAsync(byte deviceId, byte[] pdu, CancellationToken ct)
    {
        if (_serialPort == null || !_serialPort.IsOpen)
            throw new InvalidOperationException("시리얼 포트가 열려있지 않습니다. ConnectAsync()를 먼저 호출하세요.");

        await _lock.WaitAsync(ct);
        try
        {
            _serialPort.DiscardInBuffer();
            _serialPort.DiscardOutBuffer();

            // RTU 프레임: [DeviceID][PDU][CRC_Lo][CRC_Hi]
            var frame = BuildRtuFrame(deviceId, pdu);
            await Task.Run(() => _serialPort.Write(frame, 0, frame.Length), ct);

            // 응답 읽기 (예상 응답 길이 계산)
            var fc = pdu[0];
            var expectedLength = GetExpectedResponseLength(fc, pdu);
            var response = await ReadRtuResponseAsync(expectedLength, ct);

            // CRC 검증
            if (!CrcHelper.Validate(response, response.Length))
                throw new ModbusException("RTU CRC 검증 실패");

            // 예외 응답 처리
            if ((response[1] & 0x80) != 0)
                throw new ModbusException(response[2]);

            // [DeviceID][FC][Data...][CRC_Lo][CRC_Hi] → Data 부분만 반환
            var dataLength = response.Length - 4; // DeviceID(1) + FC(1) + CRC(2)
            var data = new byte[dataLength + 2]; // FC 포함 반환 (TCP와 동일한 파서 사용)
            data[0] = response[1]; // FC
            data[1] = response[2]; // ByteCount or first data byte
            Array.Copy(response, 3, data, 2, dataLength - 1);
            return data;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<byte[]> ReadRtuResponseAsync(int expectedLength, CancellationToken ct)
    {
        var buffer = new byte[expectedLength];
        var received = 0;
        var deadline = DateTime.Now.AddMilliseconds(_timeoutMs);

        while (received < expectedLength && DateTime.Now < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (_serialPort!.BytesToRead > 0)
            {
                received += await Task.Run(
                    () => _serialPort.Read(buffer, received, expectedLength - received), ct);
            }
            else
            {
                await Task.Delay(5, ct);
            }
        }

        if (received < expectedLength)
            throw new TimeoutException($"RTU 응답 타임아웃 (수신: {received}/{expectedLength} bytes)");

        return buffer;
    }

    private static int GetExpectedResponseLength(byte fc, byte[] pdu) => fc switch
    {
        0x01 or 0x02 => 5 + (((pdu[3] << 8) | pdu[4]) + 7) / 8, // DevID+FC+ByteCount+Data+CRC
        0x03 or 0x04 => 5 + ((pdu[3] << 8) | pdu[4]) * 2,
        0x05 or 0x06 => 8,
        0x0F or 0x10 => 8,
        _ => 8
    };

    private static byte[] BuildRtuFrame(byte deviceId, byte[] pdu)
    {
        var frame = new byte[1 + pdu.Length + 2];
        frame[0] = deviceId;
        Array.Copy(pdu, 0, frame, 1, pdu.Length);
        var crc = CrcHelper.Calculate(frame, 1 + pdu.Length);
        frame[^2] = (byte)(crc & 0xFF);  // CRC Low byte first
        frame[^1] = (byte)(crc >> 8);    // CRC High byte
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

    /// <summary>시리얼 포트가 열려있는지 확인합니다.</summary>
    public bool CheckConnectionHealth() => _serialPort?.IsOpen ?? false;

    private static byte Hi(ushort v) => (byte)(v >> 8);
    private static byte Lo(ushort v) => (byte)(v & 0xFF);

    public void Dispose()
    {
        Disconnect();
        _lock.Dispose();
    }
}
