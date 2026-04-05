namespace TercihAI.Backend.Models;

public sealed class ProgramSearchQuery
{
    public string? Query { get; set; }
    public string? ScoreType { get; set; }
    public string? City { get; set; }
    public string? University { get; set; }
    public string? Program { get; set; }
    public string? UniversityType { get; set; }
    public string? ProgramCode { get; set; }
    public int? MinRank { get; set; }
    public int? MaxRank { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 24;
    public bool IncludeNewPrograms { get; set; } = true;
}

public sealed class ProgramSearchResponse
{
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalRecords { get; init; }
    public int TotalFiltered { get; init; }
    public string? Source { get; init; }
    public IReadOnlyList<UniversityProgramRecord> Items { get; init; } = [];
}

public sealed class UniversityProgramRecord
{
    public string YopCode { get; init; } = "";
    public string UniversityName { get; init; } = "";
    public string Faculty { get; init; } = "";
    public string ProgramName { get; init; } = "";
    public string ProgramDetails { get; init; } = "";
    public string City { get; init; } = "";
    public string UniversityType { get; init; } = "";
    public string FeeType { get; init; } = "";
    public string EducationType { get; init; } = "";
    public string FillStatus { get; init; } = "";
    public IReadOnlyDictionary<string, YearlyAdmissionMetric> Years { get; init; } =
        new Dictionary<string, YearlyAdmissionMetric>();
}

public sealed class YearlyAdmissionMetric
{
    public int? Capacity { get; init; }
    public string? CapacityText { get; init; }
    public int? Placed { get; init; }
    public string? PlacedText { get; init; }
    public int? Ranking { get; init; }
    public string? RankingText { get; init; }
    public decimal? Score { get; init; }
    public string? ScoreText { get; init; }
    public bool IsEstimated { get; init; }
    public string? Method { get; init; }
    public IReadOnlyList<string> SourceYears { get; init; } = [];
}

public sealed class MetadataResponse
{
    public IReadOnlyList<string> Cities { get; init; } = [];
    public IReadOnlyList<string> ScoreTypes { get; init; } = [];
    public IReadOnlyList<string> UniversityTypes { get; init; } = [];
    public string ForecastMethod { get; init; } = "";
}
