using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace CopyTrading.Protocol
{
    // Message type codes
    public enum MessageType : byte
    {
        EntryMarket  = 0x01,
        EntryLimit   = 0x02,
        CloseAll     = 0x03, // P0 — bypass toutes validations
        ClosePartial = 0x04,
        Heartbeat    = 0x05,
    }

    public enum TradeDirection : byte
    {
        Buy  = 0x01,
        Sell = 0x02,
    }

    // Protocole binaire fixe : 72 octets + 32 octets HMAC = 104 octets total
    // Layout :
    //   [0]      MessageType  (1 octet)
    //   [1]      Direction    (1 octet)
    //   [2-9]    Symbole      (8 octets, ASCII padded)
    //   [10-13]  Volume       (4 octets, int32)
    //   [14-21]  Prix         (8 octets, double)
    //   [22-29]  Timestamp    (8 octets, Unix ms, int64)
    //   [30-61]  MasterId     (32 octets, ASCII padded)
    //   [62-71]  Réservé      (10 octets, zéro)
    //   [72-103] HMAC-SHA256  (32 octets)
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct TradingMessage
    {
        public const int PayloadSize = 72;
        public const int HmacSize    = 32;
        public const int TotalSize   = PayloadSize + HmacSize; // 104 octets

        public MessageType    Type;
        public TradeDirection Direction;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] Symbol;       // ASCII 8 chars

        public int    Volume;
        public double Price;
        public long   TimestampMs;  // Unix epoch ms

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] MasterId;     // ASCII 32 chars

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
        public byte[] Reserved;

        // Sérialise le payload (72 octets) sans HMAC
        public byte[] SerializePayload()
        {
            var buf = new byte[PayloadSize];
            buf[0] = (byte)Type;
            buf[1] = (byte)Direction;

            var sym = Symbol ?? new byte[8];
            Array.Copy(sym, 0, buf, 2, Math.Min(8, sym.Length));

            var volBytes = BitConverter.GetBytes(Volume);
            Array.Copy(volBytes, 0, buf, 10, 4);

            var priceBytes = BitConverter.GetBytes(Price);
            Array.Copy(priceBytes, 0, buf, 14, 8);

            var tsBytes = BitConverter.GetBytes(TimestampMs);
            Array.Copy(tsBytes, 0, buf, 22, 8);

            var mid = MasterId ?? new byte[32];
            Array.Copy(mid, 0, buf, 30, Math.Min(32, mid.Length));

            // Reserved = zéros (déjà le cas par défaut)
            return buf;
        }

        // Sérialise payload + HMAC (104 octets)
        public byte[] Serialize(byte[] secretKey)
        {
            var payload = SerializePayload();
            var hmac    = ComputeHmac(payload, secretKey);

            var result = new byte[TotalSize];
            Array.Copy(payload, 0, result, 0,           PayloadSize);
            Array.Copy(hmac,    0, result, PayloadSize, HmacSize);
            return result;
        }

        // Désérialise depuis 104 octets bruts, vérifie HMAC
        public static bool TryDeserialize(byte[] data, byte[] secretKey, out TradingMessage msg)
        {
            msg = default;
            if (data == null || data.Length < TotalSize)
                return false;

            // Vérification HMAC avant tout traitement
            var payload      = new byte[PayloadSize];
            var receivedHmac = new byte[HmacSize];
            Array.Copy(data, 0,           payload,      0, PayloadSize);
            Array.Copy(data, PayloadSize, receivedHmac, 0, HmacSize);

            var expectedHmac = ComputeHmac(payload, secretKey);
            if (!CryptographicEquals(expectedHmac, receivedHmac))
                return false;

            msg.Type      = (MessageType)payload[0];
            msg.Direction = (TradeDirection)payload[1];

            msg.Symbol = new byte[8];
            Array.Copy(payload, 2, msg.Symbol, 0, 8);

            msg.Volume      = BitConverter.ToInt32(payload, 10);
            msg.Price       = BitConverter.ToDouble(payload, 14);
            msg.TimestampMs = BitConverter.ToInt64(payload, 22);

            msg.MasterId = new byte[32];
            Array.Copy(payload, 30, msg.MasterId, 0, 32);

            msg.Reserved = new byte[10];
            Array.Copy(payload, 62, msg.Reserved, 0, 10);

            return true;
        }

        // Fabrique un message d'entrée
        public static TradingMessage CreateEntry(
            MessageType type, TradeDirection dir, string symbol,
            int volume, double price, string masterId)
        {
            return new TradingMessage
            {
                Type        = type,
                Direction   = dir,
                Symbol      = PadAscii(symbol, 8),
                Volume      = volume,
                Price       = price,
                TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                MasterId    = PadAscii(masterId, 32),
                Reserved    = new byte[10],
            };
        }

        public static TradingMessage CreateCloseAll(string symbol, string masterId)
        {
            return new TradingMessage
            {
                Type        = MessageType.CloseAll,
                Direction   = TradeDirection.Buy, // ignoré pour CloseAll
                Symbol      = PadAscii(symbol, 8),
                Volume      = 0,
                Price       = 0,
                TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                MasterId    = PadAscii(masterId, 32),
                Reserved    = new byte[10],
            };
        }

        public static TradingMessage CreateHeartbeat(string masterId)
        {
            return new TradingMessage
            {
                Type        = MessageType.Heartbeat,
                Direction   = TradeDirection.Buy,
                Symbol      = new byte[8],
                Volume      = 0,
                Price       = 0,
                TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                MasterId    = PadAscii(masterId, 32),
                Reserved    = new byte[10],
            };
        }

        public string GetSymbol()   => Encoding.ASCII.GetString(Symbol ?? new byte[8]).TrimEnd('\0');
        public string GetMasterId() => Encoding.ASCII.GetString(MasterId ?? new byte[32]).TrimEnd('\0');

        // Âge du signal en millisecondes
        public long AgeMs() =>
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - TimestampMs;

        private static byte[] PadAscii(string s, int size)
        {
            var buf = new byte[size];
            if (string.IsNullOrEmpty(s)) return buf;
            var bytes = Encoding.ASCII.GetBytes(s);
            Array.Copy(bytes, 0, buf, 0, Math.Min(size, bytes.Length));
            return buf;
        }

        private static byte[] ComputeHmac(byte[] data, byte[] key)
        {
            using var hmac = new HMACSHA256(key);
            return hmac.ComputeHash(data);
        }

        // Comparaison en temps constant — anti timing attack
        private static bool CryptographicEquals(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++)
                diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }
}
