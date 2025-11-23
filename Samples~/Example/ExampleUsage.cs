using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel;
using UnityEngine;

/// <summary>
/// AIManager の代表的な機能（通常チャット、構造化レスポンス、RealTime Schema、Function Calling）をまとめたサンプル。
/// 各操作はインスペクターのコンテキストメニューから実行できる。
/// </summary>
public class ExampleUsage : MonoBehaviour
{
    [Header("共通設定")]
    [Tooltip("テキスト系の呼び出しで使用するモデル。Gemini 3 / Gemini 2.5 / GPT などを指定可能。")]
    public AIModelType chatModel = AIModelType.Gemini25Flash;

    [Tooltip("チャット送信時のユーザープロンプト。")]
    [TextArea]
    public string freeFormPrompt = "Unity からこんにちは！今日はどんな開発をしていますか？";

    [Header("構造化レスポンス用")]
    [Tooltip("請求書解析サンプルで使用する入力文。")]
    [TextArea(5, 8)]
    public string invoicePrompt = "請求書番号は INV-001、日付は 2024-03-15、合計金額は 1500.50 USD です。顧客は田中太郎(28)、鈴木花子(25)、佐藤健(31)です。";

    [Header("RealTime Schema サンプル")]
    public RealTimeJsonSchema<SchemaParameter> schemaTemplate;
    [TextArea]
    public string schemaPrompt = "現在の状態を更新してください。";

    [ContextMenu("Send Simple Chat")]
    public async void SendSimpleChatAsync()
    {
        var messages = new List<Message>
        {
            new Message { role = MessageRole.System, content = "あなたは親切な Unity アシスタントです。" },
            new Message { role = MessageRole.User,   content = freeFormPrompt }
        };

        // 通常のテキスト応答
        var reply = await AIManager.SendMessageAsync(messages, chatModel);
        Debug.Log($"[SimpleChat] {reply}");
    }

    [ContextMenu("Send Structured Message")]
    public async void SendStructuredAsync()
    {
        var messages = new List<Message>
        {
            new Message { role = MessageRole.System, content = "以下の請求書情報を JSON 形式で抽出してください。" },
            new Message { role = MessageRole.User,   content = invoicePrompt }
        };

        // JSON Schema を自動生成し、レスポンスを Invoice 型で受け取る
        var invoice = await AIManager.SendStructuredMessageAsync<Invoice>(messages, chatModel);
        if (invoice == null)
        {
            Debug.LogWarning("[Structured] 応答の解析に失敗しました。");
            return;
        }

        Debug.Log($"[Structured] 請求書番号: {invoice.invoiceNumber}, 日付: {invoice.date}, 合計: {invoice.amount}");
        foreach (var customer in invoice.customers)
        {
            Debug.Log($"[Structured] 顧客: {customer.name} ({customer.age} 歳)");
        }
    }

    [ContextMenu("Send Real-Time Schema Message")]
    public async void SendRealTimeSchemaAsync()
    {
        if (schemaTemplate == null)
        {
            Debug.LogWarning("[RealTimeSchema] schemaTemplate が設定されていません。");
            return;
        }

        var messages = new List<Message>
        {
            new Message
            {
                role = MessageRole.User,
                content = $"{schemaTemplate.GenerateMarkDown("現在の状態")}\n{schemaPrompt}"
            }
        };

        // 実行時にスキーマの値を更新するサンプル
        var result = await AIManager.SendStructuredMessageWithRealTimeSchemaAsync(messages, schemaTemplate, chatModel);
        if (result is RealTimeJsonSchema<SchemaParameter> updated)
        {
            schemaTemplate = updated;
            Debug.Log($"[RealTimeSchema]\n{updated.GenerateMarkDown("更新後の状態")}");
        }
        else
        {
            Debug.LogWarning("[RealTimeSchema] 応答が取得できませんでした。");
        }
    }

    [ContextMenu("Execute Function Call")]
    public async void ExecuteFunctionCallAsync()
    {
        var messages = new List<Message>
        {
            new Message { role = MessageRole.System, content = "与えられた数値を加算する関数を呼び出してください。" },
            new Message { role = MessageRole.User,   content = "3.5 と 2.5 を足した結果を教えてください。" }
        };

        var functions = new List<IJsonSchema>
        {
            new AddNumbersFunction()
        };

        var result = await AIManager.SendFunctionCallMessageAsync(messages, functions, chatModel);
        if (result is AddNumbersFunction func)
        {
            Debug.Log($"[Function] 呼び出された関数: {func.Name}");
            Debug.Log($"[Function] 引数:\n{func.GenerateMarkDown()}");
        }
        else
        {
            Debug.LogWarning("[Function] 関数呼び出しに失敗しました。");
        }
    }

    #region 構造化レスポンス用データ構造
    [Serializable]
    public class Invoice
    {
        [Description("請求書番号 (例: INV-001)")]
        [SchemaRegularExpression(@"^INV-\d{3}$")]
        public string invoiceNumber;

        [Description("発行日 (YYYY-MM-DD)")]
        public string date;

        [Description("合計金額")]
        [SchemaRange(0, 1000000)]
        public float amount;

        [Description("顧客リスト")]
        public Customer[] customers;
    }

    [Serializable]
    public class Customer
    {
        [Description("顧客名")]
        public string name;

        [Description("年齢")]
        [SchemaRange(0, 150)]
        public int age;
    }
    #endregion

    #region Function Calling 用の関数定義
    public class AddNumbersFunction : FunctionSchema<SchemaParameter>
    {
        public AddNumbersFunction() : base("addNumbers")
        {
            Description = "2 つの数値を受け取り、合計を計算する関数です。";
            Parameters = new SchemaParameter[]
            {
                new SchemaParameter { ParameterName = "a", ParameterType = SchemaParameterType.Number, Description = "1 つ目の数値" },
                new SchemaParameter { ParameterName = "b", ParameterType = SchemaParameterType.Number, Description = "2 つ目の数値" }
            };
        }

        public string Description { get; }
    }
    #endregion
}
