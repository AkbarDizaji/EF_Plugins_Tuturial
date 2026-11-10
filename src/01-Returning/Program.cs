using Common;
using Demo01.Returning;

// dotnet run -- [run|seed|reset] [--fast|--perf] [--sql] [--log-sql]
return await DemoHost.RunAsync(new ReturningDemo(), args);
