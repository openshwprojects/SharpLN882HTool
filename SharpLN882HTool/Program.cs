
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
        public bool upload_ram_loader(string fname) {
            Console.WriteLine("upload_ram_loader will upload " + fname + "!");
            if (File.Exists(fname) == false)
            {
                Console.WriteLine("Can't open " + fname+"!");
                return true;
            }

            return false;
        }

    }
}