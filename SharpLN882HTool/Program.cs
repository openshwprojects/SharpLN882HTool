
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
            string toWrite = "";
            string toRead = "";
            bool bErase = false;
            bool bTerminal = false;
            int baud = 460800;
            // YModem.test();

            // Erase: SharpLN882HTool.exe -p COM3 -ef
            // Read: SharpLN882HTool.exe -p COM3 -rf 0x200000 dump.bin
            // Write: SharpLN882HTool.exe -p COM3 -wf obk.bin
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-p" && i + 1 < args.Length)
                {
                    port = args[++i];
                }
                if (args[i] == "-wf" && i + 1 < args.Length)
                {
                    toWrite = args[++i];
                }
                if (args[i] == "-ef")
                {
                    bErase = true;
                }
                if (args[i] == "-t")
                {
                    bTerminal = true;
				}
				if (args[i] == "-rf" && i + 2 < args.Length)
                {
                    i++;
                    string input = args[i];
					baud = int.Parse(input);
                    i++;
                    toRead = args[i];
                }
            }
            if(true)
            {
                bTerminal = true;
            }
           // f.get_mac_in_otp();
           // f.get_mac_local();
            if (toRead.Length>0)
            {
                LN882HFlasher f = new LN882HFlasher(port, 115200);
                Console.WriteLine("Will do dump everything");
                if(f.read_flash_to_file(toRead, baud)) Console.WriteLine("Dump done!");
                else Console.WriteLine("Dump failed!");
            }
            if(bErase)
            {
                LN882HFlasher f = new LN882HFlasher(port, 115200);
                f.upload_ram_loader("LN882H_RAM_BIN.bin");
              //  f.flash_info();
                Console.WriteLine("Will do flash erase all...");
                f.flash_erase_all();
                Console.WriteLine("Erase done!");
            }
            if (toWrite.Length > 0)
            {
                LN882HFlasher f = new LN882HFlasher(port, 115200);
                f.upload_ram_loader("LN882H_RAM_BIN.bin");
                //f.flash_info();
                Console.WriteLine("Will do flash " + toWrite + "...");
                f.flash_program(toWrite);
                Console.WriteLine("Flash done!");
            }
            if(bTerminal)
            {
                LN882HFlasher f = new LN882HFlasher(port, 115200);
                f.upload_ram_loader("LN882H_RAM_BIN.bin");
                f.runTerminal();
            }
        }
    }
}