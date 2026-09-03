using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Options;

namespace Moyai.Mcp;

/// <summary>Controls admission without terminating in-flight requests.</summary>
public sealed class ServiceAdmission
{
    private int _paused;
    public bool IsPaused => Volatile.Read(ref _paused) != 0;
    public void Pause() => Interlocked.Exchange(ref _paused, 1);
    public void Resume() => Interlocked.Exchange(ref _paused, 0);
}

/// <summary>Maps SCM pause/continue to HTTP admission while preserving host shutdown.</summary>
public sealed class PausableServiceLifetime : WindowsServiceLifetime
{
    private readonly ServiceAdmission _admission;

    public PausableServiceLifetime(IHostEnvironment environment, IHostApplicationLifetime lifetime,
        ILoggerFactory loggerFactory, IOptions<HostOptions> options, ServiceAdmission admission)
        : base(environment, lifetime, loggerFactory, options)
    {
        ServiceName = "Moyai";
        CanPauseAndContinue = true;
        _admission = admission;
    }

    protected override void OnPause() => _admission.Pause();
    protected override void OnContinue() => _admission.Resume();
}
