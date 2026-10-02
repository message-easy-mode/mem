// using System.Reflection;
// using MassTransit;
// using Microsoft.Extensions.Configuration;
// using Microsoft.Extensions.DependencyInjection;
// using Shared.Messaging.Events;

// namespace Shared.Messaging.Extensions;

// public static class MassTransitExtentions
// {
//     public static IServiceCollection AddMassTransitWithAssemblies
//         (this IServiceCollection services, IConfiguration configuration, params Assembly[] assemblies)
//     {
//         services.AddMassTransit(config =>
//         {
//             // config.SetKebabCaseEndpointNameFormatter();            

//             // config.SetInMemorySagaRepositoryProvider();



//             config.AddConsumers(assemblies);
//             config.AddSagaStateMachines(assemblies);
//             config.AddSagas(assemblies);
//             config.AddActivities(assemblies);

//             // Not Using - In Memory
//             // config.UsingInMemory((context, configurator) =>
//             // {
//             //     // configurator.UseMessageRetry(r => r.Immediate(1)); // just 1 retry
//             //     configurator.ConfigureEndpoints(context);

//             //     // // Apply global retry policy
//             //     // configurator.UseMessageRetry(r =>
//             //     // {
//             //     //     r.Interval(3, TimeSpan.FromSeconds(2)); // 3 retries, 2s apart
//             //     // });
//             // });


//             config.UsingRabbitMq((context, bus) =>
//             {
//                 bus.Host(new Uri(configuration["MessageBroker:Host"]!), host =>
//                 {
//                     host.Username(configuration["MessageBroker:UserName"]!);
//                     host.Password(configuration["MessageBroker:Password"]!);
//                 });


//                 bus.ConfigureEndpoints(context, KebabCaseEndpointNameFormatter.Instance);
//             });
//         });

//         return services;
//     }

    
// }
