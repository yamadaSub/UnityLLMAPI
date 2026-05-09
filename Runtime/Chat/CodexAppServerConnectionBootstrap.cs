using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace UnityLLMAPI.Chat
{
    /// <summary>
    /// Optional bridge used by Editor tooling to ensure a local Codex App Server is available.
    /// Runtime/player builds can ignore this; when no handler is registered it is a no-op.
    /// </summary>
    public static class CodexAppServerConnectionBootstrap
    {
        public delegate Task<string> EnsureReadyHandler(string requestedWebSocketUrl, CancellationToken cancellationToken);

        public static EnsureReadyHandler EnsureReadyAsync { get; set; }

        internal static async Task<string> TryEnsureReadyAsync(string requestedWebSocketUrl, CancellationToken cancellationToken)
        {
            var handler = EnsureReadyAsync;
            if (handler == null)
            {
                return requestedWebSocketUrl;
            }

            try
            {
                var resolvedUrl = await handler(requestedWebSocketUrl, cancellationToken);
                return string.IsNullOrWhiteSpace(resolvedUrl) ? requestedWebSocketUrl : resolvedUrl;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Codex App Server auto-start check failed: " + ex.Message);
                return requestedWebSocketUrl;
            }
        }
    }
}
