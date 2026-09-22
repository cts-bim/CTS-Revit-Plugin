using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace CTSRevitPlugin.UI.Utilities
{
    public static class ImperialLength
    {
        public static bool TryParseInches(string input, out double inches)
        {
            inches = 0;
            if (string.IsNullOrWhiteSpace(input)) return false;
            string s = input.Trim().Replace("\"", "").Replace("’", "'");
            bool negative = s.StartsWith("-");
            if (negative || s.StartsWith("+")) s = s.Substring(1).Trim();

            try
            {
                if (s.Contains("'"))
                {
                    string[] feetParts = s.Split(new[] { '\'' }, 2);
                    double feet = double.Parse(feetParts[0].Trim(), CultureInfo.InvariantCulture);
                    double rest = ParseInchesPart(feetParts.Length > 1 ? feetParts[1] : "0");
                    inches = feet * 12.0 + rest;
                }
                else
                {
                    inches = ParseInchesPart(s);
                }
                if (negative) inches = -inches;
                return true;
            }
            catch { inches = 0; return false; }
        }

        private static double ParseInchesPart(string value)
        {
            string s = value.Trim();
            if (s.Length == 0) return 0;
            double total = 0;
            string[] tokens = Regex.Split(s, @"\s+");
            foreach (string token in tokens)
            {
                if (string.IsNullOrWhiteSpace(token)) continue;
                if (token.Contains("/"))
                {
                    string[] f = token.Split('/');
                    if (f.Length != 2) throw new FormatException();
                    total += double.Parse(f[0], CultureInfo.InvariantCulture) /
                             double.Parse(f[1], CultureInfo.InvariantCulture);
                }
                else total += double.Parse(token, CultureInfo.InvariantCulture);
            }
            return total;
        }

        public static string FormatInches(double inches)
        {
            if (Math.Abs(inches) < 1e-9) return "0\"";
            bool negative = inches < 0;
            inches = Math.Abs(inches);
            int whole = (int)Math.Floor(inches + 1e-9);
            double fraction = inches - whole;

            int numerator = (int)Math.Round(fraction * 16.0);
            int denominator = 16;
            if (numerator >= denominator) { whole++; numerator = 0; }

            if (numerator != 0)
            {
                int gcd = Gcd(numerator, denominator);
                numerator /= gcd;
                denominator /= gcd;
            }

            string result = numerator == 0
                ? whole + "\""
                : (whole == 0 ? numerator + "/" + denominator : whole + " " + numerator + "/" + denominator) + "\"";
            return negative ? "-" + result : result;
        }

        private static int Gcd(int a, int b)
        {
            while (b != 0) { int t = a % b; a = b; b = t; }
            return Math.Abs(a);
        }
    }
}
