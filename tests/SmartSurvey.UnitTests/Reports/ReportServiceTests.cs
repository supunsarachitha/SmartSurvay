using Microsoft.EntityFrameworkCore;
using SmartSurvey.Application.Audit;
using SmartSurvey.Application.Common;
using SmartSurvey.Application.Exports;
using SmartSurvey.Application.Reports;
using SmartSurvey.Domain.Enums;
using SmartSurvey.Infrastructure.Exports;
using SmartSurvey.UnitTests.TestSupport;
using static SmartSurvey.UnitTests.Reports.ReportTestHarness;

namespace SmartSurvey.UnitTests.Reports;

public sealed class ReportServiceTests : IAsyncLifetime
{
    private ReportTestHarness _h = null!;
    private SampleSurvey _s = null!;

    public async Task InitializeAsync()
    {
        _h = await CreateAsync();
        _s = await _h.SeedSurveyWithResponsesAsync();
    }

    public async Task DisposeAsync() => await _h.DisposeAsync();

    [Fact]
    public async Task Create_saves_a_normalised_copy_and_writes_an_audit_entry()
    {
        var dto = Definition(_s, Widget(WidgetType.PieChart, _s.Enjoy.Id, title: "  Enjoyment  "), Widget(WidgetType.SummaryStats));
        dto.Widgets[0].Order = 7;
        dto.Widgets[1].Order = 2;
        dto.Name = "  Monthly review ";

        var created = await _h.Service.CreateAsync(dto);

        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal("Monthly review", created.Name);
        Assert.Equal([WidgetType.SummaryStats, WidgetType.PieChart], created.Widgets.Select(w => w.Type));
        Assert.Equal([0, 1], created.Widgets.Select(w => w.Order));
        Assert.Equal("Enjoyment", created.Widgets[1].Title);
        Assert.Equal("  Monthly review ", dto.Name); // the caller's object is untouched

        var loaded = await _h.Service.GetAsync(created.Id);
        Assert.Equal(created.Widgets.Select(w => w.Id), loaded.Widgets.Select(w => w.Id));
        Assert.Contains(_h.Audit.Entries, e => e.Action == AuditActions.ReportCreated && e.EntityId == created.Id.ToString());
    }

    [Fact]
    public async Task Create_rejects_a_missing_name()
    {
        var dto = Definition(_s, Widget(WidgetType.SummaryStats));
        dto.Name = " ";

        var ex = await Assert.ThrowsAsync<AppValidationException>(() => _h.Service.CreateAsync(dto));

        Assert.Contains(nameof(ReportDefinitionDto.Name), ex.Errors.Keys);
    }

    [Fact]
    public async Task Create_rejects_questions_of_another_survey()
    {
        var other = await _h.Responses.SeedSurveyAsync(title: "Other survey");
        var dto = Definition(_s, Widget(WidgetType.BarChart, other.Rating.Id));

        var ex = await Assert.ThrowsAsync<AppValidationException>(() => _h.Service.CreateAsync(dto));

        Assert.Contains("Widgets[0].QuestionId", ex.Errors.Keys);
    }

    [Fact]
    public async Task Create_rejects_an_unknown_survey()
    {
        var dto = Definition(_s, Widget(WidgetType.SummaryStats));
        dto.SurveyId = Guid.NewGuid();

        var ex = await Assert.ThrowsAsync<AppValidationException>(() => _h.Service.CreateAsync(dto));

        Assert.Contains(nameof(ReportDefinitionDto.SurveyId), ex.Errors.Keys);
    }

    [Fact]
    public async Task Update_keeps_matching_widgets_adds_new_ones_and_removes_dropped_ones()
    {
        var created = await _h.Service.CreateAsync(Definition(_s,
            Widget(WidgetType.SummaryStats),
            Widget(WidgetType.BarChart, _s.Rating.Id),
            Widget(WidgetType.PieChart, _s.Enjoy.Id)));
        var kept = created.Widgets[1];
        var dropped = created.Widgets[2];

        var edit = await _h.Service.GetAsync(created.Id);
        edit.Name = "Renamed";
        edit.Widgets.RemoveAll(w => w.Id == dropped.Id);
        edit.Widgets.Single(w => w.Id == kept.Id).Title = "Ratings";
        edit.Widgets.Add(Widget(WidgetType.TextResponses, _s.WhyNot.Id));
        edit.Widgets[^1].Order = 10;

        var updated = await _h.Service.UpdateAsync(created.Id, edit);

        Assert.Equal("Renamed", updated.Name);
        Assert.Equal([WidgetType.SummaryStats, WidgetType.BarChart, WidgetType.TextResponses], updated.Widgets.Select(w => w.Type));
        Assert.Equal("Ratings", updated.Widgets.Single(w => w.Id == kept.Id).Title);
        Assert.DoesNotContain(updated.Widgets, w => w.Id == dropped.Id);

        await using var db = _h.Db.CreateContext();
        Assert.Equal(3, await db.ReportWidgets.CountAsync(w => w.ReportId == created.Id));
        Assert.Contains(_h.Audit.Entries, e => e.Action == AuditActions.ReportUpdated);
    }

    [Fact]
    public async Task Update_gives_foreign_widget_ids_a_fresh_id()
    {
        var first = await _h.Service.CreateAsync(Definition(_s, Widget(WidgetType.SummaryStats)));
        var second = await _h.Service.CreateAsync(Definition(_s, Widget(WidgetType.LineChart)));

        var edit = await _h.Service.GetAsync(second.Id);
        edit.Widgets.Add(new ReportWidgetDto { Id = first.Widgets[0].Id, Type = WidgetType.SummaryStats, Order = 1 });
        var updated = await _h.Service.UpdateAsync(second.Id, edit);

        Assert.Equal(2, updated.Widgets.Count);
        Assert.DoesNotContain(updated.Widgets, w => w.Id == first.Widgets[0].Id);
        Assert.Single((await _h.Service.GetAsync(first.Id)).Widgets);
    }

    [Fact]
    public async Task Update_and_delete_of_an_unknown_report_throw_not_found()
    {
        var dto = Definition(_s, Widget(WidgetType.SummaryStats));

        await Assert.ThrowsAsync<NotFoundException>(() => _h.Service.UpdateAsync(Guid.NewGuid(), dto));
        await Assert.ThrowsAsync<NotFoundException>(() => _h.Service.DeleteAsync(Guid.NewGuid()));
        await Assert.ThrowsAsync<NotFoundException>(() => _h.Service.GetAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Delete_removes_the_report_and_its_widgets()
    {
        var created = await _h.Service.CreateAsync(Definition(_s, Widget(WidgetType.SummaryStats), Widget(WidgetType.LineChart)));

        await _h.Service.DeleteAsync(created.Id);

        await using var db = _h.Db.CreateContext();
        Assert.False(await db.Reports.AnyAsync(r => r.Id == created.Id));
        Assert.False(await db.ReportWidgets.AnyAsync(w => w.ReportId == created.Id));
        Assert.Contains(_h.Audit.Entries, e => e.Action == AuditActions.ReportDeleted);
    }

    [Fact]
    public async Task Duplicate_creates_an_independent_copy()
    {
        var created = await _h.Service.CreateAsync(Definition(_s, Widget(WidgetType.SummaryStats), Widget(WidgetType.PieChart, _s.Enjoy.Id)));

        var copy = await _h.Service.DuplicateAsync(created.Id);

        Assert.NotEqual(created.Id, copy.Id);
        Assert.Equal("Copy of Feedback report", copy.Name);
        Assert.Equal(created.Widgets.Select(w => w.Type), copy.Widgets.Select(w => w.Type));
        Assert.Empty(copy.Widgets.Select(w => w.Id).Intersect(created.Widgets.Select(w => w.Id)));
    }

    [Fact]
    public async Task List_filters_by_survey_and_search_and_pages_newest_first()
    {
        var other = await _h.Responses.SeedSurveyAsync(title: "Other survey");
        var a = Definition(_s, Widget(WidgetType.SummaryStats));
        a.Name = "Alpha overview";
        await _h.Service.CreateAsync(a);
        _h.Db.Time.Advance(TimeSpan.FromMinutes(1));
        var b = Definition(_s, Widget(WidgetType.SummaryStats));
        b.Name = "Beta details";
        b.Description = "Weekly OVERVIEW for the team";
        await _h.Service.CreateAsync(b);
        var c = Definition(other, Widget(WidgetType.SummaryStats));
        c.Name = "Gamma";
        await _h.Service.CreateAsync(c);

        var bySurvey = await _h.Service.ListAsync(new ReportQuery { SurveyId = _s.Definition.Id });
        var bySearch = await _h.Service.ListAsync(new ReportQuery { Search = "overview" });
        var paged = await _h.Service.ListAsync(new ReportQuery { PageSize = 2, Page = 2 });

        Assert.Equal(["Beta details", "Alpha overview"], bySurvey.Items.Select(r => r.Name));
        Assert.All(bySurvey.Items, r => Assert.Equal("Customer feedback", r.SurveyTitle));
        Assert.Equal(1, bySurvey.Items[0].WidgetCount);
        Assert.Equal(2, bySearch.TotalCount);
        Assert.Equal(3, paged.TotalCount);
        Assert.Single(paged.Items);
    }

    [Fact]
    public async Task BuildDefault_adds_summary_timeline_and_one_widget_per_question()
    {
        var report = await _h.Service.BuildDefaultAsync(_s.Definition.Id);

        Assert.Equal(Guid.Empty, report.Id);
        Assert.Equal(
            [
                WidgetType.SummaryStats, WidgetType.LineChart,
                WidgetType.PieChart,            // Q1 radio, 2 options
                WidgetType.HorizontalBarChart,  // Q2 checkbox
                WidgetType.TextResponses,       // Q3 long text
                WidgetType.BarChart,            // Q4 rating
                WidgetType.TextResponses,       // Q5 e-mail
                WidgetType.QuestionTable,       // Q6 number
                WidgetType.PieChart,            // Q7 dropdown, 3 options
            ],
            report.Widgets.Select(w => w.Type));
        Assert.Equal("Q1. Did you enjoy the product?", report.Widgets[2].Title);

        // The suggestion is a valid definition that can be saved as is.
        var saved = await _h.Service.CreateAsync(report);
        Assert.Equal(9, saved.Widgets.Count);
    }

    [Fact]
    public async Task BuildDefault_of_an_unknown_survey_throws_not_found() =>
        await Assert.ThrowsAsync<NotFoundException>(() => _h.Service.BuildDefaultAsync(Guid.NewGuid()));

    [Fact]
    public async Task Run_stamps_the_branded_product_name()
    {
        var created = await _h.Service.CreateAsync(Definition(_s, Widget(WidgetType.SummaryStats)));

        var result = await _h.Service.RunAsync(created.Id);

        Assert.Equal(created.Id, result.ReportId);
        Assert.Equal(ProductName, result.ProductName);
        Assert.Equal(4, result.TotalResponses);
    }

    [Fact]
    public async Task Preview_runs_unsaved_definitions_with_half_configured_widgets()
    {
        var dto = Definition(_s, Widget(WidgetType.SummaryStats), Widget(WidgetType.BarChart));

        var result = await _h.Service.PreviewAsync(dto);

        Assert.Null(result.ReportId);
        Assert.Null(result.Widgets[0].Error);
        Assert.Equal(ReportEngine.MissingQuestionError, result.Widgets[1].Error);
    }

    [Fact]
    public async Task Preview_requires_a_survey()
    {
        var dto = Definition(_s, Widget(WidgetType.SummaryStats));
        dto.SurveyId = Guid.Empty;

        var ex = await Assert.ThrowsAsync<AppValidationException>(() => _h.Service.PreviewAsync(dto));

        Assert.Contains(nameof(ReportDefinitionDto.SurveyId), ex.Errors.Keys);
    }

    [Theory]
    [InlineData(ExportFormat.Pdf, "application/pdf", "%PDF")]
    [InlineData(ExportFormat.Csv, "text/csv", "﻿Report,")]
    [InlineData(ExportFormat.Txt, "text/plain", "Feedback report")]
    [InlineData(ExportFormat.Xlsx, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "PK")]
    [InlineData(ExportFormat.Json, "application/json", "{")]
    public async Task Export_returns_a_named_file_in_the_requested_format(ExportFormat format, string contentType, string prefix)
    {
        var created = await _h.Service.CreateAsync(Definition(_s, Widget(WidgetType.SummaryStats), Widget(WidgetType.PieChart, _s.Enjoy.Id)));

        var file = await _h.Service.ExportAsync(created.Id, format);

        Assert.Equal(contentType, file.ContentType);
        Assert.Equal($"feedback-report-{_h.Now:yyyyMMdd-HHmm}.{format.FileExtension()}", file.FileName);
        Assert.StartsWith(prefix, System.Text.Encoding.UTF8.GetString(file.Content, 0, Math.Min(file.Content.Length, 64)));
        Assert.Contains(_h.Audit.Entries, e => e.Action == AuditActions.ReportExported && e.Details!.Contains(format.ToString().ToUpperInvariant()));
    }

    [Fact]
    public async Task Export_in_a_format_without_exporter_is_a_business_rule_violation()
    {
        await using var h = await CreateAsync(exporters: [new CsvReportExporter()]);
        var s = await h.SeedSurveyWithResponsesAsync();
        var created = await h.Service.CreateAsync(Definition(s, Widget(WidgetType.SummaryStats)));

        await Assert.ThrowsAsync<BusinessRuleException>(() => h.Service.ExportAsync(created.Id, ExportFormat.Pdf));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Every_operation_is_admin_only(bool anonymous)
    {
        var created = await _h.Service.CreateAsync(Definition(_s, Widget(WidgetType.SummaryStats)));
        if (anonymous)
        {
            _h.User.ActAsAnonymous();
        }
        else
        {
            _h.User.ActAsRespondent();
        }

        var dto = Definition(_s, Widget(WidgetType.SummaryStats));
        await Assert.ThrowsAsync<ForbiddenException>(() => _h.Service.ListAsync(new ReportQuery()));
        await Assert.ThrowsAsync<ForbiddenException>(() => _h.Service.GetAsync(created.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => _h.Service.CreateAsync(dto));
        await Assert.ThrowsAsync<ForbiddenException>(() => _h.Service.UpdateAsync(created.Id, dto));
        await Assert.ThrowsAsync<ForbiddenException>(() => _h.Service.DeleteAsync(created.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => _h.Service.DuplicateAsync(created.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => _h.Service.BuildDefaultAsync(_s.Definition.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => _h.Service.RunAsync(created.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => _h.Service.PreviewAsync(dto));
        await Assert.ThrowsAsync<ForbiddenException>(() => _h.Service.ExportAsync(created.Id, ExportFormat.Csv));
    }
}
