using System;
using System.Threading.Tasks;
using UnityEngine.Networking;

internal static class UnityWebRequestUtils
{
    internal static Task<UnityWebRequest> SendAsync(UnityWebRequest request)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        var tcs = new TaskCompletionSource<UnityWebRequest>();
        try
        {
            var operation = request.SendWebRequest();
            if (operation.isDone)
            {
                tcs.TrySetResult(request);
            }
            else
            {
                operation.completed += _ => tcs.TrySetResult(request);
            }
        }
        catch (Exception ex)
        {
            tcs.TrySetException(ex);
        }
        return tcs.Task;
    }
}
