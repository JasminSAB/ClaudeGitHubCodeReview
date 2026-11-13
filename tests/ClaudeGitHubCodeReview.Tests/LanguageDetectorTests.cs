using ClaudeGitHubCodeReview.Review;

namespace ClaudeGitHubCodeReview.Tests;

public class LanguageDetectorTests
{
    [Fact]
    public void Bosnian_comments_are_detected()
    {
        const string text = """
            // Narudžba se ne može potvrditi ako korpa nije prazna i ako kupac nema adresu.
            // Samo jedan zahtjev može promijeniti status (zaštita od duplog klika).
            // Korisnik može vidjeti samo svoje narudžbe, a administrator vidi sve.
            // Ako je popust istekao, cijena se računa bez njega.
            """;

        Assert.Equal(LanguageDetector.Bosnian, LanguageDetector.DetectText(text));
    }

    [Fact]
    public void English_comments_are_detected()
    {
        const string text = """
            // Returns the open order if there is one, otherwise creates a new one for the customer.
            // Only the first request can change the status, which protects against a double click.
            // The price is captured when the order is placed and is never changed afterwards.
            """;

        Assert.Equal(LanguageDetector.English, LanguageDetector.DetectText(text));
    }

    [Fact]
    public void Too_little_evidence_defaults_to_english() =>
        Assert.Equal(LanguageDetector.English, LanguageDetector.DetectText("// ok"));

    [Fact]
    public void Scans_comments_in_source_files_and_ignores_build_output()
    {
        var root = Directory.CreateTempSubdirectory("cgcr-lang-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "Service.cs"), """
                public class OrderService
                {
                    // Narudžba ulazi u trenutno otvoren račun, a ne u račun koji ju je kreirao
                    // Zatvaranje računa NE otkazuje narudžbe koje su još u obradi
                    // Iznos se zaokružuje naviše i ne mijenja se bez razgovora s vlasnikom
                    public void Confirm() { }
                }
                """);
            Directory.CreateDirectory(Path.Combine(root, "bin"));
            File.WriteAllText(Path.Combine(root, "bin", "Generated.cs"),
                string.Concat(Enumerable.Repeat("// the and is to of for this that with from\n", 200)));

            var result = LanguageDetector.Detect(root);

            Assert.Equal(LanguageDetector.Bosnian, result.Language);
            Assert.Equal(1, result.FilesScanned);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
