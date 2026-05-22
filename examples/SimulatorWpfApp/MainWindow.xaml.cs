using System.Collections.ObjectModel;
using System.Timers;
using System.Windows;
using System.Windows.Media;
using MAK_Modbus.Simulator;

namespace SimulatorWpfApp;

public partial class MainWindow : Window
{
    private ModbusTcpServer? _server;
    private System.Timers.Timer? _autoTimer;
    private ushort _autoAddr;
    private bool _autoRunning;

    public MainWindow()
    {
        InitializeComponent();
        RefreshHoldingRegisters();
        RefreshCoils();
    }

    // ─── 서버 시작/중지 ──────────────────────────────────────────────────────

    private void BtnStart_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(TxtPort.Text, out var port) || port < 1 || port > 65535)
        {
            MessageBox.Show("포트 번호가 올바르지 않습니다."); return;
        }

        _server = new ModbusTcpServer(port);
        _server.RequestReceived       += OnRequestReceived;
        _server.DataChanged           += OnDataChanged;
        _server.ClientConnectionChanged += OnClientChanged;

        try
        {
            _server.Start();
            BtnStart.IsEnabled = false;
            BtnStop.IsEnabled  = true;
            TxtPort.IsEnabled  = false;
            EllStatus.Fill = Brushes.LimeGreen;
            LblStatus.Content = $"대기 중 (포트 {port})";
            Log($"서버 시작: 포트 {port}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"서버 시작 실패: {ex.Message}");
            _server.Dispose();
            _server = null;
        }
    }

    private void BtnStop_Click(object sender, RoutedEventArgs e)
    {
        _server?.Stop();
        _server?.Dispose();
        _server = null;
        BtnStart.IsEnabled = true;
        BtnStop.IsEnabled  = false;
        TxtPort.IsEnabled  = true;
        EllStatus.Fill = Brushes.Gray;
        LblStatus.Content = "중지됨";
        LblClients.Content = "연결: 0개";
        Log("서버 중지");
    }

    // ─── 수동 값 설정 ────────────────────────────────────────────────────────

    private void BtnSetValue_Click(object sender, RoutedEventArgs e)
    {
        if (_server == null) { MessageBox.Show("서버를 먼저 시작하세요."); return; }
        if (!ushort.TryParse(TxtSetAddr.Text, out var addr)) { MessageBox.Show("주소 오류"); return; }

        var type = (CmbMemType.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "";

        if (type.Contains("Coil") || type.Contains("Discrete"))
        {
            var val = TxtSetValue.Text.Trim().ToLower();
            bool bval = val == "1" || val == "true" || val == "on";

            if (type.Contains("Coil")) _server.SetCoil(addr, bval);
            else _server.SetDiscreteInput(addr, bval);
            Log($"설정: {type} [{addr}] = {bval}");
        }
        else
        {
            if (!ushort.TryParse(TxtSetValue.Text, out var val)) { MessageBox.Show("값 오류 (0~65535)"); return; }
            if (type.Contains("Input")) _server.SetInputRegister(addr, val);
            else _server.SetHoldingRegister(addr, val);
            Log($"설정: {type} [{addr}] = {val}");
        }
        RefreshAll();
    }

    // ─── 자동 카운터 ─────────────────────────────────────────────────────────

    private void BtnAutoIncrement_Click(object sender, RoutedEventArgs e)
    {
        if (_server == null) { MessageBox.Show("서버를 먼저 시작하세요."); return; }
        if (!ushort.TryParse(TxtSetAddr.Text, out _autoAddr)) { MessageBox.Show("주소 오류"); return; }

        if (_autoRunning)
        {
            _autoTimer?.Stop();
            _autoTimer?.Dispose();
            _autoRunning = false;
            Log($"자동 카운터 중지 (주소 {_autoAddr})");
        }
        else
        {
            _autoRunning = true;
            _autoTimer = new System.Timers.Timer(1000);
            _autoTimer.Elapsed += (s, a) =>
            {
                if (_server == null) return;
                var cur = _server.GetHoldingRegister(_autoAddr);
                _server.SetHoldingRegister(_autoAddr, (ushort)(cur + 1));
                Dispatcher.Invoke(RefreshAll);
            };
            _autoTimer.Start();
            Log($"자동 카운터 시작 (주소 {_autoAddr}, 1초마다 +1)");
        }
    }

    // ─── DataGrid 새로고침 ───────────────────────────────────────────────────

    private void RefreshHoldingRegisters()
    {
        if (_server == null) return;
        if (!ushort.TryParse(TxtHrStart.Text, out var start)) start = 0;
        if (!ushort.TryParse(TxtHrCount.Text, out var count)) count = 20;

        var rows = new ObservableCollection<RegisterRow>();
        var values = _server.GetHoldingRegisters(start, count);
        for (int i = 0; i < count; i++)
        {
            var v = values[i];
            rows.Add(new RegisterRow
            {
                Address  = (ushort)(start + i),
                ValueDec = v.ToString(),
                ValueHex = $"0x{v:X4}",
                ValueBin = Convert.ToString(v, 2).PadLeft(16, '0')
            });
        }
        DgHoldingReg.ItemsSource = rows;
    }

    private void RefreshCoils()
    {
        if (_server == null) return;
        if (!ushort.TryParse(TxtCoilStart.Text, out var start)) start = 0;
        if (!ushort.TryParse(TxtCoilCount.Text, out var count)) count = 16;

        var rows = new ObservableCollection<CoilRow>();
        for (int i = 0; i < count; i++)
        {
            var state = _server.GetCoil((ushort)(start + i));
            rows.Add(new CoilRow
            {
                Address = (ushort)(start + i),
                State   = state ? "■ ON" : "□ OFF"
            });
        }
        DgCoils.ItemsSource = rows;
    }

    private void RefreshAll() { RefreshHoldingRegisters(); RefreshCoils(); }

    private void BtnRefreshHr_Click(object sender, RoutedEventArgs e)   => RefreshHoldingRegisters();
    private void BtnRefreshCoil_Click(object sender, RoutedEventArgs e) => RefreshCoils();

    // ─── 이벤트 핸들러 ───────────────────────────────────────────────────────

    private void OnRequestReceived(object? s, ServerRequestEventArgs e)
        => Dispatcher.Invoke(() =>
        {
            Log(e.ToString());
            RefreshAll();
        });

    private void OnDataChanged(object? s, RegisterChangedEventArgs e)
        => Dispatcher.Invoke(RefreshAll);

    private void OnClientChanged(object? s, string msg)
        => Dispatcher.Invoke(() =>
        {
            LblClients.Content = $"연결: {_server?.ConnectedClients ?? 0}개";
            Log($"[연결] {msg}");
        });

    // ─── 유틸리티 ────────────────────────────────────────────────────────────

    private void Log(string msg)
    {
        TxtLog.AppendText(msg + Environment.NewLine);
        if (ChkAutoScroll.IsChecked == true)
            LogScroll.ScrollToBottom();
    }

    private void BtnClearLog_Click(object sender, RoutedEventArgs e) => TxtLog.Clear();

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _autoTimer?.Stop();
        _server?.Stop();
    }
}

// ─── 데이터 모델 ─────────────────────────────────────────────────────────────

public class RegisterRow
{
    public ushort Address  { get; set; }
    public string ValueDec { get; set; } = "";
    public string ValueHex { get; set; } = "";
    public string ValueBin { get; set; } = "";
}

public class CoilRow
{
    public ushort Address { get; set; }
    public string State   { get; set; } = "";
}
