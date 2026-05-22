using System.IO.Ports;
using System.Windows;
using System.Windows.Media;
using MAK_Modbus.Core;
using MAK_Modbus.Models;
using MAK_Modbus.Services;

namespace WpfExample;

public partial class MainWindow : Window
{
    private IModbusClient? _client;
    private ModbusPeriodicService? _periodicService;
    private const string ReadTaskId = "read-polling";
    private const string WriteTaskId = "write-periodic";

    public MainWindow()
    {
        InitializeComponent();
        LoadSerialPorts();
        CmbBaud.ItemsSource = new[] { 1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200 };
        CmbBaud.SelectedItem = 9600;
    }

    private void LoadSerialPorts()
    {
        CmbPort.ItemsSource = ModbusClientFactory.GetAvailableSerialPorts();
        if (CmbPort.Items.Count > 0) CmbPort.SelectedIndex = 0;
    }

    // ─── 연결 타입 전환 ───────────────────────────────────────────────────────

    private void ConnectionType_Changed(object sender, RoutedEventArgs e)
    {
        if (PnlTcp == null || PnlRtu == null) return;
        PnlTcp.Visibility = RbTcp.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PnlRtu.Visibility = RbRtu.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    // ─── 연결 / 해제 ─────────────────────────────────────────────────────────

    private async void BtnConnect_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            BtnConnect.IsEnabled = false;

            if (RbTcp.IsChecked == true)
            {
                var host = TxtHost.Text.Trim();
                var port = int.Parse(TxtPort.Text);
                var timeout = int.Parse(TxtTcpTimeout.Text);
                _client = ModbusClientFactory.CreateTcp(host, port, timeout);
            }
            else
            {
                var portName = CmbPort.SelectedItem?.ToString() ?? "COM1";
                var baud = (int)(CmbBaud.SelectedItem ?? 9600);
                var parity = CmbParity.Text switch
                {
                    "Even" => Parity.Even,
                    "Odd"  => Parity.Odd,
                    _      => Parity.None
                };
                var stopBits = CmbStopBits.Text == "2" ? StopBits.Two : StopBits.One;
                _client = ModbusClientFactory.CreateRtu(portName, baud, parity, 8, stopBits);
            }

            _client.ConnectionChanged += OnConnectionChanged;
            await _client.ConnectAsync();

            _periodicService = new ModbusPeriodicService(_client);
            _periodicService.DataReceived   += OnDataReceived;
            _periodicService.WriteCompleted += OnWriteCompleted;
            _periodicService.ErrorOccurred  += OnPeriodicError;

            SetConnected(true);
        }
        catch (Exception ex)
        {
            Log($"[오류] 연결 실패: {ex.Message}");
            BtnConnect.IsEnabled = true;
        }
    }

    private void BtnDisconnect_Click(object sender, RoutedEventArgs e)
    {
        _periodicService?.StopAll();
        _periodicService?.Dispose();
        _client?.Disconnect();
        _client?.Dispose();
        _periodicService = null;
        _client = null;
        SetConnected(false);
        ResetPeriodicButtons();
    }

    // ─── 단발성 읽기 ─────────────────────────────────────────────────────────

    private async void BtnReadOnce_Click(object sender, RoutedEventArgs e)
    {
        if (_client == null) { Log("[경고] 먼저 연결하세요."); return; }

        try
        {
            var deviceId = byte.Parse(TxtReadDeviceId.Text);
            var address  = ushort.Parse(TxtReadAddress.Text);
            var count    = ushort.Parse(TxtReadCount.Text);
            var fc = GetSelectedReadFc();

            var result = await ReadAsync(deviceId, address, count, fc);
            Log($"[읽기 완료] DevID={deviceId} Addr={address} FC={fc:X2} → {result}");
        }
        catch (Exception ex) { Log($"[오류] 읽기 실패: {ex.Message}"); }
    }

    // ─── 실시간 읽기 (주기적) ────────────────────────────────────────────────

    private void BtnStartPolling_Click(object sender, RoutedEventArgs e)
    {
        if (_periodicService == null) { Log("[경고] 먼저 연결하세요."); return; }

        var config = new PeriodicReadConfig
        {
            TaskId       = ReadTaskId,
            DeviceId     = byte.Parse(TxtReadDeviceId.Text),
            StartAddress = ushort.Parse(TxtReadAddress.Text),
            Count        = ushort.Parse(TxtReadCount.Text),
            FunctionCode = GetSelectedReadFc(),
            Interval     = TimeSpan.FromMilliseconds(int.Parse(TxtPollInterval.Text))
        };

        _periodicService.StartPeriodicRead(config);
        BtnStartPolling.IsEnabled = false;
        BtnStopPolling.IsEnabled  = true;
        Log($"[폴링 시작] 주기={config.Interval.TotalMilliseconds}ms DevID={config.DeviceId} Addr={config.StartAddress}");
    }

    private void BtnStopPolling_Click(object sender, RoutedEventArgs e)
    {
        _periodicService?.StopTask(ReadTaskId);
        BtnStartPolling.IsEnabled = true;
        BtnStopPolling.IsEnabled  = false;
        Log("[폴링 중지]");
    }

    // ─── 단발성 쓰기 ─────────────────────────────────────────────────────────

    private async void BtnWriteOnce_Click(object sender, RoutedEventArgs e)
    {
        if (_client == null) { Log("[경고] 먼저 연결하세요."); return; }

        try
        {
            var deviceId = byte.Parse(TxtWriteDeviceId.Text);
            var address  = ushort.Parse(TxtWriteAddress.Text);
            var values   = ParseWriteValues();

            if (values.Length == 1)
                await _client.WriteSingleRegisterAsync(deviceId, address, values[0]);
            else
                await _client.WriteMultipleRegistersAsync(deviceId, address, values);

            Log($"[쓰기 완료] DevID={deviceId} Addr={address} 값=[{string.Join(", ", values)}]");

            if (ChkWriteReadBack.IsChecked == true)
            {
                var readBack = await _client.ReadHoldingRegistersAsync(deviceId, address, (ushort)values.Length);
                Log($"[읽기 확인] → [{string.Join(", ", readBack)}]");
            }
        }
        catch (Exception ex) { Log($"[오류] 쓰기 실패: {ex.Message}"); }
    }

    // ─── 주기적 쓰기 ─────────────────────────────────────────────────────────

    private void BtnStartPeriodicWrite_Click(object sender, RoutedEventArgs e)
    {
        if (_periodicService == null) { Log("[경고] 먼저 연결하세요."); return; }

        var config = new PeriodicWriteConfig
        {
            TaskId          = WriteTaskId,
            DeviceId        = byte.Parse(TxtWriteDeviceId.Text),
            StartAddress    = ushort.Parse(TxtWriteAddress.Text),
            RegisterValues  = ParseWriteValues(),
            Interval        = TimeSpan.FromMilliseconds(int.Parse(TxtWriteInterval.Text)),
            ReadBackAfterWrite = ChkWriteReadBack.IsChecked == true,
            ReadBackCount   = (ushort)ParseWriteValues().Length
        };

        _periodicService.StartPeriodicWrite(config);
        BtnStartPeriodicWrite.IsEnabled = false;
        BtnStopPeriodicWrite.IsEnabled  = true;
        Log($"[주기 쓰기 시작] 주기={config.Interval.TotalMilliseconds}ms");
    }

    private void BtnStopPeriodicWrite_Click(object sender, RoutedEventArgs e)
    {
        _periodicService?.StopTask(WriteTaskId);
        BtnStartPeriodicWrite.IsEnabled = true;
        BtnStopPeriodicWrite.IsEnabled  = false;
        Log("[주기 쓰기 중지]");
    }

    // ─── 이벤트 핸들러 ───────────────────────────────────────────────────────

    private void OnConnectionChanged(object? s, ModbusConnectionEventArgs e) =>
        Dispatcher.Invoke(() => Log($"[연결] {e.Message}"));

    private void OnDataReceived(object? s, ModbusReadEventArgs e) =>
        Dispatcher.Invoke(() =>
        {
            var values = e.RegisterValues != null
                ? string.Join(", ", e.RegisterValues)
                : string.Join(", ", e.CoilValues?.Select(b => b ? "1" : "0") ?? Array.Empty<string>());
            Log($"[{e.Timestamp:HH:mm:ss.fff}] Task={e.TaskId} Addr={e.StartAddress} → [{values}]");
        });

    private void OnWriteCompleted(object? s, ModbusWriteEventArgs e) =>
        Dispatcher.Invoke(() =>
            Log($"[{e.Timestamp:HH:mm:ss.fff}] 쓰기완료 Task={e.TaskId} Addr={e.StartAddress}"));

    private void OnPeriodicError(object? s, ModbusErrorEventArgs e) =>
        Dispatcher.Invoke(() =>
            Log($"[오류] Task={e.TaskId}: {e.Error.Message}"));

    // ─── 유틸리티 ────────────────────────────────────────────────────────────

    private async Task<string> ReadAsync(byte deviceId, ushort address, ushort count, ModbusFunctionCode fc)
    {
        return fc switch
        {
            ModbusFunctionCode.ReadHoldingRegisters =>
                string.Join(", ", await _client!.ReadHoldingRegistersAsync(deviceId, address, count)),
            ModbusFunctionCode.ReadInputRegisters =>
                string.Join(", ", await _client!.ReadInputRegistersAsync(deviceId, address, count)),
            ModbusFunctionCode.ReadCoils =>
                string.Join(", ", (await _client!.ReadCoilsAsync(deviceId, address, count)).Select(b => b ? "1" : "0")),
            ModbusFunctionCode.ReadDiscreteInputs =>
                string.Join(", ", (await _client!.ReadDiscreteInputsAsync(deviceId, address, count)).Select(b => b ? "1" : "0")),
            _ => "Unknown FC"
        };
    }

    private ModbusFunctionCode GetSelectedReadFc()
    {
        var tag = (CmbReadFc.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString();
        return tag switch
        {
            "04" => ModbusFunctionCode.ReadInputRegisters,
            "01" => ModbusFunctionCode.ReadCoils,
            "02" => ModbusFunctionCode.ReadDiscreteInputs,
            _    => ModbusFunctionCode.ReadHoldingRegisters
        };
    }

    private ushort[] ParseWriteValues()
    {
        return TxtWriteValues.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => ushort.Parse(s.Trim()))
            .ToArray();
    }

    private void SetConnected(bool connected)
    {
        BtnConnect.IsEnabled    = !connected;
        BtnDisconnect.IsEnabled = connected;
        EllStatus.Fill = connected ? Brushes.LimeGreen : Brushes.Gray;
        LblStatus.Content = connected ? "연결됨" : "미연결";
    }

    private void ResetPeriodicButtons()
    {
        BtnStartPolling.IsEnabled       = true;
        BtnStopPolling.IsEnabled        = false;
        BtnStartPeriodicWrite.IsEnabled = true;
        BtnStopPeriodicWrite.IsEnabled  = false;
    }

    private void Log(string message)
    {
        TxtLog.AppendText(message + Environment.NewLine);
        ScrollLog.ScrollToBottom();
    }

    private void BtnClearLog_Click(object sender, RoutedEventArgs e) => TxtLog.Clear();

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _periodicService?.StopAll();
        _client?.Disconnect();
    }
}
