using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

using Rbx2Source.Web;
using Microsoft.Win32;

using RobloxFiles;
using RobloxFiles.Enums;
using RobloxFiles.DataTypes;

using Rbx2Source.Geometry;
using Rbx2Source.Resources;

namespace Rbx2Source.Assembler
{
    public class LayeredClothingExtractor
    {
        public readonly UserAvatar Avatar;
        public static bool UseExistingObj = false;
        public ObjFile Output { get; private set; }

        public LayeredClothingExtractor(UserAvatar avatar)
        {
            Avatar = avatar;
        }

        public async Task Extract()
        {
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

            if (UseExistingObj)
            {
                var existing = Path.Combine(desktopPath, "Rbx2SourceRig (SAVE TO DESKTOP).obj");

                if (File.Exists(existing))
                {
                    string contents = File.ReadAllText(existing);

                    if (!string.IsNullOrWhiteSpace(contents))
                    {
                        Output = new ObjFile(contents);
                        return;
                    }
                }
            }

            var temp = Environment.GetEnvironmentVariable("TEMP");
            var bin = new BinaryRobloxFile();

            var workspace = new Workspace();
            workspace.SetAttribute<double>("UserId", Avatar.UserInfo.Id);
            workspace.Tags.Add("Rbx2Source_LayeredClothingExtractor");
            workspace.Parent = bin;

            // !! Silence legacy chat deprecation
            var textChatService = new TextChatService();
            textChatService.ChatVersion = ChatVersion.TextChatService;
            textChatService.Parent = bin;

            // !! Silence compatibility lighting deprecation
            var lighting = new Lighting();
            lighting.Technology = Technology.Voxel;
            lighting.Parent = bin;

            // Save place file to temp.
            var placeFile = Path.Combine(temp, "Rbx2Source_LayeredClothingExtractor.rbxl");
            await bin.SaveAsync(placeFile);

            // Write plugin to local plugins folder.
            string localAppData = Environment.GetEnvironmentVariable("localappdata");
            var pluginPath = Path.Combine(localAppData, "Roblox", "Plugins", "Rbx2Source_LayeredClothingExtractor.lua");

            var plugin = ResourceUtility.GetResource("Plugin/LayeredClothingExtractor.lua");
            Directory.CreateDirectory(Path.GetDirectoryName(pluginPath));
            File.WriteAllBytes(pluginPath, plugin);

            var startTime = DateTime.Now;
            Process studioProc = null;

            try
            {
                // Try to find Roblox Studio's location directly.
                using (var software = Registry.CurrentUser.OpenSubKey("SOFTWARE"))
                using (var roblox = software?.OpenSubKey("Roblox"))
                using (var robloxStudio = roblox?.OpenSubKey("RobloxStudio"))
                {
                    var contentFolder = robloxStudio?.GetValue("ContentFolder") as string;

                    if (string.IsNullOrWhiteSpace(contentFolder))
                        throw new Exception("Studio not found!");

                    var studioPath = Path.Combine(contentFolder, "..", "RobloxStudioBeta.exe");
                    var studioInfo = new FileInfo(studioPath);

                    if (!studioInfo.Exists)
                        throw new Exception("Studio not found!");

                    var startInfo = new ProcessStartInfo()
                    {
                        FileName = studioInfo.FullName,
                        Arguments = placeFile,
                    };

                    studioProc = Process.Start(startInfo);
                }
            }
            catch
            {
                // Alright, start it directly by file, and wait for a new RobloxStudioBeta process.
                Process.Start(placeFile);

                var launchTimeout = DateTime.Now.AddSeconds(60);

                while (true)
                {
                    foreach (var process in Process.GetProcessesByName("RobloxStudioBeta.exe"))
                    {
                        if (process.StartTime > startTime)
                        {
                            studioProc = process;
                            break;
                        }
                    }

                    if (studioProc != null)
                        break;

                    if (DateTime.Now > launchTimeout)
                        throw new TimeoutException("Timed out waiting for Roblox Studio to launch.");

                    await Task.Delay(200);
                }
            }

            string objFile = "";

            var objFilePath = Path.Combine(desktopPath, "Rbx2SourceRig (SAVE TO DESKTOP).obj");
            var fileWatcher = new FileSystemWatcher()
            {
                Path = desktopPath,
                IncludeSubdirectories = false,
                Filter = "Rbx2SourceRig (SAVE TO DESKTOP).obj"
            };

            fileWatcher.Changed += new FileSystemEventHandler((_, eventArgs) =>
            {
                Task.Delay(1000).ContinueWith((delayTask) =>
                {
                    if (objFile.Length > 0)
                        return;

                    if (File.Exists(objFilePath))
                        objFile = File.ReadAllText(objFilePath);

                    KillStudio(studioProc);
                });
            });

            fileWatcher.EnableRaisingEvents = true;

            try
            {
                var exportTimeout = DateTime.Now.AddMinutes(5);

                while (objFile.Length == 0 && studioProc != null && !studioProc.HasExited)
                {
                    if (DateTime.Now > exportTimeout)
                        throw new TimeoutException("Timed out waiting for Roblox Studio to export the OBJ file.");

                    await Task.Delay(500);
                }
            }
            finally
            {
                fileWatcher.Dispose();
            }

            KillStudio(studioProc);

            if (objFile.Length > 0)
            {
                try
                {
                    Output = new ObjFile(objFile);
                }
                catch
                {
                    Output = null;
                }
            }
        }

        private static void KillStudio(Process studioProc)
        {
            try
            {
                if (studioProc != null && !studioProc.HasExited)
                    studioProc.Kill();
            }
            catch
            {
            }
        }
    }
}
