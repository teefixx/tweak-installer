﻿//Copyright 2018 josephwalden
//Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
//The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
//THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

//todo
//test backup system (with multiple tweaks)
//reduce code size a lot
//combine gui with cli
//store tweaks on device for easy uninstall


using jLib;
using SevenZipExtractor;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.VisualBasic.FileIO;
using System.Net;
using System.Threading;
using System.Windows.Forms;
using WinSCP;

namespace Unjailbreaker
{
    class Program
    {
        static bool install = false;
        static bool uninstall = false;
        static bool convert = false;
        static bool manual = false;
        static bool jtool = false;
        static bool update = true;
        static bool uicache = false;
        static bool respring_override = false;
        static bool uicache_override = false;
        static bool onlyPerformSSHActions = false;
        static bool verbose = false;
        static List<string> skip;
        static List<string> tweaks;
        static string[] data;
        static string user;
        static Crawler crawler;

        static string convert_path(string i, bool unix = false)
        {
            if (!unix)
            {
                return i.Replace("\\", "/");//.Replace(" ", "\\ ").Replace("(", "\\(").Replace(")", "\\)").Replace("'", "\\'").Replace("@", "\\@");
            }
            else
            {
                return i.Replace("\\", "/").Replace(" ", "\\ ").Replace("(", "\\(").Replace(")", "\\)").Replace("'", "\\'").Replace("@", "\\@");
            }
        }
        static void log(string s)
        {
            if (!File.Exists("log.txt")) File.Create("log.txt").Close();
            try
            {
                File.AppendAllText("log.txt", s + Environment.NewLine);
                Console.WriteLine(s);
            }
            catch
            {
                Thread.Sleep(1000);
                File.AppendAllText("log.txt", s + Environment.NewLine);
                Console.WriteLine(s);
            }
        }
        static void finish(Session session)
        {
            if (uicache && !uicache_override)
            {
                log("Running uicache (may take up to 30 seconds)");
                session.ExecuteCommand("uicache"); //respring
            }
            if (!respring_override)
            {
                log("Respringing...");
                session.ExecuteCommand("killall -9 SpringBoard"); //respring
            }
            session.Close();
            if (verbose) log("Press any key to finish");
            if (verbose) Console.ReadLine();
        }
        static void createDirIfDoesntExist(string path)
        {
            if (!Directory.Exists(path))
            {
                if (verbose) log("Creating directory " + path);
                Directory.CreateDirectory(path);
                if (verbose) log("Created directory " + path);
            }
            else
            {
                if (verbose) log("\b\b\b\bNo need to create " + path + " as it already exists");
            }
        }
        static void deleteIfExists(string path)
        {
            if (verbose) log("Searching for " + path);
            if (File.Exists(path))
            {
                if (verbose) log("Deleting " + path);
                File.Delete(path);
                if (verbose) log("Deleted " + path);
            }
        }
        static void emptyDir(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
                if (verbose) log("Deleted " + path);
            }
            Directory.CreateDirectory(path);
            if (verbose) log("Created directory " + path);
        }
        static void moveDirIfPresent(string source, string dest, string parent = null)
        {
            if (Directory.Exists(source))
            {
                if (verbose) log("Found " + source);
                if (parent != null)
                {
                    createDirIfDoesntExist(parent);
                    if (verbose) log("Created " + parent);
                }
                FileSystem.MoveDirectory(source, dest, true);
                if (verbose) log("Moved " + source + " to " + dest);
            }
        }

        [STAThread]
        static void Main(string[] args)
        {
            deleteIfExists("log.txt");

            clean();
            getOptions(args);

            //get sftp server
            getSSHSettings();
            Session session = getSession(data[0], user, data[2], int.Parse(data[1]));
            getJailbreakSpecificOptions(session);

            if (update)
            {
                checkForUpdates();
            }

            if (onlyPerformSSHActions)
            {
                uicache = true;
                finish(session);
                return;
            }

            if (manual)
            {
                manualMode();
            }
            else
            {
                clean();
                getTweaksFromStringArray(args);
                foreach (string tweak in tweaks)
                {
                    if (tweak.Contains(".deb"))
                    {
                        extractDeb(tweak);
                    }
                    else if (tweak.Contains(".ipa"))
                    {
                        extractIPA(tweak);
                    }
                    else
                    {
                        clean();
                        extractZip(tweak);
                    }
                }
            }
            if (convert)
            {
                convertTweaks();
            }

            getFiles();
            if (verbose) log("Done");
            if (args.Length > 0)
            {
                if (install)
                {
                    installFiles(session);
                }
                else if (uninstall)
                {
                    uninstallFiles(session);
                }
            }
        }

        private static void extractZip(string path)
        {
            log("Extracting Zip " + path);
            try
            {
                using (ArchiveFile archiveFile = new ArchiveFile(path))
                {
                    if (verbose) log("Extracting zip");
                    archiveFile.Extract("temp");
                    if (verbose) log("Extracted zip");
                }
            }
            catch (Exception e)
            {
                log("Not a valid ZIP archive / Write Access Denied");
                throw e;
            }
            if (Directory.Exists("temp\\bootstrap\\"))
            {
                log("Found bootstrap");
                if (Directory.Exists("temp\\bootstrap\\Library\\SBInject\\"))
                {
                    createDirIfDoesntExist("files\\usr\\lib\\SBInject");
                    foreach (string file in Directory.GetFiles("temp\\bootstrap\\Library\\SBInject\\"))
                    {
                        File.Move(file, "files\\usr\\lib\\SBInject\\" + new FileInfo(file).Name);
                    }
                    foreach (string file in Directory.GetDirectories("temp\\bootstrap\\Library\\SBInject\\"))
                    {
                        Directory.Move(file, "files\\usr\\lib\\SBInject\\" + new DirectoryInfo(file).Name);
                    }
                    Directory.Delete("temp\\bootstrap\\Library\\SBInject", true);
                }
                moveDirIfPresent("temp\\bootstrap\\Library\\Themes\\", "files\\bootstrap\\Library\\Themes\\");
                foreach (string dir in Directory.GetDirectories("temp"))
                {
                    FileSystem.MoveDirectory(dir, "files\\" + new DirectoryInfo(dir).Name, true);
                }
                foreach (string file in Directory.GetFiles("temp"))
                {
                    File.Copy(file, "files\\" + new FileInfo(file).Name, true);
                }
            }
            else
            {
                bool found = false;
                createDirIfDoesntExist("files\\Applications\\");
                foreach (string app in Directory.GetDirectories("temp", "*", System.IO.SearchOption.AllDirectories))
                {
                    if (app.Split('\\').Contains("circuitbreaker.app"))
                    {
                        found = true;
                        break;
                    }
                }
                if (found)
                {
                    log("Found Circuit Breaker");
                    foreach (string app in Directory.GetDirectories("temp", "*", System.IO.SearchOption.AllDirectories))
                    {
                        if (!app.Contains("circuitbreaker")) continue;
                        FileSystem.CopyDirectory(app, "files\\Applications\\" + new DirectoryInfo(app).Name, true);
                        break;
                    }
                }
                else
                {
                    log("Unrecognised format. Determining ability to install");
                    List<string> exts = new List<string>();
                    List<string> directories = new List<string>();
                    foreach (string dir in Directory.GetDirectories("temp", "*", System.IO.SearchOption.AllDirectories))
                    {
                        directories.Add(new DirectoryInfo(dir).Name);
                    }
                    if (directories.Contains("bootstrap"))
                    {
                        log("Found bootstrap");
                        foreach (string dir in Directory.GetDirectories("temp", "*", System.IO.SearchOption.AllDirectories))
                        {
                            if (new DirectoryInfo(dir).Name == "bootstrap")
                            {
                                createDirIfDoesntExist("files\\bootstrap\\");
                                FileSystem.CopyDirectory(dir, "files\\bootstrap");
                                moveDirIfPresent("files\\bootstrap\\SBInject", "files\\bootstrap\\Library\\SBInject", "files\\bootstrap\\Library\\SBInject");
                                break;
                            }
                        }
                    }
                    else
                    {
                        foreach (string i in Directory.GetFiles("temp"))
                        {
                            string ext = new FileInfo(i).Extension;
                            if (!exts.Contains(ext)) exts.Add(ext);
                        }
                        if (exts.Count == 2 && exts.Contains(".dylib") && exts.Contains(".plist"))
                        {
                            log("Substrate Addon. Installing");
                            createDirIfDoesntExist("files\\usr\\lib\\SBInject");
                            foreach (string i in Directory.GetFiles("temp"))
                            {
                                File.Copy(i, "files\\usr\\lib\\SBInject\\" + new FileInfo(i).Name, true);
                            }
                            moveDirIfPresent("files\\Library\\PreferenceBundles\\", "files\\bootstrap\\Library\\PreferenceBundles\\");
                            moveDirIfPresent("files\\Library\\PreferenceLoader\\", "files\\bootstrap\\Library\\PreferenceLoader\\");
                            moveDirIfPresent("files\\Library\\LaunchDaemons\\", "files\\bootstrap\\Library\\LaunchDaemons\\");
                        }
                        else
                        {
                            log("Unsafe to install. To install this tweak you must do so manually. Press enter to continue...");
                            Console.ReadLine();
                            Environment.Exit(0);
                        }
                    }
                }
            }
        }

        private static void extractIPA(string path)
        {
            clean();
            log("Extracting IPA " + path);
            try
            {
                using (ArchiveFile archiveFile = new ArchiveFile(path))
                {
                    if (verbose) log("Extracting payload");
                    archiveFile.Extract("temp");
                }
                createDirIfDoesntExist("files\\Applications");
                foreach (string app in Directory.GetDirectories("temp\\Payload\\"))
                {
                    if (verbose) log("Moving payload");
                    Directory.Move(app, "files\\Applications\\" + new DirectoryInfo(app).Name);
                    if (verbose) log("Moved payload");
                }
            }
            catch (Exception e)
            {
                log("Not a valid IPA / Write Access Denied");
                throw e;
            }
        }

        private static void extractDeb(string path)
        {
            clean();
            log("Extracting " + path);
            try
            {
                using (ArchiveFile archiveFile = new ArchiveFile(path))
                {
                    if (verbose) log("Extracting data.tar.lzma || data.tar.gz");
                    archiveFile.Extract("temp");
                    if (verbose) log("Extracted");
                }
                if (verbose) log("Extracting data.tar");
                var p = Process.Start(@"7z.exe", "e " + "temp\\data.tar." + (File.Exists("temp\\data.tar.lzma") ? "lzma" : "gz") + " -o.");
                if (verbose) log("Extracting control file");
                p = Process.Start(@"7z.exe", "e " + "temp\\control.tar.gz -o.");
                if (verbose) log("Waiting for subprocess to complete");
                p.WaitForExit();
                if (verbose) log("Successfully extracted data.tar");
                using (ArchiveFile archiveFile = new ArchiveFile("data.tar"))
                {
                    if (verbose) log("Extracting deb files");
                    archiveFile.Extract("files");
                    if (verbose) log("Extracted");
                }
                using (ArchiveFile archiveFile = new ArchiveFile("data.tar"))
                {
                    if (verbose) log("Extracting deb files");
                    archiveFile.Extract("files");
                    archiveFile.Extract("temp");
                    if (verbose) log("Extracted");
                }
                using (ArchiveFile archiveFile = new ArchiveFile("control.tar"))
                {
                    archiveFile.Extract(".");
                }
                Dictionary<string, string> control = new Dictionary<string, string>();
                foreach (string i in File.ReadAllLines("control"))
                {
                    control.Add(i.Split(':')[0].ToLower().Replace(" ", ""), i.Split(':')[1]);
                }
                if (Directory.Exists("files\\Applications") && control.ContainsKey("skipsigning"))
                {
                    foreach (string app in Directory.GetDirectories("temp\\Applications\\"))
                    {
                        File.Create("files\\Applications\\" + app + "\\skip-signing").Close();
                    }
                }
                clean();
            }
            catch (Exception e)
            {
                log("Not a valid deb file / Write Access Denied");
                throw e;
            };
        }

        private static void getTweaksFromStringArray(string[] array)
        {
            tweaks = new List<string>();
            foreach (string i in array)
            {
                if (i.Contains(".deb"))
                {
                    if (verbose) log("Found deb: " + i);
                    tweaks.Add(i);
                }
                if (i.Contains(".ipa"))
                {
                    if (verbose) log("Found ipa: " + i);
                    tweaks.Add(i);
                }
                if (i.Contains(".zip"))
                {
                    if (verbose) log("Found zip: " + i);
                    tweaks.Add(i);
                }
            }
        }

        private static void getSSHSettings()
        {
            data = File.ReadAllLines("settings"); //get ssh settings
            for (int i = 0; i != data.Length; i++)
            {
                data[i] = data[i].Split('#')[0];
            }
            if (verbose) log("Read settings");
            user = "root";
        }

        private static void clean()
        {
            deleteIfExists("JMWCrypto.dll");
            emptyDir("files");
            emptyDir("temp");
            deleteIfExists("data.tar");
            deleteIfExists("control.tar");
            deleteIfExists("control");
        }

        private static void getOptions(string[] args)
        {
            if (args.Contains("convert")) convert = true;
            if (args.Contains("uninstall")) uninstall = true;
            if (args.Contains("install")) install = true;
            if (args.Contains("manual")) manual = true;
            if (args.Contains("dont-update")) update = false;
            if (args.Contains("dont-refresh")) uicache_override = true;
            if (args.Contains("dont-respring")) respring_override = true;
            if (args.Contains("no-install")) onlyPerformSSHActions = true;
            if (args.Contains("verbose") || File.Exists("verbose")) verbose = true;
            skip = File.Exists("skip.list") ? File.ReadAllLines("skip.list").ToList() : new List<string>();
            if (!File.Exists("settings"))
            {
                string[] def = new string[] { "192.168.1.1", "22", "" };
                File.WriteAllLines("settings", def);
            }
        }

        private static void getJailbreakSpecificOptions(Session session)
        {
            if (session.FileExists("/usr/lib/SBInject"))
            {
                if (verbose) log("You're running Electa. I'll convert tweaks to that format & add entitlements to applications");
                convert = true;
                if (!session.FileExists("/bootstrap/Library/Themes"))
                {
                    session.CreateDirectory("/bootstrap/Library/Themes");
                    session.ExecuteCommand("touch /bootstrap/Library/Themes/dont-delete");
                    log("Themes folder missing. Touching /bootstrap/Library/Themes/dont-delete to prevent this in future");
                }
                jtool = true;
            }
            if (session.FileExists("/jb/"))
            {
                if (verbose) log("You're running LibreiOS. I'll add entitlements to applications");
                jtool = true;
            }
        }

        private static Session getSession(string ip, string user, string password, int port)
        {
            log("Connecting");
            SessionOptions sessionOptions = new SessionOptions
            {
                Protocol = Protocol.Sftp,
                HostName = ip,
                UserName = user,
                Password = password,
                PortNumber = port,
                GiveUpSecurityAndAcceptAnySshHostKey = true
            };
            Session session = new Session();
            try
            {
                session.Open(sessionOptions);
            }
            catch (SessionRemoteException e)
            {
                if (e.ToString().Contains("refused")) log("Error: SSH Connection Refused\nAre you jailbroken?\nHave you entered your devices IP and port correctly?");
                else if (e.ToString().Contains("Access denied")) log("Error: SSH Connection Refused due to incorrect credentials. Are you sure you typed your password correctly?");
                else if (e.ToString().Contains("Cannot initialize SFTP protocol")) log("Error: SFTP not available. Make sure you have sftp installed by default. For Yalu or Meridian, please install \"SCP and SFTP for dropbear\" by coolstar. For LibreIOS, make sure SFTP is moved to /usr/bin/.");
                else
                {
                    log("Unknown Error. Please use the big red bug report link and include some form of crash report. Error report copying to clipboard.");
                    Thread.Sleep(2000);
                    Clipboard.SetText(e.ToString());
                    throw e;
                }
                Console.ReadLine();
                Environment.Exit(0);
            }
            log("Connected to SSH");
            return session;
        }

        private static void checkForUpdates()
        {
            if (verbose) log("Checking for updates");
            try
            {
                using (WebClient client = new WebClient())
                {
                    string version = client.DownloadString("https://raw.githubusercontent.com/josephwalden13/tweak-installer/master/bin/Debug/version.txt");
                    string current = File.ReadAllText("version.txt");
                    if (current != version)
                    {
                        log($"Version {version.Replace("\n", "")} released. Please download it from https://github.com/josephwalden13/tweak-installer/releases\nPress enter to continue...");
                        Console.ReadLine();
                    }
                }
            }
            catch
            {
                if (verbose) log("Update check failed");
            }
        }

        private static void manualMode()
        {
            createDirIfDoesntExist("files");
            log("Manual mode. Please move rootfs file into 'files' and press enter to continue");
            Console.ReadLine();
        }

        private static void convertTweaks()
        {
            log("Converting to electra tweak format");
            createDirIfDoesntExist("files\\bootstrap");
            createDirIfDoesntExist("files\\bootstrap\\Library");
            if (Directory.Exists("files\\Library\\MobileSubstrate\\"))
            {
                if (verbose) log("Found MobileSubstrate");
                createDirIfDoesntExist("files\\usr\\lib\\SBInject");
                foreach (string file in Directory.GetFiles("files\\Library\\MobileSubstrate\\DynamicLibraries\\"))
                {
                    if (verbose) log("Moving Substrate file " + file + " to SBInject");
                    File.Move(file, "files\\usr\\lib\\SBInject\\" + new FileInfo(file).Name);
                }
                foreach (string file in Directory.GetDirectories("files\\Library\\MobileSubstrate\\DynamicLibraries\\"))
                {
                    if (verbose) log("Moving Substrate dir " + file + " to SBInject");
                    Directory.Move(file, "files\\usr\\lib\\SBInject\\" + new DirectoryInfo(file).Name);
                }
                Directory.Delete("files\\Library\\MobileSubstrate", true);
                if (verbose) log("Deleted MobileSubstrate folder");
            }
            moveDirIfPresent("files\\Library\\Themes\\", "files\\bootstrap\\Library\\Themes\\");
            moveDirIfPresent("files\\Library\\LaunchDaemons\\", "files\\bootstrap\\Library\\LaunchDaemons\\");
            moveDirIfPresent("files\\Library\\PreferenceBundles\\", "files\\bootstrap\\Library\\PreferenceBundles\\");
            moveDirIfPresent("files\\Library\\PreferenceLoader\\", "files\\bootstrap\\Library\\PreferenceLoader\\");
        }

        private static void getFiles()
        {
            if (verbose) log("Getting all files");
            crawler = new Crawler(Environment.CurrentDirectory + "\\files", true); //gets all files in the tweak
            crawler.Remove("DS_STORE");
        }

        private static void installFiles(Session session)
        {
            if (session.FileExists("/plat.ent"))
            {
                session.RemoveFiles("/plat.ent");
                if (verbose) log("Removed old entitlements file from the device");
            }
            createDirIfDoesntExist("backup");
            if (Directory.Exists("files\\Applications") && jtool)
            {
                File.Copy("plat.ent", "files\\plat.ent", true);
                if (verbose) log("Entitlements needed. Copying entitlements file");
            }
            if (Directory.Exists("files\\Applications\\electra.app"))
            {
                if (verbose) log("please no");
                var f = MessageBox.Show("Please do not try this");
                Environment.Exit(0);
            }
            if (verbose) log("Creating directory list");
            string[] directories = Directory.GetDirectories("files", "*", searchOption: System.IO.SearchOption.AllDirectories);
            if (verbose) log("Got list. Creating backup folders");
            foreach (string dir in directories)
            {
                if (!Directory.Exists("backup\\" + dir.Replace("files\\", "\\")))
                {
                    Directory.CreateDirectory("backup\\" + dir.Replace("files\\", "\\"));
                }
            }
            log("Preparing to install");

            if (verbose) log("Creating local file list");
            List<string> local = new List<string>();
            crawler.Files.ForEach(i => local.Add(convert_path(i)));

            if (verbose) log("Creating remote file list");
            List<string> remote = new List<string>();
            foreach (string i in Directory.GetDirectories("files"))
            {
                string dir = new DirectoryInfo(i).Name;
                if (dir == "System")
                {
                    log("This tweak may take longer than usual to process (45 second max)");
                }
                session.ExecuteCommand("find /" + dir + " > ~/files.list");
                session.GetFiles("/var/root/files.list", "files.list");
                foreach (string file in File.ReadAllLines("files.list"))
                {
                    remote.Add(file);
                }
                File.Delete("files.list");
            }

            List<string> duplicates = new List<string>();
            foreach (string i in local)
            {
                if (remote.Contains(i))
                {
                    duplicates.Add(i);
                }
            }
            bool overwrite = false;
            foreach (var i in duplicates)
            {
                bool go = false, action = false;
                if (!overwrite)
                {
                    if (verbose) log("\b\b\b\b" + convert_path(i) + " already exists");
                    log("\b\b\b\bDo you want to backup and overwrite " + convert_path(i) + "? (y/n/a)");
                    while (true)
                    {
                        switch (Console.ReadKey().Key)
                        {
                            case ConsoleKey.Y:
                                go = true;
                                action = true;
                                break;
                            case ConsoleKey.A:
                                go = true;
                                action = true;
                                overwrite = true;
                                break;
                            case ConsoleKey.N:
                                action = false;
                                go = true;
                                break;
                        }
                        log("\n");
                        if (go) break;
                    }
                }
                else
                {
                    action = true;
                }
                if (!action)
                {
                    if (verbose) log("\b\b\b\bSkipping file " + i);
                    File.Delete("files\\" + i);
                    if (!skip.Contains(i))
                    {
                        skip.Add(i);
                    }
                }
                session.GetFiles(convert_path(i), @"backup\" + i.Replace("/", "\\"));
            }
            log("\b\b\b\b    \b\b\b\bInstalling");
            foreach (string dir in Directory.GetDirectories("files"))
            {
                if (verbose) log("Installing directory " + dir);
                session.PutFiles(dir, "/"); //put directories
            }
            foreach (string file in Directory.GetFiles("files"))
            {
                if (verbose) log("Installing file " + file);
                session.PutFiles(file, "/"); //put files
            }
            Console.Write("\b\b\b\b    \b\b\b\bDone\n");
            File.WriteAllLines("skip.list", skip);
            if (Directory.Exists("files\\Applications") && jtool)
            {
                if (verbose) log("Entitlements needed");
                session.PutFiles("plat.ent", "/");
                if (verbose) log("Sending entitlements");
                log("Signing applications");
                foreach (var app in Directory.GetDirectories("files\\Applications\\"))
                {
                    uicache = true;
                    if (verbose) log("Signing " + convert_path(app.Replace("files\\", "\\")));
                    //Crawler crawler = new Crawler(app);
                    //Dictionary<string, object> dict = (Dictionary<string, object>)Plist.readPlist(app + "\\Info.plist");
                    //string bin = dict["CFBundleExecutable"].ToString();
                    crawler = new Crawler(app, true);
                    crawler.Files.ForEach(i =>
                    {
                        bool sign = false;
                        if (new FileInfo(i).Name.Split('.').Length < 2) sign = true;
                        if (!sign)
                        {
                            if (i.Split('.').Last() == "dylib") sign = true;
                        }
                        i = convert_path(i);
                        MessageBox.Show(app);
                        if (File.Exists(app + "\\skip-signing"))
                        {
                            sign = false;
                            if (verbose) log("Skipped Signing " + i);
                        }
                        if (sign)
                        {
                            session.ExecuteCommand("jtool -e arch -arch arm64 " + convert_path(app.Replace("files\\", "\\")) + i);
                            session.ExecuteCommand("mv " + convert_path(app.Replace("files\\", "\\")) + i + ".arch_arm64 " + convert_path(app.Replace("files\\", "\\")) + i);
                            session.ExecuteCommand("jtool --sign --ent /plat.ent --inplace " + convert_path(app.Replace("files\\", "\\")) + i);
                            if (verbose) log("Signed " + convert_path(app.Replace("files\\", "\\")) + i);
                        }
                    });
                    crawler = new Crawler("files");
                    crawler.Files.ForEach(i =>
                    {
                        session.ExecuteCommand("chmod 777 " + convert_path(i.Replace("\\files", "")));
                    });
                }
            }
            finish(session);
        }

        private static void uninstallFiles(Session session)
        {
            log("Preparing to uninstall");
            bool overwrite = false;
            List<string> remove = new List<string>();
            crawler.Files.ForEach(i =>
            {
                if (!skip.Contains(i))
                {
                    bool go = false, action = false;
                    if (File.Exists("backup" + i) && !overwrite)
                    {
                        if (verbose) log("You have a backup of this file");
                        log("Do you want to restore " + convert_path(i) + " from your backup? (y/n/a)");
                        while (true)
                        {
                            switch (Console.ReadKey().Key)
                            {
                                case ConsoleKey.Y:
                                    go = true;
                                    action = true;
                                    break;
                                case ConsoleKey.A:
                                    go = true;
                                    action = true;
                                    overwrite = true;
                                    break;
                                case ConsoleKey.N:
                                    go = true;
                                    break;
                            }
                            log("\n");
                            if (go) break;
                        }
                    }
                    if (action || overwrite)
                    {
                        string path = i.Replace(i.Substring(i.LastIndexOf('\\')), "");
                        session.PutFiles(new FileInfo("backup" + convert_path(i)).ToString().Replace("/", "\\"), convert_path(path) + "/" + new FileInfo(i).Name);
                        if (verbose) log("Reinstalled " + i);
                    }
                    else
                    {
                        remove.Add(convert_path(i, true));
                    }
                }
            });
            log("Uninstalling");
            string script = "";
            foreach (string i in remove)
            {
                script += "rm " + i + "\n";
            }
            File.WriteAllText("script.sh", script);
            session.PutFiles("script.sh", "script.sh");
            session.ExecuteCommand("sh script.sh");
            if (Directory.Exists("files\\Applications"))
            {
                if (verbose) log("uicache refresh required");
                uicache = true;
            }
            log("Locating and removing *some* empty folders");
            session.ExecuteCommand("find /System/Library/Themes/ -type d -empty -delete");
            session.ExecuteCommand("find /usr/ -type d -empty -delete");
            session.ExecuteCommand("find /Applications/ -type d -empty -delete");
            session.ExecuteCommand("find /Library/ -type d -empty -delete");
            session.ExecuteCommand("find /bootstrap/Library/Themes/* -type d -empty -delete");
            session.ExecuteCommand("find /bootstrap/Library/PreferenceLoader/* -type d -empty -delete");
            session.ExecuteCommand("find /bootstrap/Library/PreferenceBundles/* -type d -empty -delete");
            session.ExecuteCommand("find /bootstrap/Library/SBInject/* -type d -empty -delete");
            if (verbose) log("Done");
            finish(session);
        }
    }
}