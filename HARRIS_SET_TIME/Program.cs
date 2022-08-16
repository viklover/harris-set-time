using System;
using System.IO;
using System.Linq;
using System.Text;
using System.IO.Ports;
using System.Threading;
using System.Collections.Generic;

using Newtonsoft.Json.Linq;

namespace HARRIS_SET_TIME
{
    class Program
    {
        static string current_path = AppDomain.CurrentDomain.BaseDirectory + $"serial_port.data";

        static SerialPort _serialPort = new SerialPort();

        static List<DCA> DCAs = new List<DCA> { };

        static Dictionary<string, string> events =
                       new Dictionary<string, string>();

        static float total_tasks = 0f;
        static float tasks_completed = 0f;

        static int last_line = 0;

        static DateTime last_send;
        static DateTime last_rec;

        static bool completed = false;
        static bool quit = false;

        static bool develope_mode = false;

        static bool ready()
        {
            if (DateTime.Now.Subtract(last_send).Seconds > 1.0 &&
                DateTime.Now.Subtract(last_rec).Seconds > 2.5)
            {
                return true;
            }

            return false;
        }

        static bool ready_to_upd()
        {
            if (DateTime.Now.Subtract(last_rec).Seconds > 2.5)
            {
                return true;
            }

            return false;
        }

        static bool passed_time_now(DateTime dt, double time)
        {
            return DateTime.Now.Subtract(dt).Seconds >= time;
        }

        public class SettingsPort
        {
            public string Name { get; set; }
            public int BaudRate { get; set; }
        }
        public class Number
        {
            public Number(string number, DCA dca)
            {
                this.number = number;
                this.dca = dca;
            }
            public string number { get; set; }
            public DCA dca { get; set; }
        }
        public class DCA
        {
            public DCA(string user)
            {
                this.User = user;
            }
            public SettingsPort Port = new SettingsPort();
            public string User { get; set; }
            public string Password { get; set; }
            public List<string> Numbers { get; set; }

        }

        public static void NewFile()
        {
            if (File.Exists(current_path))
                File.Delete(current_path);

            File.Create(current_path);
        }

        public static void Main()
        {
            NewFile();

            _serialPort.Open();

            if (_serialPort.CDHolding || develope_mode)
            {
                _serialPort.Close();

                Thread parseThread = new Thread(Parse);

                InitDCAs();
                InitParseEvents();

                parseThread.Start();

                last_rec = DateTime.Now;

                while (!completed)
                {
                    try
                    {
                        if (_serialPort.IsOpen)
                        {
                            string indata = _serialPort.ReadExisting();

                            Console.Write(indata);

                            if (indata != "")
                            {
                                last_rec = DateTime.Now;

                                File.AppendAllText(current_path, indata);
                            }
                        }
                    }
                    catch (System.IO.IOException) { }
                    catch (System.NullReferenceException) { }
                }

                Console.ReadKey(true);
            }
            else
            {
                _serialPort.Close();

                Console.WriteLine(" - НЕТ СОЕДИНЕНИЯ С МОДЕМОМ");

                Console.ReadKey(true);
            }
        }

        public static void InitDCAs()
        {
            JObject jObject;

            string path = AppDomain.CurrentDomain.BaseDirectory + "settings.json";

            string settings = File.ReadAllText(path);
            jObject = JObject.Parse(settings);

            for (int i = 0; i < jObject["DCA"].ToArray().Length; i++)
            {
                DCA dca = new DCA(jObject["DCA"][i]["User"].ToString());
                dca.Password = jObject["DCA"][i]["Password"].ToString();

                foreach (var k in JObject.FromObject(jObject["DCA"][i]["Port"]))
                {
                    switch (k.Key)
                    {
                        case "Name":
                            dca.Port.Name = k.Value.ToString();
                            break;

                        case "BaudRate":
                            dca.Port.BaudRate = Int32.Parse(k.Value.ToString());
                            break;
                    }
                }

                dca.Numbers = new List<string> { };

                for (int k = 0; k < jObject["DCA"][i]["Numbers"].ToArray().Length; k++)
                {
                    dca.Numbers.Add(jObject["DCA"][i]["Numbers"][k].ToString());
                    total_tasks++;
                }

                DCAs.Add(dca);
            }
        }
        public static void InitParseEvents()
        {
            string path = AppDomain.CurrentDomain.BaseDirectory + "parsing_events.json";

            JObject jObject = JObject.Parse(File.ReadAllText(path));

            foreach (var text in jObject)
            {
                events.Add(text.Key.ToString(), text.Value.ToString());
            }
        }

        public static IEnumerable<string> GetUpdates()
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
                            read_text = File.ReadAllLines(current_path);

                            break;
                        }
                        catch (System.IO.IOException) { }
                    }

                    foreach (string line in read_text)
                    {
                        if (current_line >= last_line || last_line == 0)
                        {
                            data += line + " \n";
                        }

                        current_line++;
                    }

                    if (current_line >= last_line)
                    {
                        last_line = current_line;

                        foreach (KeyValuePair<string, string> kvp in events)
                        {
                            if (data.Contains(kvp.Key))
                            {
                                yield return kvp.Value;

                                if (kvp.Value == "quit")
                                    quit = true;
                            }
                        }
                    }
                }

                if (quit)
                {
                    quit = false;
                    break;
                }

                yield return "";
            }
        }

        public static void Parse()
        {
            List<Number> queue = new List<Number> { };

            foreach (DCA dca in DCAs)
            {
                foreach (string number in dca.Numbers)
                {
                    if (!ListenNumber(new Number(number, dca)))
                    {
                        queue.Add(new Number(number, dca));

                    }
                    else
                    {
                        tasks_completed++;
                    }
                }
            }

            foreach (Number num in queue.ToArray())
            {
                if (ListenNumber(num))
                {
                    tasks_completed++;
                    queue.Remove(num);
                }
            }

            if (queue.ToArray().Length == 0)
            {
                Console.WriteLine($"\n ЗАДАЧА ВЫПОЛНЕНА");
            }
            else
            {
                Console.WriteLine("\n - У СЛЕДУЮЩИХ НОМЕРОВ НЕ УДАЛОСЬ УСТАНОВИТЬ ВРЕМЯ: ");

                foreach (Number num in queue)
                    Console.WriteLine($"   - {num.number}");

                Console.WriteLine($"\n ЗАДАЧА ВЫПОЛНЕНА НА {Math.Round(tasks_completed / total_tasks * 100.0).ToString()}% ");
            }

            completed = true;
        }

        public static bool ListenNumber(Number num)
        {
            string number = num.number;

            string user = num.dca.User;
            string password = num.dca.Password;

            string process = "start";
            string prev_process = "";

            int iteration = 0;

            Console.WriteLine($"\n- НОМЕР --- {number} ---");

            if (_serialPort.IsOpen)
                _serialPort.Close();

            _serialPort = new SerialPort();
            _serialPort.PortName = num.dca.Port.Name;
            _serialPort.BaudRate = num.dca.Port.BaudRate;

            Thread.Sleep(1000);

            _serialPort.Open();

            if (_serialPort.CDHolding || develope_mode)
            {
                byte[] EXT = new byte[] { 0x03 };

                DateTime last_upd_iteration = DateTime.Now;

                foreach (string update in GetUpdates())
                {
                    switch (update)
                    {
                        case "input":
                            process = "input";

                            byte[] a = Encoding.ASCII.GetBytes($"set time {DateTime.Now.ToString("HH:mm:ss")}");
                            byte[] b = GetBytes("0D");
                            byte[] c = a.Concat(b).ToArray();

                            Send(c);
                            Send(GetBytes("65 78 69 74 0D"));
                            process = "end";
                            break;

                        case "input tel":
                            process = "input tel";
                            Send(Encoding.ASCII.GetBytes(number));
                            break;

                        case "busy":
                            process = "error";
                            break;


                        case "auth":
                            process = "auth";
                            Send(GetBytes($"{ConvertToHEX(user)} 0D {ConvertToHEX(password)} 0D"));
                            break;

                        case "connected":
                            process = "connected";
                            break;

                        case "input alm":
                            process = "input alm";
                            Send(GetBytes("65 78 69 74 0D"));
                            break;



                        case "input cdr":
                            process = "input cdr";
                            Send(GetBytes("65 78 69 74 0D"));
                            break;

                        case "input edt":
                            process = "input edt";
                            Send(GetBytes("65 78 69 74 0D"));
                            break;

                        case "input sts":
                            process = "input sts";
                            Send(GetBytes("65 78 69 74 0D"));
                            break;


                        default:
                            break;
                    }

                    if (process == "start")
                    {
                        if (iteration <= 2)
                        {
                            Send(GetBytes("0D"));
                        }
                        else
                        {
                            process = "error";
                        }
                    }

                    if (process == "connected")
                    {
                        Send(EXT);
                    }

                    if (process == prev_process)
                    {
                        if (passed_time_now(last_upd_iteration, 1.0))
                        {
                            iteration++;
                            last_upd_iteration = DateTime.Now;
                        }
                    }
                    else
                    {
                        iteration = 0;
                        last_upd_iteration = DateTime.Now;
                    }

                    if (process == "error" || passed_time_now(last_rec, 15.0))
                    {
                        Console.WriteLine("- НЕТ ОТВЕТА ОТ МОДЕМА --- ПЕРЕХОД К СЛЕД.НОМЕРУ");

                        quit = true;
                    }

                    prev_process = process;
                }

                SendBreak();

                _serialPort.Close();
            }
            else
            {
                Console.WriteLine(" - НЕТ СОЕДИНЕНИЯ С МОДЕМОМ");
            }
            return (process == "end");
        }

        public static void Send(byte[] message)
        {
            if (_serialPort.IsOpen)
            {
                while (true)
                {
                    try
                    {
                        if (ready())
                        {
                            _serialPort.Write(message, 0, message.Length);

                            last_send = DateTime.Now;
                            break;
                        }
                    }
                    catch (Exception) { }
                }
            }
        }
        public static void SendBreak()
        {
            for (int i = 0; i < 2; ++i)
            {
                _serialPort.BreakState = true;
                Thread.Sleep(500);
                _serialPort.BreakState = false;
            }

            Thread.Sleep(1000);
        }

        public static byte[] GetBytes(string hex_string)
        {
            byte[] bytes;

            string[] hexValuesSplit = hex_string.Split(' ');

            string result = String.Empty;

            foreach (string hex in hexValuesSplit)
            {
                int value = Convert.ToInt32(hex, 16);
                string stringValue = Char.ConvertFromUtf32(value);
                char charValue = (char)value;
                result += stringValue;
            }

            bytes = Encoding.ASCII.GetBytes(result);

            return bytes;
        }
        public static string ConvertToHEX(string text)
        {
            bool first = true;

            string output = String.Empty;
            char[] values = text.ToCharArray();

            foreach (char letter in values)
            {
                if (!first) output += " ";

                int value = Convert.ToInt32(letter);
                output += $"{value:X}";

                first = false;
            }

            return output;
        }
    }
}