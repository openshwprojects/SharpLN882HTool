using System;
using System.IO;
using System.IO.Ports;
using System.Text;

public class YModem
{
    SerialPort port;
    byte header_pad = 0x00;
    byte data_pad = 0x1A;

    const byte SOH = 0x01;
    const byte STX = 0x02;
    const byte EOT = 0x04;
    const byte ACK = 0x06;
    const byte NAK = 0x15;
    const byte CAN = 0x18;
    const byte CRC = (byte)'C';

    public YModem(SerialPort port)
    {
        this.port = port;
    }

    public void abort(int count = 2)
    {
        for (int i = 0; i < count; i++)
            port.Write(new byte[] { CAN }, 0, 1);
    }

    public int send_file(string file_path, bool packet_size_16k = true, int retry = 20, object callback = null)
    {
        FileStream file_stream = null;
        try
        {
            file_stream = new FileStream(file_path, FileMode.Open, FileAccess.Read);
            string file_name = Path.GetFileName(file_path);
            long file_size = new FileInfo(file_path).Length;
            return send(file_stream, file_name, file_size, packet_size_16k, retry, callback);
        }
        catch (IOException e)
        {
            Console.WriteLine("ERROR: " + e.Message);
            return -1;
        }
        finally
        {
            if (file_stream != null)
                file_stream.Close();
        }
    }

    public int wait_for_next(byte ch)
    {
        int cancel_count = 0;
        while (true)
        {
            int c = read_byte_timeout(10000);
            if (c == -1) return -1;
            if (c == ch) break;
            else if (c == CAN)
            {
                cancel_count++;
                if (cancel_count == 2)
                    return -1;
            }
        }
        return 0;
    }

    public int send(FileStream data_stream, string data_name, long data_size, bool packet_size_16k = true, int retry = 20, object callback = null)
    {
        int packet_size = packet_size_16k ? 4096 * 4 : 1024;

        Console.WriteLine("YModem::send: v2 will wait for CRC...");
        if (wait_for_next(CRC) != 0)
        {
            Console.WriteLine("YModem::send: v2 wait_for_next CRC failed!");
            return -1;
        }
        Console.WriteLine("YModem::send: v2 wait_for_next CRC ok!");

        byte[] header = _make_edge_packet_header();

        if (data_name.Length > 100)
            data_name = data_name.Substring(0, 100);

        string data_size_str = data_size.ToString();
        if (data_size_str.Length > 20)
            return -2;

        string meta = data_name + '\0' + data_size_str + '\0';
        byte[] meta_bytes = Encoding.ASCII.GetBytes(meta);
        byte[] packet0 = new byte[128];
        Array.Copy(meta_bytes, packet0, meta_bytes.Length);
        for (int i = meta_bytes.Length; i < 128; i++)
            packet0[i] = header_pad;

        byte[] data_for_send = BuildPacket(header, packet0);
        Console.WriteLine("YModem::send: first packet " + BitConverter.ToString(data_for_send).Replace("-", " "));
        port.Write(data_for_send, 0, data_for_send.Length);

        Console.WriteLine("YModem::send: v2 will wait for ACK...");
        if (wait_for_next(ACK) != 0)
        {
            Console.WriteLine("YModem::send: v2 wait_for_next ACK failed!");
            return -1;
        }
        Console.WriteLine("YModem::send: v2 wait_for_next ACK ok!");

        Console.WriteLine("YModem::send: v2 will wait for second CRC...");
        if (wait_for_next(CRC) != 0)
        {
            Console.WriteLine("YModem::send: v2 wait_for_next second CRC failed!");
            return -1;
        }
        Console.WriteLine("YModem::send: v2 wait_for_next second CRC ok!");

        int sequence = 1;
        byte[] buffer = new byte[packet_size];

        while (true)
        {
            Console.WriteLine("YModem::send: at " + data_stream.Position+"!");

            int read = data_stream.Read(buffer, 0, packet_size);
            if (read == 0)
                break;

            if (read <= 128)
                packet_size = 128;

            byte[] data = new byte[packet_size];
            Array.Copy(buffer, 0, data, 0, read);
            for (int i = read; i < packet_size; i++)
                data[i] = data_pad;

            header = _make_data_packet_header(packet_size, sequence);
            data_for_send = BuildPacket(header, data);

            int error_count = 0;
            while (true)
            {
                port.Write(data_for_send, 0, data_for_send.Length);
                int c = read_byte_timeout(10000);
                if (c == ACK)
                    break;
                else
                {
                    error_count++;
                    if (error_count > retry)
                    {
                        abort();
                        return -2;
                    }
                }
            }

            sequence = (sequence + 1) % 0x100;
        }
        Console.WriteLine("YModem::send: sending loop done!");

        port.Write(new byte[] { EOT }, 0, 1);
        if (wait_for_next(NAK) != 0) return -1;
        port.Write(new byte[] { EOT }, 0, 1);
        if (wait_for_next(ACK) != 0) return -1;
        if (wait_for_next(CRC) != 0) return -1;

        header = _make_edge_packet_header();
        byte[] final = new byte[128];
        for (int i = 0; i < 128; i++) final[i] = header_pad;
        data_for_send = BuildPacket(header, final);
        port.Write(data_for_send, 0, data_for_send.Length);
        if (wait_for_next(ACK) != 0) return -1;

        return (int)data_size;
    }

    public int recv_file(string root_path, object callback = null)
    {
        while (true)
        {
            port.Write(new byte[] { CRC }, 0, 1);
            int c = read_byte_timeout(10000);
            if (c == SOH)
                break;
            if (c == STX)
                break;
        }

        bool IS_FIRST_PACKET = true;
        bool FIRST_PACKET_RECEIVED = false;
        bool WAIT_FOR_EOT = false;
        bool WAIT_FOR_END_PACKET = false;
        int sequence = 0;

        FileStream file_stream = null;
        int received_bytes = 0;

        while (true)
        {
            if (WAIT_FOR_EOT)
            {
                wait_for_eot();
                WAIT_FOR_EOT = false;
                WAIT_FOR_END_PACKET = true;
                sequence = 0;
            }
            else
            {
                if (!IS_FIRST_PACKET)
                {
                    int h = wait_for_header();
                    if (h == -1) return -1;
                }
                else
                    IS_FIRST_PACKET = false;

                int seq = read_byte_timeout(5000);
                int seq_oc = read_byte_timeout(5000);
                int packet_size = 1024;
                if (seq == -1 || seq_oc == -1) return -1;

                int header_type = port.ReadByte();
                if (header_type == SOH)
                    packet_size = 128;
                else if (header_type == STX)
                    packet_size = 1024;

                byte[] data = new byte[packet_size + 2];
                int read = port.Read(data, 0, packet_size + 2);
                if (read != packet_size + 2)
                    continue;

                if (seq != (0xFF - seq_oc)) continue;
                if (seq != sequence) continue;

                bool valid = _verify_recv_checksum(data, out byte[] valid_data);
                if (!valid) continue;

                if (seq == 0 && !FIRST_PACKET_RECEIVED && !WAIT_FOR_END_PACKET)
                {
                    port.Write(new byte[] { ACK }, 0, 1);
                    port.Write(new byte[] { CRC }, 0, 1);

                    string[] parts = Encoding.ASCII.GetString(valid_data).TrimEnd('\0').Split('\0');
                    string file_name = parts[0];
                    int file_size = int.Parse(parts[1]);
                    file_stream = new FileStream(Path.Combine(root_path, file_name), FileMode.Create);
                    FIRST_PACKET_RECEIVED = true;
                    sequence = (sequence + 1) % 0x100;
                }
                else if (!WAIT_FOR_END_PACKET)
                {
                    if (file_stream == null)
                        return -2;

                    file_stream.Write(valid_data, 0, valid_data.Length);
                    received_bytes += valid_data.Length;
                    port.Write(new byte[] { ACK }, 0, 1);
                    sequence = (sequence + 1) % 0x100;
                    if (received_bytes >= file_stream.Length)
                        WAIT_FOR_EOT = true;
                }
                else
                {
                    port.Write(new byte[] { ACK }, 0, 1);
                    break;
                }
            }
        }

        if (file_stream != null)
            file_stream.Close();
        return received_bytes;
    }

    public int wait_for_header()
    {
        int cancel_count = 0;
        while (true)
        {
            int c = read_byte_timeout(10000);
            if (c == -1) return -1;
            if (c == SOH || c == STX) return c;
            else if (c == CAN)
            {
                cancel_count++;
                if (cancel_count == 2)
                    return -1;
            }
        }
    }

    public void wait_for_eot()
    {
        int eot_count = 0;
        while (true)
        {
            int c = read_byte_timeout(10000);
            if (c == EOT)
            {
                eot_count++;
                if (eot_count == 1)
                    port.Write(new byte[] { NAK }, 0, 1);
                else if (eot_count == 2)
                {
                    port.Write(new byte[] { ACK }, 0, 1);
                    port.Write(new byte[] { CRC }, 0, 1);
                    break;
                }
            }
        }
    }

    public byte[] _make_edge_packet_header()
    {
        return new byte[] { SOH, 0x00, 0xFF };
    }

    public byte[] _make_data_packet_header(int packet_size, int sequence)
    {
        byte marker = (packet_size == 128) ? SOH : STX;
        return new byte[] { marker, (byte)sequence, (byte)(0xFF - sequence) };
    }

    public byte[] BuildPacket(byte[] header, byte[] data)
    {
        ushort crc = calc_crc(data);
        byte[] packet = new byte[header.Length + data.Length + 2];
        Array.Copy(header, 0, packet, 0, header.Length);
        Array.Copy(data, 0, packet, header.Length, data.Length);
        packet[header.Length + data.Length] = (byte)(crc >> 8);
        packet[header.Length + data.Length + 1] = (byte)(crc & 0xFF);
        return packet;
    }

    public bool _verify_recv_checksum(byte[] packet, out byte[] data)
    {
        int len = packet.Length - 2;
        data = new byte[len];
        Array.Copy(packet, 0, data, 0, len);
        ushort their_crc = (ushort)((packet[len] << 8) | packet[len + 1]);
        ushort our_crc = calc_crc(data);
        return their_crc == our_crc;
    }

    public int read_byte_timeout(int timeout)
    {
        int prevTimeout = port.ReadTimeout;
        port.ReadTimeout = timeout;
        try { return port.ReadByte(); }
        catch { return -1; }
        finally { port.ReadTimeout = prevTimeout; }
    }

    public static ushort calc_crc(byte[] data)
    {
        ushort crc = 0;
        foreach (byte b in data)
        {
            int i = ((crc >> 8) ^ b) & 0xFF;
            crc = (ushort)((crc << 8) ^ crctable[i]);
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
