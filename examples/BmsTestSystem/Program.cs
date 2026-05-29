// ─────────────────────────────────────────────────────────────────────────────
// [콘솔 앱] 기본 실행 예시
// ─────────────────────────────────────────────────────────────────────────────
using BmsTestSystem;

await using var client = BmsModbusClient.Create("192.168.1.100", 502, 3000);
await client.ConnectAsync();

var engine  = new BmsTestEngine(client);
var results = await engine.RunCommandsAsync("config/sequence.json");

Environment.Exit(results.All(r => r.Success) ? 0 : 1);


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

    public ObservableCollection<CommandResult> Results { get; } = new();

    private async void BtnConnect_Click(object sender, RoutedEventArgs e)
    {
        void AppendLog(string msg) => TxtLog.AppendText(msg + "\n");

        _client = BmsModbusClient.Create(
            ip:          TxtIp.Text,
            port:        502,
            timeoutMs:   3000,
            errorLogger: msg => Dispatcher.Invoke(() => AppendLog(msg)));

        await _client.ConnectAsync();
        AppendLog("BMS 연결 성공");
    }

    private async void BtnStartTest_Click(object sender, RoutedEventArgs e)
    {
        if (_client == null) return;

        Results.Clear();
        BtnStartTest.IsEnabled = false;
        _cts = new CancellationTokenSource();

        void AppendLog(string msg) => TxtLog.AppendText(msg + "\n");

        var progress = new Progress<CommandResult>(result =>
        {
            Results.Add(result);
            DgResults.ScrollIntoView(result);
        });

        var engine = new BmsTestEngine(_client, logger: AppendLog);

        try
        {
            var results = await engine.RunCommandsAsync(
                jsonFilePath: "config/sequence.json",
                progress:     progress,
                ct:           _cts.Token);

            bool allOk = results.All(r => r.Success);
            LblOverall.Content    = allOk ? "OK" : "FAIL";
            LblOverall.Foreground = allOk ? Brushes.Green : Brushes.Red;
        }
        catch (OperationCanceledException)
        {
            AppendLog("[Engine] 실행이 사용자에 의해 중단되었습니다.");
        }
        finally
        {
            BtnStartTest.IsEnabled = true;
        }
    }

    private void BtnStop_Click(object sender, RoutedEventArgs e)
        => _cts?.Cancel();

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
