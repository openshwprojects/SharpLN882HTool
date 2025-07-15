using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LN882HTool
{
    public class LN882HFlasher
    {
        private SerialPort _port;

        public LN882HFlasher(string portName, int baudRate, int timeoutMs = 10000)
        {
            try
            {
                Console.WriteLine("Opening port " + portName + "...");
                _port = new SerialPort(portName, baudRate);
                _port.ReadTimeout = timeoutMs;
                _port.WriteTimeout = timeoutMs;
                _port.Open();
                _port.DiscardInBuffer();
                _port.DiscardOutBuffer();
                Console.WriteLine("Port " + portName + " open!");
            }
            catch (Exception)
            {
                Console.WriteLine("Error: Open {0}, {1} baud!", portName, baudRate);
                Environment.Exit(-1);
            }
        }
        public bool upload_ram_loader(string fname)
        {
            Console.WriteLine("upload_ram_loader will upload " + fname + "!");
            if (File.Exists(fname) == false)
            {
                Console.WriteLine("Can't open " + fname + "!");
                return true;
            }

            Console.WriteLine("Sync with LN882H... wait 5 seconds");
            _port.DiscardInBuffer();
            Thread.Sleep(5000);

            string msg = "";
            while (msg != "Mar 14 2021/00:23:32\r")
            {
                Thread.Sleep(2000);
                flush_com();
                Console.WriteLine("send version... wait for:  Mar 14 2021/00:23:32");
                _port.Write("version\r\n");
                try
                {
                    msg = _port.ReadLine();
                    Console.WriteLine(msg);
                }
                catch (TimeoutException)
                {
                    msg = "";
                }
            }

            Console.WriteLine("Connect to bootloader...");
            _port.Write("download [rambin] [0x20000000] [37872]\r\n");
            Console.WriteLine("Will send file via YModem");

            YModem modem = new YModem(_port);
            modem.send_file(fname, false, 3);

            Console.WriteLine("Start program. Wait 5 seconds");
            Thread.Sleep(5000);

            msg = "";
            while (msg != "RAMCODE\r")
            {
                Thread.Sleep(5000);
                _port.DiscardInBuffer();
                Console.WriteLine("send version... wait for:  RAMCODE");
                _port.Write("version\r\n");
                try
                {
                    msg = _port.ReadLine();
                    Console.WriteLine(msg);
                    msg = _port.ReadLine();
                    Console.WriteLine(msg);
                }
                catch (TimeoutException)
                {
                    msg = "";
                }
            }

            _port.Write("flash_uid\r\n");
            try
            {
                msg = _port.ReadLine();
                msg = _port.ReadLine();
                Console.WriteLine(msg.Trim());
            }
            catch (TimeoutException)
            {
                Console.WriteLine("Timeout on flash_uid");
            }

            return true;
        }

        internal void runTerminal()
        {
            while (true)
            {
                Console.Write("Give command: ");
                string cmd = Console.ReadLine();
                if (string.IsNullOrEmpty(cmd)) continue;

                _port.Write(cmd + "\r\n");

                //   ReadUartWithTimeoutAndLineLimit(_port, maxLines: 2, timeoutMs: 1000);
                // fdump does not respect it?
                 ReadUartWithTimeoutAndLineLimit(_port, maxLines: 9999, timeoutMs: 1000);
            }
        }

        void ReadUartWithTimeoutAndLineLimit(SerialPort port, int maxLines, int timeoutMs)
        {
            int linesRead = 0;
            StringBuilder currentLine = new StringBuilder();
            DateTime lastReceived = DateTime.Now;

            while (true)
            {
                if (port.BytesToRead > 0)
                {
                    char ch = (char)port.ReadByte();
                    Console.Write(ch);
                    currentLine.Append(ch);
                    lastReceived = DateTime.Now;

                    if (currentLine.ToString().EndsWith("\r\n"))
                    {
                        linesRead++;
                        currentLine.Clear();
                        if (linesRead >= maxLines)
                            break;
                    }
                }
                else
                {
                    if ((DateTime.Now - lastReceived).TotalMilliseconds > timeoutMs)
                    {
                        Console.WriteLine("\n[Timeout waiting for device reply]");
                        break;
                    }

                    Thread.Sleep(10);
                }
            }
        }


        public void flush_com()
        {
            _port.DiscardInBuffer();
            _port.DiscardOutBuffer();
        }

        public void close()
        {
            _port.Close();
        }

        public void change_baudrate(int baudrate)
        {
            Console.WriteLine("change_baudrate: Change baudrate " + baudrate);
            _port.Write("baudrate " + baudrate + "\r\n");
            _port.ReadExisting();
            _port.BaudRate = baudrate;
            Console.WriteLine("change_baudrate: Wait 5 seconds for change");
            Thread.Sleep(5000);
            flush_com();

            string msg = "";
            while (!msg.Contains("RAMCODE"))
            {
                Console.WriteLine("change_baudrate: send version... wait for:  RAMCODE");
                Thread.Sleep(1000);
                flush_com();
                _port.Write("version\r\n");
                try
                {
                    msg = _port.ReadLine();
                    Console.WriteLine(msg);
                    msg = _port.ReadLine();
                    Console.WriteLine(msg);
                }
                catch (TimeoutException) { msg = ""; }
            }

            Console.WriteLine("change_baudrate: Baudrate change done");
        }

        public void flash_program(string filename)
        {
            Console.WriteLine("flash_program: will flash " + filename);
            change_baudrate(921600);
            Console.WriteLine("flash_program: sending startaddr");
            _port.Write("startaddr 0x0\r\n");
            Console.WriteLine(_port.ReadLine().Trim());
            Console.WriteLine(_port.ReadLine().Trim());

            Console.WriteLine("flash_program: sending update command");
            _port.Write("upgrade\r\n");
            _port.Read(new byte[7], 0, 7);

            Console.WriteLine("flash_program: sending file via ymodem");
            YModem modem = new YModem(_port);
            modem.send_file(filename, true, 3);
            Console.WriteLine("flash_program: sending file done");

            _port.Write("filecount\r\n");
            Console.WriteLine(_port.ReadLine().Trim());
            Console.WriteLine(_port.ReadLine().Trim());

            change_baudrate(115200);
        }

        public void flash_erase_all()
        {
            _port.Write("ferase_all\r\n");
            Console.WriteLine(_port.ReadLine().Trim());
            Console.WriteLine(_port.ReadLine().Trim());
        }

        public void flash_info()
        {
            _port.Write("flash_info\r\n");
            Console.WriteLine(_port.ReadLine().Trim());
            Console.WriteLine(_port.ReadLine().Trim());
        }

        public void get_mac_in_otp()
        {
            _port.Write("get_mac_in_flash_otp\r\n");
            Console.WriteLine(_port.ReadLine().Trim());
            Console.WriteLine(_port.ReadLine().Trim());
            Console.WriteLine(_port.ReadLine().Trim());
        }

        public void get_mac_local()
        {
            _port.Write("get_m_local_mac\r\n");
            Console.WriteLine(_port.ReadLine().Trim());
            Console.WriteLine(_port.ReadLine().Trim());
            Console.WriteLine(_port.ReadLine().Trim());
        }

        public void read_gpio(string pin)
        {
            _port.Write("gpio_read " + pin + "\r\n");
            Console.WriteLine(_port.ReadLine().Trim());
            Console.WriteLine(_port.ReadLine().Trim());
            Console.WriteLine(_port.ReadLine().Trim());
            Console.WriteLine(_port.ReadLine().Trim());
        }

        public void write_gpio(string pin, string val)
        {
            _port.Write("gpio_write " + pin + " " + val + "\r\n");
            Console.WriteLine(_port.ReadLine().Trim());
            Console.WriteLine(_port.ReadLine().Trim());
        }
        string readBytesSafe(int targetLen)
        {
            string s = "";
            for (int tr = 0; tr < 100; tr++)
            {
                while (_port.BytesToRead > 0)
                {
                    char c = (char)_port.ReadChar();
                    if (c != ' ')
                    {
                        s += c;
                        if (s.Length == targetLen)
                        {
                            return s.Trim();
                        }
                    }
                }
                Thread.Sleep(1);
            }
            Console.WriteLine("readBytesSafe Failed");
            return s.Trim();
        }

        public bool read_flash(int flash_addr, bool is_otp, out byte[] flash_data)
        {
            //Console.WriteLine("read_flash[" + flash_addr + "] entered");
            string cmd = is_otp ? $"flash_otp_read 0x{flash_addr:X} 0x100\r\n" : $"flash_read 0x{flash_addr:X} 0x100\r\n";
            _port.Write(cmd);

            string rep = _port.ReadLine(); // echo
            // string dataLine = _port.ReadLine().Trim();
            string dataLine = readBytesSafe(256 * 2 + 4);
            // Console.WriteLine("read_flash[" + flash_addr + "] got " + dataLine);
            string hexData = dataLine.Replace(" ", "");
            flash_data = new byte[0];

            if (hexData.Length != 256 * 2 + 4)
                return false;

            string hexPayload = hexData.Substring(0, hexData.Length - 4);
            string checksum = hexData.Substring(hexData.Length - 4);
            flash_data = HexStringToBytes(hexPayload);

            ushort calc_crc = YModem.calc_crc(flash_data);
            return calc_crc == Convert.ToUInt16(checksum, 16);
        }

        public void read_flash_to_file(string filename, int flash_size, bool is_otp = false)
        {
            Console.WriteLine("Reading flash to file " + filename + ", given size " + flash_size);
            int addr = 0;
            using (FileStream fs = new FileStream(filename, FileMode.Create))
            {
                while (addr < flash_size)
                {
                    if (read_flash(addr, is_otp, out byte[] data))
                    {
                        fs.Write(data, 0, data.Length);
                    }
                    Console.Write(".");
                    addr += 0x100;
                }
            }
            Console.WriteLine("\ndone");
        }

        public void dump_flash()
        {
            _port.Write("fdump 0x0 0x2000\r\n");
            Console.WriteLine(_port.ReadLine().Trim());
            while (true)
            {
                string msg = _port.ReadLine().Trim();
                if (msg == "pppp") break;
                Console.WriteLine(msg);
            }
        }

        public void get_gpio_all()
        {
            _port.Write("gpio_read_al\r\n");
            Console.WriteLine(_port.ReadLine().Trim());
            Console.WriteLine(_port.ReadLine().Trim());
        }

        public void get_otp_lock()
        {
            _port.Write("flash_otp_get_lock_state\r\n");
            Console.WriteLine(_port.ReadLine().Trim());
            Console.WriteLine(_port.ReadLine().Trim());
            Console.WriteLine(_port.ReadLine().Trim());
            Console.WriteLine(_port.ReadLine().Trim());
        }

        private byte[] HexStringToBytes(string hex)
        {
            int len = hex.Length;
            byte[] bytes = new byte[len / 2];
            for (int i = 0; i < len; i += 2)
                bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            return bytes;
        }

    }
}
