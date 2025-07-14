using System;
using System.IO;
using System.IO.Ports;
using System.Text;

public class YModem
{
    SerialPort _port;

    const byte SOH = 0x01;
    const byte STX = 0x02;
    const byte EOT = 0x04;
    const byte ACK = 0x06;
    const byte NAK = 0x15;
    const byte CAN = 0x18;
    const byte CRC = (byte)'C';

    public YModem(SerialPort port)
    {
        _port = port;
    }

    public int send_file(string filePath, bool packet16k, int retry)
    {
        FileStream fileStream = null;
        try
        {
            fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            string fileName = Path.GetFileName(filePath);
            long fileSize = fileStream.Length;

            return send(fileStream, fileName, fileSize, packet16k, retry);
        }
        catch (IOException e)
        {
            Console.WriteLine("YModem::send_file: error " + e.Message);
            return -1;
        }
        finally
        {
            if (fileStream != null)
                fileStream.Close();
        }
    }

    private int send(FileStream dataStream, string dataName, long dataSize, bool packet16k, int retry)
    {
        int packetSize = packet16k ? 4096 * 4 : 1024;

        Console.WriteLine("YModem::send: waiting for CRC...");
        if (wait_for_next(CRC) != 0)
        {
            Console.WriteLine("YModem::send: waiting for CRC failed!");
            return -1;
        }
        Console.WriteLine("YModem::send: waiting for CRC ok!");

        if (dataName.Length > 100)
            dataName = dataName.Substring(0, 100);

        byte[] nameBytes = Encoding.ASCII.GetBytes(dataName + '\0');
        byte[] sizeBytes = Encoding.ASCII.GetBytes(dataSize.ToString() + '\0');
        byte[] data = new byte[128];
        Array.Copy(nameBytes, 0, data, 0, nameBytes.Length);
        Array.Copy(sizeBytes, 0, data, nameBytes.Length, sizeBytes.Length);

        byte[] header = make_packet_header(0, 128);
        byte[] packet0 = BuildPacket(header, data, 128);
        _port.Write(packet0, 0, packet0.Length);

        Console.WriteLine("YModem::send: waiting for ACK...");
        if (wait_for_next(ACK) != 0)
        {
            Console.WriteLine("YModem::send: waiting for ACK failed!");
            return -1;
        }
        Console.WriteLine("YModem::send: waiting for ACK ok!");


        Console.WriteLine("YModem::send: waiting for second CRC...");
        if (wait_for_next(CRC) != 0)
        {
            Console.WriteLine("YModem::send: waiting for second CRC failed!");
            return -1;
        }
        Console.WriteLine("YModem::send: waiting for second CRC ok!");



        int sequence = 1;
        byte[] buffer = new byte[packetSize];

        while (true)
        {
            Console.WriteLine("YModem::send: at " + dataStream.Position + "!");
            int read = dataStream.Read(buffer, 0, packetSize);
            if (read == 0)
                break;

            byte[] actual = new byte[packetSize];
            Array.Copy(buffer, 0, actual, 0, read);
            for (int i = read; i < packetSize; i++)
                actual[i] = 0x1A;

            header = make_packet_header(sequence, packetSize);
            byte[] dataPacket = BuildPacket(header, actual, packetSize);

            bool acked = false;
            for (int r = 0; r < retry && !acked; r++)
            {
                _port.Write(dataPacket, 0, dataPacket.Length);
                int resp = read_byte_timeout(5000);
                if (resp == ACK)
                    acked = true;
            }

            if (!acked)
            {
                abort();
                return -2;
            }

            sequence = (sequence + 1) % 0x100;
        }

        _port.Write(new byte[] { EOT }, 0, 1);
        if (wait_for_next(NAK) != 0)
            return -1;
        _port.Write(new byte[] { EOT }, 0, 1);
        if (wait_for_next(ACK) != 0 || wait_for_next(CRC) != 0)
            return -1;

        byte[] lastHeader = make_packet_header(0, 128);
        byte[] lastData = new byte[128];
        byte[] endPacket = BuildPacket(lastHeader, lastData, 128);
        _port.Write(endPacket, 0, endPacket.Length);
        if (wait_for_next(ACK) != 0)
            return -1;

        return (int)dataSize;
    }

    private int wait_for_next(byte expected)
    {
        int cancel_count = 0;
        while (true)
        {
            int b = read_byte_timeout(10000);
            if (b == -1)
                return -1;
            if (b == expected)
                return 0;
            if (b == CAN)
            {
                cancel_count++;
                if (cancel_count == 2)
                    return -1;
            }
        }
    }

    private int read_byte_timeout(int timeoutMs)
    {
        int orig = _port.ReadTimeout;
        _port.ReadTimeout = timeoutMs;
        try { return _port.ReadByte(); }
        catch { return -1; }
        finally { _port.ReadTimeout = orig; }
    }

    private byte[] make_packet_header(int seq, int size)
    {
        byte headerType = size == 128 ? SOH : STX;
        return new byte[] { headerType, (byte)seq, (byte)(0xFF - seq) };
    }

    private byte[] BuildPacket(byte[] header, byte[] data, int size)
    {
        byte[] packet = new byte[header.Length + size + 2];
        Array.Copy(header, 0, packet, 0, header.Length);
        Array.Copy(data, 0, packet, header.Length, size);
        ushort crc = calc_crc(data);
        packet[header.Length + size] = (byte)(crc >> 8);
        packet[header.Length + size + 1] = (byte)(crc & 0xFF);
        return packet;
    }

    private void abort()
    {
        _port.Write(new byte[] { CAN, CAN }, 0, 2);
    }

    public static ushort calc_crc(byte[] data)
    {
        ushort crc = 0;
        foreach (byte b in data)
        {
            crc = (ushort)((crc << 8) ^ crctable[((crc >> 8) ^ b) & 0xFF]);
        }
        return crc;
    }

    static readonly ushort[] crctable = new ushort[256]
    {
        0x0000, 0x1021, 0x2042, 0x3063, 0x4084, 0x50A5, 0x60C6, 0x70E7,
        0x8108, 0x9129, 0xA14A, 0xB16B, 0xC18C, 0xD1AD, 0xE1CE, 0xF1EF,
        0x1231, 0x0210, 0x3273, 0x2252, 0x52B5, 0x4294, 0x72F7, 0x62D6,
        0x9339, 0x8318, 0xB37B, 0xA35A, 0xD3BD, 0xC39C, 0xF3FF, 0xE3DE,
        0x2462, 0x3443, 0x0420, 0x1401, 0x64E6, 0x74C7, 0x44A4, 0x5485,
        0xA56A, 0xB54B, 0x8528, 0x9509, 0xE5EE, 0xF5CF, 0xC5AC, 0xD58D,
        0x3653, 0x2672, 0x1611, 0x0630, 0x76D7, 0x66F6, 0x5695, 0x46B4,
        0xB75B, 0xA77A, 0x9719, 0x8738, 0xF7DF, 0xE7FE, 0xD79D, 0xC7BC,
        0x48C4, 0x58E5, 0x6886, 0x78A7, 0x0840, 0x1861, 0x2802, 0x3823,
        0xC9CC, 0xD9ED, 0xE98E, 0xF9AF, 0x8948, 0x9969, 0xA90A, 0xB92B,
        0x5AF5, 0x4AD4, 0x7AB7, 0x6A96, 0x1A71, 0x0A50, 0x3A33, 0x2A12,
        0xDBFD, 0xCBDC, 0xFBFF, 0xEBDE, 0x9B99, 0x8BB8, 0xBBFB, 0xABDA,
        0x6CA6, 0x7C87, 0x4CE4, 0x5CC5, 0x2C22, 0x3C03, 0x0C60, 0x1C41,
        0xEDA0, 0xFD81, 0xCDE2, 0xDDC3, 0xAD24, 0xBD05, 0x8D66, 0x9D47,
        0x7E91, 0x6EB0, 0x5ED3, 0x4EF2, 0x3E15, 0x2E34, 0x1E57, 0x0E76,
        0xFF99, 0xEFb8, 0xDFDB, 0xCFFA, 0xBF1D, 0xAF3C, 0x9F5F, 0x8F7E,
        0x9188, 0x81A9, 0xB1CA, 0xA1EB, 0xD10C, 0xC12D, 0xF14E, 0xE16F,
        0x1080, 0x00A1, 0x30C2, 0x20E3, 0x5004, 0x4025, 0x7046, 0x6067,
        0x83B9, 0x9398, 0xA3FB, 0xB3DA, 0xC33D, 0xD31C, 0xE37F, 0xF35E,
        0x02B1, 0x1290, 0x22F3, 0x32D2, 0x4235, 0x5214, 0x6277, 0x7256,
        0xB5EA, 0xA5CB, 0x95A8, 0x8589, 0xF56E, 0xE54F, 0xD52C, 0xC50D,
        0x34E2, 0x24C3, 0x14A0, 0x0481, 0x7466, 0x6447, 0x5424, 0x4405,
        0xA7DB, 0xB7FA, 0x8799, 0x97B8, 0xE75F, 0xF77E, 0xC71D, 0xD73C,
        0x26D3, 0x36F2, 0x0691, 0x16B0, 0x6657, 0x7676, 0x4615, 0x5634,
        0xD94C, 0xC96D, 0xF90E, 0xE92F, 0x99C8, 0x89E9, 0xB98A, 0xA9AB,
        0x5844, 0x4865, 0x7806, 0x6827, 0x18C0, 0x08E1, 0x3882, 0x28A3,
        0xCB7D, 0xDB5C, 0xEB3F, 0xFB1E, 0x8BF9, 0x9BD8, 0xABBB, 0xBB9A,
        0x4A75, 0x5A54, 0x6A37, 0x7A16, 0x0AF1, 0x1AD0, 0x2AB3, 0x3A92,
        0xFD2E, 0xED0F, 0xDD6C, 0xCD4D, 0xBDAA, 0xAD8B, 0x9DE8, 0x8DC9,
        0x7C26, 0x6C07, 0x5C64, 0x4C45, 0x3CA2, 0x2C83, 0x1CE0, 0x0CC1,
        0xEF1F, 0xFF3E, 0xCF5D, 0xDF7C, 0xAF9B, 0xBFBA, 0x8FD9, 0x9FF8,
        0x6E17, 0x7E36, 0x4E55, 0x5E74, 0x2E93, 0x3EB2, 0x0ED1, 0x1EF0
    };
}
