namespace MAK_Modbus.Helpers;

internal static class CrcHelper
{
    /// <summary>
    /// Modbus RTU CRC16 계산 (다항식 0xA001)
    /// </summary>
    internal static ushort Calculate(byte[] data, int length)
    {
        ushort crc = 0xFFFF;
        for (int i = 0; i < length; i++)
        {
            crc ^= data[i];
            for (int j = 0; j < 8; j++)
            {
                if ((crc & 0x0001) != 0)
                {
                    crc >>= 1;
                    crc ^= 0xA001;
                }
                else
                {
                    crc >>= 1;
                }
            }
        }
        return crc;
    }

    internal static bool Validate(byte[] frame, int dataLength)
    {
        if (dataLength < 4) return false;
        var expected = Calculate(frame, dataLength - 2);
        var actual = (ushort)(frame[dataLength - 2] | (frame[dataLength - 1] << 8));
        return expected == actual;
    }
}
