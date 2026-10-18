using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaskDesk.App.Notifications;
using TaskDesk.Core.Paths;
using TaskDesk.Core.Reminders;
using TaskDesk.Presentation;

namespace TaskDesk.App.Shell;

/// <summary>How the shell delivers Reminders (SPEC 7.3).</summary>
internal static class NotificationServices
{
    /// <summary>
    /// Registers <see cref="INotificationService"/>: with toast registration (the normal mode), Windows App SDK
    /// toasts that fall back to the tray balloon; without it (sandbox mode), the tray balloon only, so nothing is
    /// left behind in the registry. Needs an <see cref="Core.Shell.IBalloonTray"/> registered.
    /// </summary>
    public static IServiceCollection AddTaskDeskNotifications(this IServiceCollection services, RuntimeMode mode)
    {
        services.AddSingleton<TrayBalloonNotificationService>();
        if (!mode.RegistersNotifications)
        {
            services.AddSingleton<INotificationService>(sp => sp.GetRequiredService<TrayBalloonNotificationService>());
            return services;
        }

        services.AddSingleton<AppSdkNotificationService>();
        services.AddSingleton(sp => new NotificationDelivery(
            sp.GetRequiredService<AppSdkNotificationService>(),
            sp.GetRequiredService<TrayBalloonNotificationService>(),
            sp.GetRequiredService<ILogger<NotificationDelivery>>()));
        services.AddSingleton<INotificationService>(sp => sp.GetRequiredService<NotificationDelivery>());
        return services;
    }
}
