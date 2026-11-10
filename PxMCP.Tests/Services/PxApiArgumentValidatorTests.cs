using PxMCP.Services;

namespace PxMCP.Tests.Services;

/// <summary>
/// Unit tests for <see cref="PxApiArgumentValidator"/>.
/// </summary>
[TestFixture]
public class PxApiArgumentValidatorTests
{
    [TestCase("StatFin")]
    [TestCase("TestDb")]
    [TestCase("db1")]
    [TestCase("Väestö")]
    [TestCase("12345678901234567890123456789012345678901234567890")]
    public void EnsureValidDatabase_AcceptsPxApiDatabaseIdentifiers(string database)
    {
        Assert.That(() => PxApiArgumentValidator.EnsureValidDatabase(database), Throws.Nothing);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" ")]
    [TestCase(".")]
    [TestCase("..")]
    [TestCase("a/b")]
    [TestCase("a\\b")]
    [TestCase("a%2Fb")]
    [TestCase("Stat_Fin")]
    [TestCase("Stat-Fin")]
    [TestCase("StatFin\n")]
    [TestCase("123456789012345678901234567890123456789012345678901")]
    public void EnsureValidDatabase_RejectsUnsafeOrNonPxApiIdentifiers(string? database)
    {
        Assert.That(() => PxApiArgumentValidator.EnsureValidDatabase(database),
            Throws.TypeOf<PxApiArgumentException>()
                .With.Message.EqualTo(PxApiArgumentValidator.InvalidDatabaseMessage));
    }

    [TestCase("11re")]
    [TestCase("population")]
    [TestCase("statfin_vaerak_pxt_11re")]
    [TestCase("table-1")]
    [TestCase("12345678901234567890123456789012345678901234567890")]
    public void EnsureValidTable_AcceptsPxApiTableIdentifiers(string table)
    {
        Assert.That(() => PxApiArgumentValidator.EnsureValidTable(table), Throws.Nothing);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(".")]
    [TestCase("..")]
    [TestCase("a/b")]
    [TestCase("table.px")]
    [TestCase("table?x=1")]
    [TestCase("table#x")]
    [TestCase("table 1")]
    [TestCase("123456789012345678901234567890123456789012345678901")]
    public void EnsureValidTable_RejectsUnsafeOrNonPxApiIdentifiers(string? table)
    {
        Assert.That(() => PxApiArgumentValidator.EnsureValidTable(table),
            Throws.TypeOf<PxApiArgumentException>()
                .With.Message.EqualTo(PxApiArgumentValidator.InvalidTableMessage));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("en")]
    [TestCase("fi")]
    [TestCase("sv")]
    [TestCase("SV")]
    [TestCase("sme")]
    [TestCase("sv-FI")]
    public void EnsureValidLanguage_AcceptsOmittedOrShortLanguageTags(string? lang)
    {
        Assert.That(() => PxApiArgumentValidator.EnsureValidLanguage(lang), Throws.Nothing);
    }

    [TestCase(" ")]
    [TestCase("e")]
    [TestCase("english")]
    [TestCase("en\n")]
    [TestCase("en&debug=true")]
    [TestCase("../x")]
    [TestCase("en-")]
    [TestCase("ä")]
    public void EnsureValidLanguage_RejectsInvalidLanguageCodes(string lang)
    {
        Assert.That(() => PxApiArgumentValidator.EnsureValidLanguage(lang),
            Throws.TypeOf<PxApiArgumentException>()
                .With.Message.EqualTo(PxApiArgumentValidator.InvalidLanguageMessage));
    }

    [TestCase(1)]
    [TestCase(PxApiArgumentValidator.MaxSearchQueryLength)]
    public void EnsureValidSearchQuery_AcceptsQueriesUpToPxApiLimit(int length)
    {
        Assert.That(() => PxApiArgumentValidator.EnsureValidSearchQuery(new string('a', length)), Throws.Nothing);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("\t\n")]
    public void EnsureValidSearchQuery_RejectsMissingOrBlankQueries(string? query)
    {
        Assert.That(() => PxApiArgumentValidator.EnsureValidSearchQuery(query),
            Throws.TypeOf<PxApiArgumentException>()
                .With.Message.EqualTo(PxApiArgumentValidator.InvalidSearchQueryMessage));
    }

    [Test]
    public void EnsureValidSearchQuery_RejectsQueriesOverPxApiLimit()
    {
        Assert.That(() => PxApiArgumentValidator.EnsureValidSearchQuery(new string('a', PxApiArgumentValidator.MaxSearchQueryLength + 1)),
            Throws.TypeOf<PxApiArgumentException>()
                .With.Message.EqualTo(PxApiArgumentValidator.InvalidSearchQueryMessage));
    }
}
