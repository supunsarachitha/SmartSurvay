using SmartSurvey.Application.Responses;
using SmartSurvey.Application.Surveys;
using SmartSurvey.Domain.Enums;
using SmartSurvey.UnitTests.TestSupport;
using SmartSurvey.Web.Components.Runner;

namespace SmartSurvey.UnitTests.Ui;

/// <summary>Tests of <see cref="SurveyRunState"/>, the survey runner's page/logic/validation state machine.</summary>
public sealed class SurveyRunStateTests
{
    private readonly SampleSurvey _s = SampleSurveys.CustomerFeedback();

    [Fact]
    public void New_pass_starts_on_the_first_page_with_logic_applied()
    {
        var run = new SurveyRunState(_s.Definition);

        Assert.Equal([_s.Page1.Id, _s.Page2.Id], run.Pages.Select(p => p.Id));
        Assert.Equal(0, run.PageIndex);
        Assert.True(run.IsFirstPage);
        Assert.False(run.IsLastPage);
        Assert.Equal([_s.Enjoy.Id, _s.Features.Id], run.CurrentQuestions.Select(q => q.Id)); // "Why not?" hidden
        Assert.False(run.IsDirty);
    }

    [Fact]
    public void Answering_reveals_questions_and_renumbers_the_path()
    {
        var run = new SurveyRunState(_s.Definition);
        Assert.Equal(3, run.NumberOf(_s.Rating.Id));

        run.Answer(_s.Enjoy.Id).SelectSingle(_s.No.Id);
        run.AnswerChanged(_s.Enjoy.Id);

        Assert.Equal([_s.Enjoy.Id, _s.Features.Id, _s.WhyNot.Id], run.CurrentQuestions.Select(q => q.Id));
        Assert.Equal(3, run.NumberOf(_s.WhyNot.Id));
        Assert.Equal(4, run.NumberOf(_s.Rating.Id));
        Assert.True(run.IsDirty);
    }

    [Fact]
    public void Question_numbers_are_hidden_when_the_survey_turns_them_off()
    {
        _s.Definition.ShowQuestionNumbers = false;

        Assert.Null(new SurveyRunState(_s.Definition).NumberOf(_s.Enjoy.Id));
    }

    [Fact]
    public void Next_is_blocked_by_required_questions_of_the_current_page_only()
    {
        var run = new SurveyRunState(_s.Definition);

        Assert.False(run.TryGoNext());

        Assert.Equal(0, run.PageIndex);
        Assert.Equal(["This question is required."], run.ErrorsFor(_s.Enjoy.Id));
        Assert.False(run.Errors.ContainsKey(_s.Rating.Id)); // page 2 is not validated yet
        Assert.Equal(_s.Enjoy.Id, run.FirstErrorQuestionId);
    }

    [Fact]
    public void Fixing_an_answer_clears_its_error_live()
    {
        var run = new SurveyRunState(_s.Definition);
        run.TryGoNext();

        run.Answer(_s.Enjoy.Id).SelectSingle(_s.Yes.Id);
        run.AnswerChanged(_s.Enjoy.Id);

        Assert.Empty(run.Errors);
    }

    [Fact]
    public void Errors_of_questions_hidden_by_logic_are_dropped()
    {
        var run = new SurveyRunState(_s.Definition);
        run.Answer(_s.Enjoy.Id).SelectSingle(_s.No.Id);
        run.AnswerChanged(_s.Enjoy.Id);
        Assert.False(run.TryGoNext()); // "Why not?" is required
        Assert.True(run.Errors.ContainsKey(_s.WhyNot.Id));

        run.Answer(_s.Enjoy.Id).SelectSingle(_s.Yes.Id);
        run.AnswerChanged(_s.Enjoy.Id);

        Assert.Empty(run.Errors);
        Assert.True(run.TryGoNext());
    }

    [Fact]
    public void Next_and_back_walk_the_visible_pages()
    {
        var run = new SurveyRunState(_s.Definition);
        run.Answer(_s.Enjoy.Id).SelectSingle(_s.Yes.Id);

        Assert.True(run.TryGoNext());
        Assert.Equal(_s.Page2.Id, run.CurrentPage?.Id);
        Assert.True(run.IsLastPage);

        Assert.True(run.GoBack());
        Assert.Equal(_s.Page1.Id, run.CurrentPage?.Id);
        Assert.False(run.GoBack());
    }

    [Fact]
    public void Pages_hidden_by_logic_are_skipped()
    {
        var page3 = new SectionDto { Title = "Details", Order = 2, Questions = [new QuestionDto { Type = QuestionType.ShortText, Text = "Details" }] };
        _s.Definition.Sections.Add(page3);
        _s.Definition.LogicRules.Add(new LogicRuleDto
        {
            TargetSectionId = _s.Page2.Id,
            Action = LogicAction.Hide,
            Conditions = [new LogicConditionDto { SourceQuestionId = _s.Enjoy.Id, Operator = ConditionOperator.Equals, OptionId = _s.Yes.Id }],
        });
        var run = new SurveyRunState(_s.Definition);

        run.Answer(_s.Enjoy.Id).SelectSingle(_s.Yes.Id);
        run.AnswerChanged(_s.Enjoy.Id);

        Assert.Equal([_s.Page1.Id, page3.Id], run.Pages.Select(p => p.Id));
        Assert.True(run.TryGoNext());
        Assert.Equal(page3.Id, run.CurrentPage?.Id);
    }

    [Fact]
    public void Pages_whose_questions_are_all_hidden_are_skipped_but_empty_info_pages_are_kept()
    {
        var info = new SectionDto { Title = "Before you start", Description = "Read this.", Order = 0 };
        _s.Page1.Order = 1;
        _s.Page2.Order = 2;
        _s.Definition.Sections.Insert(0, info);
        _s.Page2.Questions.RemoveRange(1, 3); // leave only the rating question on page 2
        _s.Definition.LogicRules.Add(new LogicRuleDto
        {
            TargetQuestionId = _s.Rating.Id,
            Action = LogicAction.Show,
            Conditions = [new LogicConditionDto { SourceQuestionId = _s.Enjoy.Id, Operator = ConditionOperator.Equals, OptionId = _s.Yes.Id }],
        });

        var run = new SurveyRunState(_s.Definition);

        Assert.Equal([info.Id, _s.Page1.Id], run.Pages.Select(p => p.Id));
    }

    [Fact]
    public void Validate_all_reports_errors_on_the_current_page()
    {
        var run = new SurveyRunState(_s.Definition);
        run.Answer(_s.Enjoy.Id).SelectSingle(_s.Yes.Id);
        Assert.True(run.TryGoNext());

        Assert.False(run.ValidateAll()); // rating on page 2 is required

        Assert.Equal(_s.Page2.Id, run.CurrentPage?.Id);
        Assert.Equal([_s.Rating.Id], run.Errors.Keys);
    }

    [Fact]
    public void Validate_all_moves_back_to_an_earlier_page_when_needed()
    {
        var run = new SurveyRunState(_s.Definition, sectionIndex: 1);
        Assert.Equal(_s.Page2.Id, run.CurrentPage?.Id);
        run.Answer(_s.Rating.Id).Number = 4;

        Assert.False(run.ValidateAll());

        Assert.Equal(_s.Page1.Id, run.CurrentPage?.Id);
        Assert.Equal(_s.Enjoy.Id, run.FirstErrorQuestionId);
    }

    [Fact]
    public void Server_errors_are_mapped_to_questions_and_other_messages_returned()
    {
        var run = new SurveyRunState(_s.Definition, sectionIndex: 1);

        var general = run.ApplyServerErrors(new Dictionary<string, string[]>
        {
            [_s.Enjoy.Id.ToString()] = ["This question is required."],
            [_s.WhyNot.Id.ToString()] = ["Hidden question error."],
            ["survey"] = ["The survey changed."],
        });

        Assert.Equal([_s.Enjoy.Id], run.Errors.Keys);
        Assert.Equal(_s.Page1.Id, run.CurrentPage?.Id);
        Assert.Equal(["Hidden question error.", "The survey changed."], general);
    }

    [Fact]
    public void Resuming_restores_answers_and_page()
    {
        var saved = new List<AnswerInputDto>
        {
            new() { QuestionId = _s.Enjoy.Id, Selections = [new SelectionInputDto { OptionId = _s.No.Id }] },
            new() { QuestionId = _s.WhyNot.Id, Text = "Too slow" },
        };

        var run = new SurveyRunState(_s.Definition, saved, sectionIndex: 1);

        Assert.Equal(_s.Page2.Id, run.CurrentPage?.Id);
        Assert.Equal("Too slow", run.Answer(_s.WhyNot.Id).Text);
        Assert.Equal(2, run.AnsweredCount);
        Assert.False(run.IsDirty);

        run.Answer(_s.WhyNot.Id).Text = "changed";
        Assert.Equal("Too slow", saved[1].Text); // the draft is copied, not edited in place
    }

    [Fact]
    public void Resuming_at_a_hidden_page_falls_back_to_the_nearest_earlier_page()
    {
        _s.Definition.LogicRules.Add(new LogicRuleDto
        {
            TargetSectionId = _s.Page2.Id,
            Action = LogicAction.Show,
            Conditions = [new LogicConditionDto { SourceQuestionId = _s.Enjoy.Id, Operator = ConditionOperator.IsAnswered }],
        });

        var run = new SurveyRunState(_s.Definition, sectionIndex: 5);

        Assert.Equal(_s.Page1.Id, run.CurrentPage?.Id);
    }

    [Fact]
    public void Answers_to_unknown_questions_are_ignored_when_resuming()
    {
        var run = new SurveyRunState(_s.Definition, [new AnswerInputDto { QuestionId = Guid.NewGuid(), Text = "x" }]);

        Assert.Equal(0, run.AnsweredCount);
    }

    [Fact]
    public void Progress_counts_answered_visible_questions()
    {
        var run = new SurveyRunState(_s.Definition);
        Assert.Equal(6, run.VisibleQuestionCount);
        Assert.Equal(0, run.ProgressPercent);

        run.Answer(_s.Enjoy.Id).SelectSingle(_s.No.Id); // reveals "Why not?" → 7 visible
        run.AnswerChanged(_s.Enjoy.Id);
        run.Answer(_s.Rating.Id).Number = 5;
        run.AnswerChanged(_s.Rating.Id);

        Assert.Equal(7, run.VisibleQuestionCount);
        Assert.Equal(2, run.AnsweredCount);
        Assert.Equal(29, run.ProgressPercent);
    }

    [Fact]
    public void Request_contains_answered_questions_and_the_section_index()
    {
        var run = new SurveyRunState(_s.Definition);
        var draftId = Guid.NewGuid();
        run.Answer(_s.Enjoy.Id).SelectSingle(_s.Yes.Id);
        run.Answer(_s.Email.Id).Text = "   ";
        run.TryGoNext();
        run.Answer(_s.Age.Id).Number = 42;

        var request = run.ToRequest(draftId, "UA");

        Assert.Equal(draftId, request.ResponseId);
        Assert.Equal(1, request.CurrentSectionIndex);
        Assert.Equal("UA", request.UserAgent);
        Assert.Equal([_s.Enjoy.Id, _s.Age.Id], request.Answers.Select(a => a.QuestionId));

        request.Answers[1].Number = 1;
        Assert.Equal(42, run.Answer(_s.Age.Id).Number); // copies, not the live objects
    }

    [Fact]
    public void Reset_clears_answers_and_returns_to_the_first_page()
    {
        var run = new SurveyRunState(_s.Definition, [new AnswerInputDto { QuestionId = _s.Enjoy.Id, Selections = [new() { OptionId = _s.Yes.Id }] }], 1);

        run.Reset();

        Assert.Equal(0, run.PageIndex);
        Assert.Equal(0, run.AnsweredCount);
        Assert.True(run.IsDirty);
        run.MarkSaved();
        Assert.False(run.IsDirty);
    }

    [Fact]
    public void Options_keep_their_designed_order_without_randomisation()
    {
        var run = new SurveyRunState(_s.Definition, seed: 12345);

        Assert.Equal([_s.Reports.Id, _s.Logic.Id, _s.OtherFeature.Id], run.OptionsFor(_s.Features).Select(o => o.Id));
    }

    [Fact]
    public void Randomised_options_are_shuffled_with_free_text_options_last_and_stable()
    {
        var question = new QuestionDto { Type = QuestionType.Radio, Text = "Pick", Settings = { RandomizeOptions = true } };
        for (var i = 0; i < 8; i++)
        {
            question.Options.Add(new OptionDto { Text = $"O{i}", Order = i });
        }

        var other = new OptionDto { Text = "Other", Order = 3, AllowsFreeText = true };
        question.Options.Add(other);

        var orders = Enumerable.Range(0, 20)
            .Select(seed => SurveyRunState.ArrangeOptions(question, seed).Select(o => o.Text).ToList())
            .ToList();

        Assert.All(orders, order => Assert.Equal("Other", order[^1]));
        Assert.All(orders, order => Assert.Equal(9, order.Distinct().Count()));
        Assert.True(orders.Select(o => string.Join(",", o)).Distinct().Count() > 1); // actually shuffled
        Assert.Equal(orders[7], SurveyRunState.ArrangeOptions(question, 7).Select(o => o.Text)); // reproducible

        var run = new SurveyRunState(new SurveyDefinitionDto { Sections = [new SectionDto { Questions = [question] }] }, seed: 99);
        Assert.Same(run.OptionsFor(question), run.OptionsFor(question)); // computed once per pass
    }
}
