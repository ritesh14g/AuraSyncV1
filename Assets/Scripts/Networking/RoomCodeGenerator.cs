using System;
using System.Text.RegularExpressions;

namespace AuraSync.Networking
{
    public static class RoomCodeGenerator
    {
        private const string Prefix = "AURA-";
        private const string Chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // Omit confusing 0/O, 1/I

        public static string GenerateCode()
        {
            char[] code = new char[4];
            var rng = new Random();
            for (int i = 0; i < 4; i++)
            {
                code[i] = Chars[rng.Next(Chars.Length)];
            }
            return Prefix + new string(code);
        }

        public static string FormatCode(string rawInput)
        {
            if (string.IsNullOrEmpty(rawInput)) return "";
            string cleaned = Regex.Replace(rawInput.ToUpper().Trim(), @"[^A-Z0-9]", "");
            if (cleaned.StartsWith("AURA"))
            {
                cleaned = cleaned.Substring(4);
            }
            if (cleaned.Length > 4)
            {
                cleaned = cleaned.Substring(0, 4);
            }
            return Prefix + cleaned;
        }

        public static bool IsValidCode(string code)
        {
            if (string.IsNullOrEmpty(code)) return false;
            return Regex.IsMatch(code.Trim().ToUpper(), @"^AURA-[A-Z0-9]{4}$");
        }
    }
}
