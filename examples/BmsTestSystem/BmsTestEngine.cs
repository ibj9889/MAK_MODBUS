using BmsTestSystem.Models;
using System.Text.Json;

namespace BmsTestSystem;

/// <summary>
/// JSON 기반 Modbus 명령 실행 엔진.
///
/// sequence.json에서 ModbusCommandSet을 로드하고 명령을 순서대로 실행합니다.
/// 판정 로직 없이 Read/Write 명령을 그대로 전송하고 결과를 수집합니다.
/// </summary>
public sealed class BmsTestEngine
{
    private readonly BmsModbusClient _client;
    private readonly Action<string>  _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <param name="client">BmsModbusClient 인스턴스</param>
    /// <param name="logger">로그 콜백. null이면 Console.WriteLine 사용.</param>
    public BmsTestEngine(BmsModbusClient client, Action<string>? logger = null)
    {
        _client = client;
        _logger = logger ?? Console.WriteLine;
    }

    // ─── Public Entry Point ──────────────────────────────────────────────────

    /// <param name="jsonFilePath">sequence.json 경로</param>
    /// <param name="progress">
    ///   명령 완료 시마다 호출되는 콜백.
    ///   IProgress&lt;T&gt;는 캡처된 SynchronizationContext에서 실행되므로
    ///   WPF에서 Dispatcher.Invoke 없이 UI를 직접 업데이트할 수 있습니다.
    /// </param>
    /// <param name="ct">취소 토큰</param>
    public async Task<List<CommandResult>> RunCommandsAsync(
        string jsonFilePath,
        IProgress<CommandResult>? progress = null,
        CancellationToken ct = default)
    {
        var set = LoadCommandSet(jsonFilePath);

        Log(Bar('='));
        Log($"  명령 세트 : {set.Name}");
        Log($"  명령 수   : {set.Commands.Count}개");
        Log(Bar('='));

        var results = new List<CommandResult>(set.Commands.Count);

        foreach (var cmd in set.Commands)
        {
            if (ct.IsCancellationRequested)
            {
                Log("\n[Engine] CancellationToken 수신 — 실행을 중단합니다.");
                break;
            }

            Log($"\n[CMD] {cmd.Name}");

            var result = await ExecuteCommandAsync(cmd, ct);
            results.Add(result);

            Log($"  >> {(result.Success ? "OK" : "FAIL")} | {result.Message}");

            progress?.Report(result);

            if (cmd.DelayAfterMs > 0)
                await Task.Delay(cmd.DelayAfterMs, ct);

            if (!result.Success)
            {
                Log("\n[Engine] 통신 실패 — 실행을 중단합니다.");
                break;
            }
        }

        PrintSummary(results, set.Commands.Count);
        return results;
    }

    // ─── Command Execution ───────────────────────────────────────────────────

    private async Task<CommandResult> ExecuteCommandAsync(ModbusCommand cmd, CancellationToken ct)
    {
        var result = new CommandResult { Name = cmd.Name, Action = cmd.Action };
        try
        {
            ushort address = (ushort)ParseHexOrDec(cmd.Address);

            switch (cmd.Action.ToUpperInvariant())
            {
                case "WRITE":
                {
                    bool ok = await _client.WriteRegisterAsync(cmd.SlaveID, address, cmd.Value, ct);
                    result.Success = ok;
                    result.Message = ok
                        ? $"Write OK  addr=0x{address:X4}  val=0x{cmd.Value:X4}"
                        : $"Write FAIL  addr=0x{address:X4}";
                    break;
                }
                case "READ":
                {
                    var (ok, raw) = await _client.ReadRegistersAsync(cmd.SlaveID, address, cmd.Length, ct);
                    result.Success = ok;
                    result.RawData = ok ? raw : null;
                    result.Message = ok
                        ? BuildReadMessage(address, cmd.Length, raw)
                        : $"Read FAIL  addr=0x{address:X4}  len={cmd.Length}";
                    break;
                }
                default:
                {
                    result.Success = false;
                    result.Message = $"알 수 없는 Action: '{cmd.Action}'";
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            result.Success = false;
            result.Message = "작업이 취소되었습니다.";
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"예외 발생: {ex.Message}";
        }

        return result;
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static ModbusCommandSet LoadCommandSet(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"명령 파일을 찾을 수 없습니다: {path}");

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<ModbusCommandSet>(json, JsonOpts)
               ?? throw new InvalidDataException($"JSON 역직렬화 실패: {path}");
    }

    private static string BuildReadMessage(ushort address, int length, ushort[] raw) =>
        length == 1
            ? $"Read OK  addr=0x{address:X4}  raw=0x{raw[0]:X4} ({raw[0]})"
            : $"Read OK  addr=0x{address:X4}  len={length}  first=0x{raw[0]:X4}  last=0x{raw[^1]:X4}";

    private static int ParseHexOrDec(string s)
    {
        s = s.Trim();
        return s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? Convert.ToInt32(s, 16)
            : int.Parse(s);
    }

    private void Log(string msg) => _logger(msg);

    private void PrintSummary(List<CommandResult> results, int total)
    {
        Log("\n" + Bar('='));
        Log("  RESULT SUMMARY");
        Log(Bar('='));

        foreach (var r in results)
            Log($"  {r.Name,-40} {(r.Success ? "OK" : "FAIL")}");

        if (results.Count < total)
            Log($"  (중단 — 미실행 {total - results.Count}개)");

        int ok   = results.Count(r => r.Success);
        int fail = results.Count - ok;

        Log(Bar('-'));
        Log($"  OK={ok}  FAIL={fail}  실행={results.Count}/{total}");
        Log(Bar('='));
    }

    private static string Bar(char c) => new string(c, 64);
}
