using BmsTestSystem.Models;
using System.Text.Json;

namespace BmsTestSystem;

/// <summary>
/// JSON 기반 동적 검사 엔진 (Dynamic Test Engine).
///
/// 실행 흐름:
///   1. sequence.json → TestSequence 역직렬화
///   2. 각 TestStep을 순서대로 실행
///      - Write: BmsModbusClient.WriteRegisterAsync
///      - Read : BmsModbusClient.ReadRegistersAsync → Coefficient 적용 → 판정
///   3. 판정 결과(Pass/Fail/Error) 수집 및 최종 요약 출력
///
/// 통신 오류(Error) 발생 시 후속 스텝 없이 시퀀스를 즉시 중단합니다.
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
    /// <param name="logger">
    ///   로그 메시지 수신 콜백. null이면 Console.WriteLine에 출력.
    ///   WPF에서는 Dispatcher.Invoke로 감싼 TextBox 업데이트 메서드를 전달하세요.
    /// </param>
    public BmsTestEngine(BmsModbusClient client, Action<string>? logger = null)
    {
        _client = client;
        _logger = logger ?? Console.WriteLine;
    }

    // ─── Public Entry Point ──────────────────────────────────────────────────

    /// <param name="jsonFilePath">sequence.json 경로</param>
    /// <param name="progress">
    ///   스텝 완료 시마다 호출되는 콜백. null이면 비활성.
    ///   IProgress&lt;T&gt;는 캡처된 SynchronizationContext에서 실행되므로
    ///   WPF에서 Dispatcher.Invoke 없이 UI를 직접 업데이트할 수 있습니다.
    /// </param>
    /// <param name="ct">취소 토큰</param>
    public async Task<List<StepResult>> RunSequenceAsync(
        string jsonFilePath,
        IProgress<StepResult>? progress = null,
        CancellationToken ct = default)
    {
        var sequence = LoadSequence(jsonFilePath);

        Log(Bar('='));
        Log($"  시퀀스 : {sequence.SequenceName}  (v{sequence.Version})");
        Log($"  스텝 수 : {sequence.Steps.Count}개");
        Log(Bar('='));

        var results = new List<StepResult>(sequence.Steps.Count);

        foreach (var step in sequence.Steps)
        {
            if (ct.IsCancellationRequested)
            {
                Log("\n[Engine] CancellationToken 수신 — 시퀀스를 중단합니다.");
                break;
            }

            Log($"\n[Step {step.StepIndex:D2}] {step.StepName}");

            var result = await ExecuteStepAsync(step, ct);
            results.Add(result);

            Log($"  >> {result.Judgment,-5} | {result.Message}");

            // WPF DataGrid / ListView 실시간 업데이트 트리거
            progress?.Report(result);

            if (step.DelayAfterMs > 0)
                await Task.Delay(step.DelayAfterMs, ct);

            if (result.Judgment == StepJudgment.Error)
            {
                Log("\n[Engine] 통신 오류 감지 — 시퀀스를 중단합니다.");
                break;
            }
        }

        PrintSummary(results, sequence.Steps.Count);
        return results;
    }

    // ─── Step Dispatch ───────────────────────────────────────────────────────

    private async Task<StepResult> ExecuteStepAsync(TestStep step, CancellationToken ct)
    {
        var result = new StepResult { StepIndex = step.StepIndex, StepName = step.StepName };
        try
        {
            ushort address = (ushort)ParseHexOrDec(step.Address);

            return step.Action.ToUpperInvariant() switch
            {
                "WRITE" => await ExecuteWriteAsync(step, result, address, ct),
                "READ"  => await ExecuteReadAsync (step, result, address, ct),
                _       => Finish(result, StepJudgment.Skip, $"알 수 없는 Action: '{step.Action}'")
            };
        }
        catch (OperationCanceledException)
        {
            return Finish(result, StepJudgment.Error, "작업이 취소되었습니다.");
        }
        catch (Exception ex)
        {
            return Finish(result, StepJudgment.Error, $"예외 발생: {ex.Message}");
        }
    }

    // ─── Write ───────────────────────────────────────────────────────────────

    private async Task<StepResult> ExecuteWriteAsync(
        TestStep step, StepResult result, ushort address, CancellationToken ct)
    {
        bool ok = await _client.WriteRegisterAsync(step.SlaveID, address, step.WriteValue, ct);

        return Finish(result,
            ok ? StepJudgment.Pass : StepJudgment.Fail,
            ok
                ? $"Write OK   addr=0x{address:X4}  val=0x{step.WriteValue:X4}"
                : $"Write FAIL addr=0x{address:X4}");
    }

    // ─── Read + Coefficient + Judgment ───────────────────────────────────────

    private async Task<StepResult> ExecuteReadAsync(
        TestStep step, StepResult result, ushort address, CancellationToken ct)
    {
        var (ok, raw) = await _client.ReadRegistersAsync(step.SlaveID, address, step.Length, ct);

        if (!ok)
            return Finish(result, StepJudgment.Error,
                $"Read FAIL  addr=0x{address:X4}  len={step.Length}");

        // Coefficient 적용 → 실측값 배열
        double[] scaled = Array.ConvertAll(raw, r => r * step.Coefficient);
        result.MeasuredValues = scaled;

        // 판정 기준이 없으면 무조건 Pass (계측 전용 스텝)
        if (step.Judgment is null ||
            step.Judgment.Type.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            return Finish(result, StepJudgment.Pass, BuildReadSummary(scaled, step.Unit));
        }

        return ApplyJudgment(step, result, raw, scaled);
    }

    // ─── Judgment Dispatch ───────────────────────────────────────────────────

    private static StepResult ApplyJudgment(
        TestStep step, StepResult result, ushort[] raw, double[] scaled)
    {
        return step.Judgment!.Type.ToUpperInvariant() switch
        {
            "RANGE"   => JudgeRange  (step, result, scaled),
            "BITMASK" => JudgeBitmask(step, result, raw),
            _         => Finish(result, StepJudgment.Skip,
                             $"알 수 없는 판정 타입: '{step.Judgment.Type}'")
        };
    }

    // ─── Range 판정 ──────────────────────────────────────────────────────────

    /// <summary>
    /// LimitMin ≤ 모든 실측값 ≤ LimitMax 이면 PASS.
    /// 첫 번째 이탈 값 발견 즉시 FAIL 반환 (512셀 전압 검사 성능 최적화).
    /// </summary>
    private static StepResult JudgeRange(TestStep step, StepResult result, double[] scaled)
    {
        var j = step.Judgment!;

        for (int i = 0; i < scaled.Length; i++)
        {
            bool inRange = (!j.LimitMin.HasValue || scaled[i] >= j.LimitMin.Value)
                        && (!j.LimitMax.HasValue || scaled[i] <= j.LimitMax.Value);

            if (!inRange)
            {
                return Finish(result, StepJudgment.Fail,
                    $"FAIL  idx={i}  val={scaled[i]:F3} {step.Unit}" +
                    $"  허용=[{j.LimitMin}~{j.LimitMax}]");
            }
        }

        string passMsg = scaled.Length == 1
            ? $"PASS  val={scaled[0]:F3} {step.Unit}  [{j.LimitMin}~{j.LimitMax}]"
            : $"PASS  n={scaled.Length}" +
              $"  min={scaled.Min():F3}  max={scaled.Max():F3} {step.Unit}" +
              $"  [{j.LimitMin}~{j.LimitMax}]";

        return Finish(result, StepJudgment.Pass, passMsg);
    }

    // ─── Bitmask 판정 ────────────────────────────────────────────────────────

    /// <summary>
    /// (raw[0] &amp; CheckMask) == ExpectedBits 이면 PASS.
    /// 예: CheckMask=0x0005, ExpectedBits=0x0000 → Bit0과 Bit2가 모두 0이어야 PASS.
    /// </summary>
    private static StepResult JudgeBitmask(TestStep step, StepResult result, ushort[] raw)
    {
        var j = step.Judgment!;
        ushort mask     = (ushort)ParseHexOrDec(j.CheckMask    ?? "0xFFFF");
        ushort expected = (ushort)ParseHexOrDec(j.ExpectedBits ?? "0x0000");
        ushort actual   = raw[0];
        ushort masked   = (ushort)(actual & mask);
        bool   pass     = masked == expected;

        return Finish(result,
            pass ? StepJudgment.Pass : StepJudgment.Fail,
            $"{(pass ? "PASS" : "FAIL")}  " +
            $"raw=0x{actual:X4}  mask=0x{mask:X4}  " +
            $"masked=0x{masked:X4}  expected=0x{expected:X4}");
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static TestSequence LoadSequence(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"시퀀스 파일을 찾을 수 없습니다: {path}");

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<TestSequence>(json, JsonOpts)
               ?? throw new InvalidDataException($"JSON 역직렬화 실패: {path}");
    }

    private static StepResult Finish(StepResult r, StepJudgment judgment, string message)
    {
        r.Judgment = judgment;
        r.Message  = message;
        return r;
    }

    private static string BuildReadSummary(double[] v, string unit) =>
        v.Length == 1
            ? $"Read OK  val={v[0]:F3} {unit}"
            : $"Read OK  n={v.Length}  min={v.Min():F3}  max={v.Max():F3} {unit}";

    private static int ParseHexOrDec(string s)
    {
        s = s.Trim();
        return s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? Convert.ToInt32(s, 16)
            : int.Parse(s);
    }

    private void Log(string msg) => _logger(msg);

    private void PrintSummary(List<StepResult> results, int totalSteps)
    {
        Log("\n" + Bar('='));
        Log("  SEQUENCE RESULT SUMMARY");
        Log(Bar('='));

        foreach (var r in results)
            Log($"  [{r.StepIndex:D2}] {r.StepName,-40} {r.Judgment}");

        if (results.Count < totalSteps)
            Log($"       (오류로 중단 — 미실행 {totalSteps - results.Count}개 스텝)");

        int pass  = results.Count(r => r.Judgment == StepJudgment.Pass);
        int fail  = results.Count(r => r.Judgment == StepJudgment.Fail);
        int error = results.Count(r => r.Judgment == StepJudgment.Error);

        Log(Bar('-'));
        Log($"  PASS={pass}  FAIL={fail}  ERROR={error}" +
            $"  실행={results.Count}/{totalSteps}");

        bool overall = fail == 0 && error == 0 && results.Count == totalSteps;
        Log($"\n  OVERALL : [ {(overall ? "PASS" : "FAIL")} ]");
        Log(Bar('='));
    }

    private static string Bar(char c) => new string(c, 64);
}
