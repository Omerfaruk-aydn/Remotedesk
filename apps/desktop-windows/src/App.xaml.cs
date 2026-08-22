using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using SecureRemoteDesk.Desktop.Services;

namespace SecureRemoteDesk.Desktop;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Length > 0 && e.Args[0] == "--selftest")
        {
            // UI thread'de sync-over-async deadlock olmasın diye testi thread pool'da çalıştır
            _ = Task.Run(async () =>
            {
                int code = 0;
                try
                {
                    code = await SelfTest.RunAsync();
                }
                catch (Exception ex)
                {
                    try { File.AppendAllText(SelfTest.LogPath, $"CRASH: {ex}\n"); } catch { }
                    code = 2;
                }
                Dispatcher.Invoke(() => Shutdown(code));
            });
            return;
        }
        base.OnStartup(e);
    }
}

internal static class SelfTest
{
    public static readonly string LogPath = Path.Combine(Path.GetTempPath(), "SecureRemoteDesk.SelfTest.log");

    private class Counter { public int Passed; public int Failed; }
    private static readonly object _logLock = new();
    private static void Log(string message)
    {
        lock (_logLock)
        {
            File.AppendAllText(LogPath, message + Environment.NewLine);
        }
    }

    public static async Task<int> RunAsync()
    {
        try { File.Delete(LogPath); } catch { }
        Log("== SecureRemoteDesk self-test başlıyor ==");
        var c = new Counter();
        try
        {
            await TestFileTransfer("dosya_transfer_test.bin", 512 * 1024, c);
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
        var testDir = Path.Combine(Path.GetTempPath(), "SecureRemoteDesk.SelfTest");
        Log($"  testDir = {testDir}");
        if (Directory.Exists(testDir)) Directory.Delete(testDir, true);
        Directory.CreateDirectory(testDir);
        var srcPath = Path.Combine(testDir, fileName);
        var rng = new Random(42);
        var data = new byte[size];
        rng.NextBytes(data);
        Log($"  data üretildi ({size} bayt), dosyaya yazılıyor");
        await File.WriteAllBytesAsync(srcPath, data);
        Log($"  dosya yazıldı, hash hesaplanıyor");
        var srcHash = Convert.ToHexString(SHA256.HashData(data));
        Log($"  Kaynak: {srcPath}");
        Log($"  sha256: {srcHash}");

        const int port = 50556;
        Log($"  host oluşturuluyor (port={port})");
        await using var host = new LocalHostSessionServer(port);
        Log($"  host.StartAsync çağrılıyor");
        await host.StartAsync();
        Log($"  Host dinliyor: 127.0.0.1:{port}");

        var incomingRequest = new TaskCompletionSource();
        host.IncomingRequest += _ => { Log("  [host] IncomingRequest event fired"); incomingRequest.TrySetResult(); };
        host.StatusChanged += s => Log($"  [host] {s}");

        await using var viewer = new LocalViewerSessionClient();
        viewer.StatusChanged += s => Log($"  [viewer] {s}");

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var hostOffer = new TaskCompletionSource<IncomingFileOffer>();
        var hostChunks = new List<(string id, int idx, byte[] data)>();
        var hostComplete = new TaskCompletionSource<string>();
        host.FileOfferReceived += offer => { Log($"  [host] FILE_OFFER: {offer.FileName} {offer.SizeBytes}"); hostOffer.TrySetResult(offer); };
        host.FileChunkReceived += c0 => hostChunks.Add((c0.TransferId, c0.ChunkIndex, c0.Data));
        host.FileCompleteReceived += id => { Log($"  [host] FILE_COMPLETE: {id}"); hostComplete.TrySetResult(id); };

        Log("  viewer bağlantısı başlıyor");
        var viewerTask = Task.Run(async () =>
        {
            try { await viewer.ConnectAsync($"127.0.0.1:{port}", cts.Token); Log("  [viewer] ConnectAsync döndü"); }
            catch (Exception ex) { Log($"  [viewer] ConnectAsync exception: {ex.Message}"); throw; }
        });
        var approveTask = Task.Run(async () =>
        {
            Log("  approve task bekliyor IncomingRequest");
            await incomingRequest.Task.WaitAsync(cts.Token);
            Log("  approve task ApproveAsync çağırıyor");
            await host.ApproveAsync(remoteControlGranted: false, fileTransferGranted: true, clipboardGranted: true, cts.Token);
            Log("  approve task ApproveAsync döndü");
        });

        await Task.WhenAll(viewerTask, approveTask);
        Log("  Task.WhenAll döndü");
        await Task.Delay(500, cts.Token);
        Log("  Handshake tamam");

        var transferId = Guid.NewGuid().ToString("N");
        Log($"  Viewer → Host dosya gönderiyor (id={transferId})");
        await viewer.SendFileOfferAsync(transferId, fileName, size, cts.Token);

        var receivedOffer = await hostOffer.Task.WaitAsync(TimeSpan.FromSeconds(5), cts.Token);
        Log($"  Host offer aldı: {receivedOffer.FileName}, kabul gönderiliyor");
        await host.SendFileDecisionAsync(transferId, accepted: true, cts.Token);
        Log($"  host.SendFileDecisionAsync döndü");

        const int chunkSize = 64 * 1024;
        await using var fs = File.OpenRead(srcPath);
        Log($"  dosya açıldı: {srcPath}");
        var buffer = new byte[chunkSize];
        int idx = 0;
        int read;
        long totalSent = 0;
        while ((read = await fs.ReadAsync(buffer.AsMemory(0, chunkSize), cts.Token)) > 0)
        {
            var payload = new byte[read];
            Buffer.BlockCopy(buffer, 0, payload, 0, read);
            Log($"  chunk {idx} gönderiliyor ({read} bayt)");
            await viewer.SendFileChunkAsync(transferId, idx, payload, cts.Token);
            Log($"  chunk {idx} gönderildi");
            idx++;
            totalSent += read;
        }
        Log($"  dosya tamamen okundu, complete gönderiliyor");
        await viewer.SendFileCompleteAsync(transferId, cts.Token);
        Log($"  Viewer {idx} chunk, {totalSent} bayt gönderdi");

        var completedId = await hostComplete.Task.WaitAsync(TimeSpan.FromSeconds(10), cts.Token);
        if (completedId != transferId) throw new Exception($"Transfer ID uyuşmuyor: {completedId} != {transferId}");

        var downloadPath = Path.Combine(testDir, "received_" + fileName);
        await using (var outFs = new FileStream(downloadPath, FileMode.Create, FileAccess.Write))
        {
            foreach (var chunk in hostChunks.OrderBy(x => x.idx))
            {
                await outFs.WriteAsync(chunk.data);
            }
        }
        var rcvHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(downloadPath)));
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
        viewer.ClipboardReceived += t => { Log($"  [viewer] CLIPBOARD: {t}"); viewerClip.TrySetResult(t); };
        host.ClipboardReceived += t => { Log($"  [host] CLIPBOARD: {t}"); hostClip.TrySetResult(t); };

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
}

