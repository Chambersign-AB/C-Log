using Microsoft.Extensions.Options;
using CLog;
using CLog.Analysis;
using CLog.Configuration;
using CLog.Knowledge;
using CLog.Notifications;
using CLog.Ollama;
using CLog.Output;
using CLog.Seq;
using CLog.Storage;
using CLog.Triage;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddOptions<CLogOptions>()
    .Bind(builder.Configuration.GetSection(CLogOptions.SectionName))
    .ValidateOnStart();

builder.Services.AddSingleton(TimeProvider.System);

string Resolve(IServiceProvider services, Func<CLogOptions, string> pick)
{
    var options = services.GetRequiredService<IOptions<CLogOptions>>().Value;
    return ContentPaths.Resolve(builder.Environment.ContentRootPath, pick(options));
}

builder.Services.AddHttpClient<ISeqClient, SeqClient>();
builder.Services.AddHttpClient<IOllamaClient, OllamaClient>();

builder.Services.AddSingleton<IFingerprintStore>(services => new SqliteFingerprintStore(
    Resolve(services, o => o.DatabasePath),
    services.GetRequiredService<ILogger<SqliteFingerprintStore>>()));

builder.Services.AddSingleton<IKnowledgeSource>(services => new KnowledgeBase(
    Resolve(services, o => o.KnowledgeFile),
    services.GetRequiredService<ILogger<KnowledgeBase>>()));

builder.Services.AddSingleton<IRuleSetProvider>(services => new FileRuleSetProvider(
    Resolve(services, o => o.RulesFile),
    services.GetRequiredService<ILogger<FileRuleSetProvider>>()));

builder.Services.AddSingleton<ITriageSink>(services => new ConsoleAndFileTriageSink(
    Resolve(services, o => o.TriageLogPath),
    services.GetRequiredService<ILogger<ConsoleAndFileTriageSink>>()));

// Step two is registered only when it is switched on; without it TriageService gets no
// issue reporter and stops at the verdict, as before.
var configured = builder.Configuration.GetSection(CLogOptions.SectionName).Get<CLogOptions>() ?? new CLogOptions();
var analysis = configured.Analysis;
if (analysis.Problem() is { } problem)
{
    throw new InvalidOperationException("CLog:Analysis:Enabled is true, but " + problem + ".");
}

if (analysis.Enabled)
{
    builder.Services.AddSingleton<ISourceRepository>(services => new GitSourceRepository(
        Resolve(services, o => o.Analysis.RepoPath),
        services.GetRequiredService<ILogger<GitSourceRepository>>()));
    builder.Services.AddSingleton<CodeContextResolver>();
    builder.Services.AddSingleton<IAnalysisModel, OllamaAnalysisModel>();
    builder.Services.AddHttpClient<IIssueTracker, GitHubIssueTracker>();
    builder.Services.AddSingleton<Analyst>();
    builder.Services.AddSingleton<IssueReporter>();
}

// Notices likewise: with no channel named, TriageService gets no notifier and sends nothing.
var notify = configured.Notify;
if (notify.Problem() is { } notifyProblem)
{
    throw new InvalidOperationException("CLog:Notify:Channels is set, but " + notifyProblem + ".");
}

if (notify.Uses(NotifyOptions.Mail))
{
    builder.Services.AddHttpClient<INotifier, MailgunNotifier>();
}

if (notify.Enabled)
{
    builder.Services.AddSingleton<ErrorNotifier>();
}

builder.Services.AddSingleton<TriageService>();
builder.Services.AddHostedService<TriageWorker>();

var host = builder.Build();
await host.RunAsync();
