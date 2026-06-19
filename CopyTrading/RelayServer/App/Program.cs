using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CopyTrading.RelayServer;

// ════════════════════════════════════════════════════════════════════════════
//  CopyTrading Relay Server v2 — Application console autonome
//  Déployable sur VPS Linux (systemd) ou Windows (NSSM)
//  Ports : 8765 trading (TCP binaire) | 8766 comm (WebSocket JSON) | 8080 dashboard HTTP
// ════════════════════════════════════════════════════════════════════════════

Console.Title = "CopyTrading Relay v2";
Console.OutputEncoding = System.Text.Encoding.UTF8;

// ── Chargement configuration ──────────────────────────────────────────────

var configPath = Path.Combine(AppContext.BaseDirectory, "relay.json");

if (!File.Exists(configPath))
{
    WriteDefault(configPath);
    Console.WriteLine($"[RELAY] Fichier de config créé : {configPath}");
    Console.WriteLine("[RELAY] Modifiez relay.json puis relancez le relay.");
    return;
}

RelayConfig config;
try
{
    var json = File.ReadAllText(configPath);
    config   = JsonSerializer.Deserialize<RelayConfig>(json, new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling         = JsonCommentHandling.Skip,
    }) ?? new RelayConfig();
}
catch (Exception ex)
{
    Console.WriteLine($"[RELAY] Erreur lecture relay.json : {ex.Message}");
    return;
}

// ── Validation config ──────────────────────────────────────────────────────

if (config.SecretKey is "changeme" or "" or null)
{
    Console.WriteLine("[RELAY] ⚠️  ATTENTION : SecretKey par défaut détectée !");
    Console.WriteLine("[RELAY]    Modifiez SecretKey dans relay.json avant de déployer en production.");
}

// ── Bannière de démarrage ──────────────────────────────────────────────────

Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("╔══════════════════════════════════════════════════════╗");
Console.WriteLine("║         CopyTrading Relay Server v2                 ║");
Console.WriteLine("╚══════════════════════════════════════════════════════╝");
Console.ResetColor();
Console.WriteLine($"  Trading TCP  : port {config.TradingPort}");
Console.WriteLine($"  Comm WS      : port {config.CommPort}");
Console.WriteLine($"  Dashboard    : http://localhost:{config.DashboardPort}");
Console.WriteLine($"  SecretKey    : {new string('*', Math.Min(config.SecretKey?.Length ?? 0, 8))}...");
Console.WriteLine();

// ── Démarrage relay ────────────────────────────────────────────────────────

var relay = new RelayServer(config);

var cts = new CancellationTokenSource();

// Capture Ctrl+C et SIGTERM (systemd)
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    Console.WriteLine("\n[RELAY] Signal d'arrêt reçu — arrêt propre...");
    cts.Cancel();
};

AppDomain.CurrentDomain.ProcessExit += (_, _) =>
{
    Console.WriteLine("[RELAY] ProcessExit — arrêt propre...");
    relay.Stop();
};

relay.Start();

// ── Boucle de supervision console ─────────────────────────────────────────

Console.WriteLine("[RELAY] Démarré. Tapez 'q' pour quitter, 's' pour les stats.\n");

_ = Task.Run(async () =>
{
    while (!cts.Token.IsCancellationRequested)
    {
        await Task.Delay(1000, cts.Token).ConfigureAwait(false);
    }
    relay.Stop();
});

while (!cts.Token.IsCancellationRequested)
{
    if (!Console.IsInputRedirected && Console.KeyAvailable)
    {
        var key = Console.ReadKey(intercept: true).KeyChar;
        switch (key)
        {
            case 'q' or 'Q':
                Console.WriteLine("[RELAY] Arrêt demandé.");
                cts.Cancel();
                break;

            case 's' or 'S':
                PrintStats(relay);
                break;

            case 'h' or 'H':
                PrintHelp();
                break;
        }
    }
    else
    {
        await Task.Delay(100).ConfigureAwait(false);
    }
}

Console.WriteLine("[RELAY] Arrêté proprement.");

// ────────────────────────────────────────────────────────────────────────────

static void PrintStats(RelayServer relay)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine($"\n[STATS] {DateTime.Now:HH:mm:ss}");
    Console.ResetColor();
    Console.WriteLine($"  Clients trading connectés : {relay.TradingClientCount}");
    Console.WriteLine($"  Clients comm connectés    : {relay.CommClientCount}");
    Console.WriteLine($"  Trades relayés (session)  : {relay.TotalTradesRelayed}");
    Console.WriteLine($"  Historique trades         : {relay.TradeHistoryCount}/50");
    Console.WriteLine();
}

static void PrintHelp()
{
    Console.WriteLine("\n  q — Quitter");
    Console.WriteLine("  s — Statistiques");
    Console.WriteLine("  h — Aide\n");
}

static void WriteDefault(string path)
{
    var defaultConfig = new RelayConfig
    {
        TradingPort   = 8765,
        CommPort      = 8766,
        DashboardPort = 8080,
        SecretKey     = "changeme",
    };

    var json = JsonSerializer.Serialize(defaultConfig, new JsonSerializerOptions
    {
        WriteIndented = true,
    });

    File.WriteAllText(path, json);
}
