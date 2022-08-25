using System;
using System.IO;
using System.Linq;
using System.Text;
using System.IO.Ports;
using System.Threading;
using System.Collections.Generic;

using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

namespace HARRIS_SET_TIME
{
    class Program
    {
        public static ComPort port;
        public static List<HarrisTask> tasks = new List<HarrisTask> {};

        public static string configPath = AppDomain.CurrentDomain.BaseDirectory + "settings.json";
        public static string eventsPath = AppDomain.CurrentDomain.BaseDirectory + "parsing_events.json";

        public static string bufferPath = AppDomain.CurrentDomain.BaseDirectory + "serial_port.data";

        private static CancellationTokenSource _cancelTokenSource = new CancellationTokenSource();

        public static void Main() 
        {
            if (!File.Exists(configPath) || !File.Exists(eventsPath)) {
                throw new FileNotFoundException("Один из конфигурационных файлов не найден");
            }

            Init();
            RunTasks();

            Console.ReadKey(true);
        }

        public static void Init() {
            dynamic data = JsonConvert.DeserializeObject(File.ReadAllText(configPath));

            port = new ComPort(data.serial_port);

            foreach (dynamic dcaInfo in data.tasks) {
                Dca dcaItem = new Dca(dcaInfo);
                tasks.Add(new HarrisTask(dcaItem, _cancelTokenSource.Token));
            }

            Console.ForegroundColor = ConsoleColor.White;
        }

        public static void RunTasks()
        {
            int completedTasks = 0;

            port.WaitOpening();
            port.SetDtr(true);

            List<HarrisTask> queue = new List<HarrisTask> { };

            foreach (HarrisTask task in tasks)
            {
                if (task.StartAndWaitResult()) {
                    completedTasks++;
                } else {
                    queue.Add(task);
                }
            }

            foreach (HarrisTask task in queue.ToArray())
            {
                if (task.StartAndWaitResult()) {
                    completedTasks++;
                    queue.Remove(task);
                }
            }

            Program.port.SetDtr(false);

            Console.WriteLine();

            if (queue.ToArray().Length == 0) 
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"\n ВСЕ ЗАДАЧИ ВЫПОЛНЕНЫ УСПЕШНО");
            } 
            else 
            {
                if (completedTasks > 0)
                    Console.ForegroundColor = ConsoleColor.Yellow;
                else
                    Console.ForegroundColor = ConsoleColor.Red;

                Console.WriteLine("\n - У СЛЕДУЮЩИХ НОМЕРОВ НЕ УДАЛОСЬ УСТАНОВИТЬ ВРЕМЯ: ");
                
                foreach (HarrisTask task in queue)
                    Console.WriteLine($"   - {task.DcaInfo.number}");

                if (completedTasks > 0)
                    Console.WriteLine($"\n ЗАДАЧИ ВЫПОЛНЕНЫ НА {Math.Round((completedTasks * 1.0) / tasks.Count * 100.0, 3)}% ");
                else
                {
                    Console.WriteLine("\n НИ ОДНА ЗАДАЧА НЕ ВЫПОЛНЕНА :(");
                }
            }

            Console.WriteLine();
            Console.ResetColor();

            Program.port.Close();
        }
    }
}