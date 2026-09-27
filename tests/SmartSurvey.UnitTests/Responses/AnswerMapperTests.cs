using SmartSurvey.Application.Responses;
using SmartSurvey.Domain.Entities;
using SmartSurvey.Domain.Enums;

namespace SmartSurvey.UnitTests.Responses;

/// <summary>Tests of <see cref="AnswerMapper"/> (entity ⇄ input DTO).</summary>
public class AnswerMapperTests
{
    [Fact]
    public void Stored_answer_maps_to_an_input_dto_with_selections()
    {
        var questionId = Guid.NewGuid();
        var optionId = Guid.NewGuid();
        var answer = new Answer
        {
            QuestionId = questionId,
            TextValue = "t",
            NumberValue = 2.5,
            DateValue = new DateOnly(2026, 1, 2),
            Selections = [new AnswerSelection { OptionId = optionId, FreeText = "other" }],
        };

        var dto = AnswerMapper.ToInputDto(answer);

        Assert.Equal(questionId, dto.QuestionId);
        Assert.Equal("t", dto.Text);
        Assert.Equal(2.5, dto.Number);
        Assert.Equal(new DateOnly(2026, 1, 2), dto.Date);
        var selection = Assert.Single(dto.Selections);
        Assert.Equal(optionId, selection.OptionId);
        Assert.Equal("other", selection.FreeText);
    }

    [Theory]
    [InlineData(QuestionType.ShortText, true, false, false)]
    [InlineData(QuestionType.Email, true, false, false)]
    [InlineData(QuestionType.Rating, false, true, false)]
    [InlineData(QuestionType.Scale, false, true, false)]
    [InlineData(QuestionType.Date, false, false, true)]
    [InlineData(QuestionType.Checkbox, false, false, false)]
    public void Only_the_column_matching_the_type_is_written(QuestionType type, bool text, bool number, bool date)
    {
        var input = new AnswerInputDto
        {
            QuestionId = Guid.NewGuid(),
            Text = "t",
            Number = 3,
            Date = new DateOnly(2026, 5, 6),
        };

        var entity = AnswerMapper.ToEntity(input, type);

        Assert.Equal(input.QuestionId, entity.QuestionId);
        Assert.Equal(text ? input.Text : null, entity.TextValue);
        Assert.Equal(number ? input.Number : null, entity.NumberValue);
        Assert.Equal(date ? input.Date : null, entity.DateValue);
    }

    [Fact]
    public void Choice_answers_become_selections_linked_to_the_answer()
    {
        var other = Guid.NewGuid();
        var input = new AnswerInputDto
        {
            QuestionId = Guid.NewGuid(),
            Selections = [new SelectionInputDto { OptionId = other, FreeText = "teal" }],
        };

        var entity = AnswerMapper.ToEntity(input, QuestionType.Radio);

        var selection = Assert.Single(entity.Selections);
        Assert.Equal(other, selection.OptionId);
        Assert.Equal("teal", selection.FreeText);
        Assert.Equal(entity.Id, selection.AnswerId);
    }

    [Fact]
    public void Non_choice_answers_never_get_selections()
    {
        var input = new AnswerInputDto { Number = 1, Selections = [new SelectionInputDto { OptionId = Guid.NewGuid() }] };

        Assert.Empty(AnswerMapper.ToEntity(input, QuestionType.Number).Selections);
    }

    [Fact]
    public void Copying_values_clears_columns_of_other_types()
    {
        var target = new Answer { TextValue = "old", NumberValue = 1, DateValue = new DateOnly(2020, 1, 1) };

        AnswerMapper.CopyValues(new AnswerInputDto { Number = 9 }, QuestionType.Number, target);

        Assert.Null(target.TextValue);
        Assert.Equal(9, target.NumberValue);
        Assert.Null(target.DateValue);
    }
}
