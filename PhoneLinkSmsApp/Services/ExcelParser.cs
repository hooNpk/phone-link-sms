using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using PhoneLinkSmsApp.Models;

namespace PhoneLinkSmsApp.Services;

public sealed record SheetRow(int RowNumber, string[] Cells);

/// <summary>컬럼 번호는 0부터 (0 = A열)</summary>
public sealed record ColumnMapping(int? NameColumn, int? PhoneColumn, int? SmsColumn);

public sealed class SheetData
{
    public required string SheetName { get; init; }
    public required int HeaderRowNumber { get; init; }
    public required IReadOnlyList<string> Headers { get; init; }
    public required IReadOnlyList<SheetRow> Rows { get; init; }
}

public static partial class ExcelParser
{
    static readonly string[] NameKeywords = ["이름", "성명", "성함", "name"];
    static readonly string[] MobileKeywords = ["휴대폰", "핸드폰", "휴대전화", "모바일", "mobile", "cell"];
    static readonly string[] PhoneKeywords = ["전화", "연락처", "phone", "tel"];
    static readonly string[] SmsKeywords = ["sms", "문자", "수신"];
    const int HeaderScanRows = 10;
    const double ContentMatchRatio = 0.6;

    [GeneratedRegex(@"^[가-힣]{2,4}$")]
    private static partial Regex HangulName();

    /// <summary>데이터가 있는 모든 시트를 읽는다. 엑셀에서 파일을 열어둔 상태여도 읽을 수 있다.</summary>
    public static List<SheetData> LoadWorkbook(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var workbook = new XLWorkbook(stream);

        var sheets = new List<SheetData>();
        foreach (var ws in workbook.Worksheets)
        {
            var used = ws.RangeUsed();
            if (used == null) continue;

            int firstRow = used.FirstRow().RowNumber();
            int lastRow = used.LastRow().RowNumber();
            int lastCol = used.LastColumn().ColumnNumber();
            string[] ReadRow(int r) => Enumerable.Range(1, lastCol).Select(c => CellText(ws.Cell(r, c))).ToArray();

            int headerRow = FindHeaderRow(firstRow, Math.Min(lastRow, firstRow + HeaderScanRows - 1), ReadRow);
            var rows = new List<SheetRow>();
            for (int r = headerRow + 1; r <= lastRow; r++)
            {
                var cells = ReadRow(r);
                if (cells.All(string.IsNullOrWhiteSpace)) continue;
                rows.Add(new SheetRow(r, cells));
            }

            sheets.Add(new SheetData
            {
                SheetName = ws.Name,
                HeaderRowNumber = headerRow,
                Headers = ReadRow(headerRow),
                Rows = rows,
            });
        }
        return sheets;
    }

    /// <summary>헤더 이름으로 이름/전화/SMS 컬럼을 찾고, 없으면 내용으로 추정한다.</summary>
    public static ColumnMapping DetectColumns(SheetData sheet)
    {
        int? FindHeader(string[] keywords)
        {
            for (int i = 0; i < sheet.Headers.Count; i++)
                if (MatchesAny(sheet.Headers[i], keywords)) return i;
            return null;
        }

        var phone = FindHeader(MobileKeywords)
                    ?? FindHeader(PhoneKeywords)
                    ?? FindByContent(sheet, v => PhoneNumberNormalizer.Normalize(v) != null);
        var name = FindHeader(NameKeywords)
                   ?? FindByContent(sheet, v => HangulName().IsMatch(v));
        var sms = FindHeader(SmsKeywords);

        if (name == phone) name = null;
        if (sms == phone || sms == name) sms = null;
        return new ColumnMapping(name, phone, sms);
    }

    public static List<Recipient> BuildRecipients(SheetData sheet, ColumnMapping mapping)
    {
        var list = new List<Recipient>();
        if (mapping.PhoneColumn == null) return list;

        foreach (var row in sheet.Rows)
        {
            string Get(int? col) => col is int c && c < row.Cells.Length ? row.Cells[c].Trim() : "";
            var name = Get(mapping.NameColumn);
            var raw = Get(mapping.PhoneColumn);
            if (name.Length == 0 && raw.Length == 0) continue;

            list.Add(new Recipient
            {
                RowNumber = row.RowNumber,
                Name = name,
                RawPhone = raw,
                Phone = PhoneNumberNormalizer.Normalize(raw) ?? raw,
                SmsValue = Get(mapping.SmsColumn),
            });
        }
        return list;
    }

    /// <summary>1 → A, 27 → AA</summary>
    public static string ColumnLetter(int columnNumber)
    {
        var s = "";
        while (columnNumber > 0)
        {
            int m = (columnNumber - 1) % 26;
            s = (char)('A' + m) + s;
            columnNumber = (columnNumber - 1) / 26;
        }
        return s;
    }

    static int FindHeaderRow(int first, int last, Func<int, string[]> readRow)
    {
        string[] allKeywords = [.. NameKeywords, .. MobileKeywords, .. PhoneKeywords, .. SmsKeywords];
        int best = first, bestScore = 0;
        for (int r = first; r <= last; r++)
        {
            int score = readRow(r).Count(h => MatchesAny(h, allKeywords));
            if (score > bestScore) (best, bestScore) = (r, score);
        }
        return best;
    }

    static int? FindByContent(SheetData sheet, Func<string, bool> predicate)
    {
        if (sheet.Rows.Count == 0) return null;
        int? best = null;
        double bestRatio = ContentMatchRatio;
        for (int c = 0; c < sheet.Headers.Count; c++)
        {
            int hits = sheet.Rows.Count(r => c < r.Cells.Length && r.Cells[c].Length > 0 && predicate(r.Cells[c].Trim()));
            double ratio = hits / (double)sheet.Rows.Count;
            if (ratio > bestRatio) (best, bestRatio) = (c, ratio);
        }
        return best;
    }

    static bool MatchesAny(string header, string[] keywords)
    {
        var key = header.Replace(" ", "").ToLowerInvariant();
        return key.Length > 0 && keywords.Any(key.Contains);
    }

    static string CellText(IXLCell cell)
    {
        XLCellValue v;
        try { v = cell.Value; }
        catch { v = cell.CachedValue; }

        if (v.IsNumber)
        {
            var n = v.GetNumber();
            return n == Math.Floor(n) && Math.Abs(n) < 1e15
                ? ((long)n).ToString(CultureInfo.InvariantCulture)
                : n.ToString(CultureInfo.InvariantCulture);
        }
        if (v.IsDateTime) return v.GetDateTime().ToString("yyyy-MM-dd");
        if (v.IsText) return v.GetText().Trim();
        if (v.IsBoolean) return v.GetBoolean() ? "TRUE" : "FALSE";
        return "";
    }
}
