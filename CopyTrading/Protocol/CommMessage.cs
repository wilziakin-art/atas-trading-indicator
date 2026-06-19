using System;
using System.Collections.Generic;

namespace CopyTrading.Protocol
{
    // Messages JSON sur le canal COMM (port 8766)
    // Throttlé à 500ms max, thread Normal, pas de contrainte de taille

    public enum CommMessageType
    {
        Stats,
        DashboardMessage,  // maître → client (bannière)
        ClientStatus,      // client → relay → dashboard
        Ping,
        Pong,
        TradeHistory,
    }

    public class CommMessage
    {
        public CommMessageType Type      { get; set; }
        public string          MasterId  { get; set; }
        public string          ClientId  { get; set; }
        public long            Timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        public object          Payload   { get; set; }
    }

    // Payload Stats : envoyé par le client vers le relay/dashboard
    public class ClientStatsPayload
    {
        public string ClientId        { get; set; }
        public string Status          { get; set; }  // ACTIF / BREAKEVEN / PAUSE / STOP_TARGET / STOP_LOSS
        public double PnlDay          { get; set; }
        public int    TradesDay       { get; set; }
        public int    WinsDay         { get; set; }
        public int    LossesDay       { get; set; }
        public int    ConsecLosses    { get; set; }
        public long   LatencyMs       { get; set; }  // ping/pong mesuré
        public string LastTradeSymbol { get; set; }
        public string LastTradeDir    { get; set; }
        public double LastTradePnl    { get; set; }
        public long   LastTradeTs     { get; set; }
    }

    // Payload message dashboard → client (bannière ATAS)
    public class DashboardMessagePayload
    {
        public string TargetClientId { get; set; } // null = broadcast tous
        public string Text           { get; set; }
        public int    DurationSec    { get; set; } = 180;
    }

    // Payload historique des 50 derniers trades
    public class TradeHistoryPayload
    {
        public List<TradeRecord> Trades { get; set; } = new List<TradeRecord>();
    }

    public class TradeRecord
    {
        public long   TimestampMs { get; set; }
        public string Symbol      { get; set; }
        public string Direction   { get; set; }
        public int    Volume      { get; set; }
        public double EntryPrice  { get; set; }
        public double ExitPrice   { get; set; }
        public double PnlUsd      { get; set; }
        public string MasterId    { get; set; }
    }
}
