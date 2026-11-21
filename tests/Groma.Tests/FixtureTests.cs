namespace Groma.Tests;

public static class Fixtures
{
    public static readonly string Root = Find();

    static string Find()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "fixtures", "make.py"))) return Path.Combine(dir.FullName, "fixtures");
        throw new DirectoryNotFoundException("fixtures folder not found above " + AppContext.BaseDirectory);
    }

    /// <summary>Every scan of every sheet, as (sheet, step) in a stable order.</summary>
    public static IEnumerable<object[]> Scans() =>
        from sheet in Directory.GetDirectories(Root).Order(StringComparer.Ordinal)
        from step in Enumerable.Range(1, Directory.GetFiles(sheet, "*.graph.json").Length)
        select new object[] { Path.GetFileName(sheet), step };

    public static Graph Graph(string sheet, int step) =>
        LayoutFile.ReadGraph(File.ReadAllText(Path.Combine(Root, sheet, $"{step}.graph.json")));
}

public class FixtureTests
{
    [Theory]
    [MemberData(nameof(Fixtures.Scans), MemberType = typeof(Fixtures))]
    public void Every_fixture_graph_is_valid_input(string sheet, int step)
    {
        Assert.NotEmpty(Fixtures.Graph(sheet, step).Boxes);
    }

    [Fact]
    public void The_fixtures_cover_the_cases_the_prototypes_never_had()
    {
        var all = Fixtures.Scans().Select(s => Fixtures.Graph((string)s[0], (int)s[1])).ToList();
        bool TwoDeep(Graph g) => g.Boxes.Any(b => b.Parent is not null && g.Boxes.Single(p => p.Id == b.Parent).Parent is not null);
        bool ToContainer(Graph g) => g.Links.Any(l => g.Boxes.Any(b => b.Parent == l.To || b.Parent == l.From));
        Assert.Contains(all, TwoDeep);
        Assert.Contains(all, g => g.Links.Any(l => l.ToEdge is not null));
        Assert.Contains(all, g => g.Links.Any(l => l.FromEdge is not null));
        Assert.Contains(all, ToContainer);
        Assert.Equal(37, all.Count);
    }
}
