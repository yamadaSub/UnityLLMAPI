using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace UnityLLMAPI.Common
{
    /// <summary>
    /// UnityWebRequest 向けの Task ラッパーを提供するユーティリティ。
    /// Unity 公式の SendWebRequestAsync が未提供なバージョンでも、
    /// 共通的に await 可能な送信方法を実現する。
    /// </summary>
    internal static class UnityWebRequestUtils
    {
        /// <summary>
        /// UnityWebRequest の送信完了を Task ベースで待機する。
        /// </summary>
        /// <param name="request">送信対象のリクエスト。</param>
        internal static Task<UnityWebRequest> SendAsync(UnityWebRequest request)
        {
            return SendAsync(request, CancellationToken.None, -1);
        }

        /// <summary>
        /// UnityWebRequest の送信完了を Task ベースで待機する。キャンセル・タイムアウトを指定可能。
        /// </summary>
        /// <param name="request">送信対象のリクエスト。</param>
        /// <param name="cancellationToken">キャンセル要求時にリクエストを中断。</param>
        /// <param name="timeoutSeconds">UnityWebRequest.timeout に設定する秒数。負数で無効。</param>
        internal static Task<UnityWebRequest> SendAsync(UnityWebRequest request, CancellationToken cancellationToken, int timeoutSeconds = -1)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<UnityWebRequest>(cancellationToken);
            }

            if (timeoutSeconds > 0)
            {
                request.timeout = timeoutSeconds;
            }

            var tcs = new TaskCompletionSource<UnityWebRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationTokenRegistration ctr = default;
            if (cancellationToken.CanBeCanceled)
            {
                ctr = cancellationToken.Register(() =>
                {
                    try { request.Abort(); }
                    catch { /* ignore abort errors */ }
                    tcs.TrySetCanceled(cancellationToken);
                });
            }

            try
            {
                var operation = request.SendWebRequest();
                void CompleteRequest()
                {
                    ctr.Dispose();
                    if (cancellationToken.IsCancellationRequested)
                    {
                        tcs.TrySetCanceled(cancellationToken);
                    }
                    else
                    {
                        tcs.TrySetResult(request);
                    }
                }

                operation.completed += _ => CompleteRequest();
                if (operation.isDone)
                {
                    CompleteRequest();
                }
            }
            catch (Exception ex)
            {
                ctr.Dispose();
                // SendWebRequest 呼び出し自体が失敗した場合は例外を転送
                tcs.TrySetException(ex);
            }
            return tcs.Task;
        }
    }
}
