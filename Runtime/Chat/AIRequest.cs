using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityLLMAPI.Schema;

namespace UnityLLMAPI.Chat
{
    public enum AIRequestStatus
    {
        Pending,
        Succeeded,
        Canceled,
        Faulted
    }

    public sealed class AIRequest<T> : CustomYieldInstruction, IDisposable
    {
        private readonly Task<T> task;
        private readonly CancellationTokenSource cancellationSource;
        private bool disposed;

        internal AIRequest(Task<T> task, CancellationTokenSource cancellationSource)
        {
            this.task = task ?? throw new ArgumentNullException(nameof(task));
            this.cancellationSource = cancellationSource;
        }

        public Task<T> Task => task;
        public override bool keepWaiting => !IsDone;

        public bool IsDone => task.IsCompleted;
        public bool IsPending => !task.IsCompleted;
        public bool IsCanceled => task.IsCanceled;
        public bool IsCancellationRequested => cancellationSource != null && cancellationSource.IsCancellationRequested;
        public bool IsFaulted => task.IsFaulted;
        public bool IsCompletedSuccessfully => task.Status == TaskStatus.RanToCompletion;
        public bool IsSuccess => TryGetResult(out _);

        public AIRequestStatus Status
        {
            get
            {
                if (!task.IsCompleted) return AIRequestStatus.Pending;
                if (task.IsCanceled) return AIRequestStatus.Canceled;
                if (task.IsFaulted) return AIRequestStatus.Faulted;
                return AIRequestStatus.Succeeded;
            }
        }

        public Exception Exception
        {
            get
            {
                if (task.Exception == null) return null;
                return task.Exception.Flatten().InnerException ?? task.Exception;
            }
        }

        public string ErrorMessage
        {
            get
            {
                if (!IsDone) return null;
                if (IsCanceled) return "Request was canceled.";
                if (IsFaulted) return Exception?.Message ?? "Request failed.";
                if (!TryGetResult(out _)) return "Request completed without a result.";
                return null;
            }
        }

        public T Result
        {
            get
            {
                if (!IsDone)
                {
                    throw new InvalidOperationException("AIRequest.Result cannot be read before the request is done.");
                }

                return task.GetAwaiter().GetResult();
            }
        }

        public bool TryGetResult(out T result)
        {
            result = default;
            if (Status != AIRequestStatus.Succeeded) return false;

            result = task.GetAwaiter().GetResult();
            return (object)result != null;
        }

        public bool TryGetException(out Exception exception)
        {
            exception = Exception;
            return exception != null;
        }

        public bool Cancel()
        {
            if (disposed || IsDone || cancellationSource == null || cancellationSource.IsCancellationRequested)
            {
                return false;
            }

            cancellationSource.Cancel();
            return true;
        }

        public IEnumerator WaitUntilDone()
        {
            yield return this;
        }

        public IEnumerator WaitForResult(Action<T> onSuccess, Action<AIRequest<T>> onFailure = null)
        {
            yield return this;

            if (TryGetResult(out var result))
            {
                onSuccess?.Invoke(result);
            }
            else
            {
                onFailure?.Invoke(this);
            }
        }

        public void Dispose()
        {
            if (disposed) return;

            if (!IsDone)
            {
                Cancel();
            }

            if (cancellationSource != null)
            {
                if (IsDone)
                {
                    cancellationSource.Dispose();
                }
                else
                {
                    task.ContinueWith(
                        _ => cancellationSource.Dispose(),
                        CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                }
            }

            disposed = true;
        }
    }

    public static class AIRequest
    {
        public static AIRequest<T> Run<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));

            var cancellationSource = CreateCancellationSource(cancellationToken);
            try
            {
                var task = operation(cancellationSource.Token);
                if (task == null)
                {
                    cancellationSource.Dispose();
                    return new AIRequest<T>(
                        System.Threading.Tasks.Task.FromException<T>(
                            new InvalidOperationException("AIRequest operation returned null Task.")),
                        null);
                }

                return new AIRequest<T>(task, cancellationSource);
            }
            catch (Exception ex)
            {
                cancellationSource.Dispose();
                return new AIRequest<T>(System.Threading.Tasks.Task.FromException<T>(ex), null);
            }
        }

        public static AIRequest<T> FromTask<T>(Task<T> task)
        {
            return new AIRequest<T>(task, null);
        }

        public static AIRequest<string> SendMessage(
            List<Message> messages,
            AIModelType model,
            Dictionary<string, object> initBody = null,
            CancellationToken cancellationToken = default,
            int timeoutSeconds = -1)
        {
            return Run(
                token => AIManager.SendMessageAsync(messages, model, initBody, token, timeoutSeconds),
                cancellationToken);
        }

        public static AIRequest<RawChatStreamResult> SendMessageStream(
            List<Message> messages,
            AIModelType model,
            Dictionary<string, object> initBody = null,
            Action<string> onContentDelta = null,
            CancellationToken cancellationToken = default,
            int timeoutSeconds = -1)
        {
            return Run(
                token => AIManager.SendMessageStreamAsync(
                    messages,
                    model,
                    initBody,
                    token,
                    timeoutSeconds,
                    onContentDelta),
                cancellationToken);
        }

        public static AIRequest<T> SendStructured<T>(
            List<Message> messages,
            AIModelType model,
            Dictionary<string, object> initBody = null,
            CancellationToken cancellationToken = default,
            int timeoutSeconds = -1)
        {
            return Run(
                token => AIManager.SendStructuredMessageAsync<T>(messages, model, initBody, token, timeoutSeconds),
                cancellationToken);
        }

        public static AIRequest<T> Structured<T>(
            List<Message> messages,
            AIModelType model,
            Dictionary<string, object> initBody = null,
            CancellationToken cancellationToken = default,
            int timeoutSeconds = -1)
        {
            return SendStructured<T>(messages, model, initBody, cancellationToken, timeoutSeconds);
        }

        public static AIRequest<T> PopulateStructured<T>(
            T targetInstance,
            List<Message> messages,
            AIModelType model,
            Dictionary<string, object> initBody = null,
            CancellationToken cancellationToken = default,
            int timeoutSeconds = -1)
        {
            return Run(
                async token =>
                {
                    await AIManager.SendStructuredMessageAsync(
                        targetInstance,
                        messages,
                        model,
                        initBody,
                        token,
                        timeoutSeconds);
                    return targetInstance;
                },
                cancellationToken);
        }

        public static AIRequest<IJsonSchema> SendStructuredWithRealTimeSchema(
            List<Message> messages,
            IJsonSchema schema,
            AIModelType model,
            Dictionary<string, object> initBody = null,
            CancellationToken cancellationToken = default,
            int timeoutSeconds = -1)
        {
            return Run(
                token => AIManager.SendStructuredMessageWithRealTimeSchemaAsync(
                    messages,
                    schema,
                    model,
                    initBody,
                    token,
                    timeoutSeconds),
                cancellationToken);
        }

        public static AIRequest<Dictionary<string, object>> SendStructuredWithSchema(
            List<Message> messages,
            Dictionary<string, object> jsonSchema,
            AIModelType model,
            Dictionary<string, object> initBody = null,
            CancellationToken cancellationToken = default,
            int timeoutSeconds = -1)
        {
            return Run(
                token => AIManager.SendStructuredMessageWithSchemaAsync(
                    messages,
                    jsonSchema,
                    model,
                    initBody,
                    token,
                    timeoutSeconds),
                cancellationToken);
        }

        public static AIRequest<IJsonSchema> SendFunctionCall(
            List<Message> messages,
            List<IJsonSchema> functions,
            AIModelType model,
            Dictionary<string, object> initBody = null,
            CancellationToken cancellationToken = default,
            int timeoutSeconds = -1)
        {
            return Run(
                token => AIManager.SendFunctionCallMessageAsync(
                    messages,
                    functions,
                    model,
                    initBody,
                    token,
                    timeoutSeconds),
                cancellationToken);
        }

        public static AIRequest<ImageGenerationResponse> GenerateImages(
            List<Message> messages,
            AIModelType model = AIModelType.Gemini25FlashImage,
            Dictionary<string, object> initBody = null,
            CancellationToken cancellationToken = default,
            int timeoutSeconds = -1)
        {
            return Run(
                token => AIManager.GenerateImagesAsync(messages, model, initBody, token, timeoutSeconds),
                cancellationToken);
        }

        public static AIRequest<GeneratedImage> GenerateImage(
            List<Message> messages,
            AIModelType model = AIModelType.Gemini25FlashImage,
            Dictionary<string, object> initBody = null,
            CancellationToken cancellationToken = default,
            int timeoutSeconds = -1)
        {
            return Run(
                token => AIManager.GenerateImageAsync(messages, model, initBody, token, timeoutSeconds),
                cancellationToken);
        }

        private static CancellationTokenSource CreateCancellationSource(CancellationToken cancellationToken)
        {
            return cancellationToken.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                : new CancellationTokenSource();
        }
    }
}
