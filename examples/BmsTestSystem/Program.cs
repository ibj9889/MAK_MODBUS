// ─────────────────────────────────────────────────────────────────────────────
// [콘솔 앱] 기본 실행 예시
// ─────────────────────────────────────────────────────────────────────────────
using BmsTestSystem;

await using var client = BmsModbusClient.Create("192.168.1.100", 502, 3000);
await client.ConnectAsync();

var engine  = new BmsTestEngine(client);
var results = await engine.RunSequenceAsync("config/sequence.json");

Environment.Exit(results.All(r => r.IsPass) ? 0 : 1);


// ─────────────────────────────────────────────────────────────────────────────
// [WPF 앱] 통합 패턴 — 아래 코드를 WPF MainWindow.xaml.cs에 복사하여 사용하세요
// ─────────────────────────────────────────────────────────────────────────────
/*

using BmsTestSystem;
using BmsTestSystem.Models;
using System.Collections.ObjectModel;
using System.Windows;

public partial class MainWindow : Window
{
    private BmsModbusClient? _client;
    private CancellationTokenSource? _cts;

    // DataGrid에 바인딩할 결과 컬렉션
    public ObservableCollection<StepResult> TestResults { get; } = new();

    // ── 연결 ──────────────────────────────────────────────────────────────────

    private async void BtnConnect_Click(object sender, RoutedEventArgs e)
    {
        // 로거: WPF TextBox에 직접 append (UI 스레드에서 호출되므로 Dispatcher 불필요)
        void AppendLog(string msg) => TxtLog.AppendText(msg + "\n");

        _client = BmsModbusClient.Create(
            ip:          TxtIp.Text,
            port:        502,
            timeoutMs:   3000,
            errorLogger: msg => Dispatcher.Invoke(() => AppendLog(msg)));  // ← 오류는 백그라운드에서 발생 가능 → Dispatcher 필요

        await _client.ConnectAsync();
        AppendLog("BMS 연결 성공");
    }

    // ── 검사 시작 ─────────────────────────────────────────────────────────────

    private async void BtnStartTest_Click(object sender, RoutedEventArgs e)
    {
        if (_client == null) return;

        TestResults.Clear();
        BtnStartTest.IsEnabled = false;
        _cts = new CancellationTokenSource();

        // 로거: async void 이벤트 핸들러는 UI 스레드에서 실행되므로 Dispatcher 불필요
        void AppendLog(string msg) => TxtLog.AppendText(msg + "\n");

        // IProgress<T>: Report()는 캡처된 SynchronizationContext(UI 스레드)에서 실행됨
        // → DataGrid가 스텝 완료 즉시 갱신됨
        var progress = new Progress<StepResult>(result =>
        {
            TestResults.Add(result);                     // UI 스레드에서 안전하게 추가
            DgResults.ScrollIntoView(result);            // 최신 결과로 자동 스크롤
        });

        var engine = new BmsTestEngine(_client, logger: AppendLog);

        try
        {
            var results = await engine.RunSequenceAsync(
                jsonFilePath: "config/sequence.json",
                progress:     progress,
                ct:           _cts.Token);

            bool allPass = results.All(r => r.IsPass);
            LblOverall.Content    = allPass ? "PASS" : "FAIL";
            LblOverall.Foreground = allPass ? Brushes.Green : Brushes.Red;
        }
        catch (OperationCanceledException)
        {
            AppendLog("[Engine] 검사가 사용자에 의해 중단되었습니다.");
        }
        finally
        {
            BtnStartTest.IsEnabled = true;
        }
    }

    // ── 검사 중단 ─────────────────────────────────────────────────────────────

    private void BtnStopTest_Click(object sender, RoutedEventArgs e)
        => _cts?.Cancel();

    // ── 연결 해제 ─────────────────────────────────────────────────────────────

    private async void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _cts?.Cancel();
        if (_client != null) await _client.DisposeAsync();
    }
}

*/

// ─────────────────────────────────────────────────────────────────────────────
// [WPF csproj] 참조 추가 방법
// ─────────────────────────────────────────────────────────────────────────────
/*

WPF 프로젝트의 .csproj에 아래 참조를 추가하세요:

<ItemGroup>
  <ProjectReference Include="..\MAK_Modbus\MAK_Modbus.csproj" />
</ItemGroup>

그리고 BmsModbusClient.cs, BmsTestEngine.cs, Models/ 폴더를 WPF 프로젝트로 복사하거나
Compile Include로 링크하면 됩니다.

WPF 프로젝트 TargetFramework는 반드시 net6.0-windows 이어야 합니다:
<TargetFramework>net6.0-windows</TargetFramework>
<UseWPF>true</UseWPF>

*/
