using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace JacRed.Engine.Parsing
{
    public static class tParse
    {
        #region ReplaceBadNames
        public static string ReplaceBadNames(string html)
        {
            return html.Replace("Ванда/Вижн ", "ВандаВижн ").Replace("Ё", "Е").Replace("ё", "е").Replace("щ", "ш");
        }
        #endregion

        #region ParseCreateTime
        public static DateTime ParseCreateTime(string line, string format)
        {
            line = Regex.Replace(line, " янв\\.? ", ".01.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " февр?\\.? ", ".02.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " март?\\.? ", ".03.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " апр\\.? ", ".04.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " май\\.? ", ".05.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " июнь?\\.? ", ".06.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " июль?\\.? ", ".07.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " авг\\.? ", ".08.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " сент?\\.? ", ".09.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " окт\\.? ", ".10.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " нояб?\\.? ", ".11.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " дек\\.? ", ".12.", RegexOptions.IgnoreCase);

            line = Regex.Replace(line, " январ(ь|я)?\\.? ", ".01.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " феврал(ь|я)?\\.? ", ".02.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " марта?\\.? ", ".03.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " апрел(ь|я)?\\.? ", ".04.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " май?я?\\.? ", ".05.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " июн(ь|я)?\\.? ", ".06.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " июл(ь|я)?\\.? ", ".07.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " августа?\\.? ", ".08.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " сентябр(ь|я)?\\.? ", ".09.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " октябр(ь|я)?\\.? ", ".10.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " ноябр(ь|я)?\\.? ", ".11.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " декабр(ь|я)?\\.? ", ".12.", RegexOptions.IgnoreCase);

            line = Regex.Replace(line, " Jan ", ".01.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " Feb ", ".02.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " Mar ", ".03.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " Apr ", ".04.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " May ", ".05.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " Jun ", ".06.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " Jul ", ".07.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " Aug ", ".08.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " Sep ", ".09.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " Oct ", ".10.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " Nov ", ".11.", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, " Dec ", ".12.", RegexOptions.IgnoreCase);

            if (Regex.IsMatch(line, "^[0-9]\\."))
                line = $"0{line}";

            DateTime.TryParseExact(line.ToLower(), format, new CultureInfo("ru-RU"), DateTimeStyles.None, out DateTime createTime);
            return createTime;
        }
        #endregion
    }
}
