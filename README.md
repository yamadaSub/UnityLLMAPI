UnityLLMAPI
===========

Unity 縺九ｉ螟ｧ謇・LLM / Embedding API 繧貞他縺ｳ蜃ｺ縺吶◆繧√・謾ｯ謠ｴ繝代ャ繧ｱ繝ｼ繧ｸ縺ｧ縺吶・ 
API 繧ｭ繝ｼ縺ｮ蜿門ｾ励√Μ繧ｯ繧ｨ繧ｹ繝育函謌舌゛SON Schema 繝吶・繧ｹ縺ｮ讒矩蛹門ｿ懃ｭ斐・未謨ｰ蜻ｼ縺ｳ蜃ｺ縺励；emini 逕ｻ蜒丞・蜃ｺ蜉帙∝沂繧∬ｾｼ縺ｿ繝吶け繝医Ν險育ｮ励∪縺ｧ繧貞酔縺倥さ繝ｼ繝我ｽ鍋ｳｻ縺ｧ謇ｱ縺医∪縺吶・

譛ｬ繝峨く繝･繝｡繝ｳ繝医〒縺ｯ繧ｻ繝・ヨ繧｢繝・・縺九ｉ荳ｻ隕√↑蛻ｩ逕ｨ繝代ち繝ｼ繝ｳ縲√し繝ｳ繝励Ν繧ｳ繝ｼ繝峨・蝣ｴ謇縺ｾ縺ｧ繧剃ｿｯ迸ｰ縺ｧ縺阪ｋ繧医≧縺ｫ縺ｾ縺ｨ繧√※縺・∪縺吶・

---

繧ｻ繝・ヨ繧｢繝・・
------------
1. **API 繧ｭ繝ｼ繧貞叙蠕励＠縺ｦ迺ｰ蠅・↓逋ｻ骭ｲ**
   - 蛻ｩ逕ｨ蜿ｯ閭ｽ縺ｪ繧ｭ繝ｼ: `OPENAI_API_KEY` / `GROK_API_KEY` 縺ｾ縺溘・ `XAI_API_KEY` / `GOOGLE_API_KEY`
   - Windows (PowerShell):\
     `Set-Item -Path Env:OPENAI_API_KEY -Value "<your_key>"`
   - macOS / Linux (bash / zsh):\
     `export OPENAI_API_KEY=<your_key>`
2. **Unity Editor 縺ｧ縺ｮ繧ｭ繝ｼ險ｭ螳・(莉ｻ諢・**
   - 繝｡繝九Η繝ｼ `Tools > UnityLLMAPI > Configure API Keys` 縺九ｉ EditorUserSettings 縺ｫ菫晏ｭ伜庄閭ｽ  
     ・域囓蜿ｷ蛹悶＆繧後★繝励Ο繧ｸ繧ｧ繧ｯ繝亥､悶↓菫晏ｭ倥＆繧後ｋ縺溘ａ縲〃CS 縺ｸ縺ｯ繧ｳ繝溘ャ繝井ｸ崎ｦ√〒縺呻ｼ・
3. **繝ｩ繝ｳ繧ｿ繧､繝逕ｨ縺ｮ繧ｭ繝ｼ隗｣豎ｺ鬆・ｺ・*
   - `AIManagerBehaviour` (繝偵お繝ｩ繝ｫ繧ｭ繝ｼ荳翫・繧ｳ繝ｳ繝昴・繝阪Φ繝・
   - EditorUserSettings (`UnityLLMAPI.OPENAI_API_KEY` 縺ｪ縺ｩ)
   - 迺ｰ蠅・､画焚・・rocess 竊・User 竊・Machine・・

---

蜈ｨ菴薙・繝ｯ繝ｼ繧ｯ繝輔Ο繝ｼ
------------------
1. **Message / MessageContent 繧堤ｵ・∩遶九※繧・*  
   - 繝・く繧ｹ繝医・ `Message.content` 縺ｾ縺溘・ `MessageContent.FromText()`  
   - 逕ｻ蜒上・ `MessageContent.FromImage()`・・exture 縺九ｉ閾ｪ蜍輔〒 PNG 蛹厄ｼ峨∪縺溘・ `FromImageData` / `FromImageUrl`
2. **AIManager / EmbeddingManager 縺ｮ API 繧貞他縺ｶ**  
   - `SendMessageAsync`縲～SendStructuredMessageAsync`縲～SendFunctionCallMessageAsync`縲～GenerateImagesAsync` 縺ｪ縺ｩ
3. **繝ｬ繧ｹ繝昴Φ繧ｹ繧貞・逅・☆繧・*  
   - 繝・く繧ｹ繝亥ｿ懃ｭ斐・ string縲∵ｧ矩蛹門ｿ懃ｭ斐・莉ｻ諢上・蝙九：unction 蜻ｼ縺ｳ蜃ｺ縺励・ `IJsonSchema`縲∫判蜒冗函謌舌・ `ImageGenerationResponse`
4. **蠢・ｦ√↓蠢懊§縺ｦ陬懷勧繝ｦ繝ｼ繝・ぅ繝ｪ繝・ぅ繧呈ｴｻ逕ｨ**  
   - `TextureEncodingUtility.TryGetPngBytes`・啜exture竊単NG 縺ｮ螳牙・縺ｪ螟画鋤  
   - `UnityWebRequestUtils.SendAsync`・壼・ API 蜻ｼ縺ｳ蜃ｺ縺励〒蜈ｱ騾壼喧縺励◆ await 繝代ち繝ｼ繝ｳ

---

荳ｻ隕∵ｩ溯・縺ｨ繝昴う繝ｳ繝・
------------------
### 1. 繝・く繧ｹ繝・/ 繝槭Ν繝√Δ繝ｼ繝繝ｫ繝√Ε繝・ヨ
```csharp
var messages = new List<Message>
{
    new Message { role = MessageRole.System, content = "縺ゅ↑縺溘・ Unity 繧ｨ繝ｳ繧ｸ繝九い縺ｮ繧｢繧ｷ繧ｹ繧ｿ繝ｳ繝医〒縺吶・ },
    new Message { role = MessageRole.User,   content = "RuntimeInitializeOnLoadMethod 縺ｮ菴ｿ縺・婿繧呈蕗縺医※縲・ }
};
var reply = await AIManager.SendMessageAsync(messages, AIModelType.Gemini25Flash);
```

### 2. JSON Schema 繝吶・繧ｹ縺ｮ讒矩蛹門ｿ懃ｭ・
```csharp
var invoice = await AIManager.SendStructuredMessageAsync<Invoice>(messages, AIModelType.GPT4o);
```
謖・ｮ壹＠縺溷梛縺ｫ蜷医ｏ縺帙※ JSON Schema 繧定・蜍慕函謌舌＠縲∝ｿ懃ｭ斐ｒ繝・す繝ｪ繧｢繝ｩ繧､繧ｺ縺励∪縺吶・
`[Description]`, `[Range]`, `[RegularExpression]` 縺ｪ縺ｩ縺ｮ螻樊ｧ繧剃ｽｿ逕ｨ縺励※縲√ｈ繧願ｩｳ邏ｰ縺ｪ蛻ｶ邏・ｒ螳夂ｾｩ縺ｧ縺阪∪縺吶・

```csharp
public class Invoice
{
    [Description("隲区ｱよ嶌逡ｪ蜿ｷ (萓・ INV-001)")]
    [RegularExpression(@"^INV-\d{3}$")]
    public string InvoiceNumber;

    [Description("蜷郁ｨ磯≡鬘・)]
    [Range(0, 1000000)]
    public double TotalAmount;
}
```
### 陬懆ｶｳ: 迢ｬ閾ｪ縺ｮ繝舌Μ繝・・繧ｷ繝ｧ繝ｳ螻樊ｧ縺ｫ縺､縺・※
`[SchemaRange]` 繧・`[SchemaRegularExpression]` 縺ｯ縲´LM 縺ｫ貂｡縺・**JSON Schema 縺ｮ蛻ｶ邏・擅莉ｶ (`minimum`, `maximum`, `pattern`) 繧堤函謌舌☆繧九◆繧・* 縺ｫ菴ｿ逕ｨ縺励∪縺吶・
縺薙ｌ縺ｫ繧医ｊ縲´LM 縺檎函謌舌☆繧区ｧ矩蛹悶ョ繝ｼ繧ｿ縺ｮ蛟､縺ｮ遽・峇繧・ヵ繧ｩ繝ｼ繝槭ャ繝医ｒ蛻ｶ蠕｡縺ｧ縺阪∪縺吶・

**菴ｿ逕ｨ萓九→逕滓・縺輔ｌ繧・Schema:**
```csharp
public class UserProfile
{
    [Description("蟷ｴ鮨｢")]
    [SchemaRange(0, 150)] // JSON Schema: "minimum": 0, "maximum": 150 縺ｫ螟画鋤縺輔ｌ縺ｾ縺・
    public int Age;

    [Description("繝ｦ繝ｼ繧ｶ繝ｼID (闍ｱ蟆乗枚蟄励・縺ｿ)")]
    [SchemaRegularExpression(@"^[a-z]+$")] // JSON Schema: "pattern": "^[a-z]+$" 縺ｫ螟画鋤縺輔ｌ縺ｾ縺・
    public string UserId;
}
```

### 3. RealTime Schema / Function Calling
- `SendStructuredMessageWithRealTimeSchemaAsync`・啻RealTimeJsonSchema` 縺ｮ蛟､繧帝・蠎ｦ譖ｴ譁ｰ
  - `SchemaParameter` 縺ｫ `Min`, `Max`, `Pattern` 繧定ｨｭ螳壹☆繧九％縺ｨ縺ｧ縲！nspector 荳翫〒蛻ｶ邏・ｒ螳夂ｾｩ蜿ｯ閭ｽ縺ｧ縺吶・
- `SendFunctionCallMessageAsync`・哭LM 縺九ｉ縺ｮ髢｢謨ｰ蜻ｼ縺ｳ蜃ｺ縺礼ｵ先棡繧・`IJsonSchema` 縺ｨ縺励※蜿門ｾ・

### 4. 逕ｻ蜒丞・蜃ｺ蜉・(Gemini 2.5 Flash Image Preview / Gemini 3 Pro Image Preview)
```csharp
var editMessages = new List<Message>
{
    new Message
    {
        role = MessageRole.User,
        parts = new List<MessageContent>
        {
            MessageContent.FromText("豌ｴ蠖ｩ逕ｻ鬚ｨ縺ｫ縺励※縺上□縺輔＞縲・),
            MessageContent.FromImage(texture) // Texture2D 縺九ｉ閾ｪ蜍・PNG 螟画鋤
        }
    }
};
var images = await AIManager.GenerateImagesAsync(editMessages);
```

### 5. 蝓九ａ霎ｼ縺ｿ繝吶け繝医Ν
```csharp
var embedding = await EmbeddingManager.CreateEmbeddingAsync(
    "Unity loves C#",
    EmbeddingModelType.Gemini01_1536); // Gemini 01 縺ｮ蜃ｺ蜉帶ｬ｡蜈・ｒ 1,536 縺ｫ謖・ｮ・
var ranked = EmbeddingManager.RankByCosine(queryEmbedding, corpusEmbeddings);
```

---

繧ｵ繝ｳ繝励Ν繧ｳ繝ｼ繝・
--------------
| 繝輔ぃ繧､繝ｫ | 蜀・ｮｹ |
| --- | --- |
| `Samples~/Example/ExampleUsage.cs` | 繝・く繧ｹ繝医メ繝｣繝・ヨ / 讒矩蛹門ｿ懃ｭ・/ RealTime Schema / Function Calling |
| `Samples~/Example/VisionSamples.cs` | Gemini 逕ｻ蜒冗ｷｨ髮・・Vision 繝｢繝・Ν縺ｧ縺ｮ逕ｻ蜒剰ｧ｣譫・|
| `Samples~/Example/EmbeddingSample.cs` | Embedding 縺ｮ邱壼ｽ｢貍皮ｮ励→繧ｳ繧ｵ繧､繝ｳ鬘樔ｼｼ蠎ｦ險育ｮ・|

縺ｩ縺ｮ繧ｵ繝ｳ繝励Ν繧・MonoBehaviour 繧偵す繝ｼ繝ｳ縺ｫ驟咲ｽｮ縺励√う繝ｳ繧ｹ繝壹け繧ｿ繝ｼ縺ｮ ContextMenu 縺九ｉ螳溯｡後〒縺阪∪縺吶・ 
逕ｻ蜒冗ｳｻ縺ｯ `Application.persistentDataPath` 縺ｫ逕滓・邨先棡繧剃ｿ晏ｭ倥＠縺ｾ縺吶・

---

API 繝ｪ繝輔ぃ繝ｬ繝ｳ繧ｹ・域栢邊具ｼ・
------------------------
### Message / MessageContent
- `Message.content`・壹ユ繧ｭ繧ｹ繝医・縺ｿ縺ｮ邁｡譏灘・蜉・
- `Message.parts`・啻MessageContent` 縺ｮ繝ｪ繧ｹ繝医ゅユ繧ｭ繧ｹ繝医・逕ｻ蜒上ｒ豺ｷ蝨ｨ縺輔○繧句ｴ蜷医・縺薙■繧峨ｒ菴ｿ逕ｨ
- `MessageContent.FromImage(Texture texture, string mime = "image/png")`・啜exture 繧・PNG 縺ｫ螟画鋤縺励※逕ｻ蜒上ヱ繝ｼ繝医ｒ逕滓・・磯撼 readable 繧り・蜍募ｯｾ蠢懶ｼ・
- `MessageContent.FromImageData(byte[] data, string mime)`・壽里蟄倥・繝舌う繝亥・縺九ｉ逕滓・
- `MessageContent.FromImageUrl(string url)`・啅RL 邨檎罰縺ｧ逕ｻ蜒上ｒ蜿ら・

### AIManager
- `SendMessageAsync`・夐壼ｸｸ縺ｮ繝√Ε繝・ヨ
- `SendStructuredMessageAsync<T>`・壽ｧ矩蛹悶Ξ繧ｹ繝昴Φ繧ｹ・・SON Schema・・
- `SendStructuredMessageWithRealTimeSchemaAsync`・啌ealTimeJsonSchema 縺ｮ蛟､譖ｴ譁ｰ
- `SendFunctionCallMessageAsync`・哭LM 縺九ｉ縺ｮ髢｢謨ｰ蜻ｼ縺ｳ蜃ｺ縺礼ｵ先棡繧貞女縺大叙繧翫～IJsonSchema` 繧定ｿ斐☆
- `GenerateImagesAsync` / `GenerateImageAsync`・哦emini 逕ｻ蜒冗函謌・

### EmbeddingManager
- `CreateEmbeddingAsync(string text, EmbeddingModelType model = EmbeddingModelType.Gemini01)`・唹penAI / Gemini 縺ｮ蝓九ａ霎ｼ縺ｿ繧貞叙蠕暦ｼ・emini 01 縺ｯ繝｢繝・Ν遞ｮ蛻･縺ｧ谺｡蜈・焚繧帝∈謚橸ｼ・
- `EmbeddingModelType.Gemini01 / Gemini01_1536 / Gemini01_768`・哦emini Embedding 001 縺ｮ蜃ｺ蜉帶ｬ｡蜈・が繝励す繝ｧ繝ｳ
- `RankByCosine`・夊､・焚縺ｮ蝓九ａ霎ｼ縺ｿ縺ｫ蟇ｾ縺励※繧ｳ繧ｵ繧､繝ｳ鬘樔ｼｼ蠎ｦ縺ｧ繝ｩ繝ｳ繧ｯ莉倥￠

---

陬懆ｶｳ繝ｻ豕ｨ諢冗せ
------------
- 逕ｻ蜒冗函謌舌〒縺ｯ繝・ヵ繧ｩ繝ｫ繝医〒 PNG 繧呈桶縺・∪縺吶・PEG 遲峨′蠢・ｦ√↑蝣ｴ蜷医・ `initBody` 縺ｧ `"generationConfig"` 繧定ｿｽ蜉縺励；emini 蛛ｴ縺ｮ莉墓ｧ倥↓蜷医ｏ縺帙※縺上□縺輔＞縲・
- GPU 隱ｭ縺ｿ謌ｻ縺励・迺ｰ蠅・↓繧医▲縺ｦ繧ｳ繧ｹ繝医′螟ｧ縺阪￥縺ｪ繧句ｴ蜷医′縺ゅｊ縺ｾ縺吶るｻ郢√↓蜻ｼ縺ｳ蜃ｺ縺吝ｴ蜷医・繝・け繧ｹ繝√Ε繧偵≠繧峨°縺倥ａ readable 縺ｫ縺励※縺翫￥縺薙→繧呈耳螂ｨ縺励∪縺吶・
- API 繧ｭ繝ｼ縺瑚ｨｭ螳壹＆繧後※縺・↑縺・ｴ蜷医・繧ｨ繝ｩ繝ｼ繝ｭ繧ｰ縺ｧ繝偵Φ繝医ｒ陦ｨ遉ｺ縺励∪縺吶ゅ∪縺壹・ `AIManagerBehaviour` 縺ｮ險ｭ螳夂憾諷九ｒ遒ｺ隱阪＠縺ｦ縺上□縺輔＞縲・

---

繝ｩ繧､繧ｻ繝ｳ繧ｹ
----------
譛ｬ繝代ャ繧ｱ繝ｼ繧ｸ縺ｯ Unity 繝励Ο繧ｸ繧ｧ繧ｯ繝亥・縺ｧ縺ｮ蛻ｩ逕ｨ繧呈Φ螳壹＠縺ｦ縺・∪縺吶りｩｳ邏ｰ縺ｯ繝ｪ繝昴ず繝医Μ縺ｮ繝ｩ繧､繧ｻ繝ｳ繧ｹ繝輔ぃ繧､繝ｫ繧偵＃隕ｧ縺上□縺輔＞縲・
