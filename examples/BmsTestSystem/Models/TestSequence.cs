namespace BmsTestSystem.Models;

public sealed class TestSequence
{
    public string SequenceName { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0";
    public List<TestStep> Steps { get; set; } = new();
}

public sealed class TestStep
{
    public int StepIndex { get; set; }
    public string StepName { get; set; } = string.Empty;

    /// <summary>"Read" or "Write"</summary>
    public string Action { get; set; } = "Read";

    public byte SlaveID { get; set; } = 1;

    /// <summary>Hex 또는 Decimal 문자열. 예: "0x001F" 또는 "31"</summary>
    public string Address { get; set; } = "0x0000";

    /// <summary>읽을 레지스터 수 (Read 전용)</summary>
    public int Length { get; set; } = 1;

    /// <summary>쓸 값 (Write 전용)</summary>
    public ushort WriteValue { get; set; } = 0;

    /// <summary>Raw 값에 곱할 계수. 예: 0.1 → raw 1234 = 실제 123.4</summary>
    public double Coefficient { get; set; } = 1.0;

    public string Unit { get; set; } = string.Empty;

    /// <summary>스텝 완료 후 대기 시간 (ms). 릴레이 동작 안정화 등에 사용.</summary>
    public int DelayAfterMs { get; set; } = 0;

    public JudgmentCriteria? Judgment { get; set; }
}

public sealed class JudgmentCriteria
{
    /// <summary>"None" | "Range" | "Bitmask"</summary>
    public string Type { get; set; } = "None";

    // --- Range 판정 ---
    public double? LimitMin { get; set; }
    public double? LimitMax { get; set; }

    // --- Bitmask 판정: (RegisterValue & CheckMask) == ExpectedBits ---
    /// <summary>검사할 비트 마스크. Hex 문자열. 예: "0x0005" → Bit0, Bit2 검사</summary>
    public string? CheckMask { get; set; }

    /// <summary>마스킹 후 기대값. Hex 문자열. 예: "0x0000" → 해당 비트 모두 0이어야 PASS</summary>
    public string? ExpectedBits { get; set; }
}
