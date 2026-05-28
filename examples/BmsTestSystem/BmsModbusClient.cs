using MAK_Modbus.Core;

namespace BmsTestSystem;

/// <summary>
/// IModbusClient 전용 BMS 래퍼. 두 가지 핵심 책임을 담당합니다.
///
/// [1] 청크 분할 읽기
///     Modbus FC03은 요청당 최대 125개 레지스터만 허용합니다.
///     512개 셀 전압처럼 대용량 요청을 자동으로 분할·병합합니다.
///
/// [2] 엔디안 처리 가이드
///     ModbusTcpClient 내부의 ParseRegisterResponse가 이미 Big-Endian 바이트를
///     ushort로 올바르게 변환합니다: result[i] = (Hi << 8) | Lo
///     따라서 단일 16비트 레지스터 값에는 추가 바이트 스왑이 필요 없습니다.
///     단, 연속 2개 레지스터를 묶어 32비트 값(Float/UInt)으로 해석할 때는
///     워드 순서(Big-Endian Word Order)를 고려해야 합니다.
///     → ToFloat32 / ToUInt32 헬퍼를 사용하세요.
/// </summary>
public sealed class BmsModbusClient : IAsyncDisposable
{
    // Modbus 스펙: FC03 단일 요청 최대 레지스터 수
    private const int MaxRegistersPerRequest = 125;

    private readonly IModbusClient _inner;
    private readonly Action<string> _errorLogger;

    public bool IsConnected => _inner.IsConnected;

    /// <param name="client">IModbusClient 구현체</param>
    /// <param name="errorLogger">
    ///   오류 메시지 수신 콜백. null이면 Console.Error에 출력.
    ///   WPF에서는 Dispatcher.Invoke로 감싼 UI 업데이트 메서드를 전달하세요.
    /// </param>
    public BmsModbusClient(IModbusClient client, Action<string>? errorLogger = null)
    {
        _inner       = client;
        _errorLogger = errorLogger ?? (msg => Console.Error.WriteLine(msg));
    }

    // ── Factory ──────────────────────────────────────────────────────────────

    /// <summary>간편 생성자. IP/Port/Timeout으로 바로 인스턴스 생성.</summary>
    public static BmsModbusClient Create(
        string ip, int port = 502, int timeoutMs = 3000,
        Action<string>? errorLogger = null)
        => new(new ModbusTcpClient(ip, port, timeoutMs), errorLogger);

    // ── Connection ───────────────────────────────────────────────────────────

    public Task ConnectAsync(CancellationToken ct = default) => _inner.ConnectAsync(ct);
    public void Disconnect() => _inner.Disconnect();

    // ── Write ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 단일 Holding Register 쓰기 (FC06).
    /// 성공 true / 통신 오류 false 반환. 예외를 던지지 않습니다.
    /// </summary>
    public async Task<bool> WriteRegisterAsync(
        byte slaveId, ushort address, ushort value,
        CancellationToken ct = default)
    {
        try
        {
            await _inner.WriteSingleRegisterAsync(slaveId, address, value, ct);
            return true;
        }
        catch (Exception ex)
        {
            LogError($"WriteRegister slaveId={slaveId} addr=0x{address:X4}", ex);
            return false;
        }
    }

    // ── Read (자동 청크 분할) ────────────────────────────────────────────────

    /// <summary>
    /// Holding Register 읽기 (FC03). length &gt; 125이면 자동으로 청크 분할합니다.
    /// 반환: (Success=true, ushort[length]) 또는 (Success=false, empty[]).
    /// </summary>
    public async Task<(bool Success, ushort[] Data)> ReadRegistersAsync(
        byte slaveId, ushort startAddress, int length,
        CancellationToken ct = default)
    {
        var buffer = new List<ushort>(length);
        int remaining = length;
        ushort currentAddress = startAddress;

        try
        {
            while (remaining > 0)
            {
                ushort chunkSize = (ushort)Math.Min(remaining, MaxRegistersPerRequest);
                var chunk = await _inner.ReadHoldingRegistersAsync(slaveId, currentAddress, chunkSize, ct);
                buffer.AddRange(chunk);

                // ushort 덧셈 오버플로 방지를 위해 int 캐스팅 후 재할당
                currentAddress = (ushort)(currentAddress + chunkSize);
                remaining -= chunkSize;
            }

            return (true, buffer.ToArray());
        }
        catch (Exception ex)
        {
            LogError($"ReadRegisters slaveId={slaveId} addr=0x{startAddress:X4} len={length}", ex);
            return (false, Array.Empty<ushort>());
        }
    }

    // ── 32비트 타입 변환 헬퍼 (Big-Endian Word Order) ────────────────────────

    /// <summary>
    /// 연속 2개 레지스터 → IEEE-754 Float32.
    /// BMS 장비 기준: reg[n]=High Word, reg[n+1]=Low Word (Big-Endian Word Order).
    /// 내부적으로 Array.Reverse()를 통해 BitConverter(LE) 요건을 충족합니다.
    /// </summary>
    public static float ToFloat32(ushort highWord, ushort lowWord)
    {
        byte[] bytes =
        {
            (byte)(highWord >> 8), (byte)(highWord & 0xFF),
            (byte)(lowWord  >> 8), (byte)(lowWord  & 0xFF)
        };
        Array.Reverse(bytes); // Big-Endian → Little-Endian (BitConverter 요구사항)
        return BitConverter.ToSingle(bytes, 0);
    }

    /// <summary>연속 2개 레지스터 → UInt32 (Big-Endian Word Order).</summary>
    public static uint ToUInt32(ushort highWord, ushort lowWord)
    {
        byte[] bytes =
        {
            (byte)(highWord >> 8), (byte)(highWord & 0xFF),
            (byte)(lowWord  >> 8), (byte)(lowWord  & 0xFF)
        };
        Array.Reverse(bytes);
        return BitConverter.ToUInt32(bytes, 0);
    }

    // ── Internals ────────────────────────────────────────────────────────────

    private void LogError(string context, Exception ex)
        => _errorLogger($"[BmsModbusClient] ERROR {context}: {ex.Message}");

    public async ValueTask DisposeAsync()
    {
        _inner.Disconnect();
        _inner.Dispose();
        await ValueTask.CompletedTask;
    }
}
