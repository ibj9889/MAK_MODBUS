namespace MAK_Modbus.Models;

public class ModbusException : Exception
{
    public byte ExceptionCode { get; }
    public string ExceptionDescription { get; }

    public ModbusException(byte exceptionCode)
        : base(GetDescription(exceptionCode))
    {
        ExceptionCode = exceptionCode;
        ExceptionDescription = GetDescription(exceptionCode);
    }

    public ModbusException(string message) : base(message)
    {
        ExceptionCode = 0;
        ExceptionDescription = message;
    }

    private static string GetDescription(byte code) => code switch
    {
        0x01 => "Illegal Function (지원하지 않는 기능 코드)",
        0x02 => "Illegal Data Address (잘못된 데이터 주소)",
        0x03 => "Illegal Data Value (잘못된 데이터 값)",
        0x04 => "Slave Device Failure (슬레이브 장치 오류)",
        0x05 => "Acknowledge (처리 중, 나중에 확인)",
        0x06 => "Slave Device Busy (슬레이브 장치 사용 중)",
        0x08 => "Memory Parity Error (메모리 패리티 오류)",
        0x0A => "Gateway Path Unavailable (게이트웨이 경로 없음)",
        0x0B => "Gateway Target Device Failed (게이트웨이 대상 장치 응답 없음)",
        _ => $"Unknown Modbus Exception Code: 0x{code:X2}"
    };
}
