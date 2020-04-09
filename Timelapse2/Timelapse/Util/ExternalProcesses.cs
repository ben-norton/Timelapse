﻿using System.Diagnostics;
using System.IO;


namespace Timelapse.Util
{
    static class ExternalProcesses
    {
        // Start a default external process on the given file.
        // If for some reason we can't e.g., if the file does not exist or there is no default app, return false
        /// <summary>
        /// Start a default external process on the given file.
        /// </summary>
        /// <param name="path">The complete file path to the file to open</param>
        /// <returns>true if successful, false if the file does not exist or if there is no default application for the file's extension</returns>
        static public bool TryStartProcess(string path)
        {
            if (File.Exists(path) == false)
            {
                return false;
            }
            try
            {
                // Show the file in excel
                // Create a process that will try to show the file
                using (Process process = new Process())
                {
                    process.StartInfo.UseShellExecute = true;
                    process.StartInfo.RedirectStandardOutput = false;
                    process.StartInfo.FileName = path;
                    process.Start();
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}