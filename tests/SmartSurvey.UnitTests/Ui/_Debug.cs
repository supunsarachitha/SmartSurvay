using Bunit;
using Microsoft.Extensions.DependencyInjection;
using SmartSurvey.UnitTests.TestSupport;
using SmartSurvey.Web.Components.Survey;
using Xunit.Abstractions;
namespace SmartSurvey.UnitTests.Ui;
public sealed class DebugRunner(ITestOutputHelper output) : UiTestBase
{
    [Fact]
    public void Dump()
    {
        Services.AddSingleton<TimeProvider>(TimeProvider.System);
        var s = SampleSurveys.CustomerFeedback();
        var r = RenderComponent<SurveyRunner>(p => p.Add(x => x.Survey, s.Definition).Add(x => x.Preview, true));
        r.Find("button.btn-lg").Click();
        r.Find("button.btn-primary").Click();
        r.FindAll("label.choice-item input[type=radio]")[0].Change(true);
        output.WriteLine(string.Join(" | ", r.FindAll("button.btn-primary").Select(b => b.OuterHtml)));
        r.Find("button.btn-primary").Click();
        output.WriteLine(r.Markup[..Math.Min(3000, r.Markup.Length)]);
    }
}
