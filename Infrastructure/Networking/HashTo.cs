using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

namespace JacRed.Infrastructure.Networking
{
    public class HashTo
    {
        #region md5
        public static string md5(string IntText)
        {
            using (var md5 = MD5.Create())
            {
                var result = md5.ComputeHash(Encoding.UTF8.GetBytes(IntText));
                return BitConverter.ToString(result).Replace("-", "").ToLower();
            }
        }
        #endregion

        #region NameToHash
        public static string NameToHash(string name_or_originalname, string type)
        {
            return md5(Regex.Replace(HttpUtility.HtmlDecode(name_or_originalname), "[^а-яA-Z0-9]+", "", RegexOptions.IgnoreCase).ToLower().Trim() + ":" + type);
        }
        #endregion
    }
}
