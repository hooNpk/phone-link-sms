using ClosedXML.Excel;
using PhoneLinkSmsApp.Services;

namespace PhoneLinkSmsApp.Tests;

public class ExcelParserTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"plsms_{Guid.NewGuid():N}.xlsx");

    public void Dispose() => File.Delete(_path);

    void Save(Action<IXLWorksheet> fill, string sheetName = "명단")
    {
        using var wb = new XLWorkbook();
        fill(wb.AddWorksheet(sheetName));
        wb.SaveAs(_path);
    }

    [Fact]
    public void Detects_columns_in_party_member_list_layout()
    {
        // 실제 당원명부와 같은 헤더 구성
        Save(ws =>
        {
            string[] headers = ["당원번호", "이름", "성별", "생년월일", "광역", "지역", "시군구", "행정동", "휴대폰 번호", "SMS 수신여부", "형태", "등록일", "승인일", "당권여부"];
            for (int i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];
            ws.Cell(2, 1).Value = 123456; ws.Cell(2, 2).Value = "홍길동"; ws.Cell(2, 9).Value = "010-1234-5678"; ws.Cell(2, 10).Value = "수신";
            ws.Cell(3, 1).Value = 123457; ws.Cell(3, 2).Value = "김철수"; ws.Cell(3, 9).Value = "010-2222-3333"; ws.Cell(3, 10).Value = "거부";
        });

        var sheet = Assert.Single(ExcelParser.LoadWorkbook(_path));
        var mapping = ExcelParser.DetectColumns(sheet);

        Assert.Equal(1, mapping.NameColumn);   // B
        Assert.Equal(8, mapping.PhoneColumn);  // I
        Assert.Equal(9, mapping.SmsColumn);    // J

        var recipients = ExcelParser.BuildRecipients(sheet, mapping);
        Assert.Equal(2, recipients.Count);
        Assert.Equal("홍길동", recipients[0].Name);
        Assert.Equal(2, recipients[0].RowNumber);
        Assert.Equal("거부", recipients[1].SmsValue);
    }

    [Fact]
    public void Finds_header_below_title_rows_and_reads_numeric_phone()
    {
        Save(ws =>
        {
            ws.Cell(1, 1).Value = "2026 수원 당원 명단";
            ws.Cell(3, 1).Value = "성명";
            ws.Cell(3, 2).Value = "연락처";
            ws.Cell(4, 1).Value = "홍길동";
            ws.Cell(4, 2).Value = 1012345678;   // 숫자로 저장돼 앞 0이 빠짐
        });

        var sheet = Assert.Single(ExcelParser.LoadWorkbook(_path));
        Assert.Equal(3, sheet.HeaderRowNumber);

        var recipients = ExcelParser.BuildRecipients(sheet, ExcelParser.DetectColumns(sheet));
        var r = Assert.Single(recipients);
        Assert.Equal("010-1234-5678", r.Phone);
        Assert.Equal(4, r.RowNumber);
    }

    [Fact]
    public void Falls_back_to_content_when_headers_are_unrecognized()
    {
        Save(ws =>
        {
            ws.Cell(1, 1).Value = "A"; ws.Cell(1, 2).Value = "B";
            ws.Cell(2, 1).Value = "홍길동"; ws.Cell(2, 2).Value = "010-1111-2222";
            ws.Cell(3, 1).Value = "김영희"; ws.Cell(3, 2).Value = "010-3333-4444";
        });

        var mapping = ExcelParser.DetectColumns(Assert.Single(ExcelParser.LoadWorkbook(_path)));
        Assert.Equal(0, mapping.NameColumn);
        Assert.Equal(1, mapping.PhoneColumn);
        Assert.Null(mapping.SmsColumn);
    }

    [Theory]
    [InlineData(1, "A")]
    [InlineData(9, "I")]
    [InlineData(26, "Z")]
    [InlineData(27, "AA")]
    public void Column_letters(int n, string letter) => Assert.Equal(letter, ExcelParser.ColumnLetter(n));
}
