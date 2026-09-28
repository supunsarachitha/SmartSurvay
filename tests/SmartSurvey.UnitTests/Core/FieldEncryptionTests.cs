using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartSurvey.Domain.Entities;
using SmartSurvey.Domain.Enums;
using SmartSurvey.Infrastructure.Persistence;
using SmartSurvey.Infrastructure.Persistence.Encryption;
using SmartSurvey.UnitTests.TestSupport;

namespace SmartSurvey.UnitTests.Core;

/// <summary>Encryption at rest of respondents' free text, "Other" texts and browser details.</summary>
public sealed class FieldEncryptionTests
{
    private static DataProtectionFieldProtector NewProtector() =>
        new(new EphemeralDataProtectionProvider(), NullLogger<DataProtectionFieldProtector>.Instance);

    /// <summary>A survey with one text and one choice question, and one response answering both.</summary>
    private static async Task<(Answer Text, AnswerSelection Other, SurveyResponse Response)> SeedAsync(SqliteTestDatabase db)
    {
        var survey = new Survey { Title = "Encrypted", Slug = "encrypted" };
        var section = new SurveySection { SurveyId = survey.Id, Title = "Page" };
        var text = new Question { SurveyId = survey.Id, SectionId = section.Id, Type = QuestionType.LongText, Text = "Why?", Code = "Q1" };
        var choice = new Question { SurveyId = survey.Id, SectionId = section.Id, Type = QuestionType.Radio, Text = "Pick", Code = "Q2", Order = 1 };
        var other = new QuestionOption { QuestionId = choice.Id, Text = "Other", AllowsFreeText = true };
        choice.Options.Add(other);
        survey.Sections.Add(section);
        survey.Questions.AddRange([text, choice]);

        var response = new SurveyResponse { SurveyId = survey.Id, Status = ResponseStatus.Completed, StartedAt = db.UtcNow, UserAgent = "Mozilla/5.0 (Secret Browser)" };
        var textAnswer = new Answer { ResponseId = response.Id, QuestionId = text.Id, TextValue = "My private opinion" };
        var choiceAnswer = new Answer { ResponseId = response.Id, QuestionId = choice.Id };
        var selection = new AnswerSelection { AnswerId = choiceAnswer.Id, OptionId = other.Id, FreeText = "a secret reason" };
        choiceAnswer.Selections.Add(selection);
        response.Answers.AddRange([textAnswer, choiceAnswer]);

        await db.SeedAsync(survey, response);
        return (textAnswer, selection, response);
    }

    private static async Task<string?> RawAsync(SqliteTestDatabase db, string table, string column, Guid id)
    {
        await using var context = db.CreateContext();
#pragma warning disable EF1002 // test-only, constant identifiers
        return await context.Database.SqlQueryRaw<string?>($"SELECT \"{column}\" AS \"Value\" FROM \"{table}\" WHERE \"Id\" = {{0}}", id).SingleAsync();
#pragma warning restore EF1002
    }

    [Fact]
    public async Task Sensitive_columns_are_stored_encrypted_and_read_back_in_plain_text()
    {
        await using var db = new SqliteTestDatabase(NewProtector());
        var (text, other, response) = await SeedAsync(db);

        var rawText = await RawAsync(db, "Answers", "TextValue", text.Id);
        var rawOther = await RawAsync(db, "AnswerSelections", "FreeText", other.Id);
        var rawAgent = await RawAsync(db, "Responses", "UserAgent", response.Id);
        Assert.All([rawText, rawOther, rawAgent], raw =>
        {
            Assert.StartsWith(DataProtectionFieldProtector.Prefix, raw);
            Assert.DoesNotContain("secret", raw!, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("private", raw!, StringComparison.OrdinalIgnoreCase);
        });

        await using var context = db.CreateContext();
        Assert.Equal("My private opinion", (await context.Answers.SingleAsync(a => a.Id == text.Id)).TextValue);
        Assert.Equal(["a secret reason"], await context.AnswerSelections.Select(s => s.FreeText).ToListAsync()); // projections decrypt too
        Assert.Equal("Mozilla/5.0 (Secret Browser)", (await context.Responses.SingleAsync()).UserAgent);
        Assert.Equal(1, await context.Answers.CountAsync(a => a.TextValue != null)); // "is answered" still works in SQL
    }

    [Fact]
    public async Task Older_plain_text_stays_readable_and_is_encrypted_once_at_start_up()
    {
        await using var db = new SqliteTestDatabase(NewProtector());
        var (text, _, _) = await SeedAsync(db);
        await using (var context = db.CreateContext())
        {
            await context.Database.ExecuteSqlRawAsync("UPDATE \"Answers\" SET \"TextValue\" = 'written before encryption' WHERE \"Id\" = {0}", text.Id);
            Assert.Equal("written before encryption", (await context.Answers.SingleAsync(a => a.Id == text.Id)).TextValue);
        }

        await using (var context = db.CreateContext())
        {
            var migrator = new FieldEncryptionMigrator(context, Options.Create(new FieldEncryptionOptions()), NullLogger<FieldEncryptionMigrator>.Instance);
            Assert.Equal(1, await migrator.EncryptExistingAsync());
            Assert.Equal(0, await migrator.EncryptExistingAsync()); // idempotent
        }

        Assert.StartsWith(DataProtectionFieldProtector.Prefix, await RawAsync(db, "Answers", "TextValue", text.Id));
        await using var check = db.CreateContext();
        Assert.Equal("written before encryption", (await check.Answers.SingleAsync(a => a.Id == text.Id)).TextValue);
    }

    [Fact]
    public void Values_without_their_key_become_a_placeholder_instead_of_an_error()
    {
        var writer = NewProtector();
        var stored = writer.Protect("confidential");

        Assert.Equal("confidential", writer.Unprotect(stored));
        Assert.Equal(DataProtectionFieldProtector.UnreadablePlaceholder, NewProtector().Unprotect(stored)); // other key ring
        Assert.Equal("legacy", NewProtector().Unprotect("legacy"));
        Assert.NotEqual(writer.Protect("same"), writer.Protect("same")); // randomised encryption
    }
}
