using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Proxy
{
    internal class Logger
    {
        public string CurrentDirectory { get; set; }
        public string FileName { get; set; }
        public string FilePath { get; set; }
        internal Logger()
        {
            CurrentDirectory = Directory.GetCurrentDirectory();
            FileName = "NetworkLog.txt";
            FilePath = CurrentDirectory + "/" + FileName;
        }

        public void WriteLog(string message)
        {
            // Write to the log file
            using (StreamWriter w = File.AppendText(FilePath))
            {
                w.WriteLine($"{DateTime.Now.ToLongTimeString()} "
                    + $"{DateTime.Now.ToLongDateString()}");
                w.WriteLine(message);
                w.WriteLine("---------------------------------");
            }
        }
    }
}
