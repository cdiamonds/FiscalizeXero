using Newtonsoft.Json;
using System.Net;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace FiscalizeXero.Shared
{
    public class VC
    {
        public string WP(string myp) // write password , myp-my password
        {
            byte[] clearBytes = Encoding.Unicode.GetBytes(myp);
            using (Aes encryptor = Aes.Create())
            {
                Rfc2898DeriveBytes pdb = new Rfc2898DeriveBytes(myp, new byte[] { 0x49, 0x76, 0x61, 0x6e, 0x20, 0x4d, 0x65, 0x64, 0x76, 0x65, 0x64, 0x65, 0x76 });
                encryptor.Key = pdb.GetBytes(32);
                encryptor.IV = pdb.GetBytes(16);
                using (MemoryStream ms = new MemoryStream())
                {
                    using (CryptoStream cs = new CryptoStream(ms, encryptor.CreateEncryptor(), CryptoStreamMode.Write))
                    {
                        cs.Write(clearBytes, 0, clearBytes.Length);
                        cs.Close();
                    }
                    myp = Convert.ToBase64String(ms.ToArray());
                }
            }
            return myp;
        }

        public string RP(string storedp, string myp)  // read password, 
        {
            try
            {
                byte[] cipherBytes = Convert.FromBase64String(storedp);
                using (Aes encryptor = Aes.Create())
                {
                    Rfc2898DeriveBytes pdb = new Rfc2898DeriveBytes(myp, new byte[] { 0x49, 0x76, 0x61, 0x6e, 0x20, 0x4d, 0x65, 0x64, 0x76, 0x65, 0x64, 0x65, 0x76 });
                    encryptor.Key = pdb.GetBytes(32);
                    encryptor.IV = pdb.GetBytes(16);
                    using (MemoryStream ms = new MemoryStream())
                    {
                        using (CryptoStream cs = new CryptoStream(ms, encryptor.CreateDecryptor(), CryptoStreamMode.Write))
                        {
                            cs.Write(cipherBytes, 0, cipherBytes.Length);
                            cs.Close();
                        }
                        myp = Encoding.Unicode.GetString(ms.ToArray());
                    }
                }
                return myp;
            }
            catch
            {
                return "error";
            }
        }

        public DateTime GetUtcDate()
        {
            return DateTime.UtcNow;
        }

        public bool IsHttpSuccess(HttpStatusCode status)
        {
            return ((int)status >= 200 && (int)status < 300);
        }

        //public bool IsHttpFailure(HttpStatusCode status)
        //{
        //    return !IsHttpSuccess(status);
        //}

        public string JsonObjectToString(object jsonObject)
        {
            var json = JsonConvert.SerializeObject(jsonObject);
            return json.ToString();
        }

        public bool IsImage(byte[] fileBytes)
        {
            var headers = new List<byte[]>
            {
                Encoding.ASCII.GetBytes("BM"),      // BMP
                Encoding.ASCII.GetBytes("GIF"),     // GIF
                new byte[] { 137, 80, 78, 71 },     // PNG
                new byte[] { 73, 73, 42 },          // TIFF
                new byte[] { 77, 77, 42 },          // TIFF
                new byte[] { 255, 216, 255, 224 },  // JPEG
                new byte[] { 255, 216, 255, 225 }   // JPEG CANON
            };
            return headers.Any(x => x.SequenceEqual(fileBytes.Take(x.Length)));
        }

        public string GenerateDocNo()
        {
            int dayOfYear = DateTime.UtcNow.DayOfYear;
            int year = DateTime.UtcNow.Year;
            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            string raw = $"{dayOfYear}{year}{timestamp}";

            using (var sha256 = SHA256.Create())
            {
                byte[] fullHash = sha256.ComputeHash(Encoding.UTF8.GetBytes(raw));
                byte[] shortHash = new byte[3];
                Array.Copy(fullHash, shortHash, 3);

                const string chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
                BigInteger value = new BigInteger(shortHash.Concat(new byte[] { 0 }).ToArray());
                string result = "";
                while (value > 0)
                {
                    result = chars[(int)(value % 36)] + result;
                    value /= 36;
                }

                string hash = result.PadLeft(5, '0');
                return $"{dayOfYear.ToString("D3")}{hash}"; // Always 8 characters
            }
        }
    }
}
