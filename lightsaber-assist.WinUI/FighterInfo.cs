using System;
using System.Collections.Generic;
using System.Linq;

namespace lightsaber_assist.WinUI
{
    internal sealed record FighterInfo(string FirstName, string LastName, string Country)
    {
        public string DisplayName => string.Join(' ', new[] { LastName.ToUpperInvariant(), FirstName }.Where(s => !string.IsNullOrWhiteSpace(s)));

        public string CountryCode => Country.Trim().ToUpperInvariant();

        public Uri? FlagUri => CountryFlags.TryGetFlagUri(CountryCode);
    }

    internal static class CountryFlags
    {
        private static readonly Dictionary<string, string> Alpha3To2 = new()
        {
            ["FRA"] = "fr", ["GBR"] = "gb", ["GER"] = "de", ["DEU"] = "de", ["ITA"] = "it", ["ESP"] = "es",
            ["BEL"] = "be", ["NED"] = "nl", ["NLD"] = "nl", ["SUI"] = "ch", ["CHE"] = "ch", ["AUT"] = "at",
            ["POL"] = "pl", ["CZE"] = "cz", ["SWE"] = "se", ["NOR"] = "no", ["DEN"] = "dk", ["DNK"] = "dk",
            ["FIN"] = "fi", ["POR"] = "pt", ["PRT"] = "pt", ["IRL"] = "ie", ["USA"] = "us", ["CAN"] = "ca",
            ["BRA"] = "br", ["ARG"] = "ar", ["MEX"] = "mx", ["JPN"] = "jp", ["KOR"] = "kr", ["CHN"] = "cn",
            ["AUS"] = "au", ["NZL"] = "nz", ["RUS"] = "ru", ["UKR"] = "ua", ["HUN"] = "hu", ["ROU"] = "ro",
            ["GRE"] = "gr", ["GRC"] = "gr", ["TUR"] = "tr", ["ISR"] = "il", ["LUX"] = "lu", ["MAR"] = "ma",
        };

        public static Uri? TryGetFlagUri(string alpha3) =>
            Alpha3To2.TryGetValue(alpha3, out var a2) ? new Uri($"https://flagcdn.com/w80/{a2}.png") : null;
    }
}
