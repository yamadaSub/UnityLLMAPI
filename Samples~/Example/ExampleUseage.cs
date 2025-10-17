using System;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;

public class ExampleUsage : MonoBehaviour
{
    public RealTimeJsonSchema<SchemaParameter> schema;
    public string ask;

    [ContextMenu("Send Normal Message")]
    public async void SendNormalMessageExample()
    {
        List<Message> chatHistory = new List<Message>
        {
            new Message { role = MessageRole.System, content = "これから会話を始めます。" },
            new Message { role = MessageRole.User, content = "こんにちは、元気ですか？" }
        };
        var response = await AIManager.Instance.SendMessageAsync(chatHistory, AIModelType.GPT4o);
        Debug.Log("通常応答: " + response);
    }

    [ContextMenu("Send Structured Message")]
    public async void SendStructuredMessageExample()
    {
        List<Message> chatHistory = new List<Message>
        {
            new Message { role = MessageRole.System, content = "以下の請求書情報を解析してください。" },
            new Message { role = MessageRole.User, content = "請求書番号はINV1234、日付は2023-03-15、合計金額は1500.50です。顧客は佐々木(15)，鈴木(24)，田中(22)です" }
        };

        var invoice = await AIManager.Instance.SendStructuredMessageAsync<Invoice>(chatHistory, AIModelType.GPT4o);
        if (invoice != null)
        {
            Debug.Log("構造化応答:");
            Debug.Log("請求書番号: " + invoice.invoiceNumber);
            Debug.Log("請求日: " + invoice.date);
            Debug.Log("請求合計金額: " + invoice.amount);
            foreach (var customer in invoice.customers)
            {
                Debug.Log("顧客: " + customer);
            }
        }
        else
        {
            Debug.Log("構造化応答が取得できませんでした。");
        }
    }

    [ContextMenu("Send Real-Time Schema Message")]
    public async void SendRealTimeSchemaMessageExample()
    {
        List<Message> messages = new List<Message>
        {
            new Message { role = MessageRole.User, content = $"{schema.GenerateMarkDown("現在の状態")}\n{ask}" }
        };

        var structuredResponse = await AIManager.Instance.SendStructuredMessageWithRealTimeSchemaAsync(messages, schema, AIModelType.GPT4o);
        if (structuredResponse is RealTimeJsonSchema<SchemaParameter> responce)
        {
            schema = responce;
            Debug.Log("Structured Response: " + responce.GenerateMarkDown());
        }
        else
        {
            Debug.Log("Structured Response is null");
        }
    }

    [ContextMenu("Execute Function Call")]
    public async void ExecuteFunctionCallExample()
    {
        List<Message> chatHistory = new List<Message>
        {
            new Message { role = MessageRole.System, content = "以下の計算を実行してください。" },
            new Message { role = MessageRole.User, content = "合計6になるパラメータを作ってください。" }
        };

        List<IJsonSchema> functions = new List<IJsonSchema>
        {
            new AddNumbersFunction()
        };

        var result = await AIManager.Instance.SendFunctionCallMessageAsync(chatHistory, functions, AIModelType.GPT4o);
        if (result is AddNumbersFunction func)
        {
            Debug.Log("Function Calling 応答:");
            Debug.Log("関数名: " + func.Name);
            Debug.Log("引数: " + func.GenerateMarkDown());
        }
        else
        {
            Debug.Log("Function Calling 応答が取得できませんでした。");
        }
    }

    #region 構造化出力
    [Serializable]
    public class Invoice
    {
        public string invoiceNumber;
        public string date;
        public float amount;
        public Customer[] customers;
    }

    [Serializable]
    public class Customer
    {
        public string name;
        public int age;
    }
    #endregion

    #region Function Calling
    public class AddNumbersFunction : FunctionSchema<SchemaParameter>
    {
        public string Description => "2つの数字を加算します。";

        public AddNumbersFunction() : base("addNumbers")
        {
            Parameters = new SchemaParameter[]
            {
                new SchemaParameter { ParameterName = "a", ParameterType = SchemaParameterType.Number, Description = "1つ目の数字", Enum = new string[] { "1.5", "2.5", "3.5" } },
                new SchemaParameter { ParameterName = "b", ParameterType = SchemaParameterType.Number, Description = "2つ目の数字" }
            };
        }
    }
    #endregion
}
