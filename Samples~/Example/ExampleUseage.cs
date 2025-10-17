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
            new Message { role = MessageRole.System, content = "���ꂩ���b���n�߂܂��B" },
            new Message { role = MessageRole.User, content = "����ɂ��́A���C�ł����H" }
        };
        var response = await AIManager.Instance.SendMessageAsync(chatHistory, AIModelType.GPT4o);
        Debug.Log("�ʏ퉞��: " + response);
    }

    [ContextMenu("Send Structured Message")]
    public async void SendStructuredMessageExample()
    {
        List<Message> chatHistory = new List<Message>
        {
            new Message { role = MessageRole.System, content = "�ȉ��̐�����������͂��Ă��������B" },
            new Message { role = MessageRole.User, content = "�������ԍ���INV1234�A���t��2023-03-15�A���v���z��1500.50�ł��B�ڋq�͍��X��(15)�C���(24)�C�c��(22)�ł�" }
        };

        var invoice = await AIManager.Instance.SendStructuredMessageAsync<Invoice>(chatHistory, AIModelType.GPT4o);
        if (invoice != null)
        {
            Debug.Log("�\��������:");
            Debug.Log("�������ԍ�: " + invoice.invoiceNumber);
            Debug.Log("������: " + invoice.date);
            Debug.Log("�������v���z: " + invoice.amount);
            foreach (var customer in invoice.customers)
            {
                Debug.Log("�ڋq: " + customer);
            }
        }
        else
        {
            Debug.Log("�\�����������擾�ł��܂���ł����B");
        }
    }

    [ContextMenu("Send Real-Time Schema Message")]
    public async void SendRealTimeSchemaMessageExample()
    {
        List<Message> messages = new List<Message>
        {
            new Message { role = MessageRole.User, content = $"{schema.GenerateMarkDown("���݂̏��")}\n{ask}" }
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
            new Message { role = MessageRole.System, content = "�ȉ��̌v�Z�����s���Ă��������B" },
            new Message { role = MessageRole.User, content = "���v6�ɂȂ�p�����[�^������Ă��������B" }
        };

        List<IJsonSchema> functions = new List<IJsonSchema>
        {
            new AddNumbersFunction()
        };

        var result = await AIManager.Instance.SendFunctionCallMessageAsync(chatHistory, functions, AIModelType.GPT4o);
        if (result is AddNumbersFunction func)
        {
            Debug.Log("Function Calling ����:");
            Debug.Log("�֐���: " + func.Name);
            Debug.Log("����: " + func.GenerateMarkDown());
        }
        else
        {
            Debug.Log("Function Calling �������擾�ł��܂���ł����B");
        }
    }

    #region �\�����o��
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
        public string Description => "2�̐��������Z���܂��B";

        public AddNumbersFunction() : base("addNumbers")
        {
            Parameters = new SchemaParameter[]
            {
                new SchemaParameter { ParameterName = "a", ParameterType = SchemaParameterType.Number, Description = "1�ڂ̐���", Enum = new string[] { "1.5", "2.5", "3.5" } },
                new SchemaParameter { ParameterName = "b", ParameterType = SchemaParameterType.Number, Description = "2�ڂ̐���" }
            };
        }
    }
    #endregion
}
