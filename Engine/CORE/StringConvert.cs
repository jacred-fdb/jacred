using System.Text.RegularExpressions;

namespace JacRed.Engine.CORE
{
    public static class StringConvert
    {
        #region FindStartText
        public static string FindStartText(string data, string end, string start = null)
        {
            try
            {
                return data.Substring(0, data.IndexOf(end));
            }
            catch
            {
                return null;
            }
        }
        #endregion

        #region FindLastText
        public static string FindLastText(string data, string start, string end = null)
        {
            try
            {
                string res = data.Substring(data.IndexOf(start));
                if (end == null)
                    return res;

                return FindStartText(res, end);
            }
            catch
            {
                return null;
            }
        }
        #endregion

        #region Remove
        public static string Remove(string data, string start, string end)
        {
            try
            {
                for (int i = 0; i < 10; i++)
                {
                    int startIndex = data.IndexOf(start);
                    if (startIndex == 0)
                        break;


                    int endIndex = data.IndexOf(end);
                    if (endIndex == 0)
                    {
                        data = data.Remove(startIndex);
                        break;
                    }

                    data = data.Remove(startIndex, (endIndex - startIndex));
                }

                return data;
            }
            catch
            {
                return data;
            }
        }
        #endregion


        #region SearchName
        public static string SearchName(string val)
        {
            if (string.IsNullOrWhiteSpace(val))
                return null;

            val = Regex.Replace(val.ToLower(), "[^a-zA-Zа-яА-Я0-9Ёё]+", "").Replace("ё", "е").Replace("щ", "ш");
            if (string.IsNullOrWhiteSpace(val))
                return null;

            return val;
        }
        #endregion
    }
}
