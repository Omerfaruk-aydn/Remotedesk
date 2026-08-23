using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using SecureRemoteDesk.Desktop.Services;
using SecureRemoteDesk.Desktop.ViewModels;

namespace SecureRemoteDesk.Desktop;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // --uitest=... modunda ShellViewModel'in test config'ini set et
        // (MainWindow.xaml'da <vm:ShellViewModel /> oluşmadan ÖNCE set edilmeli)
        var uitestArg = e.Args.FirstOrDefault(a => a.StartsWith("--uitest=", StringComparison.OrdinalIgnoreCase));
        if (uitestArg is not null)
        {
            ShellViewModel.UiTest = ParseUiTest(uitestArg["--uitest=".Length..]);
        }

        if (e.Args.Length > 0 && e.Args[0] == "--selftest")
        {
            _ = Task.Run(async () =>
            {
                int code = 0;
                try
                {
                    code = await SelfTest.RunAsync();
                }
                catch (Exception ex)
                {
                    try { SelfTest.Log($"CRASH: {ex}"); } catch { }
                    code = 2;
                }
                Dispatcher.Invoke(() => Shutdown(code));
            });
            return;
        }
        base.OnStartup(e);
    }

    private static ShellViewModel.UiTestConfig ParseUiTest(string raw)
    {
        // Format: mode[:key=value,key=value...]
        // Örnekler:
        //   --uitest=host
        //   --uitest=host:approve,file,clip
        //   --uitest=viewer-send:C:\path1.bin
        //   --uitest=viewer-send:127.0.0.1:50555,C:\path1.bin,C:\path2.bin
        var parts = raw.Split(':', 2);
        var cfg = new ShellViewModel.UiTestConfig
        {
            Mode = parts[0],
            DebugLogPath = Path.Combine(Path.GetTempPath(), $"SecureRemoteDesk.UiTest.{Environment.ProcessId}.log")
        };
        try { File.Delete(cfg.DebugLogPath); } catch { }
        UiTestLog(cfg, $"PID={Environment.ProcessId} MODE={cfg.Mode} RAW={raw}");
        if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[1]))
        {
            var tokens = parts[1].Split(',');
            if (cfg.Mode == "host")
            {
                cfg.AutoApprove = true;
                foreach (var t in tokens)
                {
                    var tk = t.Trim();
                    if (tk == "approve") cfg.AutoApprove = true;
                    else if (tk == "file") cfg.AllowFileTransfer = true;
                    else if (tk == "clip" || tk == "clipboard") cfg.AllowClipboard = true;
                }
            }
            else if (cfg.Mode == "viewer-send")
            {
                cfg.ConnectCode = tokens[0].Trim();
                cfg.SendFiles = tokens.Skip(1).Select(t => t.Trim()).Where(t => t.Length > 0).ToArray();
                UiTestLog(cfg, $"ConnectCode={cfg.ConnectCode} SendFiles=[{string.Join(",", cfg.SendFiles ?? Array.Empty<string>())}]");
            }
        }
        else if (cfg.Mode == "host")
        {
            cfg.AutoApprove = true;
            cfg.AllowFileTransfer = true;
        }
        return cfg;
    }

    internal static void UiTestLog(ShellViewModel.UiTestConfig cfg, string msg)
    {
        if (cfg is null || string.IsNullOrEmpty(cfg.DebugLogPath)) return;
        try
        {
            File.AppendAllText(cfg.DebugLogPath, $"[{DateTime.Now:HH:mm:ss.fff}] [{Environment.ProcessId}] {msg}\r\n");
        }
        catch { }
    }
}

internal static class SelfTest
{
    public static readonly string LogPath = Path.Combine(Path.GetTempPath(), "SecureRemoteDesk.SelfTest.log");
    private const int FileChunkSize = 64 * 1024;

    private class Counter { public int Passed; public int Failed; }
    private static readonly object _logLock = new();
    private static bool _bomWritten = false;

    public static void Log(string message)
    {
        lock (_logLock)
        {
            if (!_bomWritten)
            {
                // PowerShell 5.1 + Türkçe Windows ANSI okumasın diye UTF-8 BOM
                File.WriteAllBytes(LogPath, new byte[] { 0xEF, 0xBB, 0xBF });
                _bomWritten = true;
            }
            File.AppendAllText(LogPath, message + Environment.NewLine, new UTF8Encoding(false));
        }
    }

    public static async Task<int> RunAsync()
    {
        _bomWritten = false; // yeni run için sıfırla
        try { File.Delete(LogPath); } catch { }
        Log("== SecureRemoteDesk self-test başlıyor ==");
        var c = new Counter();
        try
        {
            await TestFileTransfer("dosya_transfer_test.bin", 512 * 1024, c);
            await TestLargeFile("buyuk_dosya_test.bin", 200 * 1024 * 1024, c);
            await TestParallelFiles(c);
            await TestClipboard(c);
        }
        catch (Exception ex)
        {
            Log($"[FAIL] beklenmeyen hata: {ex}");
            c.Failed++;
        }

        Log($"== Sonuç: {c.Passed} geçti, {c.Failed} kaldı ==");
        return c.Failed == 0 ? 0 : 1;
    }

    private static async Task TestFileTransfer(string fileName, int size, Counter c)
    {
        Log($"\n[TEST] Dosya aktarımı: {fileName} ({size} bytes)");
        var srcPath = Path.Combine(Path.GetTempPath(), "SecureRemoteDesk.SelfTest", fileName);
        await PrepareTestFileAsync(srcPath, size, seed: 42);
        var srcHash = await FileHashAsync(srcPath);
        Log($"  Kaynak: {srcPath}");
        Log($"  sha256: {srcHash}");

        const int port = 50556;
        await using var host = new LocalHostSessionServer(port);
        await host.StartAsync();
        Log($"  Host dinliyor: 127.0.0.1:{port}");

        var incomingRequest = new TaskCompletionSource();
        host.IncomingRequest += _ => incomingRequest.TrySetResult();
        host.StatusChanged += s => Log($"  [host] {s}");

        await using var viewer = new LocalViewerSessionClient();
        viewer.StatusChanged += s => Log($"  [viewer] {s}");

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var hostOffer = new TaskCompletionSource<IncomingFileOffer>();
        var hostChunks = new List<(string id, int idx, byte[] data)>();
        var hostComplete = new TaskCompletionSource<string>();
        host.FileOfferReceived += offer => { Log($"  [host] FILE_OFFER: {offer.FileName} {offer.SizeBytes}"); hostOffer.TrySetResult(offer); };
        host.FileChunkReceived += c0 => hostChunks.Add((c0.TransferId, c0.ChunkIndex, c0.Data));
        host.FileCompleteReceived += id => { Log($"  [host] FILE_COMPLETE: {id}"); hostComplete.TrySetResult(id); };

        var viewerTask = Task.Run(async () => await viewer.ConnectAsync($"127.0.0.1:{port}", cts.Token));
        var approveTask = Task.Run(async () =>
        {
            await incomingRequest.Task.WaitAsync(cts.Token);
            await host.ApproveAsync(remoteControlGranted: false, fileTransferGranted: true, clipboardGranted: true, cts.Token);
        });

        await Task.WhenAll(viewerTask, approveTask);
        await Task.Delay(300, cts.Token);
        Log("  Handshake tamam");

        var transferId = Guid.NewGuid().ToString("N");
        await viewer.SendFileOfferAsync(transferId, fileName, size, cts.Token);

        var receivedOffer = await hostOffer.Task.WaitAsync(TimeSpan.FromSeconds(5), cts.Token);
        await host.SendFileDecisionAsync(transferId, accepted: true, cts.Token);

        const int chunkSize = 64 * 1024;
        await using var fs = File.OpenRead(srcPath);
        var buffer = new byte[chunkSize];
        int idx = 0;
        int read;
        long totalSent = 0;
        while ((read = await fs.ReadAsync(buffer.AsMemory(0, chunkSize), cts.Token)) > 0)
        {
            var payload = new byte[read];
            Buffer.BlockCopy(buffer, 0, payload, 0, read);
            await viewer.SendFileChunkAsync(transferId, idx++, payload, cts.Token);
            totalSent += read;
        }
        await viewer.SendFileCompleteAsync(transferId, cts.Token);
        Log($"  Viewer {idx} chunk, {totalSent} bayt gönderdi");

        var completedId = await hostComplete.Task.WaitAsync(TimeSpan.FromSeconds(30), cts.Token);
        if (completedId != transferId) throw new Exception($"Transfer ID uyuşmuyor: {completedId} != {transferId}");

        var downloadPath = Path.Combine(Path.GetTempPath(), "SecureRemoteDesk.SelfTest", "received_" + fileName);
        await using (var outFs = new FileStream(downloadPath, FileMode.Create, FileAccess.Write))
        {
            foreach (var chunk in hostChunks.OrderBy(x => x.idx))
            {
                await outFs.WriteAsync(chunk.data);
            }
        }
        var rcvHash = await FileHashAsync(downloadPath);
        Log($"  Host aldığı dosya: {downloadPath}");
        Log($"  sha256: {rcvHash}");

        if (srcHash == rcvHash)
        {
            Log($"  [PASS] Dosya bütünlüğü doğrulandı (boyut={size} bayt)");
            c.Passed++;
        }
        else
        {
            Log($"  [FAIL] Hash uyuşmuyor: {srcHash} != {rcvHash}");
            c.Failed++;
        }
    }

    private static async Task TestLargeFile(string fileName, int size, Counter c)
    {
        Log($"\n[TEST] Büyük dosya aktarımı: {fileName} ({size / (1024 * 1024)} MB)");
        var srcPath = Path.Combine(Path.GetTempPath(), "SecureRemoteDesk.SelfTest", fileName);
        await PrepareTestFileAsync(srcPath, size, seed: 1337);
        var srcHash = await FileHashAsync(srcPath);
        Log($"  Kaynak: {srcPath}");
        Log($"  sha256: {srcHash}");

        const int port = 50558;
        await using var host = new LocalHostSessionServer(port);
        await host.StartAsync();
        var incomingRequest = new TaskCompletionSource();
        host.IncomingRequest += _ => incomingRequest.TrySetResult();
        host.StatusChanged += s => Log($"  [host] {s}");

        await using var viewer = new LocalViewerSessionClient();
        viewer.StatusChanged += s => Log($"  [viewer] {s}");

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(180));
        var hostOffer = new TaskCompletionSource<IncomingFileOffer>();
        var hostChunks = new List<(string id, int idx, byte[] data)>();
        var hostComplete = new TaskCompletionSource<string>();
        host.FileOfferReceived += offer => hostOffer.TrySetResult(offer);
        host.FileChunkReceived += c0 => hostChunks.Add((c0.TransferId, c0.ChunkIndex, c0.Data));
        host.FileCompleteReceived += id => hostComplete.TrySetResult(id);

        var viewerTask = Task.Run(async () => await viewer.ConnectAsync($"127.0.0.1:{port}", cts.Token));
        var approveTask = Task.Run(async () =>
        {
            await incomingRequest.Task.WaitAsync(cts.Token);
            await host.ApproveAsync(remoteControlGranted: false, fileTransferGranted: true, clipboardGranted: true, cts.Token);
        });
        await Task.WhenAll(viewerTask, approveTask);
        await Task.Delay(300, cts.Token);
        Log("  Handshake tamam");

        var transferId = Guid.NewGuid().ToString("N");
        var swTotal = Stopwatch.StartNew();
        await viewer.SendFileOfferAsync(transferId, fileName, size, cts.Token);
        await hostOffer.Task.WaitAsync(TimeSpan.FromSeconds(5), cts.Token);
        await host.SendFileDecisionAsync(transferId, accepted: true, cts.Token);

        const int chunkSize = 64 * 1024;
        await using var fs = File.OpenRead(srcPath);
        var buffer = new byte[chunkSize];
        int idx = 0;
        long totalSent = 0;
        int read;
        while ((read = await fs.ReadAsync(buffer.AsMemory(0, chunkSize), cts.Token)) > 0)
        {
            var payload = new byte[read];
            Buffer.BlockCopy(buffer, 0, payload, 0, read);
            await viewer.SendFileChunkAsync(transferId, idx++, payload, cts.Token);
            totalSent += read;
        }
        await viewer.SendFileCompleteAsync(transferId, cts.Token);
        var elapsed = swTotal.Elapsed;
        Log($"  Viewer {idx} chunk, {totalSent} bayt gönderdi ({elapsed.TotalSeconds:0.0} sn, {totalSent / 1024.0 / 1024.0 / Math.Max(0.001, elapsed.TotalSeconds):0.0} MB/s)");

        await hostComplete.Task.WaitAsync(TimeSpan.FromSeconds(60), cts.Token);
        var downloadPath = Path.Combine(Path.GetTempPath(), "SecureRemoteDesk.SelfTest", "received_" + fileName);
        await using (var outFs = new FileStream(downloadPath, FileMode.Create, FileAccess.Write))
        {
            foreach (var chunk in hostChunks.OrderBy(x => x.idx))
            {
                await outFs.WriteAsync(chunk.data);
            }
        }
        var rcvHash = await FileHashAsync(downloadPath);
        Log($"  sha256 (alınan): {rcvHash}");
        if (srcHash == rcvHash)
        {
            Log($"  [PASS] Büyük dosya bütünlüğü doğrulandı (boyut={size:N0} bayt, ~{size / (1024L * 1024L)} MB)");
            c.Passed++;
            // 2 GB scaled throughput hesabı: aynı MB/s oranı korunursa 2GB ~= elapsed * 10
            var scaled = elapsed.TotalSeconds * (2L * 1024L * 1024L * 1024L) / size;
            Log($"  (Scaled 2 GB tahmini süre: {scaled:0} sn, throughput: {totalSent / 1024.0 / 1024.0 / Math.Max(0.001, elapsed.TotalSeconds):0.0} MB/s)");
        }
        else
        {
            Log($"  [FAIL] Hash uyuşmuyor: {srcHash} != {rcvHash}");
            c.Failed++;
        }
    }

    private static async Task TestParallelFiles(Counter c)
    {
        Log("\n[TEST] Çoklu dosya paralel/sıralı gönderim");
        const int fileSize = 2 * 1024 * 1024; // 2 MB her biri
        const int fileCount = 3;
        var fileNames = Enumerable.Range(1, fileCount).Select(i => $"paralel_{i}.bin").ToArray();
        var srcPaths = new string[fileCount];
        var srcHashes = new string[fileCount];
        for (int i = 0; i < fileCount; i++)
        {
            srcPaths[i] = Path.Combine(Path.GetTempPath(), "SecureRemoteDesk.SelfTest", fileNames[i]);
            await PrepareTestFileAsync(srcPaths[i], fileSize, seed: 1000 + i);
            srcHashes[i] = await FileHashAsync(srcPaths[i]);
        }
        Log($"  {fileCount} dosya × {fileSize / 1024} KB");

        const int port = 50559;
        await using var host = new LocalHostSessionServer(port);
        await host.StartAsync();
        var incomingRequest = new TaskCompletionSource();
        host.IncomingRequest += _ => incomingRequest.TrySetResult();
        host.StatusChanged += s => Log($"  [host] {s}");

        await using var viewer = new LocalViewerSessionClient();
        viewer.StatusChanged += s => Log($"  [viewer] {s}");

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var receivedOffers = new Dictionary<string, IncomingFileOffer>();
        var hostChunks = new ConcurrentDictionary<string, List<(int idx, byte[] data)>>();
        var hostComplete = new ConcurrentDictionary<string, byte>();
        var offersLock = new object();
        host.FileOfferReceived += offer =>
        {
            lock (offersLock) receivedOffers[offer.TransferId] = offer;
            hostChunks[offer.TransferId] = new List<(int, byte[])>();
        };
        host.FileChunkReceived += c0 =>
        {
            if (hostChunks.TryGetValue(c0.TransferId, out var list)) list.Add((c0.ChunkIndex, c0.Data));
        };
        host.FileCompleteReceived += id => hostComplete[id] = 1;

        var viewerTask = Task.Run(async () => await viewer.ConnectAsync($"127.0.0.1:{port}", cts.Token));
        var approveTask = Task.Run(async () =>
        {
            await incomingRequest.Task.WaitAsync(cts.Token);
            await host.ApproveAsync(remoteControlGranted: false, fileTransferGranted: true, clipboardGranted: true, cts.Token);
        });
        await Task.WhenAll(viewerTask, approveTask);
        await Task.Delay(300, cts.Token);
        Log("  Handshake tamam");

        // 3 dosyayı PARALEL başlat (her biri ayrı transferId, fire-and-forget)
        var sw = Stopwatch.StartNew();
        var transferIds = new string[fileCount];
        for (int i = 0; i < fileCount; i++)
        {
            transferIds[i] = Guid.NewGuid().ToString("N");
            int copy = i;
            _ = Task.Run(async () =>
            {
                await viewer.SendFileOfferAsync(transferIds[copy], fileNames[copy], fileSize, cts.Token);
                // Offer alındıysa (lock içinde kontrol) kararı bekle
                IncomingFileOffer? matched = null;
                while (matched is null)
                {
                    lock (offersLock)
                    {
                        matched = receivedOffers.FirstOrDefault(kv => kv.Value.TransferId == transferIds[copy]).Value;
                    }
                    if (matched is null) await Task.Delay(50, cts.Token);
                }
                // Karar: otomatik accept
                await host.SendFileDecisionAsync(transferIds[copy], accepted: true, cts.Token);
                // Chunk gönder
                await using var fs = File.OpenRead(srcPaths[copy]);
                var buf = new byte[FileChunkSize];
                int idx = 0;
                int read;
                while ((read = await fs.ReadAsync(buf.AsMemory(0, FileChunkSize), cts.Token)) > 0)
                {
                    var payload = new byte[read];
                    Buffer.BlockCopy(buf, 0, payload, 0, read);
                    await viewer.SendFileChunkAsync(transferIds[copy], idx++, payload, cts.Token);
                }
                await viewer.SendFileCompleteAsync(transferIds[copy], cts.Token);
            });
        }
        // Tüm transfer'ler tamamlanana kadar bekle
        var deadline = DateTime.UtcNow.AddSeconds(45);
        while (hostComplete.Count < fileCount && DateTime.UtcNow < deadline)
        {
            await Task.Delay(200, cts.Token);
        }
        sw.Stop();
        if (hostComplete.Count < fileCount)
        {
            Log($"  [FAIL] Tüm transferler tamamlanmadı: {hostComplete.Count}/{fileCount}");
            c.Failed++;
            return;
        }
        Log($"  Tüm {fileCount} dosya {sw.Elapsed.TotalSeconds:0.0} sn'de tamamlandı");

        // Hash doğrulama
        bool allOk = true;
        for (int i = 0; i < fileCount; i++)
        {
            var downloadPath = Path.Combine(Path.GetTempPath(), "SecureRemoteDesk.SelfTest", "received_" + fileNames[i]);
            await using (var outFs = new FileStream(downloadPath, FileMode.Create, FileAccess.Write))
            {
                var ordered = hostChunks[transferIds[i]].OrderBy(x => x.idx);
                foreach (var (_, data) in ordered) await outFs.WriteAsync(data);
            }
            var rcvHash = await FileHashAsync(downloadPath);
            if (srcHashes[i] != rcvHash)
            {
                Log($"  [FAIL] {fileNames[i]} hash uyuşmuyor: beklenen={srcHashes[i]} alınan={rcvHash}");
                allOk = false;
            }
        }
        if (allOk)
        {
            Log($"  [PASS] {fileCount} dosya paralel gönderildi ve hash'ler doğrulandı (toplam {sw.Elapsed.TotalSeconds:0.0} sn)");
            // Sıralı olsaydı ~= fileCount * (tek dosya süresi) olurdu. Parallel: en yavaş transfer. Karşılaştırma için yorum:
            Log($"  (3 dosya paralel gönderildi; sıralı gönderimde süre ~3x daha fazla olurdu)");
            c.Passed++;
        }
        else
        {
            c.Failed++;
        }
    }

    private static async Task TestClipboard(Counter c)
    {
        Log("\n[TEST] Clipboard senkronizasyonu");
        const int port = 50557;
        await using var host = new LocalHostSessionServer(port);
        await host.StartAsync();
        var incomingRequest = new TaskCompletionSource();
        host.IncomingRequest += _ => incomingRequest.TrySetResult();

        await using var viewer = new LocalViewerSessionClient();
        var viewerClip = new TaskCompletionSource<string>();
        var hostClip = new TaskCompletionSource<string>();
        viewer.ClipboardReceived += t => { Log($"  [viewer] CLIPBOARD: {Trunc(t, 60)}"); viewerClip.TrySetResult(t); };
        host.ClipboardReceived += t => { Log($"  [host] CLIPBOARD: {Trunc(t, 60)}"); hostClip.TrySetResult(t); };

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var viewerTask = Task.Run(async () => await viewer.ConnectAsync($"127.0.0.1:{port}", cts.Token));
        var approveTask = Task.Run(async () =>
        {
            await incomingRequest.Task.WaitAsync(cts.Token);
            await host.ApproveAsync(remoteControlGranted: false, fileTransferGranted: false, clipboardGranted: true, cts.Token);
        });
        await Task.WhenAll(viewerTask, approveTask);
        await Task.Delay(300, cts.Token);
        Log("  Handshake tamam (clipboard izni açık)");

        var sendText = "ping-from-host-" + DateTime.UtcNow.Ticks;
        await host.SendClipboardAsync(sendText, cts.Token);
        var received = await viewerClip.Task.WaitAsync(TimeSpan.FromSeconds(5), cts.Token);
        if (received == sendText)
        {
            Log($"  [PASS] Host → Viewer clipboard: '{received}'");
            c.Passed++;
        }
        else
        {
            Log($"  [FAIL] Beklenen '{sendText}', gelen '{received}'");
            c.Failed++;
        }

        var sendText2 = "pong-from-viewer-" + DateTime.UtcNow.Ticks;
        await viewer.SendClipboardAsync(sendText2, cts.Token);
        var received2 = await hostClip.Task.WaitAsync(TimeSpan.FromSeconds(5), cts.Token);
        if (received2 == sendText2)
        {
            Log($"  [PASS] Viewer → Host clipboard: '{received2}'");
            c.Passed++;
        }
        else
        {
            Log($"  [FAIL] Beklenen '{sendText2}', gelen '{received2}'");
            c.Failed++;
        }
    }

    private static string Trunc(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    private static async Task PrepareTestFileAsync(string path, int size, int seed)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        if (File.Exists(path)) File.Delete(path);
        // Bellek dostu: 1 MB chunk ile yaz
        await using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, FileOptions.SequentialScan);
        var rng = new Random(seed);
        var buf = new byte[1024 * 1024];
        long written = 0;
        while (written < size)
        {
            int toWrite = (int)Math.Min(buf.Length, size - written);
            rng.NextBytes(buf);
            await fs.WriteAsync(buf.AsMemory(0, toWrite));
            written += toWrite;
        }
    }

    private static async Task<string> FileHashAsync(string path)
    {
        using var sha = SHA256.Create();
        await using var fs = File.OpenRead(path);
        return Convert.ToHexString(await sha.ComputeHashAsync(fs));
    }
}
