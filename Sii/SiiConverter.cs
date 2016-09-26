using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sii
{
    public static class SiiConverter
    {
        /// <summary>
        /// Converts a float to its Sii ieee754 hexa notation
        /// </summary>
        /// <param name="f">The float value</param>
        /// <returns></returns>
        public static string ToHexString(float f)
        {
            var bytes = BitConverter.GetBytes(f);
            var i = BitConverter.ToInt32(bytes, 0);
            return "&" + i.ToString("X8");
        }

        /// <summary>
        /// Converts a hex value or an ieee754 hexa notation to a float value
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        public static float FromHexString(string s)
        {
            // Correct formating from SII files
            if (s.StartsWith("&"))
                s = s.Substring(1);

            // Convert to a hex parsible value
            if (!s.StartsWith("0x"))
                s = $"0x{s}";

            var i = Convert.ToInt32(s, 16);
            var bytes = BitConverter.GetBytes(i);
            return BitConverter.ToSingle(bytes, 0);
        }
    }
}
