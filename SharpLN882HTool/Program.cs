
using System;
using System.IO;
using System.IO.Ports;
using System.Threading;

namespace LN882HTool
{

    class Program
    {
        static void Main(string[] args)
        {
            string port = "COM3";

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-p" && i + 1 < args.Length)
                {
                    port = args[++i];
                }
            }
            LN882HFlasher f = new LN882HFlasher(port, 115200);
            f.upload_ram_loader("LN882H_RAM_BIN.bin");
        }
    }
    public class LN882HFlasher
    {
        private SerialPort _port;

        public LN882HFlasher(string portName, int baudRate, int timeoutMs = 200)
        {
            try
            {
                Console.WriteLine("Opening port " + portName+"...");
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
        public void flush_com()
        {
            _port.DiscardInBuffer();
            _port.DiscardOutBuffer();
        }


    }
}