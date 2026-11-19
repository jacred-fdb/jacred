using System.Linq;
using JacRed.Infrastructure.Parsing;
using Xunit;

namespace JacRed.Tests.Parsing;

public class RussianLanguageNamesTests
{
    [Theory]
    [InlineData("русские (софтсаб)", "rus")]
    [InlineData("Русские 1 - 23(софтсаб)", "rus")]
    [InlineData("русский, японский", "jpn rus")]
    [InlineData("английские (полухардсаб), русские (софтсаб) и другие.", "eng rus")]
    [InlineData("китайские (хардсаб)", "chi")]
    [InlineData("русские, английские, испанские, итальянские, китайские, корейские, немецкие, португальские, тайские, французские, японские",
        "chi eng fre ger ita jpn kor por rus spa tha")]
    [InlineData("белорусские", "bel")]
    [InlineData("софт - рус", "rus")]
    [InlineData("софт -рус, 1-14 (хард - англ)", "eng rus")]
    [InlineData("rus (софтсаб)", "rus")]
    [InlineData("яп", "jpn")]
    [InlineData("JAP", "jpn")]
    public void Codes_NamedLanguages(string text, string codes)
    {
        Assert.Equal(codes.Split(' '), RussianLanguageNames.Codes(text).OrderBy(c => c));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("нет")]
    [InlineData("1. Сrunchyroll 2. SovetRomantica")]
    [InlineData("Русь, Япония, корм, English dub")]
    public void Codes_NoLanguage_Null(string text)
    {
        Assert.Null(RussianLanguageNames.Codes(text));
    }
}
