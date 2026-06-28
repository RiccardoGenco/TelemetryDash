using System.Net.Sockets;
using System.Reactive.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TelemetryDash.Core.Enums;
using TelemetryDash.Core.Interfaces;
using TelemetryDash.Core.Models;

namespace TelemetryDash.Infrastructure.Plugins;

// Exported via [InheritedExport] on IDataSourcePlugin — no explicit [Export] to avoid duplicate registration.
public class TcpReceiver : IDataSourcePlugin
{
    private readonly ILogger<TcpReceiver> _logger;
    private TcpClient? _client;
    private bool _isConnected;
    private string _host = "127.0.0.1";
    private int _port = 5000;

    public string Name => "TCP Receiver";

    public TcpReceiver() : this(NullLogger<TcpReceiver>.Instance) { }

    public TcpReceiver(ILogger<TcpReceiver> logger, string host = "127.0.0.1", int port = 5000)
    {
        _logger = logger;
        _host = host;
        _port = port;
    }

    public async Task ConnectAsync()
    {
        _client = new TcpClient();
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _client.ConnectAsync(_host, _port, cts.Token);
        }
        catch (OperationCanceledException)
        {
            _client.Dispose();
            _client = null;
            throw new InvalidOperationException(
                $"Connection to {_host}:{_port} timed out. Ensure tcp_simulator.py is running.");
        }
        catch (SocketException ex)
        {
            _client.Dispose();
            _client = null;
            throw new InvalidOperationException(
                $"Cannot connect to {_host}:{_port} — {ex.Message}. Run: python tools/tcp_simulator.py", ex);
        }

        _isConnected = true;
        _logger.LogInformation("TCP connected to {Host}:{Port}", _host, _port);
    }

    public Task DisconnectAsync()
    {
        _isConnected = false;
        _client?.Close();
        _client?.Dispose();
        _logger.LogInformation("TCP disconnected");
        return Task.CompletedTask;
    }

    public IObservable<TelemetryReading> GetDataStream(CancellationToken ct)
    {
        return Observable.Create<TelemetryReading>(async (observer, token) =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, token);

            try
            {
                while (_isConnected && !linked.Token.IsCancellationRequested && _client?.Connected == true)
                {
                    var reading = await ReadFrameAsync(_client, linked.Token);
                    if (reading is not null)
                        observer.OnNext(reading);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                observer.OnError(ex);
            }

            observer.OnCompleted();
        });
    }

    private static async Task<TelemetryReading?> ReadFrameAsync(TcpClient client, CancellationToken ct)
    {
        var stream = client.GetStream();

        // Read 4-byte payload length header (uint32 LE)
        var lenBuf = await ReadExactAsync(stream, 4, ct);
        if (lenBuf is null) return null;

        var payloadLength = BitConverter.ToInt32(lenBuf, 0);
        if (payloadLength <= 0 || payloadLength > 1024)
            return null;

        // Read payload
        var payload = await ReadExactAsync(stream, payloadLength, ct);
        if (payload is null) return null;

        return ParseFrame(payload);
    }

    private static TelemetryReading ParseFrame(byte[] payload)
    {
        // Binary format (little-endian):
        // [8 byte] timestamp (Unix ms, int64 LE)
        // [1 byte] channel ID index
        // [8 byte] value (double LE)
        // [1 byte] quality flag (0=OK, 1=WARN, 2=ERR)

        var timestamp = BitConverter.ToInt64(payload, 0);
        var channelIndex = payload[8];
        var value = BitConverter.ToDouble(payload, 9);
        var quality = payload[17];

        var channelNames = new[] { "TEMP_A1", "PRESS_B2", "VIB_C3", "FLOW_D4" };
        var channelId = channelIndex < channelNames.Length
            ? channelNames[channelIndex]
            : $"CH_{channelIndex}";

        return new TelemetryReading
        {
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(timestamp).UtcDateTime,
            ChannelId = channelId,
            Value = Math.Round(value, 3),
            Quality = (QualityFlag)Math.Min(quality, (byte)2),
        };
    }

    private static async Task<byte[]?> ReadExactAsync(NetworkStream stream, int count, CancellationToken ct)
    {
        var buffer = new byte[count];
        var offset = 0;

        while (offset < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset), ct);
            if (read == 0) return null; // Connection closed
            offset += read;
        }

        return buffer;
    }
}
