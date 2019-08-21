using System;
using System.Diagnostics;

namespace PdfConverterFunction
{
    static class Extensions
    {
        public static bool IsRunning(this Process process)
        {
            if (process == null)
                throw new ArgumentNullException("process");

            try
            {
                var proc = Process.GetProcessById(process.Id);
                return !proc.HasExited;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }
}
