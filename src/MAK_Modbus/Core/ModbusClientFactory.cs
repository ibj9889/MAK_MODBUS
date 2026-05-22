using System.IO.Ports;

namespace MAK_Modbus.Core;

/// <summary>
/// Modbus 클라이언트를 쉽게 생성하는 팩토리 클래스
/// </summary>
public static class ModbusClientFactory
{
    /// <summary>Modbus TCP 클라이언트 생성</summary>
    /// <param name="host">PLC/장치 IP 주소 또는 호스트명</param>
    /// <param name="port">포트번호 (기본값 502)</param>
    /// <param name="timeoutMs">타임아웃 (ms, 기본값 3000)</param>
    public static ModbusTcpClient CreateTcp(string host, int port = 502, int timeoutMs = 3000)
        => new(host, port, timeoutMs);

    /// <summary>Modbus RTU 클라이언트 생성</summary>
    /// <param name="portName">시리얼 포트 (예: "COM3", "/dev/ttyUSB0")</param>
    /// <param name="baudRate">통신 속도 (기본값 9600)</param>
    /// <param name="parity">패리티 (기본값 None)</param>
    /// <param name="dataBits">데이터 비트 (기본값 8)</param>
    /// <param name="stopBits">스톱 비트 (기본값 One)</param>
    /// <param name="timeoutMs">타임아웃 (ms, 기본값 1000)</param>
    public static ModbusRtuClient CreateRtu(
        string portName,
        int baudRate = 9600,
        Parity parity = Parity.None,
        int dataBits = 8,
        StopBits stopBits = StopBits.One,
        int timeoutMs = 1000)
        => new(portName, baudRate, parity, dataBits, stopBits, timeoutMs);

    /// <summary>사용 가능한 시리얼 포트 목록 반환</summary>
    public static string[] GetAvailableSerialPorts()
        => SerialPort.GetPortNames();
}
