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
