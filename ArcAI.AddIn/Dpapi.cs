using System;
using System.Security.Cryptography;
using System.Text;

namespace ArcAI.AddIn
{
    /// <summary>Windows DPAPI (current user). Never write API keys in plaintext.</summary>
    internal static class Dpapi
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ArcAI.Assistant.v1");

        public static string Protect(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return string.Empty;
            byte[] data = Encoding.UTF8.GetBytes(plainText);
            byte[] protectedData = ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(protectedData);
        }

        public static string Unprotect(string protectedBase64)
        {
            if (string.IsNullOrEmpty(protectedBase64))
                return string.Empty;
            try
            {
                byte[] data = Convert.FromBase64String(protectedBase64);
                byte[] plain = ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
            catch (Exception ex)
            {
                Log.Error("DPAPI unprotect failed; the stored API key is ignored.", ex);
                return string.Empty;
            }
        }
    }
}
