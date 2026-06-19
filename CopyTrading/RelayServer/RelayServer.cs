using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CopyTrading.Protocol;

namespace CopyTrading.RelayServer
{
    // Relay Server v2 — dual-canal indépendant
    // Port 8765 : canal TRADING (binaire, TCP_NODELAY, priorité haute)
    // Port 8766 : canal COMM/STATS (WebSocket JSON, throttlé)
    // Port 8080 : dashboard HTTP statique

    public class RelayServer
    {
        private readonly RelayConfig _config;
        private readonly byte[]      _secretKey;

        // Canal TRADING
        private readonly ConcurrentDictionary<string, TcpClient>         _tradingClients = new();
        private          TcpListener                                       _tradingListener;
        private          Thread                                            _tradingThread;

        // Canal COMM
        private readonly ConcurrentDictionary<string, WebSocket>          _commClients    = new();
        private          HttpListener                                      _commListener;
        private          Thread                                            _commThread;

        // Historique trades (max 50)
        private readonly ConcurrentQueue<TradeRecord>                      _tradeHistory   = new();
        private const    int                                               MaxHistory      = 50;

        // Stats clients (cache mémoire, zéro requête réseau au rendu)
        private readonly ConcurrentDictionary<string, ClientStatsPayload>  _clientStats    = new();

        private volatile bool _running;
        private readonly CancellationTokenSource _cts = new();

        public RelayServer(RelayConfig config)
        {
            _config    = config;
            _secretKey = Encoding.UTF8.GetBytes(config.SecretKey);
        }

        public void Start()
        {
            _running = true;

            _tradingThread = new Thread(RunTradingListener)
            {
                IsBackground = true,
                Priority     = ThreadPriority.AboveNormal,
                Name         = "Relay-Trading-8765"
            };
            _tradingThread.Start();

            _commThread = new Thread(RunCommListener)
            {
                IsBackground = true,
                Priority     = ThreadPriority.Normal,
                Name         = "Relay-Comm-8766"
            };
            _commThread.Start();

            Task.Run(() => RunDashboardServer(_cts.Token));
            Task.Run(() => RunHeartbeatMonitor(_cts.Token));

            Console.WriteLine($"[RELAY] Démarré — Trading:{_config.TradingPort} Comm:{_config.CommPort} Dashboard:{_config.DashboardPort}");
        }

        public void Stop()
        {
            _running = false;
            _cts.Cancel();
            _tradingListener?.Stop();
            _commListener?.Stop();
        }

        // ────────────────────────────────────────────────────────────────
        // CANAL TRADING — TCP pur, binaire, TCP_NODELAY
        // ────────────────────────────────────────────────────────────────

        private void RunTradingListener()
        {
            _tradingListener = new TcpListener(IPAddress.Any, _config.TradingPort);
            _tradingListener.Start();

            while (_running)
            {
                try
                {
                    var client = _tradingListener.AcceptTcpClient();
                    ConfigureTradingSocket(client);
                    Task.Run(() => HandleTradingClient(client));
                }
                catch (SocketException) when (!_running) { break; }
                catch (Exception ex) { Console.WriteLine($"[TRADING-ACCEPT] {ex.Message}"); }
            }
        }

        private static void ConfigureTradingSocket(TcpClient client)
        {
            client.NoDelay           = true;  // TCP_NODELAY — désactive Nagle
            client.SendBufferSize    = 8192;  // 8KB — force envoi immédiat
            client.ReceiveBufferSize = 8192;
            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            // Keepalive agressif : 10s idle, 5s interval, 3 probes
            if (OperatingSystem.IsLinux())
            {
                client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime,     10);
                client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval,  5);
                client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 3);
            }
        }

        private async Task HandleTradingClient(TcpClient tcp)
        {
            var endpoint  = tcp.Client.RemoteEndPoint?.ToString() ?? "unknown";
            var clientId  = string.Empty;
            var isMaster  = false;
            var stream    = tcp.GetStream();
            var headerBuf = new byte[64]; // ID client + rôle en header ASCII

            try
            {
                // Handshake : lire header "MASTER:id\n" ou "CLIENT:id\n"
                int read = await stream.ReadAsync(headerBuf, 0, headerBuf.Length);
                var header = Encoding.ASCII.GetString(headerBuf, 0, read).Trim();

                if (header.StartsWith("MASTER:"))
                {
                    isMaster = true;
                    clientId = header.Substring(7);
                    Console.WriteLine($"[TRADING] Maître connecté : {clientId} ({endpoint})");
                    await RelayFromMaster(stream, clientId);
                }
                else if (header.StartsWith("CLIENT:"))
                {
                    clientId = header.Substring(7);
                    _tradingClients[clientId] = tcp;
                    Console.WriteLine($"[TRADING] Client connecté : {clientId} ({endpoint})");
                    // Les clients ne font qu'écouter sur ce canal
                    await KeepClientAlive(stream, clientId, _cts.Token);
                }
                else
                {
                    Console.WriteLine($"[TRADING] Header invalide depuis {endpoint}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TRADING] Déconnexion {clientId ?? endpoint} : {ex.Message}");
            }
            finally
            {
                if (!string.IsNullOrEmpty(clientId) && !isMaster)
                    _tradingClients.TryRemove(clientId, out _);
                tcp.Close();
            }
        }

        private async Task RelayFromMaster(NetworkStream stream, string masterId)
        {
            var buf = new byte[TradingMessage.TotalSize];

            while (_running)
            {
                int totalRead = 0;
                while (totalRead < TradingMessage.TotalSize)
                {
                    int n = await stream.ReadAsync(buf, totalRead, TradingMessage.TotalSize - totalRead);
                    if (n == 0) return; // déconnexion propre
                    totalRead += n;
                }

                if (!TradingMessage.TryDeserialize(buf, _secretKey, out var msg))
                {
                    Console.WriteLine($"[TRADING] HMAC invalide depuis maître {masterId}");
                    continue;
                }

                if (msg.Type == MessageType.Heartbeat) continue;

                // Broadcast non-bloquant — un client lent n'impacte jamais les autres
                var snapshot = _tradingClients.ToArray();
                foreach (var kv in snapshot)
                {
                    var clientId = kv.Key;
                    var tcp      = kv.Value;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var ns = tcp.GetStream();
                            await ns.WriteAsync(buf, 0, TradingMessage.TotalSize);
                        }
                        catch
                        {
                            _tradingClients.TryRemove(clientId, out _);
                        }
                    });
                }
            }
        }

        private static async Task KeepClientAlive(NetworkStream stream, string clientId, CancellationToken ct)
        {
            // Lit les pings éventuels, maintient la connexion ouverte
            var buf = new byte[TradingMessage.TotalSize];
            try
            {
                while (!ct.IsCancellationRequested)
                    if (await stream.ReadAsync(buf, 0, buf.Length, ct) == 0) break;
            }
            catch { }
        }

        // ────────────────────────────────────────────────────────────────
        // CANAL COMM — WebSocket JSON, throttlé 500ms
        // ────────────────────────────────────────────────────────────────

        private void RunCommListener()
        {
            _commListener = new HttpListener();
            _commListener.Prefixes.Add($"http://+:{_config.CommPort}/");
            _commListener.Start();

            while (_running)
            {
                try
                {
                    var ctx = _commListener.GetContext();
                    if (ctx.Request.IsWebSocketRequest)
                        Task.Run(() => HandleCommWebSocket(ctx));
                    else
                        SendJsonStats(ctx.Response);
                }
                catch (HttpListenerException) when (!_running) { break; }
                catch (Exception ex) { Console.WriteLine($"[COMM-ACCEPT] {ex.Message}"); }
            }
        }

        private async Task HandleCommWebSocket(HttpListenerContext ctx)
        {
            var wsCtx    = await ctx.AcceptWebSocketAsync(null);
            var ws       = wsCtx.WebSocket;
            var clientId = ctx.Request.Headers["X-Client-Id"] ?? Guid.NewGuid().ToString();
            var secret   = ctx.Request.Headers["X-Secret-Key"];

            if (secret != _config.SecretKey)
            {
                await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Unauthorized", _cts.Token);
                return;
            }

            _commClients[clientId] = ws;
            Console.WriteLine($"[COMM] Client connecté : {clientId}");

            try
            {
                var buf = new byte[4096];
                while (ws.State == WebSocketState.Open && !_cts.Token.IsCancellationRequested)
                {
                    var result = await ws.ReceiveAsync(buf, _cts.Token);
                    if (result.MessageType == WebSocketMessageType.Close) break;

                    var json = Encoding.UTF8.GetString(buf, 0, result.Count);
                    ProcessCommMessage(clientId, json);
                }
            }
            catch { }
            finally
            {
                _commClients.TryRemove(clientId, out _);
                Console.WriteLine($"[COMM] Client déconnecté : {clientId}");
                if (ws.State == WebSocketState.Open)
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Bye", CancellationToken.None);
            }
        }

        private void ProcessCommMessage(string senderId, string json)
        {
            try
            {
                var msg = JsonSerializer.Deserialize<CommMessage>(json);
                if (msg == null) return;

                switch (msg.Type)
                {
                    case CommMessageType.ClientStatus:
                        if (msg.Payload is JsonElement el)
                        {
                            var stats = el.Deserialize<ClientStatsPayload>();
                            if (stats != null) _clientStats[senderId] = stats;
                        }
                        BroadcastComm(json, exclude: senderId);
                        break;

                    case CommMessageType.DashboardMessage:
                        BroadcastComm(json);
                        break;

                    case CommMessageType.Ping:
                        var pong = JsonSerializer.Serialize(new CommMessage
                        {
                            Type      = CommMessageType.Pong,
                            ClientId  = senderId,
                            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        });
                        SendToClient(senderId, pong);
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[COMM] Erreur traitement message : {ex.Message}");
            }
        }

        private void BroadcastComm(string json, string exclude = null)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            foreach (var kv in _commClients.ToArray())
            {
                if (kv.Key == exclude) continue;
                _ = Task.Run(async () =>
                {
                    try { await kv.Value.SendAsync(bytes, WebSocketMessageType.Text, true, _cts.Token); }
                    catch { _commClients.TryRemove(kv.Key, out _); }
                });
            }
        }

        private void SendToClient(string clientId, string json)
        {
            if (!_commClients.TryGetValue(clientId, out var ws)) return;
            var bytes = Encoding.UTF8.GetBytes(json);
            _ = Task.Run(async () =>
            {
                try { await ws.SendAsync(bytes, WebSocketMessageType.Text, true, _cts.Token); }
                catch { _commClients.TryRemove(clientId, out _); }
            });
        }

        // Enregistre un trade dans l'historique (appelé depuis l'extérieur ou via comm)
        public void RecordTrade(TradeRecord trade)
        {
            _tradeHistory.Enqueue(trade);
            while (_tradeHistory.Count > MaxHistory)
                _tradeHistory.TryDequeue(out _);
        }

        // ────────────────────────────────────────────────────────────────
        // DASHBOARD HTTP — port 8080, stats JSON + fichier HTML statique
        // ────────────────────────────────────────────────────────────────

        private async Task RunDashboardServer(CancellationToken ct)
        {
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://+:{_config.DashboardPort}/");
            listener.Start();
            Console.WriteLine($"[DASHBOARD] HTTP sur port {_config.DashboardPort}");

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var ctx = await listener.GetContextAsync();
                    var path = ctx.Request.Url?.AbsolutePath ?? "/";

                    if (path == "/api/stats")
                        SendJsonStats(ctx.Response);
                    else if (path == "/api/history")
                        SendJsonHistory(ctx.Response);
                    else
                        SendDashboardHtml(ctx.Response);
                }
                catch (HttpListenerException) when (ct.IsCancellationRequested) { break; }
                catch (Exception ex) { Console.WriteLine($"[DASHBOARD] {ex.Message}"); }
            }

            listener.Stop();
        }

        private void SendJsonStats(HttpListenerResponse resp)
        {
            var stats = new
            {
                clients      = _clientStats.Values.ToArray(),
                clientCount  = _tradingClients.Count,
                timestamp    = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            };
            var json  = JsonSerializer.Serialize(stats);
            var bytes = Encoding.UTF8.GetBytes(json);
            resp.ContentType     = "application/json";
            resp.ContentLength64 = bytes.Length;
            resp.Headers.Add("Access-Control-Allow-Origin", "*");
            resp.OutputStream.Write(bytes, 0, bytes.Length);
            resp.OutputStream.Close();
        }

        private void SendJsonHistory(HttpListenerResponse resp)
        {
            var json  = JsonSerializer.Serialize(_tradeHistory.ToArray());
            var bytes = Encoding.UTF8.GetBytes(json);
            resp.ContentType     = "application/json";
            resp.ContentLength64 = bytes.Length;
            resp.Headers.Add("Access-Control-Allow-Origin", "*");
            resp.OutputStream.Write(bytes, 0, bytes.Length);
            resp.OutputStream.Close();
        }

        private static void SendDashboardHtml(HttpListenerResponse resp)
        {
            // Redirige vers le fichier dashboard statique
            resp.Redirect("/index.html");
            resp.Close();
        }

        // ────────────────────────────────────────────────────────────────
        // HEARTBEAT MONITOR — détecte les clients morts
        // ────────────────────────────────────────────────────────────────

        private async Task RunHeartbeatMonitor(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);

                foreach (var kv in _tradingClients.ToArray())
                {
                    if (!kv.Value.Connected)
                    {
                        _tradingClients.TryRemove(kv.Key, out _);
                        Console.WriteLine($"[HEARTBEAT] Client trading mort détecté : {kv.Key}");
                    }
                }
            }
        }
    }

    public class RelayConfig
    {
        public int    TradingPort   { get; set; } = 8765;
        public int    CommPort      { get; set; } = 8766;
        public int    DashboardPort { get; set; } = 8080;
        public string SecretKey     { get; set; } = "changeme";
    }
}
