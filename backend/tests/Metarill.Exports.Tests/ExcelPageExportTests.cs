using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using ClosedXML.Excel;
using Metarill.Core.Exports;
using Metarill.Core.Security;
using Metarill.Runtime.Exports;
using Xunit;

namespace Metarill.Exports.Tests;

public sealed class ExcelPageExportTests
{
    /// <summary>真实XLSX写入器以Base64文本保存字节，不改图片、不注入公式。</summary>
    [Fact]
    public async Task Workbook_binary_values_are_lossless_Base64_text()
    {
        using var fixture=new ExportFixture();byte[] bytes=[0,255,0,127];
        var ticket=await fixture.Service.CreateAsync(new("page1","二进制",["字节","空字节","空值"],[new object?[]{bytes,Array.Empty<byte>(),null}]),fixture.Actor);
        var content=await fixture.Service.OpenAsync(ticket,fixture.Actor);
        await using(content.Stream)
        {
            using var workbook=new XLWorkbook(content.Stream);var sheet=workbook.Worksheet(1);
            Assert.Equal("AP8Afw==",sheet.Cell(2,1).GetString());Assert.Equal(bytes,Convert.FromBase64String(sheet.Cell(2,1).GetString()));Assert.False(sheet.Cell(2,1).HasFormula);Assert.Equal(XLDataType.Text,sheet.Cell(2,1).DataType);
            Assert.Equal("",sheet.Cell(2,2).GetString());Assert.True(sheet.Cell(2,3).IsEmpty());
        }
    }
    [Fact]
    public async Task Workbook_preserves_typed_values_large_ids_and_literal_strings()
    {
        using var fixture = new ExportFixture();
        object?[] values = [null, true, 42, 12.34m, 9007199254740993L, "0000123", "=HYPERLINK(\"https://evil.invalid\",\"click\")", "+1+1", "-1+1", "@SUM(A1:A2)", "'原始单引号", "\t=1+1",
            new DateTime(2026,10,18,13,45,20), new DateOnly(2026,10,18), new TimeOnly(13,45,20), TimeSpan.FromHours(30), 123456789012345678.123456789m,
            JsonSerializer.Deserialize<JsonElement>("{\"value\":\"=1+1\"}"), new DateTimeOffset(2026,10,18,13,45,20,TimeSpan.FromHours(8))];
        var ticket = await fixture.Service.CreateAsync(new("page1", "导出数据", Enumerable.Range(0, values.Length).Select(i => "列"+i).ToArray(), [values]), fixture.Actor);
        Assert.Matches("^[a-f0-9]{64}$", ticket);
        var content = await fixture.Service.OpenAsync(ticket, fixture.Actor);
        await using (content.Stream)
        {
            Assert.True(content.Stream.CanSeek); Assert.Equal(ExcelPageExportService.ExcelContentType, content.ContentType);
            using var workbook = new XLWorkbook(content.Stream); var sheet = workbook.Worksheet(1);
            Assert.True(sheet.Cell(2,1).IsEmpty()); Assert.Equal(XLDataType.Boolean,sheet.Cell(2,2).DataType); Assert.True(sheet.Cell(2,2).GetBoolean());
            Assert.Equal(XLDataType.Number,sheet.Cell(2,3).DataType); Assert.Equal(42,sheet.Cell(2,3).GetDouble());
            Assert.Equal(12.34,sheet.Cell(2,4).GetDouble()); Assert.Equal("9007199254740993",sheet.Cell(2,5).GetString());
            Assert.Equal("0000123",sheet.Cell(2,6).GetString());
            for(var index=6;index<=11;index++) { Assert.Equal(values[index],sheet.Cell(2,index+1).GetString()); Assert.False(sheet.Cell(2,index+1).HasFormula); }
            Assert.Equal(XLDataType.DateTime,sheet.Cell(2,13).DataType); Assert.Equal(values[12],sheet.Cell(2,13).GetDateTime());
            Assert.Equal(new DateTime(2026,10,18),sheet.Cell(2,14).GetDateTime()); Assert.Equal(new TimeSpan(13,45,20),sheet.Cell(2,15).GetTimeSpan());
            Assert.Equal(TimeSpan.FromHours(30),sheet.Cell(2,16).GetTimeSpan()); Assert.Equal("123456789012345678.123456789",sheet.Cell(2,17).GetString());
            Assert.Equal("{\"value\":\"=1+1\"}",sheet.Cell(2,18).GetString()); Assert.Contains("+08:00",sheet.Cell(2,19).GetString());
        }
        var xmlContent = await fixture.Service.OpenAsync(ticket, fixture.Actor);
        await using (xmlContent.Stream)
        {
            using var archive = new ZipArchive(xmlContent.Stream,ZipArchiveMode.Read,true);
            foreach(var entry in archive.Entries.Where(e=>e.FullName.StartsWith("xl/worksheets/",StringComparison.Ordinal)&&e.FullName.EndsWith(".xml",StringComparison.Ordinal)))
            { using var source=entry.Open();var xml=XDocument.Load(source);Assert.DoesNotContain(xml.Descendants(),element=>element.Name.LocalName=="f"); }
            Assert.DoesNotContain(archive.Entries, entry=>entry.FullName.StartsWith("xl/externalLinks",StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task Tickets_are_bound_to_exact_account_and_project_even_for_admin()
    {
        using var fixture=new ExportFixture();var ticket=await fixture.Service.CreateAsync(Request(),fixture.Actor);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>fixture.Service.OpenAsync(ticket,fixture.Actor with {AccountId=2,IsAdministrator=true}));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>fixture.Service.OpenAsync(ticket,fixture.Actor with {ProjectId="other",IsAdministrator=true}));
        foreach(var invalid in new[]{"../secret.xlsx","https://evil.invalid/file",ticket[..^1]+"g",new string('a',64)})
            await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>fixture.Service.OpenAsync(invalid,fixture.Actor));
        var allowed=await fixture.Service.OpenAsync(ticket,fixture.Actor);await allowed.Stream.DisposeAsync();
    }

    [Fact]
    public async Task Expiry_revokes_access_and_removes_private_files()
    {
        using var fixture=new ExportFixture();var ticket=await fixture.Service.CreateAsync(Request(),fixture.Actor);
        Assert.Single(Directory.GetFiles(fixture.Root));fixture.Clock.Advance(TimeSpan.FromMinutes(16));
        Assert.Equal(1,await fixture.Service.CleanupExpiredAsync());Assert.Empty(Directory.GetFiles(fixture.Root));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>fixture.Service.OpenAsync(ticket,fixture.Actor));
    }

    [Fact]
    public async Task Title_cleaning_does_not_create_paths_and_retains_unicode()
    {
        using var fixture=new ExportFixture();var title="../../客户/订单:*?[]\r\n\""+string.Concat(Enumerable.Repeat("😀",40))+"'";
        var ticket=await fixture.Service.CreateAsync(Request(title),fixture.Actor);var content=await fixture.Service.OpenAsync(ticket,fixture.Actor);
        await using(content.Stream)
        {
            Assert.EndsWith(".xlsx",content.FileName);Assert.DoesNotContain('/',content.FileName);Assert.DoesNotContain('\\',content.FileName);Assert.DoesNotContain('\r',content.FileName);Assert.DoesNotContain('\n',content.FileName);
            using var workbook=new XLWorkbook(content.Stream);Assert.InRange(workbook.Worksheet(1).Name.Length,1,31);
        }
        Assert.All(Directory.GetFiles(fixture.Root),path=>Assert.Matches("^[a-f0-9]{32}\\.xlsx$",Path.GetFileName(path)));
    }

    [Fact]
    public async Task Limits_reject_before_leaking_files_or_tickets()
    {
        using var fixture=new ExportFixture(new ExportOptions {MaxRows=1,MaxColumns=2,MaxCells=4,MaxCellCharacters=10,MaxTotalTextCharacters=15});
        await Assert.ThrowsAsync<InvalidOperationException>(()=>fixture.Service.CreateAsync(new("p","t",["a"],[[1],[2]]),fixture.Actor));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>fixture.Service.CreateAsync(new("p","t",["a","b","c"],[]),fixture.Actor));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>fixture.Service.CreateAsync(new("p","t",["a","b"],[[1]]),fixture.Actor));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>fixture.Service.CreateAsync(new("p","t",["a"],[[new string('x',11)]]),fixture.Actor));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>fixture.Service.CreateAsync(new("p","t",["1234567890"],[["1234567890"]]),fixture.Actor));
        if(Directory.Exists(fixture.Root))Assert.Empty(Directory.GetFiles(fixture.Root));
    }

    [Fact]
    public async Task Disk_limit_unknown_types_invalid_numbers_and_cancel_cleanup_partial_files()
    {
        using var small=new ExportFixture(new ExportOptions {MaxFileBytes=100});
        var limited=await Assert.ThrowsAnyAsync<Exception>(()=>small.Service.CreateAsync(Request(),small.Actor));Assert.Contains("大小上限",limited.ToString());Assert.Empty(Directory.GetFiles(small.Root));
        using var fixture=new ExportFixture();
        await Assert.ThrowsAsync<NotSupportedException>(()=>fixture.Service.CreateAsync(new("p","t",["a"],[[new object()]]),fixture.Actor));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>fixture.Service.CreateAsync(new("p","t",["a"],[[double.NaN]]),fixture.Actor));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>fixture.Service.CreateAsync(Request(),fixture.Actor,new CancellationToken(true)));
        if(Directory.Exists(fixture.Root))Assert.Empty(Directory.GetFiles(fixture.Root));
    }

    [Fact]
    public async Task Ticket_capacity_is_released_after_expiry_and_crash_orphans_are_cleaned()
    {
        using var fixture=new ExportFixture(new ExportOptions {MaxActiveTickets=1});await fixture.Service.CreateAsync(Request(),fixture.Actor);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>fixture.Service.CreateAsync(Request(),fixture.Actor));
        fixture.Clock.Advance(TimeSpan.FromMinutes(16));await fixture.Service.CleanupExpiredAsync();
        var ticket=await fixture.Service.CreateAsync(Request(),fixture.Actor);Assert.NotEmpty(ticket);
        var orphan=Path.Combine(fixture.Root,Guid.NewGuid().ToString("N")+".xlsx.part");await File.WriteAllTextAsync(orphan,"partial");File.SetLastWriteTimeUtc(orphan,fixture.Clock.GetUtcNow().UtcDateTime.Subtract(TimeSpan.FromHours(1)));
        var unrelated=Path.Combine(fixture.Root,"keep.txt");await File.WriteAllTextAsync(unrelated,"unrelated");
        await fixture.Service.CleanupExpiredAsync();Assert.False(File.Exists(orphan));Assert.True(File.Exists(unrelated));
    }

    [Fact]
    public async Task Maximum_apostrophe_text_preserves_all_characters_without_formula()
    {
        using var fixture=new ExportFixture();var text="'"+new string('x',32766);var ticket=await fixture.Service.CreateAsync(new("p","t",["a"],[[text]]),fixture.Actor);var content=await fixture.Service.OpenAsync(ticket,fixture.Actor);
        await using(content.Stream){using var workbook=new XLWorkbook(content.Stream);Assert.Equal(text,workbook.Worksheet(1).Cell(2,1).GetString());Assert.False(workbook.Worksheet(1).Cell(2,1).HasFormula);}
    }

    [Fact]
    public async Task Concurrent_capacity_is_atomic_and_expiry_does_not_break_open_stream()
    {
        using var fixture=new ExportFixture(new ExportOptions {MaxActiveTickets=1,MaxConcurrentExports=2});
        async Task<string?> TryCreate()
        { try{return await fixture.Service.CreateAsync(Request(),fixture.Actor);}catch(InvalidOperationException){return null;} }
        var results=await Task.WhenAll(Task.Run(TryCreate),Task.Run(TryCreate));
        var ticket=Assert.Single(results,result=>result is not null)!;
        Assert.Single(Directory.GetFiles(fixture.Root));
        var content=await fixture.Service.OpenAsync(ticket,fixture.Actor);
        await using(content.Stream)
        {
            fixture.Clock.Advance(TimeSpan.FromMinutes(16));await fixture.Service.CleanupExpiredAsync();
            Assert.Empty(Directory.GetFiles(fixture.Root));Assert.Equal((int)'P',content.Stream.ReadByte());Assert.Equal((int)'K',content.Stream.ReadByte());
            await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>fixture.Service.OpenAsync(ticket,fixture.Actor));
        }
    }

    [Fact]
    public async Task Restart_invalidates_old_ticket_and_eventually_cleans_orphan_file()
    {
        using var fixture=new ExportFixture();var ticket=await fixture.Service.CreateAsync(Request(),fixture.Actor);
        var restarted=new ExcelPageExportService(new ExportOptions {RootDirectory=fixture.Root},fixture.Clock);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>restarted.OpenAsync(ticket,fixture.Actor));
        Assert.Single(Directory.GetFiles(fixture.Root));fixture.Clock.Advance(TimeSpan.FromMinutes(16));
        Assert.Equal(1,await restarted.CleanupExpiredAsync());Assert.Empty(Directory.GetFiles(fixture.Root));
    }
    [Fact]
    public async Task Worksheet_title_cannot_end_with_apostrophe_after_truncation()
    {
        using var fixture=new ExportFixture();var ticket=await fixture.Service.CreateAsync(Request(new string('a',30)+"'B"),fixture.Actor);var content=await fixture.Service.OpenAsync(ticket,fixture.Actor);
        await using(content.Stream){using var workbook=new XLWorkbook(content.Stream);Assert.False(workbook.Worksheet(1).Name.EndsWith('\''));Assert.Equal("Metarill",workbook.Properties.Author);}
    }
    private static PageExportRequest Request(string title="虚构导出")=>new("page1",title,["名称","数量"],[["虚构客户",2]]);
}

internal sealed class ExportFixture : IDisposable
{
    private readonly string _directory=Path.Combine(Directory.GetCurrentDirectory(),".local","export-test-"+Guid.NewGuid().ToString("N"));
    public string Root=>Path.Combine(_directory,"objects");
    public FakeClock Clock {get;}=new();
    public RuntimeActor Actor {get;}=new(1,"虚构测试账号",false,"project1","admin1");
    public ExcelPageExportService Service {get;}
    public ExportFixture(ExportOptions? options=null)=>Service=new((options??new ExportOptions()) with {RootDirectory=Root},Clock);
    public void Dispose()
    {
        var local=Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(),".local"))+Path.DirectorySeparatorChar;var target=Path.GetFullPath(_directory);
        if(target.StartsWith(local,StringComparison.OrdinalIgnoreCase)&&Directory.Exists(target))Directory.Delete(target,true);
    }
}
internal sealed class FakeClock : TimeProvider
{
    private DateTimeOffset _now=DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow()=>_now;
    public void Advance(TimeSpan amount)=>_now=_now.Add(amount);
}
