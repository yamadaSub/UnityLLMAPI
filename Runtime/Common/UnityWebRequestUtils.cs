using System;
using System.Threading.Tasks;
using UnityEngine.Networking;

/// <summary>
/// UnityWebRequest 向けの Task ラッパーを提供するユーティリティ。
/// Unity 公式の SendWebRequestAsync が未提供なバージョンでも、
/// 共通的に await 可能な待ち方を実現する。
/// </summary>
internal static class UnityWebRequestUtils
{
    /// <summary>
    /// UnityWebRequest の送信完了を Task ベースで待機する。
    /// </summary>
    /// <param name="request">送信対象のリクエスト。</param>
    internal static Task<UnityWebRequest> SendAsync(UnityWebRequest request)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        var tcs = new TaskCompletionSource<UnityWebRequest>();
        try
        {
            var operation = request.SendWebRequest();

            // すでに完了している場合は即座に結果を返す。
            if (operation.isDone)
            {
                tcs.TrySetResult(request);
            }
            else
            {
                // 完了時に Task を成功扱いで完了させる。
                operation.completed += _ => tcs.TrySetResult(request);
            }
        }
        catch (Exception ex)
        {
            // SendWebRequest 呼び出し自体が失敗した場合は例外を転送。
            tcs.TrySetException(ex);
        }
        return tcs.Task;
    }
}
