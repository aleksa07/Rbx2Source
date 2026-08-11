using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Rbx2Source.Compiler
{
    public class ThirdPartyUtility
    {
        private string appPath;
        private HashSet<UtilParameter> parameters;

        public ThirdPartyUtility(string path)
        {
            appPath = path;
            parameters = new HashSet<UtilParameter>();
        }

        public void AddParameter(UtilParameter parameter)
        {
            parameters.Add(parameter);
        }
		
        public void AddParameter(string name = "", string value = "")
        {
            var param = new UtilParameter(name, value);
            AddParameter(param);
        }

        public void AddFile(string filePath)
        {
            var file = new UtilParameter("", filePath);
            AddParameter(file);
        }

        public Process Run()
        {
            var paramStrings = parameters
                .Select(param => param.ToString())
                .ToArray();

            ProcessStartInfo info = new ProcessStartInfo()
            {
                Arguments = string.Join(" ", paramStrings),
                FileName = appPath,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true
            };

            return Process.Start(info);
        }

        public Task RunWithOutput()
        {
            var paramStrings = parameters
                .Select(param => param.ToString())
                .ToArray();

            ProcessStartInfo info = new ProcessStartInfo()
            {
                Arguments = string.Join(" ", paramStrings),
                FileName = appPath,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            Process process = Process.Start(info);

            Task outputTask = DrainOutput(process.StandardOutput);
            Task errorTask = DrainOutput(process.StandardError);

            return Task.WhenAll(outputTask, errorTask)
                .ContinueWith(completed =>
                {
                    process.WaitForExit();
                    process.Dispose();
                });
        }

        private static async Task DrainOutput(StreamReader reader)
        {
            while (true)
            {
                string line = await reader.ReadLineAsync().ConfigureAwait(false);

                if (line == null)
                    break;

                Rbx2Source.Print(line);
            }
        }
    }
}
