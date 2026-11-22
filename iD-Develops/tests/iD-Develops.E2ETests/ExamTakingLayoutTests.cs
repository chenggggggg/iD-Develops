using System.Text.Json;
using System.Text.RegularExpressions;
using iD_Develops.Data;
using iD_Develops.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace iD_Develops.E2ETests;

public sealed class ExamTakingLayoutTests : DiagnosticPageTest
{
    [Theory]
    [InlineData(false, "en-us", 1440)]
    [InlineData(false, "nl-nl", 768)]
    [InlineData(false, "nl-nl", 390)]
    [InlineData(false, "en-us", 320)]
    [InlineData(false, "nl-nl", 844)]
    [InlineData(true, "en-us", 1440)]
    [InlineData(true, "en-us", 390)]
    public async Task TakingLayoutKeepsQuestionsWithAnswersAndPreservesKeyboardAutosave(bool portal, string culture, int width)
    {
        await using var account = await CatalogBrowsingFixture.CreateAsync();
        await using var data = await TakingFixture.CreateAsync(account.User.Id, portal);
        var height = width switch { 320 => 568, 390 => 844, 844 => 390, _ => 900 };
        await Page.SetViewportSizeAsync(width, height);
        await StubMediaAsync();
        var path = portal ? $"/examination/{data.Record.Id}" : $"/{culture}/examination/level-test/{data.Record.Id}";
        if (portal)
            await BrowserLogin.SignInAsync(Page, account.User.Email!, account.Password, path, Page.Locator("#question-form"));
        else
            Assert.True((await Page.GotoAsync(LocalTestApplication.PublicBaseUrl + path, new() { WaitUntil = WaitUntilState.DOMContentLoaded }))!.Ok);

        await VerifyTakingLayoutAsync(data, culture, width, portal);
    }

    private async Task StubMediaAsync()
    {
        await Page.RouteAsync("**/e2e-exam-context.svg", route => route.FulfillAsync(new()
        {
            ContentType = "image/svg+xml",
            Body = """<svg xmlns="http://www.w3.org/2000/svg" width="400" height="480" viewBox="0 0 400 480"><rect width="400" height="480" fill="#eef6fb"/><rect x="32" y="32" width="336" height="416" rx="12" fill="#005190"/><text x="200" y="245" fill="white" text-anchor="middle" font-family="sans-serif" font-size="24">Question context</text></svg>"""
        }));
        await Page.RouteAsync("**/e2e-exam-audio.wav", route => route.FulfillAsync(new()
        {
            ContentType = "audio/wav",
            BodyBytes = Convert.FromBase64String("UklGRiQAAABXQVZFZm10IBAAAAABAAEAQB8AAEAfAAABAAgAZGF0YQAAAAA=")
        }));
    }

    private async Task VerifyTakingLayoutAsync(TakingFixture data, string culture, int width, bool portal)
    {
        await Expect(Page.Locator("#countdown")).ToHaveTextAsync(new Regex(@"^00:\d{2}:\d{2}$"));
        Assert.NotEqual("00:00:00", await Page.Locator("#countdown").InnerTextAsync());
        await Page.WaitForFunctionAsync("document.querySelector('[data-exam-question-image]')?.naturalWidth > 0");
        var metrics = await Page.EvaluateAsync<JsonElement>("""
            () => {
                const rect = selector => document.querySelector(selector).getBoundingClientRect();
                const header = rect('[data-exam-header]');
                const context = rect('[data-exam-context]');
                const media = rect('[data-exam-context-media]');
                const question = rect('[data-exam-question]');
                const answers = rect('#question-answers');
                const label = document.querySelector('label[for="question1_A"]');
                return {
                    clearance: media.top - header.bottom,
                    questionClearance: question.top - media.bottom,
                    answerGap: answers.top - question.bottom,
                    questionOffset: question.left - context.left,
                    answerOffset: answers.left - question.left,
                    answerWidth: answers.width,
                    choiceHeight: label.getBoundingClientRect().height,
                    choiceAlignment: getComputedStyle(label).textAlign,
                    pageWidth: document.documentElement.scrollWidth,
                    viewportWidth: innerWidth
                };
            }
            """);
        Assert.True(metrics.GetProperty("clearance").GetDouble() >= 16, metrics.ToString());
        Assert.True(metrics.GetProperty("questionClearance").GetDouble() >= 16, metrics.ToString());
        Assert.InRange(metrics.GetProperty("answerGap").GetDouble(), 8, 24);
        Assert.InRange(Math.Abs(metrics.GetProperty("questionOffset").GetDouble()), 0, 1);
        Assert.InRange(Math.Abs(metrics.GetProperty("answerOffset").GetDouble()), 0, 1);
        Assert.InRange(metrics.GetProperty("answerWidth").GetDouble(), 200, 700);
        Assert.True(metrics.GetProperty("choiceHeight").GetDouble() >= 44);
        Assert.Equal("left", metrics.GetProperty("choiceAlignment").GetString());
        Assert.True(metrics.GetProperty("pageWidth").GetDouble() <= metrics.GetProperty("viewportWidth").GetDouble(), metrics.ToString());

        await SaveAsync(() => Page.Locator("label[for='question1_A']").ClickAsync());
        await Expect(Page.Locator("#question1_A")).ToBeCheckedAsync();
        await SaveAsync(() => Page.Locator("label[for='question1_A']").ClickAsync());
        await Expect(Page.Locator("#question1_A")).Not.ToBeCheckedAsync();
        // Exercise keyboard entry before selecting and moving between answers.
        await Page.GetByLabel(culture == "nl-nl" ? "Hulp bij de vraag" : "Question help", new() { Exact = true }).FocusAsync();
        await Page.Keyboard.PressAsync("Tab");
        await Expect(Page.Locator("#question1_A")).ToBeFocusedAsync();
        await SaveAsync(() => Page.Keyboard.PressAsync("Space"));
        await SaveAsync(() => Page.Keyboard.PressAsync("ArrowDown"));
        await Expect(Page.Locator("#question1_B")).ToBeCheckedAsync();
        await Expect(Page.Locator("#question1_B")).ToBeFocusedAsync();
        var focusIndicator = await Page.Locator("label[for='question1_B']").EvaluateAsync<JsonElement>("""
            label => {
                const style = getComputedStyle(label);
                const input = document.getElementById(label.htmlFor);
                return { width: parseFloat(style.outlineWidth), style: style.outlineStyle,
                    focused: input.matches(':focus'), visible: input.matches(':focus-visible'),
                    outlineVariable: style.getPropertyValue('--tw-outline-style') };
            }
            """);
        Assert.True(focusIndicator.GetProperty("width").GetDouble() >= 2 && focusIndicator.GetProperty("style").GetString() != "none", focusIndicator.ToString());
        await Page.EvaluateAsync("window.scrollTo(0, document.documentElement.scrollHeight)");
        await Expect(Page.Locator("label[for='question1_D']")).ToBeInViewportAsync();
        Assert.True(await Page.EvaluateAsync<bool>("document.querySelector('label[for=\"question1_D\"]').getBoundingClientRect().bottom <= document.querySelector('[data-exam-pagination]').getBoundingClientRect().top"));
        Assert.InRange(await Page.Locator("[data-exam-header]").EvaluateAsync<double>("header => header.getBoundingClientRect().top"), 0, 1);
        await Page.EvaluateAsync("window.scrollTo(0, 0)");
        await Page.ScreenshotAsync(new() { Path = Path.Combine(AppContext.BaseDirectory, $"b22-{(portal ? "portal" : "public")}-{culture}-{width}.png"), FullPage = true });

        await GoToQuestionAsync(data.Exam.Questions.Single(question => question.QuestionNumber == 2).Id);
        var openAnswer = Page.GetByRole(AriaRole.Textbox, new() { Name = "Explain your choice.", Exact = true });
        await Expect(openAnswer).ToBeVisibleAsync();
        await Expect(Page.Locator("#question-form audio")).ToHaveAttributeAsync("controls", "");
        await SaveAsync(() => openAnswer.FillAsync("I can contact my teacher to discuss the next lesson."));
        await GoToQuestionAsync(data.Exam.Questions.Single(question => question.QuestionNumber == 3).Id);
        await Expect(Page.Locator("#question3_true")).ToBeAttachedAsync();
        await SaveAsync(() => Page.Locator("label[for='question3_true']").ClickAsync());
        await Expect(Page.Locator("#question3_true")).ToBeCheckedAsync();
        await GoToQuestionAsync(data.Exam.Questions.Single(question => question.QuestionNumber == 1).Id);
        await Expect(Page.Locator("#question1_B")).ToBeCheckedAsync();
        await Page.ReloadAsync(new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Expect(Page.Locator("#question1_B")).ToBeCheckedAsync();
        await Page.GetByLabel(culture == "nl-nl" ? "Hulp bij de vraag" : "Question help", new() { Exact = true }).ClickAsync();
        await Expect(Page.GetByText("Choose the most suitable response.", new() { Exact = true })).ToBeVisibleAsync();

        await using var db = LocalTestApplication.CreateDbContext();
        var saved = await db.ParticipantAnswers.Where(answer => answer.RecordId == data.Record.Id)
            .Select(answer => new { answer.Question.QuestionNumber, answer.AnswerText })
            .ToDictionaryAsync(answer => answer.QuestionNumber, answer => answer.AnswerText);
        Assert.Equal("B", saved[1]);
        Assert.Equal("I can contact my teacher to discuss the next lesson.", saved[2]);
        Assert.Equal("True", saved[3]);
        Assert.Equal(ExamStatus.InProgress, (await db.Records.SingleAsync(record => record.Id == data.Record.Id)).ExamStatus);
    }

    [Theory]
    [InlineData("en-us", 320, 568)]
    [InlineData("nl-nl", 390, 844)]
    [InlineData("nl-nl", 844, 390)]
    public async Task LongExamNavigationAndSubmissionRemainReachable(string culture, int width, int height)
    {
        await using var account = await CatalogBrowsingFixture.CreateAsync();
        await using var data = await TakingFixture.CreateAsync(account.User.Id, false, 30);
        await Page.SetViewportSizeAsync(width, height);
        await StubMediaAsync();
        var lastQuestion = data.Exam.Questions.Single(question => question.QuestionNumber == 30);
        var url = $"{LocalTestApplication.PublicBaseUrl}/{culture}/examination/level-test/{data.Record.Id}?questionId={lastQuestion.Id}";
        Assert.Equal(200, (await Page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded }))!.Status);
        var submit = Page.Locator("#submitExam");
        await Page.ScreenshotAsync(new() { Path = Path.Combine(AppContext.BaseDirectory, $"exam-mobile-navigation-{width}.png") });
        await Expect(submit).ToBeInViewportAsync();
        var navigation = Page.GetByRole(AriaRole.Group, new() { Name = culture == "nl-nl" ? "Vraagnavigatie" : "Question navigation", Exact = true });
        var active = navigation.Locator("[aria-current='page']");
        await Expect(active).ToHaveAttributeAsync("data-question-id", lastQuestion.Id.ToString());
        await Expect(active).ToBeInViewportAsync();
        await Expect(Page.Locator("[data-exam-progress]")).ToHaveTextAsync(culture == "nl-nl" ? "Vraag 30 van 30" : "Question 30 of 30");
        Assert.True(await Page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));

        await SaveAsync(() => Page.GetByRole(AriaRole.Textbox).FillAsync("My final answer"));
        await Page.ReloadAsync(new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Expect(Page.GetByRole(AriaRole.Textbox)).ToHaveValueAsync("My final answer");
        await Expect(active).ToBeInViewportAsync();
        var first = navigation.Locator("[data-action='load-question']").First;
        await first.FocusAsync();
        await Expect(first).ToBeInViewportAsync();
        await Expect(submit).ToBeInViewportAsync();
        await first.PressAsync("Enter");
        await Expect(Page.Locator("#question1_A")).ToBeAttachedAsync();
        await Expect(Page.Locator("[data-exam-progress]")).ToHaveTextAsync(culture == "nl-nl" ? "Vraag 1 van 30" : "Question 1 of 30");
        Assert.InRange(await Page.Locator("[data-exam-header]").EvaluateAsync<double>("element => element.getBoundingClientRect().height"), 0, height / 3.0);

        await submit.ClickAsync();
        var dialog = Page.GetByRole(AriaRole.Dialog);
        await Expect(dialog).ToBeVisibleAsync();
        await Expect(dialog.GetByRole(AriaRole.Button, new() { Name = culture == "nl-nl" ? "Annuleren" : "Cancel", Exact = true })).ToBeFocusedAsync();
        var confirm = dialog.GetByRole(AriaRole.Button, new() { Name = culture == "nl-nl" ? "Toch inleveren" : "Submit anyway", Exact = true });
        await Expect(confirm).ToBeInViewportAsync();
        await confirm.FocusAsync();
        await Page.Keyboard.PressAsync("Tab");
        await Expect(dialog.GetByRole(AriaRole.Button, new() { Name = culture == "nl-nl" ? "Annuleren" : "Cancel", Exact = true })).ToBeFocusedAsync();
        await Page.Keyboard.PressAsync("Shift+Tab");
        await Expect(confirm).ToBeFocusedAsync();
        await Page.ScreenshotAsync(new() { Path = Path.Combine(AppContext.BaseDirectory, $"exam-mobile-confirmation-{width}.png") });
        await Page.Keyboard.PressAsync("Escape");
        await Expect(dialog).Not.ToBeVisibleAsync();
        await Expect(submit).ToBeFocusedAsync();
        await submit.ClickAsync();
        await confirm.ClickAsync();
        await Expect(Page.Locator("[data-exam-completion]")).ToBeVisibleAsync();
        await using var db = LocalTestApplication.CreateDbContext();
        Assert.Equal(ExamStatus.Completed, (await db.Records.SingleAsync(record => record.Id == data.Record.Id)).ExamStatus);
        Assert.Equal("My final answer", (await db.ParticipantAnswers.SingleAsync(answer => answer.RecordId == data.Record.Id && answer.QuestionId == lastQuestion.Id)).AnswerText);
    }

    [Theory]
    [InlineData(1440, 900)]
    [InlineData(390, 844)]
    public async Task PublicExamPaginationSupportsMouseWheelDragAndKeyboardWithoutAccidentalNavigation(int width, int height)
    {
        await using var account = await CatalogBrowsingFixture.CreateAsync();
        await using var data = await TakingFixture.CreateAsync(account.User.Id, portal: false, questionCount: 30);
        await Page.SetViewportSizeAsync(width, height);
        await StubMediaAsync();
        await Page.GotoAsync($"{LocalTestApplication.PublicBaseUrl}/en-us/examination/level-test/{data.Record.Id}", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await SaveAsync(() => Page.Locator("label[for='question1_B']").ClickAsync());
        var navigation = Page.Locator("[data-exam-question-scroll]");
        await Expect(navigation).ToHaveAttributeAsync("data-overflow", "true");
        var current = await Page.Locator("#questionId").InputValueAsync();
        var questionRequests = 0;
        Page.Request += (_, request) => { if (request.Url.Contains("handler=QuestionShell")) questionRequests++; };
        var bounds = (await navigation.BoundingBoxAsync())!;
        await navigation.FocusAsync();
        await Page.Keyboard.PressAsync("Home");
        await Page.Mouse.MoveAsync(bounds.X + bounds.Width / 2, bounds.Y + 22);
        var pageTop = await Page.EvaluateAsync<double>("scrollY");
        await Page.Mouse.WheelAsync(0, 160);
        await Page.WaitForFunctionAsync("document.querySelector('[data-exam-question-scroll]').scrollLeft > 20");
        Assert.InRange(Math.Abs(await Page.EvaluateAsync<double>("scrollY") - pageTop), 0, 1);
        await Page.Keyboard.PressAsync("Home");
        await Page.Mouse.WheelAsync(160, 0);
        await Page.WaitForFunctionAsync("document.querySelector('[data-exam-question-scroll]').scrollLeft > 20");
        await Page.Keyboard.PressAsync("Home");
        var start = await navigation.EvaluateAsync<JsonElement>("""
            element => {
                const track = element.getBoundingClientRect();
                const button = [...element.querySelectorAll('button')].filter(button => {
                    const rect = button.getBoundingClientRect();
                    return rect.left >= track.left && rect.right <= track.right - 6;
                }).at(-1).getBoundingClientRect();
                return {x:button.left + button.width / 2, y:button.top + button.height / 2};
            }
            """);
        await Page.Mouse.MoveAsync((float)start.GetProperty("x").GetDouble(), (float)start.GetProperty("y").GetDouble());
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(bounds.X + 12, (float)start.GetProperty("y").GetDouble(), new() { Steps = 12 });
        await Expect(navigation).ToHaveAttributeAsync("data-dragging", "true");
        await Page.Mouse.UpAsync();
        await Page.EvaluateAsync("() => new Promise(requestAnimationFrame)");
        Assert.True(await navigation.EvaluateAsync<double>("element => element.scrollLeft") > 20);
        Assert.Equal(current, await Page.Locator("#questionId").InputValueAsync());
        Assert.Equal(0, questionRequests);
        await Expect(Page.Locator("#submitExam")).ToBeInViewportAsync();
        await navigation.FocusAsync();
        await Page.Keyboard.PressAsync("End");
        var last = navigation.Locator("button").Last;
        await Expect(last).ToBeInViewportAsync();
        // At the horizontal boundary, vertical wheel input must remain usable.
        await Page.EvaluateAsync("window.scrollTo(0, 0)");
        await Page.Mouse.MoveAsync(bounds.X + bounds.Width / 2, bounds.Y + 22);
        await Page.Mouse.WheelAsync(0, 120);
        await Page.WaitForFunctionAsync("scrollY > 5");
        await last.FocusAsync();
        await Page.Keyboard.PressAsync("Enter");
        await Expect(Page.GetByRole(AriaRole.Textbox)).ToBeVisibleAsync();
        await SaveAsync(() => Page.GetByRole(AriaRole.Textbox).FillAsync("Saved after scrolling"));
        await navigation.FocusAsync();
        await Page.Keyboard.PressAsync("Home");
        var first = navigation.Locator("button").First;
        await Expect(first).ToBeInViewportAsync();
        await first.ClickAsync();
        await Expect(Page.Locator("#question1_B")).ToBeCheckedAsync();
        await Page.Locator("#submitExam").ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Dialog)).ToBeVisibleAsync();
        await Page.Keyboard.PressAsync("Escape");
        await using var db = LocalTestApplication.CreateDbContext();
        var lastQuestion = data.Exam.Questions.Single(question => question.QuestionNumber == 30);
        Assert.Equal("Saved after scrolling", (await db.ParticipantAnswers.SingleAsync(answer => answer.RecordId == data.Record.Id && answer.QuestionId == lastQuestion.Id)).AnswerText);
    }

    [Theory]
    [InlineData(320, 568)]
    [InlineData(390, 844)]
    [InlineData(844, 390)]
    [InlineData(1440, 900)]
    public async Task PublicExamMobileImagesPrecedeRelatedTextAndDesktopKeepsItsColumns(int width, int height)
    {
        await using var account = await CatalogBrowsingFixture.CreateAsync();
        await using var data = await TakingFixture.CreateAsync(account.User.Id, portal: false);
        await using var db = LocalTestApplication.CreateDbContext();
        await db.ExamVersionQuestions.Where(question => question.ExamVersionId == data.Record.ExamVersionId)
            .ExecuteUpdateAsync(update => update.SetProperty(question => question.ImageReference, $"{LocalTestApplication.PublicBaseUrl}/e2e-exam-context.svg")
                .SetProperty(question => question.Scenario, "<p>Read this scenario together with the question that follows.</p>"));
        await Page.SetViewportSizeAsync(width, height);
        await StubMediaAsync();
        await Page.GotoAsync($"{LocalTestApplication.PublicBaseUrl}/en-us/examination/level-test/{data.Record.Id}", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        foreach (var question in data.Exam.Questions.OrderBy(question => question.QuestionNumber))
        {
            if (question.QuestionNumber != 1) await GoToQuestionAsync(question.Id);
            await Page.WaitForFunctionAsync("document.querySelector('[data-exam-question-image]')?.naturalWidth > 0");
            var layout = await Page.EvaluateAsync<JsonElement>("""
                () => {
                    const rect = selector => {
                        const value = document.querySelector(selector).getBoundingClientRect();
                        return {top:value.top,bottom:value.bottom,left:value.left,right:value.right,height:value.height};
                    };
                    return {image:rect('[data-exam-question-image]'),context:rect('[data-exam-context]'),question:rect('[data-exam-question]'),
                        answers:rect('#question-answers'),header:rect('[data-exam-header]'),overflow:document.documentElement.scrollWidth > innerWidth};
                }
                """);
            var image = layout.GetProperty("image");
            var context = layout.GetProperty("context");
            var prompt = layout.GetProperty("question");
            if (width < 1024)
            {
                Assert.True(image.GetProperty("bottom").GetDouble() <= context.GetProperty("top").GetDouble(), layout.ToString());
                Assert.True(context.GetProperty("bottom").GetDouble() <= prompt.GetProperty("top").GetDouble(), layout.ToString());
                Assert.True(image.GetProperty("height").GetDouble() <= height * 0.4 + 1, layout.ToString());
                if (question.QuestionNumber != 2)
                    Assert.InRange(prompt.GetProperty("top").GetDouble() - context.GetProperty("bottom").GetDouble(), 16, 32);
            }
            else
            {
                Assert.True(image.GetProperty("left").GetDouble() >= context.GetProperty("right").GetDouble(), layout.ToString());
                Assert.InRange(Math.Abs(image.GetProperty("top").GetDouble() - context.GetProperty("top").GetDouble()), 0, 1);
            }
            Assert.True(layout.GetProperty("answers").GetProperty("top").GetDouble() > prompt.GetProperty("bottom").GetDouble(), layout.ToString());
            Assert.False(layout.GetProperty("overflow").GetBoolean());
            if (question.QuestionNumber == 1)
                await Page.ScreenshotAsync(new() { Path = Path.Combine(AppContext.BaseDirectory, "test-results", $"exam-image-order-{width}.png"), FullPage = true });
        }
    }

    [Fact]
    public async Task PublicExamPaginationChromiumTouchSwipeKeepsTapNavigationWorking()
    {
        await using var account = await CatalogBrowsingFixture.CreateAsync();
        await using var data = await TakingFixture.CreateAsync(account.User.Id, portal: false, questionCount: 30);
        await using var mobile = await Browser.NewContextAsync(new() { ViewportSize = new() { Width = 390, Height = 844 }, IsMobile = true, HasTouch = true });
        var page = await mobile.NewPageAsync();
        await page.RouteAsync("**/e2e-exam-context.svg", route => route.FulfillAsync(new() { ContentType = "image/svg+xml", Body = "<svg xmlns='http://www.w3.org/2000/svg' width='400' height='480'><rect width='400' height='480' fill='#005190'/></svg>" }));
        await page.GotoAsync($"{LocalTestApplication.PublicBaseUrl}/en-us/examination/level-test/{data.Record.Id}", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var navigation = page.Locator("[data-exam-question-scroll]");
        await Expect(navigation).ToHaveAttributeAsync("data-overflow", "true");
        var current = await page.Locator("#questionId").InputValueAsync();
        var bounds = (await navigation.BoundingBoxAsync())!;
        var cdp = await mobile.NewCDPSessionAsync(page);
        var y = bounds.Y + 22;
        var start = bounds.X + bounds.Width - 24;
        await cdp.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object> { ["type"] = "touchStart", ["touchPoints"] = new[] { new { x = start, y, id = 0 } } });
        for (var step = 1; step <= 10; step++)
        {
            await cdp.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object> { ["type"] = "touchMove", ["touchPoints"] = new[] { new { x = start - step * 16, y, id = 0 } } });
            await page.EvaluateAsync("() => new Promise(requestAnimationFrame)");
        }
        await cdp.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object> { ["type"] = "touchEnd", ["touchPoints"] = Array.Empty<object>() });
        await page.WaitForFunctionAsync("document.querySelector('[data-exam-question-scroll]').scrollLeft > 40");
        Assert.Equal(current, await page.Locator("#questionId").InputValueAsync());
        await Expect(page.Locator("#submitExam")).ToBeInViewportAsync();
        await page.WaitForFunctionAsync("""
            async () => {
                const element = document.querySelector('[data-exam-question-scroll]');
                const left = element.scrollLeft;
                for (let frame = 0; frame < 4; frame++) {
                    await new Promise(requestAnimationFrame);
                    if (Math.abs(element.scrollLeft - left) > 0.5) return false;
                }
                return true;
            }
            """);
        // Tap a visible number after the native swipe, without focus/scrollIntoView.
        var target = await navigation.EvaluateAsync<JsonElement>("""
            element => {
                const track = element.getBoundingClientRect();
                const button = [...element.querySelectorAll('button')].find(button => {
                    const rect = button.getBoundingClientRect();
                    return rect.left >= track.left + 4 && rect.right <= track.right - 4 && button.getAttribute('aria-current') !== 'page';
                });
                const rect = button.getBoundingClientRect();
                return {id:button.dataset.questionId,x:rect.left+rect.width/2,y:rect.top+rect.height/2};
            }
            """);
        await page.Touchscreen.TapAsync((float)target.GetProperty("x").GetDouble(), (float)target.GetProperty("y").GetDouble());
        await Expect(page.Locator("#questionId")).ToHaveValueAsync(target.GetProperty("id").GetString()!);
        await cdp.DetachAsync();
    }
    private async Task SaveAsync(Func<Task> action)
    {
        var response = await Page.RunAndWaitForResponseAsync(action,
            result => result.Request.Method == "POST" && result.Url.Contains("handler=SubmitAnswer", StringComparison.Ordinal));
        Assert.Equal(200, response.Status);
        Assert.True((await response.JsonAsync())!.Value.GetProperty("success").GetBoolean());
    }

    private async Task GoToQuestionAsync(int questionId)
    {
        await Page.Locator($"[data-action='load-question'][data-question-id='{questionId}']").ClickAsync();
        await Expect(Page.Locator("#questionId")).ToHaveValueAsync(questionId.ToString());
    }

    private sealed class TakingFixture(ApplicationDbContext db) : IAsyncDisposable
    {
        public Exam Exam { get; private set; } = null!;
        public iD_Develops.Models.Record Record { get; private set; } = null!;

        public static async Task<TakingFixture> CreateAsync(string userId, bool portal, int questionCount = 3)
        {
            var fixture = new TakingFixture(LocalTestApplication.CreateDbContext());
            try { await fixture.SeedAsync(userId, portal, questionCount); return fixture; }
            catch { await fixture.DisposeAsync(); throw; }
        }

        private async Task SeedAsync(string userId, bool portal, int questionCount)
        {
            Exam = new Exam
            {
                Name = "Language and communication for work, learning, and everyday conversations",
                CreatedByUserId = userId,
                PublishStatus = ExamPublishStatus.Published,
                TimeLimitSeconds = 900,
                IntroductionPrimaryLanguage = "Read each question and choose your answer.",
                Questions =
                [
                    new MultipleChoiceQuestion
                    {
                        QuestionNumber = 1,
                        Text = "<p>What is the most suitable response when you want to discuss your next lesson?</p>",
                        Scenario = "<p>Your teacher has sent you a message about your next lesson. Read the information and choose the response that best fits the situation.</p>",
                        ImageReference = $"{LocalTestApplication.PublicBaseUrl}/e2e-exam-context.svg",
                        MessageBeforeQuestion = "<p>Choose the most suitable response.</p>",
                        Score = 2,
                        CorrectAnswers = [new() { Text = "B" }],
                        MultipleChoiceAnswer = new()
                        {
                            AnswerA = "I will contact my teacher to discuss a suitable date and time for the next lesson.",
                            AnswerB = "Could we discuss the next lesson together? I would like to ask a few questions before we make an appointment.",
                            AnswerC = new string('W', 100),
                            AnswerD = "I do not need a reply."
                        }
                    },
                    new OpenQuestion { QuestionNumber = 2, Text = "Explain your choice.", AudioReference = $"{LocalTestApplication.PublicBaseUrl}/e2e-exam-audio.wav", Score = 2, CorrectAnswers = [new() { Text = "Contact my teacher" }] },
                    new TrueOrFalseQuestion { QuestionNumber = 3, Text = "Can you contact your teacher?", Score = 1, CorrectAnswers = [new() { Text = "True" }] }
                ]
            };
            for (var number = 4; number <= questionCount; number++)
                Exam.Questions.Add(new OpenQuestion { QuestionNumber = number, Text = $"Question {number}: explain your answer.", Score = 1, CorrectAnswers = [new() { Text = "Example answer" }] });
            db.Exams.Add(Exam);
            await db.SaveChangesAsync();
            var version = new ExamVersion
            {
                Exam = Exam,
                VersionNumber = 1,
                Name = Exam.Name,
                TimeLimitSeconds = 900,
                PublicSlug = portal ? null : "level-test",
                PublishedAtUtc = DateTime.UtcNow,
                Questions = Exam.Questions.Select(question => new ExamVersionQuestion
                {
                    SourceQuestionId = question.Id,
                    QuestionNumber = question.QuestionNumber,
                    QuestionType = question switch { MultipleChoiceQuestion => "MultipleChoice", TrueOrFalseQuestion => "TrueOrFalse", _ => "Open" },
                    Text = question.Text,
                    Scenario = question.Scenario,
                    ImageReference = question.ImageReference,
                    AudioReference = question.AudioReference,
                    MessageBeforeQuestion = question.MessageBeforeQuestion,
                    Score = question.Score,
                    AnswerA = (question as MultipleChoiceQuestion)?.MultipleChoiceAnswer?.AnswerA,
                    AnswerB = (question as MultipleChoiceQuestion)?.MultipleChoiceAnswer?.AnswerB,
                    AnswerC = (question as MultipleChoiceQuestion)?.MultipleChoiceAnswer?.AnswerC,
                    AnswerD = (question as MultipleChoiceQuestion)?.MultipleChoiceAnswer?.AnswerD,
                    CorrectAnswers = question.CorrectAnswers.Select(answer => new ExamVersionCorrectAnswer { Text = answer.Text }).ToList()
                }).ToList()
            };
            Record = new iD_Develops.Models.Record
            {
                Exam = Exam,
                ExamVersion = version,
                UserId = portal ? userId : null,
                StartDateTime = DateTime.UtcNow.AddSeconds(-10),
                EndDateTime = DateTime.UtcNow.AddSeconds(890),
                ExamStatus = ExamStatus.InProgress,
                StatusReason = RecordStatusReason.Started
            };
            db.Records.Add(Record);
            await db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (Record != null)
                {
                    await db.ParticipantAnswers.Where(answer => answer.RecordId == Record.Id).ExecuteDeleteAsync();
                    await db.Records.Where(record => record.Id == Record.Id).ExecuteDeleteAsync();
                    await db.ApplicationAuditEvents.Where(audit => audit.EntityId == Record.Id.ToString()).ExecuteDeleteAsync();
                }
                if (Exam?.Id > 0)
                {
                    await db.ExamLogs.Where(log => log.ExamId == Exam.Id).ExecuteDeleteAsync();
                    await db.Questions.Where(question => question.ExamId == Exam.Id).ExecuteDeleteAsync();
                    await db.Exams.Where(exam => exam.Id == Exam.Id).ExecuteDeleteAsync();
                }
            }
            finally { await db.DisposeAsync(); }
        }
    }
}
