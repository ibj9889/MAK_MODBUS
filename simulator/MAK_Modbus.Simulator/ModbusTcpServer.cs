using System.Net;
using System.Net.Sockets;

namespace MAK_Modbus.Simulator;

/// <summary>
/// Modbus TCP 서버 (슬레이브/시뮬레이터).
/// 실제 PLC 없이 통신 테스트에 사용합니다.
/// </summary>
public sealed class ModbusTcpServer : IDisposable
{
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private readonly object _dataLock = new();

    // ─── 메모리 뱅크 ─────────────────────────────────────────────────────────
    private readonly ushort[] _holdingRegisters  = new ushort[65536];
    private readonly ushort[] _inputRegisters    = new ushort[65536];
    private readonly bool[]   _coils             = new bool[65536];
    private readonly bool[]   _discreteInputs    = new bool[65536];

    // ─── 속성 ────────────────────────────────────────────────────────────────
    public int Port { get; }
    public bool IsRunning { get; private set; }
    public int ConnectedClients { get; private set; }

    // ─── 이벤트 ──────────────────────────────────────────────────────────────
    /// <summary>클라이언트로부터 요청을 받을 때마다 발생</summary>
    public event EventHandler<ServerRequestEventArgs>? RequestReceived;

    /// <summary>레지스터/코일 값이 변경될 때 발생</summary>
    public event EventHandler<RegisterChangedEventArgs>? DataChanged;

    /// <summary>클라이언트 연결/해제 시 발생</summary>
    public event EventHandler<string>? ClientConnectionChanged;

    public ModbusTcpServer(int port = 5020)
    {
        Port = port;
    }

    // ─── 시작 / 중지 ─────────────────────────────────────────────────────────

    public void Start()
    {
        if (IsRunning) return;
        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Any, Port);
        _listener.Start();
        IsRunning = true;
        _ = AcceptClientsAsync(_cts.Token);
    }

    public void Stop()
    {
        IsRunning = false;
        _cts?.Cancel();
        _listener?.Stop();
        ConnectedClients = 0;
    }

    // ─── 데이터 접근 (외부에서 시뮬레이터 값 설정) ────────────────────────────

    public ushort GetHoldingRegister(ushort address)
    { lock (_dataLock) return _holdingRegisters[address]; }

    public void SetHoldingRegister(ushort address, ushort value)
    { lock (_dataLock) _holdingRegisters[address] = value; }

    public ushort[] GetHoldingRegisters(ushort start, ushort count)
    {
        lock (_dataLock)
        {
            var r = new ushort[count];
            Array.Copy(_holdingRegisters, start, r, 0, count);
            return r;
        }
    }

    public void SetHoldingRegisters(ushort start, ushort[] values)
    {
        lock (_dataLock)
            Array.Copy(values, 0, _holdingRegisters, start, values.Length);
        DataChanged?.Invoke(this, new RegisterChangedEventArgs("HoldingRegister", start, (ushort)values.Length));
    }

    public ushort GetInputRegister(ushort address)
    { lock (_dataLock) return _inputRegisters[address]; }

    public void SetInputRegister(ushort address, ushort value)
    { lock (_dataLock) _inputRegisters[address] = value; }

    public void SetInputRegisters(ushort start, ushort[] values)
    {
        lock (_dataLock)
            Array.Copy(values, 0, _inputRegisters, start, values.Length);
        DataChanged?.Invoke(this, new RegisterChangedEventArgs("InputRegister", start, (ushort)values.Length));
    }

    public bool GetCoil(ushort address)
    { lock (_dataLock) return _coils[address]; }

    public void SetCoil(ushort address, bool value)
    { lock (_dataLock) _coils[address] = value; }

    public bool GetDiscreteInput(ushort address)
    { lock (_dataLock) return _discreteInputs[address]; }

    public void SetDiscreteInput(ushort address, bool value)
    { lock (_dataLock) _discreteInputs[address] = value; }

    // ─── 내부 서버 로직 ──────────────────────────────────────────────────────

    private async Task AcceptClientsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync(ct);
                ConnectedClients++;
                ClientConnectionChanged?.Invoke(this,
                    $"클라이언트 연결: {client.Client.RemoteEndPoint} (현재 {ConnectedClients}개)");
                _ = HandleClientAsync(client, ct);
            }
            catch (OperationCanceledException) { break; }
            catch { /* 서버 중지 시 무시 */ }
        }
    }

    private async Task HandleClientAsync(TcpClient tcpClient, CancellationToken ct)
    {
        var remote = tcpClient.Client.RemoteEndPoint?.ToString() ?? "unknown";
        using (tcpClient)
        {
            using var stream = tcpClient.GetStream();
            var mbap = new byte[7];

            while (!ct.IsCancellationRequested)
            {
                // MBAP 헤더 읽기 (7 bytes)
                if (!await TryReadExactAsync(stream, mbap, ct)) break;

                var transId  = (ushort)((mbap[0] << 8) | mbap[1]);
                var pduLen   = ((mbap[4] << 8) | mbap[5]) - 1;
                var unitId   = mbap[6];

                if (pduLen <= 0) break;

                var pdu = new byte[pduLen];
                if (!await TryReadExactAsync(stream, pdu, ct)) break;

                var responsePdu = ProcessRequest(pdu, unitId);

                // MBAP 응답 빌드
                var respBodyLen = (ushort)(1 + responsePdu.Length);
                var response = new byte[7 + responsePdu.Length];
                response[0] = mbap[0]; response[1] = mbap[1]; // Transaction ID echo
                response[2] = 0x00;    response[3] = 0x00;    // Protocol ID
                response[4] = (byte)(respBodyLen >> 8);
                response[5] = (byte)(respBodyLen & 0xFF);
                response[6] = unitId;
                Array.Copy(responsePdu, 0, response, 7, responsePdu.Length);

                try { await stream.WriteAsync(response, ct); }
                catch { break; }
            }
        }

        ConnectedClients = Math.Max(0, ConnectedClients - 1);
        ClientConnectionChanged?.Invoke(this,
            $"클라이언트 해제: {remote} (현재 {ConnectedClients}개)");
    }

    private byte[] ProcessRequest(byte[] pdu, byte unitId)
    {
        var fc = pdu[0];
        try
        {
            return fc switch
            {
                0x01 => FC01_ReadCoils(pdu, unitId),
                0x02 => FC02_ReadDiscreteInputs(pdu, unitId),
                0x03 => FC03_ReadHoldingRegisters(pdu, unitId),
                0x04 => FC04_ReadInputRegisters(pdu, unitId),
                0x05 => FC05_WriteSingleCoil(pdu, unitId),
                0x06 => FC06_WriteSingleRegister(pdu, unitId),
                0x0F => FC0F_WriteMultipleCoils(pdu, unitId),
                0x10 => FC10_WriteMultipleRegisters(pdu, unitId),
                _    => new byte[] { (byte)(fc | 0x80), 0x01 } // Illegal function
            };
        }
        catch
        {
            return new byte[] { (byte)(fc | 0x80), 0x04 };
        }
    }

    // FC01
    private byte[] FC01_ReadCoils(byte[] pdu, byte unitId)
    {
        var start = (ushort)((pdu[1] << 8) | pdu[2]);
        var count = (ushort)((pdu[3] << 8) | pdu[4]);
        var byteCount = (byte)((count + 7) / 8);
        var data = new byte[byteCount];
        lock (_dataLock)
            for (int i = 0; i < count; i++)
                if (_coils[start + i]) data[i / 8] |= (byte)(1 << (i % 8));
        RequestReceived?.Invoke(this, new ServerRequestEventArgs(unitId, 0x01, start, count, "READ Coils"));
        var resp = new byte[2 + byteCount];
        resp[0] = 0x01; resp[1] = byteCount;
        Array.Copy(data, 0, resp, 2, byteCount);
        return resp;
    }

    // FC02
    private byte[] FC02_ReadDiscreteInputs(byte[] pdu, byte unitId)
    {
        var start = (ushort)((pdu[1] << 8) | pdu[2]);
        var count = (ushort)((pdu[3] << 8) | pdu[4]);
        var byteCount = (byte)((count + 7) / 8);
        var data = new byte[byteCount];
        lock (_dataLock)
            for (int i = 0; i < count; i++)
                if (_discreteInputs[start + i]) data[i / 8] |= (byte)(1 << (i % 8));
        RequestReceived?.Invoke(this, new ServerRequestEventArgs(unitId, 0x02, start, count, "READ DiscreteInput"));
        var resp = new byte[2 + byteCount];
        resp[0] = 0x02; resp[1] = byteCount;
        Array.Copy(data, 0, resp, 2, byteCount);
        return resp;
    }

    // FC03
    private byte[] FC03_ReadHoldingRegisters(byte[] pdu, byte unitId)
    {
        var start = (ushort)((pdu[1] << 8) | pdu[2]);
        var count = (ushort)((pdu[3] << 8) | pdu[4]);
        var byteCount = (byte)(count * 2);
        var resp = new byte[2 + byteCount];
        resp[0] = 0x03; resp[1] = byteCount;
        lock (_dataLock)
            for (int i = 0; i < count; i++)
            {
                resp[2 + i * 2]     = (byte)(_holdingRegisters[start + i] >> 8);
                resp[2 + i * 2 + 1] = (byte)(_holdingRegisters[start + i] & 0xFF);
            }
        RequestReceived?.Invoke(this, new ServerRequestEventArgs(unitId, 0x03, start, count, "READ HoldingReg"));
        return resp;
    }

    // FC04
    private byte[] FC04_ReadInputRegisters(byte[] pdu, byte unitId)
    {
        var start = (ushort)((pdu[1] << 8) | pdu[2]);
        var count = (ushort)((pdu[3] << 8) | pdu[4]);
        var byteCount = (byte)(count * 2);
        var resp = new byte[2 + byteCount];
        resp[0] = 0x04; resp[1] = byteCount;
        lock (_dataLock)
            for (int i = 0; i < count; i++)
            {
                resp[2 + i * 2]     = (byte)(_inputRegisters[start + i] >> 8);
                resp[2 + i * 2 + 1] = (byte)(_inputRegisters[start + i] & 0xFF);
            }
        RequestReceived?.Invoke(this, new ServerRequestEventArgs(unitId, 0x04, start, count, "READ InputReg"));
        return resp;
    }

    // FC05
    private byte[] FC05_WriteSingleCoil(byte[] pdu, byte unitId)
    {
        var addr  = (ushort)((pdu[1] << 8) | pdu[2]);
        var value = pdu[3] == 0xFF;
        lock (_dataLock) _coils[addr] = value;
        RequestReceived?.Invoke(this, new ServerRequestEventArgs(unitId, 0x05, addr, 1, $"WRITE Coil={value}"));
        DataChanged?.Invoke(this, new RegisterChangedEventArgs("Coil", addr, 1));
        return pdu; // Echo
    }

    // FC06
    private byte[] FC06_WriteSingleRegister(byte[] pdu, byte unitId)
    {
        var addr  = (ushort)((pdu[1] << 8) | pdu[2]);
        var value = (ushort)((pdu[3] << 8) | pdu[4]);
        lock (_dataLock) _holdingRegisters[addr] = value;
        RequestReceived?.Invoke(this, new ServerRequestEventArgs(unitId, 0x06, addr, 1, $"WRITE Reg={value}"));
        DataChanged?.Invoke(this, new RegisterChangedEventArgs("HoldingRegister", addr, 1));
        return pdu; // Echo
    }

    // FC0F
    private byte[] FC0F_WriteMultipleCoils(byte[] pdu, byte unitId)
    {
        var start = (ushort)((pdu[1] << 8) | pdu[2]);
        var count = (ushort)((pdu[3] << 8) | pdu[4]);
        lock (_dataLock)
            for (int i = 0; i < count; i++)
                _coils[start + i] = (pdu[6 + i / 8] & (1 << (i % 8))) != 0;
        RequestReceived?.Invoke(this, new ServerRequestEventArgs(unitId, 0x0F, start, count, "WRITE MultiCoils"));
        DataChanged?.Invoke(this, new RegisterChangedEventArgs("Coil", start, count));
        return new byte[] { 0x0F, pdu[1], pdu[2], pdu[3], pdu[4] };
    }

    // FC10
    private byte[] FC10_WriteMultipleRegisters(byte[] pdu, byte unitId)
    {
        var start = (ushort)((pdu[1] << 8) | pdu[2]);
        var count = (ushort)((pdu[3] << 8) | pdu[4]);
        lock (_dataLock)
            for (int i = 0; i < count; i++)
                _holdingRegisters[start + i] = (ushort)((pdu[6 + i * 2] << 8) | pdu[6 + i * 2 + 1]);
        RequestReceived?.Invoke(this, new ServerRequestEventArgs(unitId, 0x10, start, count, "WRITE MultiRegs"));
        DataChanged?.Invoke(this, new RegisterChangedEventArgs("HoldingRegister", start, count));
        return new byte[] { 0x10, pdu[1], pdu[2], pdu[3], pdu[4] };
    }

    private static async Task<bool> TryReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read;
            try { read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), ct); }
            catch { return false; }
            if (read == 0) return false;
            offset += read;
        }
        return true;
    }

    public void Dispose() => Stop();
}
