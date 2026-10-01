using Microsoft.Extensions.Options;
using CLog;
using CLog.Configuration;
using CLog.Knowledge;
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

builder.Services.AddSingleton<TriageService>();
builder.Services.AddHostedService<TriageWorker>();

var host = builder.Build();
await host.RunAsync();
