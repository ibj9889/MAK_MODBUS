namespace BmsTestSystem;

/// <summary>셀 전압 통계 결과. CalcStats()의 반환값.</summary>
public sealed class CellVoltageStats
{
    public int    Count     { get; init; }
    public double Min       { get; init; }
    public double Max       { get; init; }
    public double Average   { get; init; }
    /// <summary>Max - Min (편차 범위)</summary>
    public double Range     { get; init; }
    /// <summary>표준편차</summary>
    public double StdDev    { get; init; }
    /// <summary>최솟값 셀 인덱스 (0-based)</summary>
    public int    MinIndex  { get; init; }
    /// <summary>최댓값 셀 인덱스 (0-based)</summary>
    public int    MaxIndex  { get; init; }
    /// <summary>허용 범위 이탈 셀 수</summary>
    public int    FailCount { get; init; }
    public bool   AllPass   { get; init; }
}

public static class BmsCellCalculator
{
    // ── 전체 읽기 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 지정한 셀 수만큼 읽어 double[] 배열로 반환합니다.
    /// 125개 초과 시 BmsModbusClient가 자동으로 청크 분할합니다.
    /// 통신 실패 시 null 반환.
    /// </summary>
    public static async Task<double[]?> ReadAllCellVoltagesAsync(
        BmsModbusClient client,
        byte   slaveId        = 1,
        ushort startAddress   = 0x1401,
        int    cellCount      = 512,
        double coefficient    = 1.0,
        CancellationToken ct  = default)
    {
        var (ok, raw) = await client.ReadRegistersAsync(slaveId, startAddress, cellCount, ct);
        if (!ok) return null;

        return Array.ConvertAll(raw, r => r * coefficient);
    }

    // ── 통계 계산 (ushort 원시값 입력) ──────────────────────────────────────

    /// <summary>
    /// ushort[] 원시 레지스터 배열을 받아 coefficient를 적용한 뒤 통계를 계산합니다.
    /// limitMin/limitMax는 Pass/Fail 판정 기준입니다.
    /// </summary>
    public static CellVoltageStats CalcStats(
        ushort[] rawValues,
        double coefficient = 1.0,
        double limitMin    = 2500.0,
        double limitMax    = 4200.0)
    {
        double[] values = Array.ConvertAll(rawValues, r => r * coefficient);
        return CalcStats(values, limitMin, limitMax);
    }

    // ── 통계 계산 (double 변환값 입력) ──────────────────────────────────────

    /// <summary>
    /// 이미 계수가 적용된 double[] 배열로 통계를 계산합니다.
    /// </summary>
    public static CellVoltageStats CalcStats(
        double[] values,
        double limitMin = 2500.0,
        double limitMax = 4200.0)
    {
        if (values.Length == 0)
            return new CellVoltageStats();

        double min    = values[0];
        double max    = values[0];
        int    minIdx = 0;
        int    maxIdx = 0;
        double sum    = 0;
        int    fail   = 0;

        for (int i = 0; i < values.Length; i++)
        {
            double v = values[i];
            sum += v;
            if (v < min) { min = v; minIdx = i; }
            if (v > max) { max = v; maxIdx = i; }
            if (v < limitMin || v > limitMax) fail++;
        }

        double avg = sum / values.Length;

        double variance = 0;
        foreach (var v in values)
            variance += (v - avg) * (v - avg);

        return new CellVoltageStats
        {
            Count     = values.Length,
            Min       = min,
            Max       = max,
            Average   = avg,
            Range     = max - min,
            StdDev    = Math.Sqrt(variance / values.Length),
            MinIndex  = minIdx,
            MaxIndex  = maxIdx,
            FailCount = fail,
            AllPass   = fail == 0
        };
    }

    // ── 이탈 셀 분석 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 평균 대비 ±threshold 이상 이탈한 셀 목록을 반환합니다.
    /// 결과는 편차 절댓값 내림차순으로 정렬됩니다.
    /// </summary>
    public static List<(int Index, double Voltage, double Deviation)> FindOutliers(
        double[] values,
        double threshold = 50.0)
    {
        if (values.Length == 0)
            return new List<(int, double, double)>();

        double avg = values.Average();
        var result = new List<(int Index, double Voltage, double Deviation)>();

        for (int i = 0; i < values.Length; i++)
        {
            double dev = values[i] - avg;
            if (Math.Abs(dev) > threshold)
                result.Add((i, values[i], dev));
        }

        result.Sort((a, b) => Math.Abs(b.Deviation).CompareTo(Math.Abs(a.Deviation)));
        return result;
    }

    // ── Pass/Fail 배열 ────────────────────────────────────────────────────────

    /// <summary>
    /// 각 셀의 Pass/Fail을 bool[] 배열로 반환합니다.
    /// DataGrid 색상 조건 표시에 활용할 수 있습니다.
    /// </summary>
    public static bool[] EvaluateAll(
        double[] values,
        double limitMin = 2500.0,
        double limitMax = 4200.0)
    {
        var result = new bool[values.Length];
        for (int i = 0; i < values.Length; i++)
            result[i] = values[i] >= limitMin && values[i] <= limitMax;
        return result;
    }

    // ── SoC 추정 (단순 선형 보간) ────────────────────────────────────────────

    /// <summary>
    /// 단일 셀 전압으로 SoC(%)를 추정합니다.
    /// 정확한 SoC는 BMS에서 직접 읽는 것이 원칙이며, 이 메서드는 참고용입니다.
    /// </summary>
    public static double EstimateSoc(
        double cellVoltage_mV,
        double vMin_mV = 2800.0,
        double vMax_mV = 4200.0)
    {
        double soc = (cellVoltage_mV - vMin_mV) / (vMax_mV - vMin_mV) * 100.0;
        return Math.Clamp(soc, 0.0, 100.0);
    }

    // ── 팩 전압 합산 ─────────────────────────────────────────────────────────

    /// <summary>직렬 구성 셀 전압의 합(팩 전압)을 반환합니다.</summary>
    public static double CalcPackVoltage(double[] cellVoltages_mV)
        => cellVoltages_mV.Sum();
}
