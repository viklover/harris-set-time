using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HARRIS_SET_TIME
{
    public class HarrisTask
    {
        private Task<bool> task;
        private CancellationToken _token;

        public Dca DcaInfo;

        public bool Status = false;

        public HarrisTask(Dca dcaObject, CancellationToken token)
        {
            DcaInfo = dcaObject;
            _token = token;
        }

        public bool Run()
        {
            Console.WriteLine("\n - НОМЕР: " + DcaInfo.number + "\n");

            Program.port.SetBaudrate(DcaInfo.baudrate != 0 ? DcaInfo.baudrate : ComPort.baudrate);

            _token.Register(() => { });

            int iteration = 0;

            DateTime lastUpdIteration = DateTime.Now;
            Program.port.InputThread.LastRec = DateTime.Now;

            string prevUpdate = "";

            bool were_updates = false;
            bool quit = false;

            while (!Status && !quit)
            {
                foreach (string update in Program.port.GetUpdates())
                {
                    if (update == "input")
                    {
                        Program.port.Send(
                            Encoding.ASCII.GetBytes($"set time {DateTime.Now.ToString("HH:mm:ss")}")
                            .Concat(ComPort.GetBytesFromHex("0D")).ToArray()
                        );
                        Program.port.Send(ComPort.GetBytesFromHex("65 78 69 74 0D"));
                        Status = true;
                    }
                    else if (update == "input tel")
                    {
                        Program.port.Send(Encoding.ASCII.GetBytes(DcaInfo.number));
                    }
                    else if (update == "busy" || update == "quit" || update == "fail call" || passed_time_now(Program.port.InputThread.LastRec, 15.0))
                    {
                        quit = true;
                        break;
                    }
                    else if (update == "auth")
                    {
                        Program.port.Send(
                            ComPort.GetBytesFromHex(
                                $"{ComPort.ConvertToHex(DcaInfo.user)} 0D {ComPort.ConvertToHex(DcaInfo.password)} 0D"
                            )
                        );
                    }
                    else if (update == "connected")
                    {
                        Program.port.SendEXT();
                    }
                    else if (update == "input alm" || update == "input cdr" || update == "input edt" ||
                             update == "input sts")
                    {
                        Program.port.Send(ComPort.GetBytesFromHex("65 78 69 74 0D"));
                    }
                    else
                    {
                        if (!were_updates && (iteration == 0 || iteration % 4 == 0))
                        { 
                            Program.port.Send(ComPort.GetBytesFromHex("0D"));
                        }
                    }

                    if (update == prevUpdate)
                    {
                        if (passed_time_now(lastUpdIteration, 1.0))
                        {
                            iteration++;
                            lastUpdIteration = DateTime.Now;
                        }
                    }
                    else
                    {
                        iteration = 0;
                        lastUpdIteration = DateTime.Now;
                    }

                    if (update != "")
                        were_updates = true;

                    prevUpdate = update;

                    Thread.Sleep(100);
                }
            }

            Program.port.SendBreak();

            return Status;
        }

        public bool StartAndWaitResult()
        {
            task = new Task<bool>(Run, _token);
            task.Start();
            return task.Result;
        }

        public bool passed_time_now(DateTime dt, double time)
        {
            return DateTime.Now.Subtract(dt).Seconds >= time;
        }
    }
}
