using System;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;

namespace ShtikLive.Slides
{
    public static class ResiliencePolicy
    {
        public static Policy Create(ILogger logger)
        {
            var retry = Policy.Handle<Exception>(e => !(e is BrokenCircuitException))
                .WaitAndRetryForeverAsync(n => TimeSpan.FromMilliseconds(Math.Min((n + 1) * 100, 500)),
                    (result, _) =>
                    {
                        if (result != null)
                            logger.LogWarning(result, result.Message);
                    });

            var breaker = Policy.Handle<Exception>()
                .CircuitBreakerAsync(5, TimeSpan.FromMinutes(1),
                    (result, _) => logger.LogWarning("Circuit breaker tripped."),
                    () => logger.LogWarning("Circuit breaker reset."));

            return Policy.WrapAsync(retry, breaker);
        }
    }
}