namespace BmsTestSystem.Models;

public enum StepJudgment { Pass, Fail, Skip, Error }

public sealed class StepResult
{
    public int StepIndex { get; set; }
    public string StepName { get; set; } = string.Empty;
    public StepJudgment Judgment { get; set; }
    public string Message { get; set; } = string.Empty;

    /// <summary>Coefficient 적용 후 실측값 배열 (Read 스텝에서만 채워짐)</summary>
    public double[]? MeasuredValues { get; set; }

    public DateTime Timestamp { get; } = DateTime.Now;

    public bool IsPass => Judgment == StepJudgment.Pass;
}
