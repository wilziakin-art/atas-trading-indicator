using System;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CopyTrading.Protocol;

namespace CopyTrading.ClientIndicator
{
    // Thread COMM — priorité Normal, port 8766
    // Responsabilité : stats, messages dashboard, ping/pong, messages bannière ATAS
    // Aucun lock partagé avec le canal TRADING

    public class ClientCommConfig
    {
        public string RelayHost         { get; set; } = "127.0.0.1";
        public int    RelayCommPort     { get; set; } = 8766;
        public string SecretKey         { get; set; } = "changeme";
        public string ClientId          { get; set; } = "client1";
        public int    StatsIntervalMs   { get; set; } = 500;   // throttle 500ms
        public int    ReconnectMs       { get; set; } = 3000;
    }

    public class ClientCommEngine
    {
        private readonly ClientCommConfig _cfg;
        private readonly RiskManager      _risk;

        private ClientWebSocket      _ws;
        private volatile bool        _connected;
        private volatile bool        _running;
        private long                 _lastPingSentMs;
        private volatile long        _latencyMs;
        private readonly CancellationTokenSource _cts = new();

        // Callback vers l'overlay ATAS
        public Action<string, int>  OnDashboardMessage { get; set; } // (texte, duréeSec)
        public Action<long>         OnLatencyMeasured  { get; set; }

        // Getter lecture atomique pour l'overlay
        public bool IsConnected => _connected;
        public long LatencyMs   => _latencyMs;

        // Fournisseur de stats depuis l'extérieur (ClientIndicator remplit ça)
        public Func<ClientStatsPayload> GetStats { get; set; }

        public ClientCommEngine(ClientCommConfig cfg, RiskManager risk)
        {
            _cfg  = cfg;
            _risk = risk;
        }

        public void Start()
        {
            _running = true;
            Task.Run(ConnectLoop, _cts.Token);
        }

        public void Stop()
        {
            _running = false;
            _cts.Cancel();
            _ws?.Abort();
        }

        // Envoie un message stats au relay (appelé par la boucle throttlée)
        public async Task SendStatsAsync(ClientStatsPayload stats)
        {
            if (!_connected || _ws?.State != WebSocketState.Open) return;

            var msg = new CommMessage
            {
                Type      = CommMessageType.ClientStatus,
                ClientId  = _cfg.ClientId,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Payload   = stats,
            };

            await SendJsonAsync(msg);
        }

        private async Task ConnectLoop()
        {
            while (_running && !_cts.Token.IsCancellationRequested)
            {
                try
                {
                    _ws = new ClientWebSocket();
                    _ws.Options.SetRequestHeader("X-Client-Id",  _cfg.ClientId);
                    _ws.Options.SetRequestHeader("X-Secret-Key", _cfg.SecretKey);

                    var uri = new Uri($"ws://{_cfg.RelayHost}:{_cfg.RelayCommPort}/");
                    await _ws.ConnectAsync(uri, _cts.Token);

                    _connected = true;
                    Console.WriteLine($"[COMM-ENGINE] Connecté à {uri}");

                    // Lance les deux tâches en parallèle
                    var receiveTask = ReceiveLoop();
                    var statsTask   = StatsLoop();
                    var pingTask    = PingLoop();

                    await Task.WhenAny(receiveTask, statsTask, pingTask);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[COMM-ENGINE] Déconnexion : {ex.Message}");
                }
                finally
                {
                    _connected = false;
                    _ws?.Abort();
                }

                if (_running)
                    await Task.Delay(_cfg.ReconnectMs, _cts.Token).ConfigureAwait(false);
            }
        }

        private async Task ReceiveLoop()
        {
            var buf = new byte[8192];

            while (_ws.State == WebSocketState.Open && !_cts.Token.IsCancellationRequested)
            {
                var result = await _ws.ReceiveAsync(buf, _cts.Token);
                if (result.MessageType == WebSocketMessageType.Close) break;

                var json = Encoding.UTF8.GetString(buf, 0, result.Count);
                ProcessIncoming(json);
            }
        }

        private void ProcessIncoming(string json)
        {
            try
            {
                var msg = JsonSerializer.Deserialize<CommMessage>(json);
                if (msg == null) return;

                switch (msg.Type)
                {
                    case CommMessageType.DashboardMessage:
                        if (msg.Payload is JsonElement el)
                        {
                            var payload = el.Deserialize<DashboardMessagePayload>();
                            if (payload != null &&
                                (payload.TargetClientId == null || payload.TargetClientId == _cfg.ClientId))
                            {
                                Console.WriteLine($"[COMM-ENGINE] Message dashboard : {payload.Text}");
                                OnDashboardMessage?.Invoke(payload.Text, payload.DurationSec);
                            }
                        }
                        break;

                    case CommMessageType.Pong:
                        if (_lastPingSentMs > 0)
                        {
                            var latency = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _lastPingSentMs;
                            Interlocked.Exchange(ref _latencyMs, latency);
                            OnLatencyMeasured?.Invoke(latency);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[COMM-ENGINE] Erreur parsing : {ex.Message}");
            }
        }

        // Stats throttlées à 500ms
        private async Task StatsLoop()
        {
            while (_ws.State == WebSocketState.Open && !_cts.Token.IsCancellationRequested)
            {
                await Task.Delay(_cfg.StatsIntervalMs, _cts.Token).ConfigureAwait(false);

                if (GetStats == null) continue;

                try
                {
                    var stats = GetStats();
                    stats.LatencyMs = _latencyMs;

                    var summary = _risk.GetSummary();
                    stats.Status       = summary.State.ToString();
                    stats.PnlDay       = summary.PnlJour;
                    stats.TradesDay    = summary.TradesJour;
                    stats.WinsDay      = summary.WinsJour;
                    stats.ConsecLosses = summary.ConsecLosses;

                    await SendStatsAsync(stats);
                }
                catch { }
            }
        }

        // Ping toutes les 10s pour mesurer la latence
        private async Task PingLoop()
        {
            while (_ws.State == WebSocketState.Open && !_cts.Token.IsCancellationRequested)
            {
                await Task.Delay(10000, _cts.Token).ConfigureAwait(false);

                _lastPingSentMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var ping = new CommMessage
                {
                    Type      = CommMessageType.Ping,
                    ClientId  = _cfg.ClientId,
                    Timestamp = _lastPingSentMs,
                };
                await SendJsonAsync(ping);
            }
        }

        private async Task SendJsonAsync(object obj)
        {
            if (_ws?.State != WebSocketState.Open) return;
            var json  = JsonSerializer.Serialize(obj);
            var bytes = Encoding.UTF8.GetBytes(json);
            await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, _cts.Token);
        }
    }
}
