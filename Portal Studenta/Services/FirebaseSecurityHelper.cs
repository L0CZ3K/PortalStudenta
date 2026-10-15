using System;
using System.Linq;

namespace Portal_Studenta.Services
{
    /// <summary>
    /// Zapewnia integralność i bezpieczeństwo kluczy Firebase RTDB oraz kodów grup.
    /// Zabezpiecza przed niedozwolonymi znakami (., $, #, [, ], /, |, spacja) i kolizjami.
    /// </summary>
    public static class FirebaseSecurityHelper
    {
        public const string GroupCodeChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

        /// <summary>
        /// Oczyszcza dowolny ciąg znaków, aby mógł być bezpiecznie użyty jako klucz w Firebase Realtime Database.
        /// Zastępuje znaki zabronione: '.', '$', '#', '[', ']', '/', '|' oraz spację znakiem '_'.
        /// W przypadku wartości pustej lub null zwraca 'EMPTY'.
        /// </summary>
        public static string SanitizeKey(string? key)
        {
            if (string.IsNullOrWhiteSpace(key)) return "EMPTY";
            var trimmed = key.Trim();

            // Szybka ścieżka (Zero Alloc): jeśli nie ma żadnych znaków zabronionych, zwracamy bez alokacji
            bool needsSanitization = false;
            foreach (char ch in trimmed)
            {
                if (ch is '.' or '$' or '#' or '[' or ']' or '/' or '|' or ' ')
                {
                    needsSanitization = true;
                    break;
                }
            }

            if (!needsSanitization) return trimmed;

            // Ścieżka pojedynczej alokacji w jednym przebiegu (zastępuje 8 alokacji pośrednich)
            return string.Create(trimmed.Length, trimmed, (span, source) =>
            {
                for (int i = 0; i < source.Length; i++)
                {
                    char c = source[i];
                    span[i] = c is '.' or '$' or '#' or '[' or ']' or '/' or '|' or ' ' ? '_' : c;
                }
            });
        }

        /// <summary>
        /// Sprawdza, czy podany kod grupy jest poprawny:
        /// 5–12 znaków alfanumerycznych (A-Z, 0-9), bez spacji i symboli specjalnych.
        /// </summary>
        public static bool IsValidGroupCode(string? code)
        {
            if (string.IsNullOrWhiteSpace(code)) return false;
            var c = code.Trim().ToUpperInvariant();
            return c.Length >= 5 && c.Length <= 12 && c.All(ch => GroupCodeChars.Contains(ch));
        }

        /// <summary>
        /// Generuje losowy kod grupy o zadanej długości (domyślnie 6), z ograniczeniem bezpiecznym [5..12].
        /// </summary>
        public static string GenerateRandomGroupCode(int length = 6)
        {
            length = Math.Clamp(length, 5, 12);
            var buffer = new char[length];
            for (int i = 0; i < length; i++)
                buffer[i] = GroupCodeChars[Random.Shared.Next(GroupCodeChars.Length)];
            return new string(buffer);
        }
    }
}
