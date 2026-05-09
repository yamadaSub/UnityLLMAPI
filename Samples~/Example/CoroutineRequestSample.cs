using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityLLMAPI.Chat;

public class CoroutineRequestSample : MonoBehaviour
{
    public AIModelType model = AIModelType.Gemini25Flash;

    [TextArea]
    public string prompt = "Create a compact patrol plan for a stealth game enemy.";

    private Coroutine runningCoroutine;
    private AIRequest<EnemyPlan> runningRequest;

    [ContextMenu("Run Coroutine Request")]
    public void RunCoroutineRequest()
    {
        CancelCoroutineRequest();
        runningCoroutine = StartCoroutine(RunCoroutineRequestRoutine());
    }

    [ContextMenu("Cancel Coroutine Request")]
    public void CancelCoroutineRequest()
    {
        runningRequest?.Dispose();
        runningRequest = null;

        if (runningCoroutine != null)
        {
            StopCoroutine(runningCoroutine);
            runningCoroutine = null;
        }
    }

    private IEnumerator RunCoroutineRequestRoutine()
    {
        var messages = new List<Message>
        {
            new Message
            {
                role = MessageRole.System,
                content = "Return a JSON object that matches the requested C# schema."
            },
            new Message { role = MessageRole.User, content = prompt }
        };

        runningRequest = AIRequest.SendStructured<EnemyPlan>(
            messages,
            model,
            timeoutSeconds: 60);

        yield return PlayLocalPreparation();

        if (!runningRequest.TryGetResult(out var plan))
        {
            yield return runningRequest;
        }

        if (runningRequest.TryGetResult(out plan))
        {
            Debug.Log($"[CoroutineRequest] {plan.enemyName}: {plan.summary}");
        }
        else
        {
            Debug.LogWarning($"[CoroutineRequest] No result. status={runningRequest.Status}, error={runningRequest.ErrorMessage}");
        }

        runningRequest.Dispose();
        runningRequest = null;
        runningCoroutine = null;
    }

    private IEnumerator PlayLocalPreparation()
    {
        for (var i = 0; i < 30; i++)
        {
            yield return null;
        }
    }

    private void OnDisable()
    {
        CancelCoroutineRequest();
    }

    [Serializable]
    public class EnemyPlan
    {
        public string enemyName;
        public string summary;
        public string[] patrolPoints;
    }
}
