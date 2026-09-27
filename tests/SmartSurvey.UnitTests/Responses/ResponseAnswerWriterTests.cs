using SmartSurvey.Application.Responses;
using SmartSurvey.Application.Surveys;
using SmartSurvey.UnitTests.TestSupport;
using static SmartSurvey.UnitTests.Responses.ResponseTestData;

namespace SmartSurvey.UnitTests.Responses;

/// <summary>Tests of <see cref="ResponseAnswerWriter.Sanitize"/>.</summary>
public class ResponseAnswerWriterTests
{
    private static Dictionary<Guid, QuestionDto> Questions(SampleSurvey s) =>
        s.Definition.AllQuestions().ToDictionary(q => q.Id);

    [Fact]
    public void Answers_to_unknown_questions_and_null_entries_are_ignored()
    {
        var s = SampleSurveys.CustomerFeedback();

        var clean = ResponseAnswerWriter.Sanitize(Questions(s), [
            null,
            new AnswerInputDto { QuestionId = Guid.NewGuid(), Text = "stranger" },
            Number(s.Rating, 4),
        ]);

        var answer = Assert.Single(clean);
        Assert.Equal(s.Rating.Id, answer.Key);
        Assert.Equal(4, answer.Value.Number);
    }

    [Fact]
    public void Last_answer_to_a_question_wins()
    {
        var s = SampleSurveys.CustomerFeedback();

        var clean = ResponseAnswerWriter.Sanitize(Questions(s), [Number(s.Rating, 1), Number(s.Rating, 5)]);

        Assert.Equal(5, clean[s.Rating.Id].Number);
    }

    [Fact]
    public void Trailing_empty_answer_clears_the_question()
    {
        var s = SampleSurveys.CustomerFeedback();

        var clean = ResponseAnswerWriter.Sanitize(Questions(s), [Text(s.Email, "a@b.co"), Text(s.Email, "  ")]);

        Assert.Empty(clean);
    }

    [Fact]
    public void Each_answer_is_cleaned_for_its_question_type()
    {
        var s = SampleSurveys.CustomerFeedback();
        var dirty = Choice(s.Enjoy, s.No, s.Yes);
        dirty.Text = "irrelevant";

        var clean = ResponseAnswerWriter.Sanitize(Questions(s), [dirty]);

        var answer = clean[s.Enjoy.Id];
        Assert.Null(answer.Text);
        Assert.Equal(s.No.Id, Assert.Single(answer.Selections).OptionId);
    }
}
