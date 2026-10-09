using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Globalization;
using System.Resources;

namespace iD_Develops.Tests.Services;

public sealed partial class PublicLocalizationTests
{
    private static readonly (string[] Sources, string ResourceBase)[] LocalizedSurfaces =
    [
        (["Pages/About.cshtml"], "Resources/Pages/AboutModel"),
        (["Pages/Communication-Training.cshtml"], "Resources/Pages/Communication_TrainingModel"),
        (["Pages/Contact.cshtml", "Pages/Contact.cshtml.cs"], "Resources/Pages/ContactModel"),
        (["Pages/Cookie-Policy.cshtml"], "Resources/Pages/Cookie_PolicyModel"),
        (["Pages/Disclaimer.cshtml"], "Resources/Pages/DisclaimerModel"),
        (["Pages/Enterprise.cshtml"], "Resources/Pages/EnterpriseModel"),
        (["Pages/Error.cshtml", "Pages/Error.cshtml.cs"], "Resources/Pages/ErrorModel"),
        (["Pages/Examination/Results.cshtml", "Pages/Examination/Results.cshtml.cs"], "Resources/Pages/Examination/ResultsModel"),
        (["Pages/FreeDownloads.cshtml"], "Resources/Pages/FreeDownloadsModel"),
        (["Pages/Index.cshtml"], "Resources/Pages/IndexModel"),
        (["Pages/Language-And-Communication/Index.cshtml"], "Resources/Pages/LanguageAndCommunication/IndexModel"),
        (["Pages/Language-And-Communication/English.cshtml"], "Resources/Pages/LanguageAndCommunication/EnglishModel"),
        (["Pages/Language-And-Communication/GoDutch.cshtml"], "Resources/Pages/LanguageAndCommunication/GoDutchModel"),
        (["Pages/Language-And-Communication/Communication/Index.cshtml"], "Resources/Pages/LanguageAndCommunication/Communication/IndexModel"),
        (["Pages/Language-And-Communication/Communication/Free30MinutesConsult.cshtml"], "Resources/Pages/LanguageAndCommunication/Communication/Free30MinutesConsultModel"),
        (["Pages/Language-And-Communication/Communication/iDeas.cshtml"], "Resources/Pages/LanguageAndCommunication/Communication/iDeasModel"),
        (["Pages/Nt2-Integration.cshtml"], "Resources/Pages/Nt2IntegrationModel"),
        (["Pages/PaymentProcessing.cshtml"], "Resources/Pages/PaymentProcessingModel"),
        (["Pages/PaymentResult.cshtml", "Pages/PaymentResult.cshtml.cs"], "Resources/Pages/PaymentResultModel"),
        (["Pages/Personal-Development.cshtml"], "Resources/Pages/Personal_DevelopmentModel"),
        (["Pages/Privacy.cshtml"], "Resources/Pages/PrivacyModel"),
        (["Pages/Pricing.cshtml", "Pages/Products.cshtml"], "Resources/Pages/ProductsModel"),
        (["Pages/Product.cshtml", "Pages/Product.cshtml.cs"], "Resources/Pages/ProductModel"),
        (["Pages/Success.cshtml"], "Resources/Pages/SuccessModel"),
        (["Pages/Terms-Conditions.cshtml"], "Resources/Pages/Terms_ConditionsModel"),
        (["Pages/Expat-Support.cshtml"], "Resources/Pages/Expat_SupportModel"),
        (["Pages/Portal/Examination/Level-Test-Introduction.cshtml", "Pages/Portal/Examination/Level-Test-Introduction.cshtml.cs"], "Resources/Pages/Portal/Examination/LevelTestIntroductionModel"),
        (["Pages/Portal/Examination/Level-Test.cshtml"], "Resources/Pages/Portal/Examination/LevelTestModel"),
        (["Pages/Portal/Examination/Completed.cshtml", "Pages/Portal/Examination/Completed.cshtml.cs", "Pages/Portal/Examination/_LevelTestCompletedPartial.cshtml", "Pages/Examination/Completed.cshtml"], "Resources/Pages/Portal/Examination/CompletedModel"),
        (["Pages/Shared/Examination/_ExaminationHeader.cshtml"], "Resources/Pages/Shared/Examination/ExaminationHeaderModel"),
        (["Pages/Shared/Examination/_Pagination.cshtml"], "Resources/Pages/Shared/Examination/PaginationModel"),
        (["Pages/Shared/Examination/Questions/_QuestionShell.cshtml", "Pages/Shared/Examination/Questions/_QuestionBody.Take.cshtml"], "Resources/Pages/Shared/Examination/Questions/QuestionShellModel"),
        (["Pages/Shared/_Layout.cshtml"], "Resources/Pages/Shared/_Layout"),
        (["Pages/Shared/_LoginPartial.cshtml"], "Resources/Pages/Shared/_LoginPartial")
    ];

    [Fact]
    public void ResourceFiles_HaveMatchingEnglishAndDutchKeysWithoutDuplicates()
    {
        var root = FindApplicationRoot();
        var resourceFiles = Directory.GetFiles(Path.Combine(root, "Resources", "Pages"), "*.resx", SearchOption.AllDirectories);
        var pairs = resourceFiles
            .GroupBy(path => CultureSuffixRegex().Replace(path, string.Empty), StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var pair in pairs)
        {
            Assert.Equal(2, pair.Count());

            var cultures = pair.ToDictionary(
                path => CultureSuffixRegex().Match(path).Groups[1].Value,
                LoadKeys,
                StringComparer.OrdinalIgnoreCase);

            Assert.True(cultures.ContainsKey("en-US"), $"Missing en-US resource for {pair.Key}.");
            Assert.True(cultures.ContainsKey("nl-NL"), $"Missing nl-NL resource for {pair.Key}.");
            Assert.Equal(cultures["en-US"], cultures["nl-NL"]);
        }
    }

    [Fact]
    public void LocalizedPublicSurfaces_ReferenceKeysPresentInBothCultures()
    {
        var root = FindApplicationRoot();

        foreach (var (sources, resourceBase) in LocalizedSurfaces)
        {
            var englishKeys = LoadKeys(Path.Combine(root, $"{resourceBase}.en-US.resx".Replace('/', Path.DirectorySeparatorChar)));
            var dutchKeys = LoadKeys(Path.Combine(root, $"{resourceBase}.nl-NL.resx".Replace('/', Path.DirectorySeparatorChar)));

            foreach (var source in sources)
            {
                var sourcePath = Path.Combine(root, source.Replace('/', Path.DirectorySeparatorChar));
                var contents = File.ReadAllText(sourcePath);
                var referencedKeys = LiteralLocalizerKeyRegex()
                    .Matches(contents)
                    .Select(match => match.Groups[1].Value)
                    .Distinct(StringComparer.Ordinal);

                foreach (var key in referencedKeys)
                {
                    Assert.Contains(key, englishKeys);
                    Assert.Contains(key, dutchKeys);
                }
            }
        }
    }

    [Fact]
    public void PublicRazorViews_DoNotBranchOnDutchCultureForContent()
    {
        var root = FindApplicationRoot();
        var pagesRoot = Path.Combine(root, "Pages");
        var razorFiles = Directory.GetFiles(pagesRoot, "*.cshtml", SearchOption.AllDirectories);

        foreach (var razorFile in razorFiles)
        {
            var relativePath = Path.GetRelativePath(pagesRoot, razorFile).Replace('\\', '/');
            if (relativePath.StartsWith("Portal/", StringComparison.OrdinalIgnoreCase) &&
                !relativePath.StartsWith("Portal/Examination/Level-Test-Introduction", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var contents = File.ReadAllText(razorFile);
            Assert.DoesNotContain("isDutch", contents, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("iD_Develops.Resources.Pages.IndexModel", "HeroTitleLine1")]
    [InlineData("iD_Develops.Resources.Pages.Shared._Layout", "Nav_Organizations")]
    [InlineData("iD_Develops.Resources.Pages.Examination.ResultsModel", "TakeLevelTestButton")]
    [InlineData("iD_Develops.Resources.Pages.Portal.Examination.CompletedModel", "CompletedTitle")]
    public void CompiledResources_ResolveNewKeysInBothCultures(string resourceBaseName, string key)
    {
        var resourceManager = new ResourceManager(resourceBaseName, typeof(global::iD_Develops.Pages.IndexModel).Assembly);

        foreach (var cultureName in new[] { "en-US", "nl-NL" })
        {
            var value = resourceManager.GetString(key, CultureInfo.GetCultureInfo(cultureName));
            Assert.False(string.IsNullOrWhiteSpace(value), $"Missing compiled value for {resourceBaseName}.{key} ({cultureName}).");
            Assert.NotEqual(key, value);
        }
    }

    [Fact]
    public void PublicRazorPageLinks_PreserveTheCurrentCulture()
    {
        var root = FindApplicationRoot();
        var pagesRoot = Path.Combine(root, "Pages");
        var razorFiles = Directory.GetFiles(pagesRoot, "*.cshtml", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(pagesRoot, path).StartsWith($"Portal{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

        foreach (var razorFile in razorFiles)
        {
            var contents = File.ReadAllText(razorFile);
            foreach (Match match in PageLinkRegex().Matches(contents))
            {
                var destination = match.Groups[1].Value;
                if (destination.StartsWith("/Portal/", StringComparison.OrdinalIgnoreCase) ||
                    match.Value.Contains("\"/Portal/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Assert.Contains("asp-route-culture=", match.Value, StringComparison.Ordinal);
            }
        }
    }

    private static SortedSet<string> LoadKeys(string path)
    {
        var document = XDocument.Load(path);
        var keys = document.Root?
            .Elements("data")
            .Select(element => element.Attribute("name")?.Value)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .ToList() ?? [];

        var duplicateKeys = keys
            .GroupBy(key => key, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        Assert.True(duplicateKeys.Count == 0, $"Duplicate resource keys in {path}: {string.Join(", ", duplicateKeys)}");
        return new SortedSet<string>(keys, StringComparer.Ordinal);
    }

    private static string FindApplicationRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "iD-Develops.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the application project root.");
    }

    [GeneratedRegex(@"\.(en-US|nl-NL)\.resx$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CultureSuffixRegex();

    [GeneratedRegex("(?:Localizer|localizer|_localizer)\\s*\\[\\s*\\\"([^\\\"]+)\\\"")]
    private static partial Regex LiteralLocalizerKeyRegex();

    [GeneratedRegex("<a\\b(?=[^>]*\\basp-page\\s*=\\s*\\\"([^\\\"]+)\\\")[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex PageLinkRegex();
}
