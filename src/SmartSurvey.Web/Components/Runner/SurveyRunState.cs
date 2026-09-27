using SmartSurvey.Application.Logic;
using SmartSurvey.Application.Responses;
using SmartSurvey.Application.Surveys;
using SmartSurvey.Domain.Enums;

namespace SmartSurvey.Web.Components.Runner;

/// <summary>
/// One respondent's pass through a survey: the answers being edited, the pages visible for those
/// answers (live conditional logic), the current page, validation errors and the per-respondent
/// option order. Pure C# (no Blazor dependencies) so the whole runner flow is unit-testable; the
/// <c>SurveyRunner</c> component only renders it and talks to the services.
/// </summary>
/// <remarks>
/// Logic and validation use exactly the code the server runs on submission
/// (<see cref="LogicEvaluator"/>, <see cref="ResponseValidator"/>). Answers to questions that logic
/// hides are kept locally (they come back if the respondent changes their mind) but are neither
/// validated nor counted; the server discards them on submission.
/// </remarks>
public sealed class SurveyRunState
{
    private readonly Dictionary<Guid, AnswerInputDto> _answers;
    private readonly Dictionary<Guid, QuestionDto> _questions;
    private readonly Dictionary<Guid, IReadOnlyList<OptionDto>> _optionOrder = [];
    private readonly Dictionary<Guid, List<string>> _errors = [];
    private readonly Dictionary<Guid, int> _numbers = [];
    private readonly List<SectionDto> _orderedSections;
    private readonly int _seed;
    private SurveyVisibility _visibility = null!;
    private List<SectionDto> _pages = [];
    private Guid? _currentSectionId;

    /// <summary>Starts (or resumes) a pass through <paramref name="survey"/>.</summary>
    /// <param name="survey">Survey design.</param>
    /// <param name="answers">Previously saved answers (draft), if any.</param>
    /// <param name="sectionIndex">
    /// Page to resume at, as an index into all sections in display order (the value stored with drafts).
    /// When that page is hidden for the restored answers, the nearest earlier visible page is used.
    /// </param>
    /// <param name="seed">Seed of the option shuffle; keep it stable for one respondent (e.g. across prerendering).</param>
    public SurveyRunState(SurveyDefinitionDto survey, IEnumerable<AnswerInputDto>? answers = null, int sectionIndex = 0, int seed = 0)
    {
        ArgumentNullException.ThrowIfNull(survey);
        Survey = survey;
        _seed = seed;
        _orderedSections = survey.Sections.OrderBy(s => s.Order).ToList();
        _questions = survey.AllQuestions().GroupBy(q => q.Id).ToDictionary(g => g.Key, g => g.First());

        // Every question gets an (empty) answer object so the UI can bind to it directly.
        _answers = _questions.Keys.ToDictionary(id => id, id => new AnswerInputDto { QuestionId = id });
        foreach (var answer in answers ?? [])
        {
            if (_questions.ContainsKey(answer.QuestionId))
            {
                _answers[answer.QuestionId] = answer.Clone();
            }
        }

        Refresh();
        _currentSectionId = ResolvePage(sectionIndex)?.Id;
    }

    /// <summary>The survey design.</summary>
    public SurveyDefinitionDto Survey { get; }

    /// <summary>
    /// The pages the respondent walks through: visible sections, minus sections whose questions are all
    /// hidden by logic (sections without any questions are kept as information pages).
    /// </summary>
    public IReadOnlyList<SectionDto> Pages => _pages;

    /// <summary>Zero-based position of the current page in <see cref="Pages"/> (-1 when the survey has no pages).</summary>
    public int PageIndex => _currentSectionId is { } id ? _pages.FindIndex(p => p.Id == id) : -1;

    /// <summary>The current page (null when the survey has no pages).</summary>
    public SectionDto? CurrentPage => PageIndex >= 0 ? _pages[PageIndex] : null;

    /// <summary>True on the first page.</summary>
    public bool IsFirstPage => PageIndex <= 0;

    /// <summary>True on the last page (the one with the submit button).</summary>
    public bool IsLastPage => PageIndex >= _pages.Count - 1;

    /// <summary>Visible questions of the current page in display order.</summary>
    public IReadOnlyList<QuestionDto> CurrentQuestions => CurrentPage is { } page ? _visibility.VisibleQuestions(page) : [];

    /// <summary>Validation errors keyed by question id (only questions that currently have errors).</summary>
    public IReadOnlyDictionary<Guid, List<string>> Errors => _errors;

    /// <summary>First question (in display order) with a validation error, if any.</summary>
    public Guid? FirstErrorQuestionId => Survey.AllQuestions().FirstOrDefault(q => _errors.ContainsKey(q.Id))?.Id;

    /// <summary>True when answers changed since the last <see cref="MarkSaved"/>.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>True when at least one visible question is answered.</summary>
    public bool HasAnswers => AnsweredCount > 0;

    /// <summary>Number of visible questions (all pages).</summary>
    public int VisibleQuestionCount => _visibility.VisibleQuestionIds.Count;

    /// <summary>Number of visible questions that have an answer.</summary>
    public int AnsweredCount => _visibility.VisibleQuestionIds.Count(IsAnswered);

    /// <summary>Share of visible questions answered, 0–100.</summary>
    public int ProgressPercent => VisibleQuestionCount == 0 ? 0 : (int)Math.Round(100.0 * AnsweredCount / VisibleQuestionCount);

    /// <summary>The editable answer object of a question.</summary>
    public AnswerInputDto Answer(Guid questionId) => _answers[questionId];

    /// <summary>Errors of one question (empty when valid).</summary>
    public IReadOnlyList<string> ErrorsFor(Guid questionId) => _errors.TryGetValue(questionId, out var list) ? list : [];

    /// <summary>
    /// One-based question number across all visible questions, or null when the survey hides numbers.
    /// Numbers follow the respondent's path, so they stay contiguous when logic hides questions.
    /// </summary>
    public int? NumberOf(Guid questionId) =>
        Survey.ShowQuestionNumbers && _numbers.TryGetValue(questionId, out var number) ? number : null;

    /// <summary>
    /// Options of a choice question in the order this respondent sees them: shuffled when the question
    /// has <see cref="Domain.ValueObjects.QuestionSettings.RandomizeOptions"/>, with "free text" options
    /// always last. The order is computed once and stays stable for the whole pass.
    /// </summary>
    public IReadOnlyList<OptionDto> OptionsFor(QuestionDto question)
    {
        if (!_optionOrder.TryGetValue(question.Id, out var options))
        {
            options = ArrangeOptions(question, _seed);
            _optionOrder[question.Id] = options;
        }

        return options;
    }

    /// <summary>
    /// Call after the respondent changed an answer: re-evaluates logic, marks the pass dirty and gives
    /// live feedback — an error shown for the question is re-checked (and cleared once fixed), errors of
    /// questions that logic just hid are dropped.
    /// </summary>
    public void AnswerChanged(Guid questionId)
    {
        IsDirty = true;
        Refresh();

        if (_errors.ContainsKey(questionId) && _questions.TryGetValue(questionId, out var question))
        {
            var errors = ResponseValidator.ValidateQuestion(question, _answers[questionId]);
            if (errors.Count == 0)
            {
                _errors.Remove(questionId);
            }
            else
            {
                _errors[questionId] = errors;
            }
        }

        foreach (var hidden in _errors.Keys.Where(id => !_visibility.IsQuestionVisible(id)).ToList())
        {
            _errors.Remove(hidden);
        }
    }

    /// <summary>Validates the current page; errors replace the previous ones. Returns true when it is valid.</summary>
    public bool ValidateCurrentPage()
    {
        _errors.Clear();
        if (CurrentPage is not { } page)
        {
            return true;
        }

        foreach (var (id, errors) in ResponseValidator.Validate(Survey, _answers, _visibility, [page.Id]))
        {
            _errors[id] = errors;
        }

        return _errors.Count == 0;
    }

    /// <summary>Validates the current page and moves to the next one. Returns false (staying put) when invalid.</summary>
    public bool TryGoNext()
    {
        if (!ValidateCurrentPage())
        {
            return false;
        }

        if (!IsLastPage)
        {
            _currentSectionId = _pages[PageIndex + 1].Id;
        }

        return true;
    }

    /// <summary>Moves to the previous page (errors are cleared). Returns false on the first page.</summary>
    public bool GoBack()
    {
        if (IsFirstPage)
        {
            return false;
        }

        _errors.Clear();
        _currentSectionId = _pages[PageIndex - 1].Id;
        return true;
    }

    /// <summary>
    /// Validates every visible page (before submitting). When invalid, jumps to the first page with an
    /// error so the respondent sees it. Returns true when everything is valid.
    /// </summary>
    public bool ValidateAll()
    {
        _errors.Clear();
        foreach (var (id, errors) in ResponseValidator.Validate(Survey, _answers, _visibility))
        {
            _errors[id] = errors;
        }

        GoToFirstError();
        return _errors.Count == 0;
    }

    /// <summary>
    /// Shows errors returned by the server (keyed by question id) and jumps to the first affected page.
    /// Returns the messages that do not belong to a visible question, to be shown as a general alert.
    /// </summary>
    public List<string> ApplyServerErrors(IReadOnlyDictionary<string, string[]> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        _errors.Clear();
        var general = new List<string>();

        foreach (var (key, messages) in errors)
        {
            if (Guid.TryParse(key, out var id) && _visibility.IsQuestionVisible(id))
            {
                _errors[id] = [.. messages];
            }
            else
            {
                general.AddRange(messages);
            }
        }

        GoToFirstError();
        return general.Distinct().ToList();
    }

    /// <summary>
    /// The draft/submission payload: every answered question (hidden ones included — the server
    /// discards those on submission) and the current page as an index into all sections.
    /// </summary>
    public SaveResponseRequest ToRequest(Guid? responseId, string? userAgent = null) => new()
    {
        ResponseId = responseId,
        CurrentSectionIndex = CurrentPage is { } page ? Math.Max(0, _orderedSections.IndexOf(page)) : 0,
        Answers = Survey.AllQuestions()
            .Select(q => _answers[q.Id])
            .Where(a => a.HasValue(_questions[a.QuestionId].Type))
            .Select(a => a.Clone())
            .ToList(),
        UserAgent = userAgent,
    };

    /// <summary>Records that the current answers have been saved.</summary>
    public void MarkSaved() => IsDirty = false;

    /// <summary>Clears every answer and error and returns to the first page ("start over").</summary>
    public void Reset()
    {
        foreach (var id in _questions.Keys)
        {
            _answers[id] = new AnswerInputDto { QuestionId = id };
        }

        _errors.Clear();
        IsDirty = true;
        Refresh();
        _currentSectionId = _pages.FirstOrDefault()?.Id;
    }

    /// <summary>
    /// Arranges the options of <paramref name="question"/> for display. Without
    /// <see cref="Domain.ValueObjects.QuestionSettings.RandomizeOptions"/> this is the designed order; with it,
    /// the regular options are shuffled deterministically from <paramref name="seed"/> and the question id,
    /// and "free text" options (e.g. "Other, please specify") follow in their designed order.
    /// </summary>
    public static IReadOnlyList<OptionDto> ArrangeOptions(QuestionDto question, int seed)
    {
        ArgumentNullException.ThrowIfNull(question);
        var ordered = question.Options.OrderBy(o => o.Order).ToList();
        if (!question.Settings.RandomizeOptions || !question.Type.IsChoice())
        {
            return ordered;
        }

        var shuffled = ordered.Where(o => !o.AllowsFreeText).ToList();

        // Guid.GetHashCode is derived from the bytes, so the order is reproducible for a seed.
        var random = new Random(seed ^ question.Id.GetHashCode());
        for (var i = shuffled.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }

        shuffled.AddRange(ordered.Where(o => o.AllowsFreeText));
        return shuffled;
    }

    private bool IsAnswered(Guid questionId) =>
        _questions.TryGetValue(questionId, out var question) && _answers[questionId].HasValue(question.Type);

    /// <summary>Re-evaluates logic, the page list and question numbers; keeps the current page when possible.</summary>
    private void Refresh()
    {
        _visibility = LogicEvaluator.Evaluate(Survey, _answers);
        _pages = _visibility.VisibleSections(Survey)
            .Where(s => s.Questions.Count == 0 || _visibility.VisibleQuestions(s).Count > 0)
            .ToList();

        _numbers.Clear();
        foreach (var question in _pages.SelectMany(_visibility.VisibleQuestions))
        {
            _numbers[question.Id] = _numbers.Count + 1;
        }

        // Conditions only look backwards, so answers on the current page never hide it; this guard covers
        // designs that changed underneath a resumed draft.
        if (_currentSectionId is { } current && _pages.All(p => p.Id != current))
        {
            _currentSectionId = ResolvePage(_orderedSections.FindIndex(s => s.Id == current))?.Id;
        }
    }

    /// <summary>The page at <paramref name="sectionIndex"/> of all sections, or the nearest earlier visible one.</summary>
    private SectionDto? ResolvePage(int sectionIndex)
    {
        if (_pages.Count == 0)
        {
            return null;
        }

        for (var i = Math.Min(sectionIndex, _orderedSections.Count - 1); i >= 0; i--)
        {
            var page = _pages.FirstOrDefault(p => p.Id == _orderedSections[i].Id);
            if (page is not null)
            {
                return page;
            }
        }

        return _pages[0];
    }

    private void GoToFirstError()
    {
        if (FirstErrorQuestionId is { } id && _pages.FirstOrDefault(p => p.Questions.Any(q => q.Id == id)) is { } page)
        {
            _currentSectionId = page.Id;
        }
    }
}
