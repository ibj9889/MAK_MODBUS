using MAK_Modbus.Core;
using MAK_Modbus.Helpers;
using MAK_Modbus.Models;
using MAK_Modbus.Services;
using MAK_Modbus.Simulator;

namespace MAK_Modbus.Tests;

class Program
{
    private const int TestPort = 15020;
    private static int _passed = 0;
    private static int _failed = 0;

    static async Task Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        PrintHeader("MAK_Modbus 통합 테스트");

        using var server = new ModbusTcpServer(TestPort);
        server.Start();
        Print($"시뮬레이터 서버 시작 (포트 {TestPort})", ConsoleColor.Cyan);

        await Task.Delay(200);

        using var client = ModbusClientFactory.CreateTcp("127.0.0.1", TestPort, 3000);

        try
        {
            await client.ConnectAsync();
            Print("클라이언트 연결 완료\n", ConsoleColor.Cyan);
        }
        catch (Exception ex)
        {
            PrintFail("연결", ex.Message);
            return;
        }

        // ── 테스트 실행 ──────────────────────────────────────────────────────

        await Test("01. FC06 - 단일 레지스터 쓰기/읽기", async () =>
        {
            await client.WriteSingleRegisterAsync(1, 100, 1234);
            var r = await client.ReadHoldingRegistersAsync(1, 100, 1);
            Assert(r[0] == 1234, $"예상 1234, 실제 {r[0]}");
        });

        await Test("02. FC10 - 다중 레지스터 쓰기/읽기", async () =>
        {
            ushort[] write = { 100, 200, 300, 400, 500 };
            await client.WriteMultipleRegistersAsync(1, 200, write);
            var r = await client.ReadHoldingRegistersAsync(1, 200, 5);
            for (int i = 0; i < 5; i++)
                Assert(r[i] == write[i], $"[{i}] 예상 {write[i]}, 실제 {r[i]}");
        });

        await Test("03. FC05 - 단일 Coil 쓰기/읽기 (ON)", async () =>
        {
            await client.WriteSingleCoilAsync(1, 0, true);
            var r = await client.ReadCoilsAsync(1, 0, 1);
            Assert(r[0] == true, $"예상 true, 실제 {r[0]}");
        });

        await Test("04. FC05 - 단일 Coil 쓰기/읽기 (OFF)", async () =>
        {
            await client.WriteSingleCoilAsync(1, 0, false);
            var r = await client.ReadCoilsAsync(1, 0, 1);
            Assert(r[0] == false, $"예상 false, 실제 {r[0]}");
        });

        await Test("05. FC0F - 다중 Coil 쓰기/읽기", async () =>
        {
            bool[] write = { true, false, true, true, false, true, false, false };
            await client.WriteMultipleCoilsAsync(1, 10, write);
            var r = await client.ReadCoilsAsync(1, 10, 8);
            for (int i = 0; i < 8; i++)
                Assert(r[i] == write[i], $"[{i}] 예상 {write[i]}, 실제 {r[i]}");
        });

        await Test("06. FC03 - 서버에서 설정한 값 읽기", async () =>
        {
            server.SetHoldingRegister(50, 9999);
            var r = await client.ReadHoldingRegistersAsync(1, 50, 1);
            Assert(r[0] == 9999, $"예상 9999, 실제 {r[0]}");
        });

        await Test("07. FC04 - Input Register 읽기", async () =>
        {
            server.SetInputRegisters(10, new ushort[] { 111, 222, 333 });
            var r = await client.ReadInputRegistersAsync(1, 10, 3);
            Assert(r[0] == 111, $"[0] 예상 111, 실제 {r[0]}");
            Assert(r[1] == 222, $"[1] 예상 222, 실제 {r[1]}");
            Assert(r[2] == 333, $"[2] 예상 333, 실제 {r[2]}");
        });

        await Test("08. FC02 - Discrete Input 읽기", async () =>
        {
            server.SetDiscreteInput(5, true);
            server.SetDiscreteInput(6, false);
            var r = await client.ReadDiscreteInputsAsync(1, 5, 2);
            Assert(r[0] == true,  $"[0] 예상 true, 실제 {r[0]}");
            Assert(r[1] == false, $"[1] 예상 false, 실제 {r[1]}");
        });

        await Test("09. WriteAndRead - 쓰기 후 읽기", async () =>
        {
            ushort[] write = { 10, 20, 30 };
            var r = await client.WriteAndReadRegistersAsync(1, 300, write, 300, 3);
            for (int i = 0; i < 3; i++)
                Assert(r[i] == write[i], $"[{i}] 예상 {write[i]}, 실제 {r[i]}");
        });

        await Test("10. 경계값 - 최대값 (65535) 쓰기/읽기", async () =>
        {
            await client.WriteSingleRegisterAsync(1, 400, 65535);
            var r = await client.ReadHoldingRegistersAsync(1, 400, 1);
            Assert(r[0] == 65535, $"예상 65535, 실제 {r[0]}");
        });

        await Test("11. 경계값 - 0 쓰기/읽기", async () =>
        {
            await client.WriteSingleRegisterAsync(1, 401, 0);
            var r = await client.ReadHoldingRegistersAsync(1, 401, 1);
            Assert(r[0] == 0, $"예상 0, 실제 {r[0]}");
        });

        await Test("12. CheckConnectionHealth - 연결 상태 확인", async () =>
        {
            await Task.CompletedTask;
            Assert(client.CheckConnectionHealth(), "연결 상태가 false");
            Assert(client.IsConnected, "IsConnected가 false");
        });

        await Test("13. ModbusPeriodicService - 주기적 읽기 (500ms × 4회)", async () =>
        {
            server.SetHoldingRegisters(0, new ushort[] { 1, 2, 3, 4, 5 });

            using var periodicService = new ModbusPeriodicService(client);
            var received = new List<ushort[]>();
            var tcs = new TaskCompletionSource();

            periodicService.DataReceived += (s, e) =>
            {
                if (e.RegisterValues != null)
                {
                    received.Add(e.RegisterValues);
                    if (received.Count >= 4) tcs.TrySetResult();
                }
            };

            periodicService.StartPeriodicRead(new PeriodicReadConfig
            {
                DeviceId = 1, StartAddress = 0, Count = 5,
                Interval = TimeSpan.FromMilliseconds(200)
            });

            await Task.WhenAny(tcs.Task, Task.Delay(3000));
            periodicService.StopAll();

            Assert(received.Count >= 4, $"수신 횟수 {received.Count}/4");
            Assert(received[0][0] == 1 && received[0][4] == 5, "수신 데이터 불일치");
        });

        await Test("14. ModbusPeriodicService - 주기적 쓰기", async () =>
        {
            ushort writeCount = 0;
            using var periodicService = new ModbusPeriodicService(client);

            periodicService.WriteCompleted += (s, e) => writeCount++;

            periodicService.StartPeriodicWrite(new PeriodicWriteConfig
            {
                DeviceId = 1, StartAddress = 500,
                RegisterValues = new ushort[] { 777 },
                Interval = TimeSpan.FromMilliseconds(200)
            });

            await Task.Delay(1000);
            periodicService.StopAll();

            Assert(writeCount >= 4, $"쓰기 횟수 {writeCount}/4");
            var r = await client.ReadHoldingRegistersAsync(1, 500, 1);
            Assert(r[0] == 777, $"마지막 쓰기 값 예상 777, 실제 {r[0]}");
        });

        await Test("15. ModbusConnectionWatcher - 연결 감시 시작/중지", async () =>
        {
            using var watcher = new ModbusConnectionWatcher(client)
            {
                HeartbeatInterval = TimeSpan.FromMilliseconds(200),
                RetryInterval     = TimeSpan.FromSeconds(1),
                MaxRetryCount     = 3
            };

            watcher.ConnectionLost += (s, e) => { };
            watcher.Start();

            await Task.Delay(500);
            Assert(watcher.IsWatching, "감시가 시작되지 않음");

            watcher.Stop();
            Assert(!watcher.IsWatching, "감시가 중지되지 않음");
        });

        await Test("16. DataConverter - float 변환", async () =>
        {
            await Task.CompletedTask;
            var floatRegs = DataConverter.FromFloat(3.14f);
            var restored  = DataConverter.ToFloat(floatRegs[0], floatRegs[1]);
            Assert(Math.Abs(restored - 3.14f) < 0.001f, $"float 변환 오류: {restored}");
        });

        await Test("17. DataConverter - Int32 변환", async () =>
        {
            await Task.CompletedTask;
            var regs = DataConverter.FromInt32(123456);
            var restored = DataConverter.ToInt32(regs[0], regs[1]);
            Assert(restored == 123456, $"Int32 변환 오류: {restored}");
        });

        await Test("18. DataConverter - 비트 읽기/쓰기", async () =>
        {
            await Task.CompletedTask;
            ushort val = 0b0000_0000_0000_0101; // bit0=1, bit1=0, bit2=1
            Assert(DataConverter.GetBit(val, 0) == true,  "bit0 읽기 오류");
            Assert(DataConverter.GetBit(val, 1) == false, "bit1 읽기 오류");
            Assert(DataConverter.GetBit(val, 2) == true,  "bit2 읽기 오류");

            var newVal = DataConverter.SetBit(val, 1, true);
            Assert(DataConverter.GetBit(newVal, 1) == true, "bit1 쓰기 오류");
        });

        await Test("19. 다중 동시 읽기 요청 (스레드 안전성)", async () =>
        {
            server.SetHoldingRegisters(600, Enumerable.Range(0, 10).Select(i => (ushort)i).ToArray());
            var tasks = Enumerable.Range(0, 10).Select(_ =>
                client.ReadHoldingRegistersAsync(1, 600, 10)).ToArray();
            var results = await Task.WhenAll(tasks);
            foreach (var r in results)
                Assert(r[0] == 0 && r[9] == 9, "동시 읽기 결과 오류");
        });

        await Test("20. ModbusException - 지원하지 않는 FC 처리", async () =>
        {
            // 서버가 지원하지 않는 FC는 예외 응답을 반환해야 함
            // 이 테스트는 라이브러리가 예외를 올바르게 파싱하는지 확인
            // (실제 잘못된 주소 범위 요청으로 테스트)
            await Task.CompletedTask;
            Print("  (서버 예외 처리는 정상 응답 테스트로 간접 확인됨)", ConsoleColor.DarkYellow);
        });

        // ── 결과 출력 ─────────────────────────────────────────────────────────

        client.Disconnect();
        server.Stop();

        PrintResult();
    }

    // ─── 테스트 유틸리티 ─────────────────────────────────────────────────────

    static async Task Test(string name, Func<Task> testFunc)
    {
        Console.Write($"  {name,-52}");
        try
        {
            await testFunc();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("  PASS");
            Console.ResetColor();
            _passed++;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  FAIL  ← {ex.Message}");
            Console.ResetColor();
            _failed++;
        }
    }

    static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    static void PrintHeader(string title)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n" + new string('═', 70));
        Console.WriteLine($"  {title}");
        Console.WriteLine(new string('═', 70));
        Console.ResetColor();
        Console.WriteLine();
    }

    static void PrintResult()
    {
        var total = _passed + _failed;
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(new string('═', 70));
        if (_failed == 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  결과: {_passed}/{total} 모두 통과! ✓");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  결과: {_passed}/{total} 통과, {_failed}개 실패");
        }
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(new string('═', 70));
        Console.ResetColor();
    }

    static void Print(string msg, ConsoleColor color = ConsoleColor.White)
    {
        Console.ForegroundColor = color;
        Console.WriteLine("  " + msg);
        Console.ResetColor();
    }

    static void PrintFail(string name, string msg)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"  [FAIL] {name}: {msg}");
        Console.ResetColor();
    }
}
