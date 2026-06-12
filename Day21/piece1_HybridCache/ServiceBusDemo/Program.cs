using Azure.Messaging.ServiceBus;
using ServiceBusDemo.Handlers;
using ServiceBusDemo.Publisher;
using ServiceBusDemo.Services;
using ServiceBusDemo.Workers;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((ctx, services) =>
    {
        var connStr = ctx.Configuration.GetConnectionString("ServiceBus")
            ?? throw new InvalidOperationException(
                "Missing ConnectionStrings:ServiceBus in appsettings.json");

        // Single long-lived client shared by publisher and both consumer workers
        services.AddSingleton(sp => new ServiceBusClient(connStr));

        // In-memory idempotency store (keyed "subscriptionName:messageId")
        services.AddSingleton<ProcessedMessageStore>();

        // Publisher holds a single ServiceBusSender for the topic
        services.AddSingleton<TopicPublisher>();

        // Handlers — one per subscription
        services.AddSingleton<AnalyticsHandler>();
        services.AddSingleton<NotificationHandler>();

        // ConsumerWorker × 2: each wraps a different handler / subscription.
        // MaxConcurrentCalls=2 inside each worker simulates competing consumers.
        //
        // IMPORTANT: AddHostedService<T>(factory) calls TryAddEnumerable, which silently
        // drops a second registration of the same concrete type. Use AddSingleton<IHostedService>
        // directly so both workers are registered as distinct IHostedService instances.
        services.AddSingleton<IHostedService>(sp => new ConsumerWorker(
            sp.GetRequiredService<ServiceBusClient>(),
            sp.GetRequiredService<ProcessedMessageStore>(),
            sp.GetRequiredService<AnalyticsHandler>(),
            sp.GetRequiredService<ILoggerFactory>().CreateLogger<ConsumerWorker>()));

        services.AddSingleton<IHostedService>(sp => new ConsumerWorker(
            sp.GetRequiredService<ServiceBusClient>(),
            sp.GetRequiredService<ProcessedMessageStore>(),
            sp.GetRequiredService<NotificationHandler>(),
            sp.GetRequiredService<ILoggerFactory>().CreateLogger<ConsumerWorker>()));

        // Orchestrator publishes the demo scenario and probes the DLQ when done
        services.AddHostedService<DemoOrchestratorService>();
    })
    .Build();

await host.RunAsync();
