# MAK_Modbus

C# WPF 프로젝트에서 바로 사용할 수 있는 **Modbus TCP / RTU** 통신 라이브러리입니다.

- Modbus TCP (MBAP 헤더 기반)
- Modbus RTU (시리얼 포트 + CRC16)
- 단발성 Read / Write
- 주기적 Read / Write (사용자 지정 인터벌)
- Write 후 바로 Read 기능
- 이벤트 기반 실시간 데이터 수신

---

## 프로젝트 구조

```
MAK_Modbus/
├── src/
│   └── MAK_Modbus/                   ← 라이브러리 (참조 추가 대상)
│       ├── Core/
│       │   ├── IModbusClient.cs      ← 공통 인터페이스
│       │   ├── ModbusTcpClient.cs    ← TCP 구현체
│       │   ├── ModbusRtuClient.cs    ← RTU 구현체
│       │   └── ModbusClientFactory.cs← 생성 헬퍼
│       ├── Models/
│       │   ├── ModbusFunctionCode.cs
│       │   ├── ModbusException.cs
│       │   ├── ModbusEventArgs.cs
│       │   └── PeriodicConfig.cs
│       ├── Services/
│       │   └── ModbusPeriodicService.cs
│       └── Helpers/
│           ├── CrcHelper.cs
│           └── DataConverter.cs      ← float/int32 변환 유틸
├── examples/
│   └── WpfExample/                   ← WPF 예제 프로그램
└── MAK_Modbus.sln
```

---

## 설치 / 참조 방법

### 1. 프로젝트 참조 추가 (같은 솔루션)

```xml
<!-- YourProject.csproj -->
<ItemGroup>
  <ProjectReference Include="..\MAK_Modbus\src\MAK_Modbus\MAK_Modbus.csproj" />
</ItemGroup>
```

### 2. 네임스페이스

```csharp
using MAK_Modbus.Core;
using MAK_Modbus.Models;
using MAK_Modbus.Services;
using MAK_Modbus.Helpers;
```

---

## WPF 프로젝트 연동 가이드 (Visual Studio)

### Step 1 - 라이브러리 다운로드

GitHub에서 클론하거나 ZIP으로 받습니다.

```bash
git clone https://github.com/ibj9889/MAK_MODBUS.git
```

또는 GitHub 페이지에서 **Code → Download ZIP** 후 압축 해제

---

### Step 2 - 내 WPF 솔루션에 라이브러리 프로젝트 추가

Visual Studio에서 내 WPF 프로젝트가 열린 상태에서:

1. **솔루션 탐색기** 에서 솔루션 우클릭
2. **추가 → 기존 프로젝트** 클릭
3. 다운받은 폴더에서 `src/MAK_Modbus/MAK_Modbus.csproj` 선택

> 이제 솔루션 탐색기에 **MAK_Modbus** 프로젝트가 추가됩니다.

---

### Step 3 - 내 WPF 프로젝트에서 MAK_Modbus 참조 추가

1. 솔루션 탐색기에서 **내 WPF 프로젝트** 우클릭
2. **추가 → 프로젝트 참조**
3. `MAK_Modbus` 체크 → 확인

또는 `.csproj` 파일에 직접 추가:

```xml
<!-- MyWpfApp.csproj -->
<ItemGroup>
  <ProjectReference Include="..\MAK_MODBUS\src\MAK_Modbus\MAK_Modbus.csproj" />
</ItemGroup>
```

---

### Step 4 - NuGet 패키지 설치 (RTU 사용 시만 필요)

RTU(시리얼 포트) 방식을 쓸 경우 추가로 설치:

1. **도구 → NuGet 패키지 관리자 → 패키지 관리자 콘솔**
2. 아래 명령 실행:

```
Install-Package System.IO.Ports
```

TCP만 사용할 경우 이 단계는 생략해도 됩니다.

---

### Step 5 - 실제 WPF에서 전체 연동 예제

아래는 **버튼 클릭 → TCP 연결 → 실시간 읽기 → 화면 표시** 하는 완전한 예제입니다.

#### MainWindow.xaml

```xml
<Window x:Class="MyApp.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Modbus 테스터" Height="400" Width="600">
    <StackPanel Margin="15">

        <!-- 연결 -->
        <StackPanel Orientation="Horizontal" Margin="0,0,0,10">
            <TextBox x:Name="TxtIp" Width="150" Text="192.168.1.100" Margin="0,0,5,0"/>
            <TextBox x:Name="TxtPort" Width="60" Text="502" Margin="0,0,5,0"/>
            <Button Content="연결" Click="BtnConnect_Click" Width="70" Margin="0,0,5,0"/>
            <Button Content="해제" Click="BtnDisconnect_Click" Width="70"/>
            <Ellipse x:Name="EllStatus" Width="12" Height="12"
                     Fill="Gray" Margin="10,0,0,0" VerticalAlignment="Center"/>
        </StackPanel>

        <!-- 읽기 -->
        <StackPanel Orientation="Horizontal" Margin="0,0,0,10">
            <TextBox x:Name="TxtAddress" Width="80" Text="0" Margin="0,0,5,0"/>
            <TextBox x:Name="TxtCount" Width="60" Text="10" Margin="0,0,5,0"/>
            <Button Content="단발 읽기" Click="BtnReadOnce_Click" Width="90" Margin="0,0,5,0"/>
            <Button Content="실시간 시작" Click="BtnStartPolling_Click" Width="90" Margin="0,0,5,0"/>
            <Button Content="중지" Click="BtnStopPolling_Click" Width="60"/>
        </StackPanel>

        <!-- 쓰기 -->
        <StackPanel Orientation="Horizontal" Margin="0,0,0,10">
            <TextBox x:Name="TxtWriteAddr" Width="80" Text="0" Margin="0,0,5,0"/>
            <TextBox x:Name="TxtWriteVal" Width="100" Text="1234" Margin="0,0,5,0"/>
            <Button Content="쓰기" Click="BtnWrite_Click" Width="70"/>
        </StackPanel>

        <!-- 결과 -->
        <TextBox x:Name="TxtResult" Height="200" IsReadOnly="True"
                 FontFamily="Consolas" AcceptsReturn="True"
                 VerticalScrollBarVisibility="Auto" Background="#F5F5F5"/>

    </StackPanel>
</Window>
```

#### MainWindow.xaml.cs

```csharp
using System.Windows;
using System.Windows.Media;
using MAK_Modbus.Core;
using MAK_Modbus.Models;
using MAK_Modbus.Services;

namespace MyApp;

public partial class MainWindow : Window
{
    private IModbusClient? _client;
    private ModbusPeriodicService? _service;

    public MainWindow() => InitializeComponent();

    // ── 연결 ────────────────────────────────────────────
    private async void BtnConnect_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _client = ModbusClientFactory.CreateTcp(TxtIp.Text, int.Parse(TxtPort.Text));
            await _client.ConnectAsync();

            _service = new ModbusPeriodicService(_client);
            _service.DataReceived  += OnDataReceived;
            _service.ErrorOccurred += OnError;

            EllStatus.Fill = Brushes.LimeGreen;
            Log("연결 성공!");
        }
        catch (Exception ex) { Log($"연결 실패: {ex.Message}"); }
    }

    private void BtnDisconnect_Click(object sender, RoutedEventArgs e)
    {
        _service?.StopAll();
        _client?.Disconnect();
        EllStatus.Fill = Brushes.Gray;
        Log("연결 해제");
    }

    // ── 단발성 읽기 ──────────────────────────────────────
    private async void BtnReadOnce_Click(object sender, RoutedEventArgs e)
    {
        if (_client == null) return;
        var addr  = ushort.Parse(TxtAddress.Text);
        var count = ushort.Parse(TxtCount.Text);

        var values = await _client.ReadHoldingRegistersAsync(
            deviceId: 1, startAddress: addr, count: count);

        Log($"읽기 결과: [{string.Join(", ", values)}]");
    }

    // ── 실시간 주기 읽기 ─────────────────────────────────
    private void BtnStartPolling_Click(object sender, RoutedEventArgs e)
    {
        if (_service == null) return;
        _service.StartPeriodicRead(new PeriodicReadConfig
        {
            DeviceId     = 1,
            StartAddress = ushort.Parse(TxtAddress.Text),
            Count        = ushort.Parse(TxtCount.Text),
            Interval     = TimeSpan.FromMilliseconds(500)  // 500ms 주기
        });
        Log("실시간 읽기 시작 (500ms)");
    }

    private void BtnStopPolling_Click(object sender, RoutedEventArgs e)
    {
        _service?.StopAll();
        Log("실시간 읽기 중지");
    }

    // ── 쓰기 ────────────────────────────────────────────
    private async void BtnWrite_Click(object sender, RoutedEventArgs e)
    {
        if (_client == null) return;
        var addr  = ushort.Parse(TxtWriteAddr.Text);
        var value = ushort.Parse(TxtWriteVal.Text);

        await _client.WriteSingleRegisterAsync(deviceId: 1, address: addr, value: value);
        Log($"쓰기 완료: 주소={addr}, 값={value}");
    }

    // ── 이벤트 수신 ──────────────────────────────────────
    private void OnDataReceived(object? s, ModbusReadEventArgs e)
    {
        // 백그라운드 스레드에서 오므로 반드시 Dispatcher.Invoke 사용
        Dispatcher.Invoke(() =>
        {
            if (e.RegisterValues != null)
                Log($"[{e.Timestamp:HH:mm:ss.fff}] [{string.Join(", ", e.RegisterValues)}]");
        });
    }

    private void OnError(object? s, ModbusErrorEventArgs e)
    {
        Dispatcher.Invoke(() => Log($"오류: {e.Error.Message}"));
    }

    private void Log(string msg)
    {
        TxtResult.AppendText(msg + "\n");
        TxtResult.ScrollToEnd();
    }
}
```

---

### Step 6 - RTU(시리얼 포트) 연결로 바꾸는 경우

TCP 대신 RTU로 변경할 때는 `BtnConnect_Click` 안의 클라이언트 생성 부분만 교체합니다.

```csharp
// TCP → 이 줄을 삭제하고
_client = ModbusClientFactory.CreateTcp(TxtIp.Text, 502);

// RTU → 이 줄로 교체
_client = ModbusClientFactory.CreateRtu(
    portName: "COM3",   // 장치 관리자에서 확인한 포트번호
    baudRate: 9600
);
```

나머지 Read/Write 코드는 TCP와 **완전히 동일**합니다.

---

### 연동 흐름 요약

```
[Visual Studio WPF 프로젝트]
        │
        ├── 1. MAK_Modbus 프로젝트 참조 추가
        ├── 2. ModbusClientFactory.CreateTcp() 또는 CreateRtu()로 클라이언트 생성
        ├── 3. await client.ConnectAsync()  →  연결
        ├── 4. new ModbusPeriodicService(client)  →  서비스 생성
        ├── 5. service.DataReceived += OnDataReceived  →  이벤트 등록
        ├── 6. service.StartPeriodicRead(config)  →  실시간 읽기 시작
        │       └── OnDataReceived 이벤트로 데이터 수신
        │           └── Dispatcher.Invoke()로 UI 업데이트
        └── 7. 종료 시 service.StopAll() + client.Disconnect()
```

---

## 사용 방법 (API 매뉴얼)

### TCP 클라이언트 생성 및 연결

```csharp
// 팩토리로 생성
var client = ModbusClientFactory.CreateTcp("192.168.1.100", port: 502, timeoutMs: 3000);

// 연결 이벤트 등록
client.ConnectionChanged += (s, e) =>
{
    Console.WriteLine($"연결 상태: {e.IsConnected} - {e.Message}");
};

// 연결
await client.ConnectAsync();

// 해제
client.Disconnect();
client.Dispose();
```

### RTU 클라이언트 생성 및 연결

```csharp
using System.IO.Ports;

var client = ModbusClientFactory.CreateRtu(
    portName: "COM3",
    baudRate: 9600,
    parity: Parity.None,
    dataBits: 8,
    stopBits: StopBits.One,
    timeoutMs: 1000
);

await client.ConnectAsync();
```

> 사용 가능한 포트 목록: `ModbusClientFactory.GetAvailableSerialPorts()`

---

### 단발성 읽기 (Read Once)

```csharp
// FC03 - Holding Register 읽기 (Device ID=1, 주소 100부터 10개)
ushort[] values = await client.ReadHoldingRegistersAsync(
    deviceId: 1, startAddress: 100, count: 10);

// FC04 - Input Register
ushort[] inputs = await client.ReadInputRegistersAsync(1, 0, 5);

// FC01 - Coil 상태 읽기
bool[] coils = await client.ReadCoilsAsync(1, 0, 8);

// FC02 - Discrete Input 읽기
bool[] discs = await client.ReadDiscreteInputsAsync(1, 0, 8);
```

---

### 단발성 쓰기 (Write Once)

```csharp
// FC06 - 단일 레지스터 쓰기 (주소 100에 1234 쓰기)
await client.WriteSingleRegisterAsync(deviceId: 1, address: 100, value: 1234);

// FC10 - 다중 레지스터 쓰기
await client.WriteMultipleRegistersAsync(1, 100, new ushort[] { 10, 20, 30 });

// FC05 - 단일 Coil 쓰기
await client.WriteSingleCoilAsync(1, 0, value: true);

// FC0F - 다중 Coil 쓰기
await client.WriteMultipleCoilsAsync(1, 0, new bool[] { true, false, true, true });
```

---

### Write 후 Read (단발성)

```csharp
// 주소 100에 값을 쓰고, 주소 100부터 3개 읽어서 반환
ushort[] result = await client.WriteAndReadRegistersAsync(
    deviceId: 1,
    writeAddress: 100,
    writeValues: new ushort[] { 100, 200, 300 },
    readAddress: 100,
    readCount: 3
);
```

---

### 실시간 주기적 읽기

```csharp
var periodicService = new ModbusPeriodicService(client);

// 데이터 수신 이벤트 등록
periodicService.DataReceived += (sender, e) =>
{
    // WPF: UI 스레드에서 업데이트
    Dispatcher.Invoke(() =>
    {
        if (e.RegisterValues != null)
        {
            Console.WriteLine($"[{e.Timestamp:HH:mm:ss.fff}] Addr={e.StartAddress}");
            foreach (var v in e.RegisterValues)
                Console.WriteLine($"  값: {v}");
        }
    });
};

// 오류 이벤트
periodicService.ErrorOccurred += (sender, e) =>
{
    Console.WriteLine($"오류 [{e.TaskId}]: {e.Error.Message}");
};

// 주기 읽기 시작 (500ms 간격, FC03, 주소 100부터 10개)
var config = new PeriodicReadConfig
{
    TaskId       = "my-read-task",      // 식별자 (나중에 StopTask 호출 시 사용)
    DeviceId     = 1,
    StartAddress = 100,
    Count        = 10,
    FunctionCode = ModbusFunctionCode.ReadHoldingRegisters,
    Interval     = TimeSpan.FromMilliseconds(500)
};
periodicService.StartPeriodicRead(config);

// 중지
periodicService.StopTask("my-read-task");

// 모두 중지
periodicService.StopAll();
```

---

### 주기적 쓰기

```csharp
var writeConfig = new PeriodicWriteConfig
{
    TaskId          = "my-write-task",
    DeviceId        = 1,
    StartAddress    = 200,
    RegisterValues  = new ushort[] { 1, 2, 3 },
    Interval        = TimeSpan.FromSeconds(1),   // 1초마다 쓰기
    ReadBackAfterWrite = true,                    // 쓴 후 바로 읽기
    ReadBackCount   = 3
};

periodicService.WriteCompleted += (s, e) =>
{
    Dispatcher.Invoke(() => Console.WriteLine($"쓰기 완료: {e.Timestamp:HH:mm:ss}"));
};

periodicService.StartPeriodicWrite(writeConfig);
```

---

### 여러 작업 동시 실행

```csharp
// Task A: 100ms마다 센서 값 읽기
periodicService.StartPeriodicRead(new PeriodicReadConfig
{
    TaskId = "sensor", DeviceId = 1, StartAddress = 0, Count = 5,
    Interval = TimeSpan.FromMilliseconds(100)
});

// Task B: 1초마다 제어 명령 쓰기
periodicService.StartPeriodicWrite(new PeriodicWriteConfig
{
    TaskId = "control", DeviceId = 1, StartAddress = 100,
    RegisterValues = new ushort[] { 500 },
    Interval = TimeSpan.FromSeconds(1)
});

// 개별 중지
periodicService.StopTask("sensor");
periodicService.StopTask("control");

// 실행 중 Task 목록 확인
var runningIds = periodicService.GetRunningTaskIds();
```

---

### 데이터 변환 유틸리티 (DataConverter)

```csharp
using MAK_Modbus.Helpers;

// 레지스터 2개 → float (Big-Endian)
float temp = DataConverter.ToFloat(registers[0], registers[1]);

// float → 레지스터 2개
ushort[] floatRegs = DataConverter.FromFloat(3.14f);
await client.WriteMultipleRegistersAsync(1, 100, floatRegs);

// 레지스터 2개 → Int32
int rpm = DataConverter.ToInt32(registers[2], registers[3]);

// 비트 읽기/쓰기
bool bit3 = DataConverter.GetBit(registers[0], 3);
ushort newVal = DataConverter.SetBit(registers[0], 3, true);
```

---

### WPF 완전한 예제 (ViewModel 패턴)

```csharp
// MainViewModel.cs
public class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IModbusClient _client;
    private readonly ModbusPeriodicService _service;
    private ObservableCollection<string> _log = new();

    public ObservableCollection<string> Log => _log;
    private string _status = "미연결";
    public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }

    public MainViewModel()
    {
        _client = ModbusClientFactory.CreateTcp("192.168.1.100");
        _service = new ModbusPeriodicService(_client);
        _service.DataReceived += OnDataReceived;
        _service.ErrorOccurred += OnError;
    }

    public async Task ConnectAsync()
    {
        await _client.ConnectAsync();
        Status = "연결됨";

        // 즉시 폴링 시작
        _service.StartPeriodicRead(new PeriodicReadConfig
        {
            DeviceId = 1, StartAddress = 0, Count = 10,
            Interval = TimeSpan.FromMilliseconds(200)
        });
    }

    private void OnDataReceived(object? s, ModbusReadEventArgs e)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            if (e.RegisterValues != null)
                _log.Insert(0, $"[{e.Timestamp:HH:mm:ss.fff}] [{string.Join(", ", e.RegisterValues)}]");
            if (_log.Count > 200) _log.RemoveAt(_log.Count - 1);
        });
    }

    private void OnError(object? s, ModbusErrorEventArgs e)
    {
        App.Current.Dispatcher.Invoke(() => Status = $"오류: {e.Error.Message}");
    }

    public void Dispose()
    {
        _service.StopAll();
        _client.Disconnect();
        _service.Dispose();
        _client.Dispose();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
```

---

### 연결 상태 감시 및 자동 재연결 (ModbusConnectionWatcher)

연결이 끊어졌을 때 자동으로 감지하고 재연결을 시도합니다.

```csharp
var client = ModbusClientFactory.CreateTcp("192.168.1.100");
await client.ConnectAsync();

var watcher = new ModbusConnectionWatcher(client)
{
    // ── 연결 감시 설정 ──────────────────────────────
    HeartbeatInterval  = TimeSpan.FromSeconds(2),   // 2초마다 실시간 연결 체크 (짧을수록 빠른 감지)

    // ── 재연결 설정 ─────────────────────────────────
    RetryInterval      = TimeSpan.FromSeconds(5),   // 5초 간격으로 재연결 시도
    MaxRetryCount      = 10,                         // 최대 10회 시도 (-1이면 횟수 제한 없음)
    TotalRetryTimeout  = TimeSpan.FromMinutes(2),    // 2분 넘게 실패하면 포기 (null이면 시간 제한 없음)
};

// MaxRetryCount와 TotalRetryTimeout 중 먼저 도달한 조건으로 중단됩니다.
// 예: 5초 간격으로 10회 = 최대 50초, 하지만 2분 제한이 먼저 도달할 수 있음

// 연결 끊김 감지
watcher.ConnectionLost += (s, e) =>
{
    Dispatcher.Invoke(() => StatusLabel.Content = "연결 끊김 - 재연결 중...");
};

// 재연결 성공
watcher.Reconnected += (s, e) =>
{
    Dispatcher.Invoke(() =>
    {
        StatusLabel.Content = "재연결 성공!";
        // 필요하면 주기 읽기 재시작
        periodicService.StartPeriodicRead(readConfig);
    });
};

// 최대 재시도 초과 (MaxRetryCount 설정 시)
watcher.ReconnectFailed += (s, e) =>
{
    Dispatcher.Invoke(() => StatusLabel.Content = "재연결 포기 - 수동 연결 필요");
};

// 재연결 시도 중 오류 알림
watcher.RetryError += (s, e) =>
{
    Dispatcher.Invoke(() => Log($"재시도 오류: {e.Error.Message}"));
};

// 감시 시작 / 중지
watcher.Start();
watcher.Stop();
```

#### 재연결 시 커스텀 동작 지정

TCP 재연결 시 클라이언트를 새로 만들어야 하는 경우:

```csharp
IModbusClient client = ModbusClientFactory.CreateTcp("192.168.1.100");
await client.ConnectAsync();

var watcher = new ModbusConnectionWatcher(client, reconnectAction: async () =>
{
    // 기존 연결 정리 후 재연결
    client.Disconnect();
    await Task.Delay(500);
    await client.ConnectAsync();
});
watcher.Start();
```

#### 빠른 연결 상태 확인 (헬스 체크)

```csharp
// 실제 소켓 수준 체크 (IsConnected보다 신뢰도 높음)
bool isAlive = client.CheckConnectionHealth();

// IsConnected는 마지막 통신 시점 기준 → 끊겨도 true를 반환할 수 있음
bool basic = client.IsConnected;
```

---

## Function Code 요약표

| FC   | 이름                    | 방향      | 데이터 타입 | 메서드                              |
|------|-------------------------|-----------|-------------|-------------------------------------|
| 0x01 | Read Coils              | 읽기      | bool[]      | `ReadCoilsAsync`                   |
| 0x02 | Read Discrete Inputs    | 읽기전용  | bool[]      | `ReadDiscreteInputsAsync`          |
| 0x03 | Read Holding Registers  | 읽기      | ushort[]    | `ReadHoldingRegistersAsync`        |
| 0x04 | Read Input Registers    | 읽기전용  | ushort[]    | `ReadInputRegistersAsync`          |
| 0x05 | Write Single Coil       | 쓰기      | bool        | `WriteSingleCoilAsync`             |
| 0x06 | Write Single Register   | 쓰기      | ushort      | `WriteSingleRegisterAsync`         |
| 0x0F | Write Multiple Coils    | 쓰기      | bool[]      | `WriteMultipleCoilsAsync`          |
| 0x10 | Write Multiple Registers| 쓰기      | ushort[]    | `WriteMultipleRegistersAsync`      |

---

## Modbus 주소 체계

| 주소 범위     | 영역               | FC (읽기) | FC (쓰기) |
|--------------|-------------------|-----------|-----------|
| 00001~09999  | Coil (Output)     | 01        | 05, 0F    |
| 10001~19999  | Discrete Input    | 02        | -         |
| 30001~39999  | Input Register    | 04        | -         |
| 40001~49999  | Holding Register  | 03        | 06, 10    |

> 코드에서 사용하는 주소는 **0-based offset** 입니다.  
> 예: Holding Register 40001 → `startAddress = 0`

---

## 요구사항

- .NET 6.0 이상
- WPF 예제는 Windows 전용 (`net6.0-windows`)
- NuGet: `System.IO.Ports` (RTU 사용 시)

---

## 빌드 방법

```bash
# 솔루션 전체 빌드
dotnet build MAK_Modbus.sln

# 라이브러리만 빌드
dotnet build src/MAK_Modbus/MAK_Modbus.csproj

# WPF 예제 실행 (Windows에서)
dotnet run --project examples/WpfExample/WpfExample.csproj
```

---

## 라이센스

MIT License
