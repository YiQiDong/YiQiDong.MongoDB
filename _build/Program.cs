using Quick.Build;
using SharpCompress.Archives;
using SharpCompress.Common;
using System.IO.Compression;

var productDir = "YiQiDong.MongoDB";
var appFolder = QbFolder.GetAppFolder();
if (Environment.CurrentDirectory == appFolder)
    Environment.CurrentDirectory = Path.GetFullPath("../../../../");
else if (Environment.CurrentDirectory == Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(appFolder))))
    Environment.CurrentDirectory = Path.GetFullPath("../");

var dbVersionString = "9.0.8";

//https://fastdl.mongodb.org/windows/mongodb-windows-x86_64-9.0.8.zip
//https://fastdl.mongodb.org/linux/mongodb-linux-aarch64-ubuntu2204-9.0.8.tgz
//https://fastdl.mongodb.org/linux/mongodb-linux-x86_64-ubuntu2204-9.0.8.tgz

string URL_TEMPLATE_WINDOWS = "https://fastdl.mongodb.org/windows/mongodb-windows-{0}-{1}.zip";
string URL_TEMPLATE_LINUX = "https://fastdl.mongodb.org/linux/mongodb-linux-{0}-ubuntu2204-{1}.tgz";


Console.WriteLine("----------------------------------");
Console.WriteLine("  欢迎使用MongoDB编译脚本");
Console.WriteLine("----------------------------------");
Version version;

var httpClient = QbNet.GetHttpClient();


Console.WriteLine("请选择运行平台(一个都不选代表全选)：");
var ridDict = new Dictionary<string, string>()
{
    ["win-x64"] = "win-x64",
    ["linux-x64"] = "linux-x64",
    ["linux-arm64"] = "linux-arm64"
};

var rids = QbSelect.MultiSelect(ridDict.ToArray(), selectedForegroundColor: ConsoleColor.Green);
if (rids.Length == 0)
    rids = ridDict.Keys.ToArray();

var binFolder = Path.Combine(Environment.CurrentDirectory, "bin");
if (!Directory.Exists(binFolder))
    Directory.CreateDirectory(binFolder);
var cacheFolder = Path.Combine(binFolder, "cache");
if (!Directory.Exists(cacheFolder))
    Directory.CreateDirectory(cacheFolder);
var displayDownloadProgress = new Action<QbNet.TransferProgress>(t =>
{
    QbConsole.DisplaySameLineInConsole($"[{(t.Current * 100 / t.Total).ToString("N0")}%]进度：{t.Current.ToString("N0")}/{t.Total.ToString("N0")}，速度：{t.Speed.ToString("N0")}，剩余时间：{t.RemainingTime}");
});
var buildTime = DateTime.Now;

foreach (var rid in rids)
{
    Console.WriteLine($"开始打包[{rid}]...");

    var buildFolder = Path.Combine(binFolder, "build");
    if (Directory.Exists(buildFolder))
    {
        Console.WriteLine($"正在清理构建目录...");
        Directory.Delete(buildFolder, true);
    }
    var tmpFolder = Path.Combine(binFolder, "tmp");
    if (Directory.Exists(tmpFolder))
    {
        Console.WriteLine($"正在清理临时目录...");
        Directory.Delete(tmpFolder, true);
    }
    switch (rid)
    {
        case "win-x64":
            {
                //开始下载win-x64版本                    
                var url = string.Format(URL_TEMPLATE_WINDOWS, "x86_64",dbVersionString);
                var file = Path.Combine(cacheFolder, Path.GetFileName(url));
                if (!File.Exists(file))
                {
                    Console.WriteLine($"正在从[{url}]下载文件...");
                    QbNet.DownloadFile(url, file, CancellationToken.None, displayDownloadProgress).Wait();
                    Console.WriteLine();
                }
                Console.WriteLine($"正在解压文件[{file}]...");
                var ignoreKeywords = new[] { "mongos.exe", "Install-Compass", "vc_redist" };
                var ignoreSuffixes = new[] { ".pdb" };
                using (var archive = ZipFile.OpenRead(file))
                {
                    foreach (var entry in archive.Entries)
                    {
                        if (ignoreKeywords.Any(item => entry.FullName.Contains(item)))
                            continue;
                        if (ignoreSuffixes.Any(item => entry.FullName.EndsWith(item)))
                            continue;

                        var extractFilePath = Path.Combine(tmpFolder, entry.FullName);
                        try
                        {
                            if (extractFilePath.EndsWith("/"))
                            {
                                Directory.CreateDirectory(extractFilePath);
                            }
                            else
                            {
                                entry.ExtractToFile(extractFilePath);
                            }
                        }
                        catch
                        {
                            Console.WriteLine(extractFilePath);
                            throw;
                        }
                    }
                }
                Directory.Move(Directory.GetDirectories(tmpFolder)[0], buildFolder);
                Thread.Sleep(1000);
                break;
            }
        default:
            {
                var file = string.Empty;
                var url = string.Empty;

                var ignoreKeywords = new[] { "mongos", "install_compass" };
                var ignoreSuffixes = new[] { ".a" };

                url = string.Format(URL_TEMPLATE_LINUX, rid == "linux-x64" ? "x86_64" : "aarch64", dbVersionString);
                file = Path.Combine(cacheFolder, Path.GetFileName(url));
                if (!File.Exists(file))
                {
                    Console.WriteLine($"正在从[{url}]下载文件...");
                    QbNet.DownloadFile(url, file, CancellationToken.None, displayDownloadProgress).Wait();
                    Console.WriteLine();
                }
                Console.WriteLine($"正在解压文件[{file}]...");
                var tarArchiveFile = Path.Combine(cacheFolder, "tmp.tar");
                //解压gz文件
                using (var fileStream = File.Open(file, FileMode.Open))
                using (var gzStream = new GZipStream(fileStream, CompressionMode.Decompress))
                using (var fs = File.OpenWrite(tarArchiveFile))
                    gzStream.CopyTo(fs);
                //解压tar文件
                using (var tarArchive = SharpCompress.Archives.Tar.TarArchive.OpenArchive(tarArchiveFile))
                {
                    foreach (var entry in tarArchive.Entries.Where(entry => !entry.IsDirectory))
                    {
                        if (ignoreKeywords.Any(item => entry.Key.Contains(item)))
                            continue;
                        if (ignoreSuffixes.Any(item => entry.Key.EndsWith(item)))
                            continue;
                        var extractFilePath = Path.Combine(tmpFolder, entry.Key);
                        try
                        {
                            if (entry.IsDirectory)
                            {
                                Directory.CreateDirectory(extractFilePath);
                            }
                            else
                            {
                                if (!string.IsNullOrEmpty(entry.LinkTarget))
                                    continue;
                                var folder = Path.GetDirectoryName(extractFilePath);
                                if (!Directory.Exists(folder))
                                    Directory.CreateDirectory(folder);
                                entry.WriteToFile(extractFilePath);
                            }
                        }
                        catch
                        {
                            Console.WriteLine(extractFilePath);
                            throw;
                        }
                    }
                }
                Thread.Sleep(1000);
                Directory.Move(Directory.GetDirectories(tmpFolder)[0], buildFolder);
                break;
            }
    }
    Directory.Delete(tmpFolder, true);

    Console.WriteLine("正在发布YiQiDong.MongoDB项目...");
    QbCommand.Run("dotnet", $"publish {productDir} -c Release -o {buildFolder} -r {rid}");
        var imageMetaFile = Path.Combine(buildFolder, "YiQiDong.Image.json");
    var imageInfo = new YiQiDong.Protocol.V1.Model.ImageInfo()
    {
        DefaultId = "MongoDB",
        Name = "MongoDB",
        Version = dbVersionString,
        BuildTime = buildTime.ToString("yyyy-MM-dd HH:mm:ss"),
        Tags = ["数据库"],
        Description = "MongoDB是一个流行的开源文档型数据库，它使用类似 JSON 的文档模型存储数据，这使得数据存储变得非常灵活。",
        Platform = [rid],
        Path = ["bin"]
    };
    //修改Agent的值
    if (rid.StartsWith("win-"))
        imageInfo.AgentExecute = $"{productDir}.exe";
    else
        imageInfo.AgentExecute = productDir;
    File.WriteAllText(imageMetaFile, imageInfo.ToJson());

    var outFile = $"bin/MongoDB-{dbVersionString}-{rid}_{buildTime:yyyyMMddHHmmss}.ymg";
    Console.WriteLine($"正在制作易启动镜像[{rid}]...");
    using (var archive = SharpCompress.Archives.Zip.ZipArchive.CreateArchive())
    {
        archive.AddAllFromDirectory(buildFolder);
        archive.SaveTo(outFile, CompressionType.LZMA);
    }
    Directory.Delete(buildFolder, true);
}
Console.WriteLine("完成");
QbGui.OpenFolder("bin");