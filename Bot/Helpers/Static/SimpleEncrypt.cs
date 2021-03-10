using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SysBot.ACNHOrders
{
    // JS: Decrypt using https://berichan.github.io/GetDodoCode/
    public static class SimpleEncrypt
    {
        public static readonly List<char> DodoChars = new List<char>(new char[33]
        {
            'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'J', 'K', 'L', 'M', 'N', 'P', 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y',
            '0', '1', '2', '3', '4', '5', '6', '7', '8', '9'
        });

        public static string SimpleEncryptToBase64(string szPlainText, int szEncryptionKey)
        {
            var crypt = EncryptDodo(szPlainText, szEncryptionKey);
            return btoa(crypt);
        }

        private static string EncryptDodo(string szPlainText, int szEncryptionKey)
        {
            StringBuilder szInputStringBuild = new StringBuilder(szPlainText);
            StringBuilder szOutStringBuild = new StringBuilder(szPlainText.Length);
            char Textch;
            int encryptShift = szEncryptionKey / 100;
            for (int iCount = 0; iCount < szPlainText.Length; iCount++)
            {
                Textch = szInputStringBuild[iCount];
                var index = DodoChars.IndexOf(Textch);
                var extraShift = iCount % 2 == 0 ? encryptShift : -encryptShift;
                Textch = (char)((index + szEncryptionKey + extraShift) % 33);
                szOutStringBuild.Append(Textch);
            }
            return szOutStringBuild.ToString();
        }

        private static string btoa(string toEncode)
        {
            byte[] bytes = Encoding.GetEncoding(28591).GetBytes(toEncode);
            string toReturn = Convert.ToBase64String(bytes);
            return toReturn;
        }
    }
}
