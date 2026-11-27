using Quick.Shell.Utils;
using System.Diagnostics;
using YiQiDong.Agent;
using System.Runtime.InteropServices;
using MongoDB.Bson;
using MongoDB.Driver;

namespace YiQiDong.MongoDB.Utils;

public class DbUtils
{
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

    public static string CreateRootUser(string host, int port)
    {
        var settings = new MongoClientSettings()
        {
            Server = new MongoServerAddress(host,port)
        };
        try
        {
            using (var client = new MongoClient(settings))
            {
                var adminDb = client.GetDatabase("admin");
                var pwd = Guid.NewGuid().ToString("N");
                var command = new BsonDocument
                {
                    { "createUser", "root" },
                    { "pwd", pwd },
                    {
                        "roles", new BsonArray
                        {
                            new BsonDocument { { "role", "root" }, { "db", "admin" } }
                        }
                    }
                };
                var result = adminDb.RunCommand<BsonDocument>(command);
                return pwd;
            }
        }
        catch (MongoCommandException ex)
        {
            throw new IOException($"创建失败 (code={ex.Code}, {ex.CodeName}): {ex.Message}");
        }
    }

    public static async Task ShutdownDb(string host, int port, string user, string password)
    {
        var settings = new MongoClientSettings()
        {
            Server = new MongoServerAddress(host,port),
            Credential = MongoCredential.CreateCredential("admin", user, password)
        };
        using (var client = new MongoClient(settings))
        {
            var adminDb = client.GetDatabase("admin");
            await adminDb.RunCommandAsync<BsonDocument>(new BsonDocument
            {
                {"shutdown", 1}
            });
        }
    }

    public static void ModifyPassword(string host, int port, string user, string oldPassword, string newPassword)
    {
        var settings = new MongoClientSettings()
        {
            Server = new MongoServerAddress(host,port),
            Credential = MongoCredential.CreateCredential("admin", user, oldPassword)
        };
        using (var client = new MongoClient(settings))
        {
            var adminDb = client.GetDatabase("admin");
            adminDb.RunCommand<BsonDocument>(new BsonDocument
        {
            { "updateUser", user },
            { "pwd", newPassword }
        });
        }
    }
}
