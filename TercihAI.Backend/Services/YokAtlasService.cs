using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using TercihAI.Backend.Models;

namespace TercihAI.Backend.Services;

public sealed partial class YokAtlasService(
    HttpClient httpClient,
    IMemoryCache cache,
    ILogger<YokAtlasService> logger)
{
    private const string SearchEndpoint =
        "https://yokatlas.yok.gov.tr/server_side/server_processing-atlas2016-TS-t4.php";

    private static readonly string[] ForecastPriorityYears = ["2024", "2025"];

    public async Task<ProgramSearchResponse> SearchProgramsAsync(
        ProgramSearchQuery query,
        CancellationToken cancellationToken)
    {
        var normalizedQuery = NormalizeQuery(query);
        var cacheKey = BuildCacheKey(normalizedQuery);

        if (cache.TryGetValue(cacheKey, out ProgramSearchResponse? cachedResponse) &&
            cachedResponse is not null)
        {
            return cachedResponse;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, SearchEndpoint)
        {
            Content = new FormUrlEncodedContent(BuildPayload(normalizedQuery))
        };

        request.Headers.Referrer = new Uri(
            $"https://yokatlas.yok.gov.tr/tercih-sihirbazi-t4-tablo.php?p={normalizedQuery.ScoreType}");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "YÖK Atlas araması başarısız oldu. Durum: {StatusCode}, Gövde: {Body}",
                response.StatusCode,
                responseText);

            throw new InvalidOperationException("YÖK Atlas verisine şu anda erişilemiyor.");
        }

        if (!responseText.TrimStart().StartsWith("{", StringComparison.Ordinal))
        {
            logger.LogWarning("Beklenmeyen YÖK Atlas yanıtı alındı: {Body}", responseText);
            throw new InvalidOperationException(
                "YÖK Atlas yanıtı beklenen JSON formatında gelmedi.");
        }

        using var document = JsonDocument.Parse(responseText);
        var root = document.RootElement;

        var items = new List<UniversityProgramRecord>();

        if (root.TryGetProperty("data", out var dataElement) &&
            dataElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in dataElement.EnumerateArray())
            {
                items.Add(MapProgram(row));
            }
        }

        var searchResponse = new ProgramSearchResponse
        {
            Page = normalizedQuery.Page,
            PageSize = normalizedQuery.PageSize,
            TotalRecords = TryGetInt(root, "recordsTotal") ?? items.Count,
            TotalFiltered = TryGetInt(root, "recordsFiltered") ?? items.Count,
            Source = TryGetString(root, "source"),
            Items = items
        };

        cache.Set(
            cacheKey,
            searchResponse,
            new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
            });

        return searchResponse;
    }

    public async Task<UniversityProgramRecord?> GetProgramByCodeAsync(
        string yopCode,
        string? scoreType,
        CancellationToken cancellationToken)
    {
        var response = await SearchProgramsAsync(
            new ProgramSearchQuery
            {
                ProgramCode = yopCode,
                ScoreType = scoreType,
                Page = 1,
                PageSize = 1,
                IncludeNewPrograms = true
            },
            cancellationToken);

        return response.Items.FirstOrDefault();
    }

    private static ProgramSearchQuery NormalizeQuery(ProgramSearchQuery query)
    {
        return new ProgramSearchQuery
        {
            Query = query.Query?.Trim(),
            ScoreType = NormalizeScoreType(query.ScoreType),
            City = NormalizeUpperValue(query.City),
            University = NormalizeUpperValue(query.University),
            Program = query.Program?.Trim(),
            UniversityType = query.UniversityType?.Trim(),
            ProgramCode = query.ProgramCode?.Trim(),
            MinRank = query.MinRank is > 0 ? query.MinRank : null,
            MaxRank = query.MaxRank is > 0 ? query.MaxRank : null,
            Page = Math.Max(query.Page, 1),
            PageSize = Math.Clamp(query.PageSize, 1, 100),
            IncludeNewPrograms = query.IncludeNewPrograms
        };
    }

    private static string NormalizeScoreType(string? scoreType)
    {
        var normalized = scoreType?.Trim().ToLowerInvariant();

        return normalized switch
        {
            "ea" => "ea",
            "dil" => "dil",
            "söz" => "söz",
            "soz" => "söz",
            _ => "say"
        };
    }

    private static string? NormalizeUpperValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().ToUpper(new CultureInfo("tr-TR"));
    }

    private static Dictionary<string, string> BuildPayload(ProgramSearchQuery query)
    {
        var payload = CreateBasePayload(query.Page, query.PageSize);

        payload["search[value]"] = query.Query ?? "";
        payload["puan_turu"] = query.ScoreType ?? "say";
        payload["ust_bs"] = query.MinRank?.ToString(CultureInfo.InvariantCulture) ?? "";
        payload["alt_bs"] = query.MaxRank?.ToString(CultureInfo.InvariantCulture) ?? "";
        payload["yeniler"] = query.IncludeNewPrograms ? "1" : "0";
        payload["kilavuz_kodu"] = query.ProgramCode ?? "";
        payload["universite"] = FormatArrayValue(query.University);
        payload["program"] = FormatArrayValue(query.Program);
        payload["sehir"] = FormatArrayValue(query.City);
        payload["universite_turu"] = FormatArrayValue(query.UniversityType);
        payload["ogretim_turu"] = "[]";
        payload["doluluk"] = "[]";

        return payload;
    }

    private static Dictionary<string, string> CreateBasePayload(int page, int pageSize)
    {
        var payload = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["draw"] = "4",
            ["start"] = ((page - 1) * pageSize).ToString(CultureInfo.InvariantCulture),
            ["length"] = pageSize.ToString(CultureInfo.InvariantCulture),
            ["search[value]"] = "",
            ["search[regex]"] = "false",
            ["order[0][column]"] = "34",
            ["order[0][dir]"] = "desc",
            ["order[1][column]"] = "41",
            ["order[1][dir]"] = "asc",
            ["order[2][column]"] = "42",
            ["order[2][dir]"] = "asc"
        };

        for (var index = 0; index <= 44; index++)
        {
            payload[$"columns[{index}][data]"] = index.ToString(CultureInfo.InvariantCulture);
            payload[$"columns[{index}][name]"] = "";
            payload[$"columns[{index}][searchable]"] = "true";
            payload[$"columns[{index}][orderable]"] =
                IsOrderableColumn(index) ? "true" : "false";
            payload[$"columns[{index}][search][value]"] = "";
            payload[$"columns[{index}][search][regex]"] = "false";
        }

        return payload;
    }

    private static bool IsOrderableColumn(int index)
    {
        return index is not 0 and not 1 and not 2 and not 4 and not 6 and not 7 and not 8 and
            not 9 and not 10 and not 14 and not 15;
    }

    private static string FormatArrayValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "[]"
            : JsonSerializer.Serialize(new[] { value.Trim() });
    }

    private static string BuildCacheKey(ProgramSearchQuery query)
    {
        return string.Join(
            "|",
            query.Query,
            query.ScoreType,
            query.City,
            query.University,
            query.Program,
            query.UniversityType,
            query.ProgramCode,
            query.MinRank,
            query.MaxRank,
            query.Page,
            query.PageSize,
            query.IncludeNewPrograms);
    }

    private static UniversityProgramRecord MapProgram(JsonElement row)
    {
        var years = new Dictionary<string, YearlyAdmissionMetric>
        {
            ["2024"] = CreateActualMetric(row, "2024"),
            ["2025"] = CreateActualMetric(row, "2025")
        };

        years["2026"] = CreateForecastMetric(years);

        return new UniversityProgramRecord
        {
            YopCode = GetCleanLine(row, 1, 0),
            UniversityName = GetCleanLine(row, 2, 0),
            Faculty = GetCleanLine(row, 2, 1),
            ProgramName = GetCleanLine(row, 4, 0),
            ProgramDetails = GetCleanLine(row, 4, 1),
            City = GetCleanCell(row, 6),
            UniversityType = GetCleanCell(row, 7),
            FeeType = GetCleanCell(row, 8),
            EducationType = GetCleanCell(row, 9),
            FillStatus = GetCleanCell(row, 14),
            Years = years
        };
    }

    private static YearlyAdmissionMetric CreateActualMetric(JsonElement row, string year)
    {
        var yearIndex = year switch
        {
            "2025" => 1,
            "2024" => 2,
            "2023" => 3,
            "2022" => 4,
            _ => 1
        };

        var capacityText = GetCellLine(row, 10, yearIndex);
        var placedText = GetCellLine(row, 15, yearIndex);
        var rankingText = GetCellLine(row, 19, yearIndex);
        var scoreText = GetCellLine(row, 27, yearIndex);

        return new YearlyAdmissionMetric
        {
            Capacity = ParseCompositeNumber(capacityText),
            CapacityText = EmptyAsNull(capacityText),
            Placed = ParseCompositeNumber(placedText),
            PlacedText = EmptyAsNull(placedText),
            Ranking = ParseRanking(rankingText),
            RankingText = EmptyAsNull(rankingText),
            Score = ParseScore(scoreText),
            ScoreText = EmptyAsNull(scoreText),
            IsEstimated = false
        };
    }

    private static YearlyAdmissionMetric CreateForecastMetric(
        IReadOnlyDictionary<string, YearlyAdmissionMetric> years)
    {
        var sourceYears = ForecastPriorityYears
            .Where(years.ContainsKey)
            .Where(year => years[year].Ranking is not null || years[year].Score is not null)
            .ToList();

        if (sourceYears.Count == 0)
        {
            sourceYears = years
                .Where(pair => pair.Value.Ranking is not null || pair.Value.Score is not null)
                .Select(pair => pair.Key)
                .ToList();
        }

        var metrics = sourceYears.Select(year => years[year]).ToList();
        var capacityAverage = Average(metrics.Select(metric => metric.Capacity));
        var placedAverage = Average(metrics.Select(metric => metric.Placed));
        var rankingAverage = Average(metrics.Select(metric => metric.Ranking));
        var scoreAverage = Average(metrics.Select(metric => metric.Score));

        var method = sourceYears.SequenceEqual(ForecastPriorityYears)
            ? "2024 ve 2025 ortalaması"
            : "Mevcut yılların ortalaması";

        return new YearlyAdmissionMetric
        {
            Capacity = capacityAverage,
            CapacityText = capacityAverage?.ToString(CultureInfo.InvariantCulture),
            Placed = placedAverage,
            PlacedText = placedAverage?.ToString(CultureInfo.InvariantCulture),
            Ranking = rankingAverage,
            RankingText = FormatRanking(rankingAverage),
            Score = scoreAverage,
            ScoreText = scoreAverage?.ToString("0.00000", CultureInfo.InvariantCulture),
            IsEstimated = true,
            Method = method,
            SourceYears = sourceYears
        };
    }

    private static string GetCleanCell(JsonElement row, int index)
    {
        return ExtractPlainText(GetCellValue(row, index));
    }

    private static string GetCleanLine(JsonElement row, int index, int lineIndex)
    {
        return GetCellLine(row, index, lineIndex);
    }

    private static string GetCellLine(JsonElement row, int index, int lineIndex)
    {
        var rawValue = GetCellValue(row, index);
        var lines = SplitHtmlLines(rawValue);

        if (lineIndex < 0 || lineIndex >= lines.Length)
        {
            return "";
        }

        return ExtractPlainText(lines[lineIndex]);
    }

    private static string GetCellValue(JsonElement row, int index)
    {
        if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() <= index)
        {
            return "";
        }

        var cell = row[index];

        return cell.ValueKind switch
        {
            JsonValueKind.Null => "",
            JsonValueKind.String => cell.GetString() ?? "",
            _ => cell.ToString()
        };
    }

    private static string[] SplitHtmlLines(string html)
    {
        return BrRegex().Split(html);
    }

    private static string ExtractPlainText(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return "";
        }

        var withoutTags = HtmlTagRegex().Replace(html, " ");
        var decoded = WebUtility.HtmlDecode(withoutTags)
            .Replace('\u00A0', ' ')
            .Replace("Detay", "")
            .Replace("Listeme Ekle", "")
            .Trim();

        return SpaceRegex().Replace(decoded, " ").Trim();
    }

    private static int? ParseCompositeNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains("---", StringComparison.Ordinal))
        {
            return null;
        }

        var normalized = value.Replace(".", "", StringComparison.Ordinal).Trim();

        if (normalized.Contains('('))
        {
            normalized = normalized[..normalized.IndexOf('(')];
        }

        if (int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var direct))
        {
            return direct;
        }

        if (!normalized.Contains('+'))
        {
            return null;
        }

        var total = 0;

        foreach (var part in normalized.Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var partValue))
            {
                return null;
            }

            total += partValue;
        }

        return total;
    }

    private static int? ParseRanking(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains("---", StringComparison.Ordinal))
        {
            return null;
        }

        var normalized = value.Replace(".", "", StringComparison.Ordinal).Trim();

        return int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ranking)
            ? ranking
            : null;
    }

    private static decimal? ParseScore(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains("---", StringComparison.Ordinal))
        {
            return null;
        }

        var normalized = value.Replace(".", "", StringComparison.Ordinal)
            .Replace(",", ".", StringComparison.Ordinal)
            .Trim();

        return decimal.TryParse(
            normalized,
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out var score)
            ? score
            : null;
    }

    private static int? Average(IEnumerable<int?> values)
    {
        var numbers = values.Where(value => value.HasValue).Select(value => value!.Value).ToList();

        return numbers.Count == 0
            ? null
            : (int)Math.Round(numbers.Average(), MidpointRounding.AwayFromZero);
    }

    private static decimal? Average(IEnumerable<decimal?> values)
    {
        var numbers = values.Where(value => value.HasValue).Select(value => value!.Value).ToList();

        return numbers.Count == 0
            ? null
            : Math.Round(numbers.Average(), 5, MidpointRounding.AwayFromZero);
    }

    private static string? EmptyAsNull(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string? FormatRanking(int? ranking)
    {
        return ranking?.ToString("N0", CultureInfo.GetCultureInfo("tr-TR"));
    }

    private static int? TryGetInt(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt32(out var value) => value,
            JsonValueKind.String when int.TryParse(property.GetString(), out var textValue) => textValue,
            _ => null
        };
    }

    private static string? TryGetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    [GeneratedRegex("<br\\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BrRegex();

    [GeneratedRegex("<[^>]+>", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex("\\s+", RegexOptions.Multiline)]
    private static partial Regex SpaceRegex();
}
