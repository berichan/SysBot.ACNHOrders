using System;
using System.Text;

namespace SysBot.ACNHOrders
{
    public static class WebExtensions
    {
        static readonly char[] padding = { '=' };

        public static string WebSafeBase64Encode(this string str)
        {
            var bytes = Encoding.UTF8.GetBytes(str);
            return Convert.ToBase64String(bytes).MakeWebSafe();
        }

        public static string MakeWebSafe(this string b64str) => b64str.TrimEnd(padding).Replace('+', '-').Replace('/', '_');
        
        public static string WebSafeBase64Decode(this string str)
        {
            string incoming = str
                .Replace('_', '/').Replace('-', '+');
            switch (str.Length % 4)
            {
                case 2: incoming += "=="; break;
                case 3: incoming += "="; break;
            }
            byte[] bytes = Convert.FromBase64String(incoming);
            return Encoding.UTF8.GetString(bytes);
        }

        public static bool TryStringBetweenStrings(this string str, string start, string end, out string processed)
        {
            processed = string.Empty;
            try
            {
                int pFrom = str.IndexOf(start);
                if (pFrom == -1)
                    return false;
                pFrom += start.Length;
                int pTo = str.LastIndexOf(end);
                if (pTo == -1)
                    return false;

                processed = str.Substring(pFrom, pTo - pFrom);
                return true;
            }
            catch { return false; }
        }
    }
}
