using Quick.Shell.Utils;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System;
using YiQiDong.Agent;
using System.Runtime.InteropServices;
using MongoDB.Bson;
using MongoDB.Driver;

namespace YiQiDong.MongoDB.Utils;

public class DbUtils
{
    /*
    public static void Init(string imageFolder)
    {
        //Linux系统上添加LD_LIBRARY_PATH环境变量
        if (OperatingSystem.IsLinux())
        {
            var mysqlLibDir = Path.Combine(imageFolder, "lib");
            //添加PATH环境变量
            var path = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH");
            if (string.IsNullOrEmpty(path))
                path = mysqlLibDir;
            else
                path = $"{path}:{mysqlLibDir}";
            Environment.SetEnvironmentVariable("LD_LIBRARY_PATH", path);
        }
    }

    private static bool IsRunAsRoot()
    {
        AgentContext.LogInfo("正在检查当前用户...");
        var tmpPsi = new ProcessStartInfo("whoami");
        tmpPsi.RedirectStandardOutput = true;
        var tmpProcess = Process.Start(tmpPsi);
        var account = tmpProcess.StandardOutput.ReadToEnd();
        return account.Trim() == "root";
    }
*/
    private static void outputNotSupportOsAndArchitecture()
    {
        AgentContext.LogWarn($"不支持的操作系统[{RuntimeInformation.OSDescription}]+平台架构[{RuntimeInformation.OSArchitecture}]。");
    }


    public static ProcessStartInfo GetPsi(string imageFolder, string dataFolder, params string[] arguments)
    {
        var process_filename = "";
        var process_arguments = new List<string>
            {
                $"--config={Path.Combine(dataFolder, "mongod.conf")}",
                $"--dbpath={Path.Combine(dataFolder, "data")}"
            };
        if (arguments != null)
            process_arguments.AddRange(arguments);

        if (OperatingSystem.IsWindows())
        {
            process_filename = Path.Combine(imageFolder, "bin", "mongod.exe");
        }
        else if (OperatingSystem.IsLinux())
        {
            process_filename = Path.Combine(imageFolder, "bin", "mongod");
            //为进程添加可执行权限
            UnixUtils.AddExecutePermissionToFile(process_filename);
        }
        else
        {
            outputNotSupportOsAndArchitecture();
        }
        var psi = ProcessUtils.CreateProcessStartInfo(process_filename, process_arguments.ToArray());
        ProcessUtils.ProcessProcessStartInfo(psi);
        psi.WorkingDirectory = dataFolder;
        return psi;
    }

    public static async Task ShutdownDb(string host, int port)
    {
        var connectionString = $"mongodb://{host}:{port}";
        var client = new MongoClient(connectionString);
        var adminDb = client.GetDatabase("admin");
        await adminDb.RunCommandAsync<BsonDocument>(new BsonDocument
        {
            {"shutdown", 1}
        });
    }
}
