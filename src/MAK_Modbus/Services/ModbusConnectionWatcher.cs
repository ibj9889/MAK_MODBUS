using MAK_Modbus.Core;
using MAK_Modbus.Models;

namespace MAK_Modbus.Services;

/// <summary>
/// Modbus 연결 상태를 주기적으로 감시하고, 끊기면 자동으로 재연결을 시도합니다.
/// </summary>
public sealed class ModbusConnectionWatcher : IDisposable
{
    private readonly IModbusClient _client;
    private readonly Func<Task> _reconnectAction;
    private CancellationTokenSource? _cts;
    private bool _wasConnected;

    // ─── 설정 ────────────────────────────────────────────────────────────────

    /// <summary>연결 상태 체크 주기 (기본값 2초). 짧을수록 끊김을 빨리 감지합니다.</summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>재연결 시도 간격 (기본값 5초)</summary>
    public TimeSpan RetryInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 최대 재연결 시도 횟수. -1이면 무한 재시도 (기본값 -1).
    /// TotalRetryTimeout과 함께 사용하면 먼저 도달한 조건으로 중단됩니다.
    /// </summary>
    public int MaxRetryCount { get; set; } = -1;

    /// <summary>
    /// 재연결을 포기하기까지의 총 허용 시간. null이면 시간 제한 없음 (기본값 null).
    /// 예: TimeSpan.FromMinutes(2) → 2분 동안 계속 실패하면 포기
    /// </summary>
    public TimeSpan? TotalRetryTimeout { get; set; } = null;

    // ─── 상태 ────────────────────────────────────────────────────────────────

    public bool IsWatching { get; private set; }

    /// <summary>현재까지 재연결 시도 횟수</summary>
    public int CurrentRetryCount { get; private set; }

    /// <summary>연결이 끊긴 시각 (재연결 성공 시 null로 초기화)</summary>
    public DateTime? DisconnectedAt { get; private set; }

    // ─── 이벤트 ──────────────────────────────────────────────────────────────

    /// <summary>연결이 끊겼을 때 발생</summary>
    public event EventHandler<ModbusConnectionEventArgs>? ConnectionLost;

    /// <summary>재연결에 성공했을 때 발생</summary>
    public event EventHandler<ModbusConnectionEventArgs>? Reconnected;

    /// <summary>모든 재연결 시도가 실패했을 때 발생 (MaxRetryCount 초과)</summary>
    public event EventHandler<ModbusConnectionEventArgs>? ReconnectFailed;

    /// <summary>재연결 시도 중 발생하는 오류 알림</summary>
    public event EventHandler<ModbusErrorEventArgs>? RetryError;

    // ─── 생성자 ──────────────────────────────────────────────────────────────

    /// <param name="client">감시할 Modbus 클라이언트</param>
    /// <param name="reconnectAction">재연결 시 호출할 함수. null이면 client.ConnectAsync() 사용</param>
    public ModbusConnectionWatcher(IModbusClient client, Func<Task>? reconnectAction = null)
    {
        _client = client;
        _reconnectAction = reconnectAction ?? (() => client.ConnectAsync());
    }

    // ─── 시작 / 중지 ─────────────────────────────────────────────────────────

    /// <summary>연결 감시 시작</summary>
    public void Start()
    {
        if (IsWatching) return;
        _cts = new CancellationTokenSource();
        _wasConnected = _client.IsConnected;
        IsWatching = true;
        CurrentRetryCount = 0;
        DisconnectedAt = null;
        _ = WatchLoopAsync(_cts.Token);
    }

    /// <summary>연결 감시 중지</summary>
    public void Stop()
    {
        IsWatching = false;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    // ─── 감시 루프 ────────────────────────────────────────────────────────────

    private async Task WatchLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(HeartbeatInterval);
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            try
            {
                var isAlive = _client.CheckConnectionHealth();

                if (_wasConnected && !isAlive)
                {
                    // 연결이 살아있었는데 끊김 감지
                    _wasConnected = false;
                    DisconnectedAt = DateTime.Now;
                    ConnectionLost?.Invoke(this,
                        new ModbusConnectionEventArgs(false, "연결이 끊어졌습니다. 재연결을 시도합니다."));

                    await ReconnectLoopAsync(ct).ConfigureAwait(false);
                }
                else if (!_wasConnected && isAlive)
                {
                    // 외부에서 재연결된 경우 감지
                    _wasConnected = true;
                    CurrentRetryCount = 0;
                    DisconnectedAt = null;
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                RetryError?.Invoke(this, new ModbusErrorEventArgs(ex));
            }
        }
    }

    private async Task ReconnectLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            // 최대 횟수 초과 확인
            if (MaxRetryCount >= 0 && CurrentRetryCount >= MaxRetryCount)
            {
                ReconnectFailed?.Invoke(this, new ModbusConnectionEventArgs(false,
                    $"재연결 포기: 최대 시도 횟수 {MaxRetryCount}회 초과"));
                Stop();
                return;
            }

            // 총 허용 시간 초과 확인
            if (TotalRetryTimeout.HasValue && DisconnectedAt.HasValue)
            {
                var elapsed = DateTime.Now - DisconnectedAt.Value;
                if (elapsed >= TotalRetryTimeout.Value)
                {
                    ReconnectFailed?.Invoke(this, new ModbusConnectionEventArgs(false,
                        $"재연결 포기: 허용 시간 {TotalRetryTimeout.Value.TotalSeconds}초 초과 " +
                        $"(경과: {elapsed.TotalSeconds:F0}초)"));
                    Stop();
                    return;
                }
            }

            await Task.Delay(RetryInterval, ct).ConfigureAwait(false);
            CurrentRetryCount++;

            try
            {
                await _reconnectAction().ConfigureAwait(false);

                if (_client.IsConnected)
                {
                    _wasConnected = true;
                    DisconnectedAt = null;
                    CurrentRetryCount = 0;
                    Reconnected?.Invoke(this, new ModbusConnectionEventArgs(true, "재연결 성공!"));
                    return;
                }
            }
            catch (Exception ex)
            {
                RetryError?.Invoke(this, new ModbusErrorEventArgs(ex,
                    $"재연결 시도 {CurrentRetryCount}회 실패: {ex.Message}"));
            }
        }
    }

    public void Dispose() => Stop();
}
