namespace MAK_Modbus.Helpers;

public static class DataConverter
{
    /// <summary>ushort 레지스터 2개를 float (Big-Endian)으로 변환</summary>
    public static float ToFloat(ushort highWord, ushort lowWord)
    {
        var bytes = new byte[]
        {
            (byte)(highWord >> 8), (byte)(highWord & 0xFF),
            (byte)(lowWord >> 8), (byte)(lowWord & 0xFF)
        };
        return BitConverter.ToSingle(bytes, 0);
    }

    /// <summary>float를 ushort 2개 배열 (Big-Endian)로 변환</summary>
    public static ushort[] FromFloat(float value)
    {
        var bytes = BitConverter.GetBytes(value);
        return new ushort[]
        {
            (ushort)((bytes[0] << 8) | bytes[1]),
            (ushort)((bytes[2] << 8) | bytes[3])
        };
    }

    /// <summary>ushort 레지스터 2개를 int32 (Big-Endian)로 변환</summary>
    public static int ToInt32(ushort highWord, ushort lowWord)
    {
        return (highWord << 16) | lowWord;
    }

    /// <summary>int32를 ushort 2개 배열로 변환</summary>
    public static ushort[] FromInt32(int value)
    {
        return new ushort[]
        {
            (ushort)(value >> 16),
            (ushort)(value & 0xFFFF)
        };
    }

    /// <summary>ushort 레지스터 2개를 uint32로 변환</summary>
    public static uint ToUInt32(ushort highWord, ushort lowWord)
    {
        return (uint)((highWord << 16) | lowWord);
    }

    /// <summary>ushort 2개로 ASCII 문자열 변환 (2바이트 = 문자 2개)</summary>
    public static string ToAsciiString(ushort[] registers)
    {
        var bytes = new List<byte>();
        foreach (var reg in registers)
        {
            bytes.Add((byte)(reg >> 8));
            bytes.Add((byte)(reg & 0xFF));
        }
        return System.Text.Encoding.ASCII.GetString(bytes.ToArray()).TrimEnd('\0');
    }

    /// <summary>ushort 배열의 특정 비트 상태 확인</summary>
    public static bool GetBit(ushort value, int bitIndex)
    {
        return (value & (1 << bitIndex)) != 0;
    }

    /// <summary>ushort 값의 특정 비트 설정/해제</summary>
    public static ushort SetBit(ushort value, int bitIndex, bool state)
    {
        if (state)
            return (ushort)(value | (1 << bitIndex));
        else
            return (ushort)(value & ~(1 << bitIndex));
    }
}
