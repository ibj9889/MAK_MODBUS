using MAK_Modbus.Core;
using MAK_Modbus.Models;

namespace MAK_Modbus.Services;

/// <summary>
/// Modbus 주기적 읽기/쓰기 서비스.
/// 여러 개의 주기 작업을 동시에 관리할 수 있습니다.
/// </summary>
public sealed class ModbusPeriodicService : IDisposable
{
    private readonly IModbusClient _client;
    private readonly Dictionary<string, CancellationTokenSource> _tasks = new();
    private readonly object _tasksLock = new();

    /// <summary>주기적 읽기 데이터 수신 시 발생</summary>
    public event EventHandler<ModbusReadEventArgs>? DataReceived;

    /// <summary>주기적 쓰기 완료 시 발생</summary>
    public event EventHandler<ModbusWriteEventArgs>? WriteCompleted;

    /// <summary>오류 발생 시 이벤트</summary>
    public event EventHandler<ModbusErrorEventArgs>? ErrorOccurred;

    public ModbusPeriodicService(IModbusClient client)
    {
        _client = client;
    }

    // ─── Periodic Read ───────────────────────────────────────────────────────

    /// <summary>
    /// 주기적 읽기 시작. TaskId가 이미 실행 중이면 먼저 중지 후 재시작합니다.
    /// </summary>
    /// <param name="config">읽기 설정 (DeviceId, StartAddress, Count, Interval 등)</param>
    /// <returns>TaskId (나중에 StopTask(taskId) 호출 시 사용)</returns>
    public string StartPeriodicRead(PeriodicReadConfig config)
    {
        StopTask(config.TaskId);

        var cts = new CancellationTokenSource();
        lock (_tasksLock) { _tasks[config.TaskId] = cts; }

        _ = RunPeriodicReadAsync(config, cts.Token);
        return config.TaskId;
    }

    private async Task RunPeriodicReadAsync(PeriodicReadConfig config, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(config.Interval);
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            try
            {
                await ReadAndFireEventAsync(config, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, new ModbusErrorEventArgs(ex, config.TaskId));
            }
        }
    }

    private async Task ReadAndFireEventAsync(PeriodicReadConfig config, CancellationToken ct)
    {
        switch (config.FunctionCode)
        {
            case ModbusFunctionCode.ReadHoldingRegisters:
            {
                var values = await _client.ReadHoldingRegistersAsync(
                    config.DeviceId, config.StartAddress, config.Count, ct);
                DataReceived?.Invoke(this, new ModbusReadEventArgs(
                    config.DeviceId, config.StartAddress, config.FunctionCode, values, config.TaskId));
                break;
            }
            case ModbusFunctionCode.ReadInputRegisters:
            {
                var values = await _client.ReadInputRegistersAsync(
                    config.DeviceId, config.StartAddress, config.Count, ct);
                DataReceived?.Invoke(this, new ModbusReadEventArgs(
                    config.DeviceId, config.StartAddress, config.FunctionCode, values, config.TaskId));
                break;
            }
            case ModbusFunctionCode.ReadCoils:
            {
                var values = await _client.ReadCoilsAsync(
                    config.DeviceId, config.StartAddress, config.Count, ct);
                DataReceived?.Invoke(this, new ModbusReadEventArgs(
                    config.DeviceId, config.StartAddress, config.FunctionCode, values, config.TaskId));
                break;
            }
            case ModbusFunctionCode.ReadDiscreteInputs:
            {
                var values = await _client.ReadDiscreteInputsAsync(
                    config.DeviceId, config.StartAddress, config.Count, ct);
                DataReceived?.Invoke(this, new ModbusReadEventArgs(
                    config.DeviceId, config.StartAddress, config.FunctionCode, values, config.TaskId));
                break;
            }
            default:
                throw new NotSupportedException($"주기 읽기에서 지원하지 않는 FC: {config.FunctionCode}");
        }
    }

    // ─── Periodic Write ──────────────────────────────────────────────────────

    /// <summary>
    /// 주기적 쓰기 시작. ReadBackAfterWrite=true 설정 시 쓰기 후 읽기도 수행합니다.
    /// </summary>
    /// <param name="config">쓰기 설정</param>
    /// <returns>TaskId</returns>
    public string StartPeriodicWrite(PeriodicWriteConfig config)
    {
        StopTask(config.TaskId);

        var cts = new CancellationTokenSource();
        lock (_tasksLock) { _tasks[config.TaskId] = cts; }

        _ = RunPeriodicWriteAsync(config, cts.Token);
        return config.TaskId;
    }

    private async Task RunPeriodicWriteAsync(PeriodicWriteConfig config, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(config.Interval);
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            try
            {
                await WriteAsync(config, ct).ConfigureAwait(false);
                WriteCompleted?.Invoke(this, new ModbusWriteEventArgs(
                    config.DeviceId, config.StartAddress, config.TaskId));

                if (config.ReadBackAfterWrite && config.RegisterValues != null)
                {
                    var values = await _client.ReadHoldingRegistersAsync(
                        config.DeviceId, config.StartAddress, config.ReadBackCount, ct);
                    DataReceived?.Invoke(this, new ModbusReadEventArgs(
                        config.DeviceId, config.StartAddress,
                        ModbusFunctionCode.ReadHoldingRegisters, values, config.TaskId));
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, new ModbusErrorEventArgs(ex, config.TaskId));
            }
        }
    }

    private async Task WriteAsync(PeriodicWriteConfig config, CancellationToken ct)
    {
        if (config.RegisterValues != null)
        {
            if (config.RegisterValues.Length == 1)
                await _client.WriteSingleRegisterAsync(
                    config.DeviceId, config.StartAddress, config.RegisterValues[0], ct);
            else
                await _client.WriteMultipleRegistersAsync(
                    config.DeviceId, config.StartAddress, config.RegisterValues, ct);
        }
        else if (config.CoilValues != null)
        {
            if (config.CoilValues.Length == 1)
                await _client.WriteSingleCoilAsync(
                    config.DeviceId, config.StartAddress, config.CoilValues[0], ct);
            else
                await _client.WriteMultipleCoilsAsync(
                    config.DeviceId, config.StartAddress, config.CoilValues, ct);
        }
        else
        {
            throw new InvalidOperationException("RegisterValues 또는 CoilValues 중 하나를 설정해야 합니다.");
        }
    }

    // ─── Task Management ─────────────────────────────────────────────────────

    /// <summary>특정 TaskId의 주기 작업 중지</summary>
    public void StopTask(string taskId)
    {
        CancellationTokenSource? cts;
        lock (_tasksLock)
        {
            if (!_tasks.TryGetValue(taskId, out cts)) return;
            _tasks.Remove(taskId);
        }
        cts.Cancel();
        cts.Dispose();
    }

    /// <summary>모든 주기 작업 중지</summary>
    public void StopAll()
    {
        List<CancellationTokenSource> allCts;
        lock (_tasksLock)
        {
            allCts = new List<CancellationTokenSource>(_tasks.Values);
            _tasks.Clear();
        }
        foreach (var cts in allCts)
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    /// <summary>현재 실행 중인 TaskId 목록</summary>
    public IReadOnlyList<string> GetRunningTaskIds()
    {
        lock (_tasksLock) { return _tasks.Keys.ToList(); }
    }

    /// <summary>특정 Task가 실행 중인지 확인</summary>
    public bool IsTaskRunning(string taskId)
    {
        lock (_tasksLock) { return _tasks.ContainsKey(taskId); }
    }

    public void Dispose() => StopAll();
}
