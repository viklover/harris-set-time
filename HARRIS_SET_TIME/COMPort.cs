using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace HARRIS_SET_TIME
{
    public class ComPort
    {
        public SerialPort Port = new SerialPort();

        public ReadingInputDataThread InputThread;

        public DateTime LastSend;

        public static byte[] EXT = new byte[] { 0x03 };

        public int LastLine = 0;

        public Dictionary<string, string> Events =
            new Dictionary<string, string>();

        public ComPort(dynamic config)
        {
            Port.PortName = config.name;
            Port.BaudRate = config.baudrate;

            InputThread = new ReadingInputDataThread(Port);

            JObject jObject = JObject.Parse(File.ReadAllText(Program.eventsPath));

            foreach (var text in jObject)
                Events.Add(text.Key, text.Value.ToString());
        }

        public void WaitOpening()
        {
            bool openErr = false;
            bool cdholdingErr = false;

            while ((!Port.IsOpen && !CDHolding()) || (Port.IsOpen && !CDHolding()))
            {
                try
                {
                    if (!Port.IsOpen)
                        Port.Open();

                    openErr = false;

                    if (!CDHolding() && !cdholdingErr)
                    {
                        Console.WriteLine("Нет соединения с портом DCA. Ожидаю подключения..");
                        cdholdingErr = true;
                        Thread.Sleep(5000);
                    }
                }
                catch (Exception)
                {
                    if (!openErr)
                    {
                        Console.WriteLine("Не могу открыть порт");
                        openErr = true;
                    }

                    Thread.Sleep(5000);
                }
            }

            if (InputThread.IsRunning())
                InputThread.StartListening();

            Console.WriteLine(Port.IsOpen);
        }

        public bool CDHolding()
        {
            try
            {
                return Port.CDHolding;
            }
            catch (InvalidOperationException) {}

            return false;
        }

        public void Close()
        {
            InputThread.StopListening();
            Port.Close();
        }

        public void Send(byte[] message)
        {
            while (!ready_to_send(LastSend)) ;
            
            Port.Write(message, 0, message.Length);
            LastSend = DateTime.Now;
        }

        public void SendEXT() 
        {
            Send(EXT);
        }

        public void SendBreak()
        {
            for (int i = 0; i < 2; ++i)
            {
                Port.BreakState = true;
                Thread.Sleep(500);
                Port.BreakState = false;
            }
            Thread.Sleep(1000);
        }

        public void SetDtr(bool value)
        {
            Port.DtrEnable = value;
        }


        public static byte[] GetBytesFromHex(string hexString)
        {
            var hexValuesSplit = hexString.Split(' ');

            var result = string.Empty;

            foreach (var hex in hexValuesSplit)
            {
                var value = Convert.ToInt32(hex, 16);
                var stringValue = char.ConvertFromUtf32(value);
                result += stringValue;
            }

            var bytes = Encoding.ASCII.GetBytes(result);

            return bytes;
        }

        public static string ConvertToHex(string text)
        {
            var first = true;

            var output = String.Empty;
            var values = text.ToCharArray();

            foreach (var letter in values)
            {
                if (!first) output += " ";

                var value = Convert.ToInt32(letter);
                output += $"{value:X}";

                first = false;
            }

            return output;
        }


        public bool ready_to_upd()
        {
            if (DateTime.Now.Subtract(InputThread.LastRec).Seconds > 2.5)
            {
                return true;
            }
            return false;
        }

        public bool ready_to_send(DateTime LastSend)
        {
            if (DateTime.Now.Subtract(LastSend).Seconds > 1.0 &&
                DateTime.Now.Subtract(InputThread.LastRec).Seconds > 2.5)
            {
                return true;
            }
            return false;
        }

        public IEnumerable<string> GetUpdates()
        {
            while (true)
            {
                if (ready_to_upd())
                {
                    int current_line = 0;
                    string data = String.Empty;
                    string[] read_text;
                    while (true)
                    {
                        try
                        {
                            read_text = File.ReadAllLines(Program.bufferPath);
                            break;
                        }
                        catch (System.IO.IOException) { }
                    }
                    foreach (string line in read_text)
                    {
                        if (current_line >= LastLine || LastLine == 0)
                        {
                            data += line + " \n";
                        }
                        current_line++;
                    }
                    if (current_line >= LastLine)
                    {
                        LastLine = current_line;
                        foreach (KeyValuePair<string, string> kvp in Events)
                        {
                            if (data.Contains(kvp.Key))
                            {
                                yield return kvp.Value;

                                //if (kvp.Value == "quit")
                                //    quit = true;
                            }
                        }
                    }
                }
                if (InputThread.StopThread)
                    break;

                yield return "";
            }
        }
    }

    public class ReadingInputDataThread {

        private readonly Thread _thread;
        private readonly SerialPort _serialPort;

        public DateTime LastRec;

        public bool StopThread = false;

        public ReadingInputDataThread(SerialPort port)
        {
            _serialPort = port;
            _thread = new Thread(ListeningPort);
        }

        public void StartListening()
        {
            _thread.Start();
        }

        public void StopListening()
        {
            StopThread = true;
            _thread.Join();
        }

        public void ListeningPort()
        {
            while (!StopThread)
            {
                try
                {
                    if (_serialPort.IsOpen)
                    {
                        string indata = _serialPort.ReadExisting();
                        Console.Write(indata);

                        if (indata != "")
                        {
                            LastRec = DateTime.Now;
                            File.AppendAllText(Program.bufferPath, indata);
                        }
                    }
                }
                catch (System.IO.IOException) { }
                catch (System.NullReferenceException) { }
            }
        }

        public bool IsRunning()
        {
            return _thread.ThreadState == ThreadState.Running;
        }
    }
}
