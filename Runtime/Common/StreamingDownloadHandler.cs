using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine.Networking;

namespace UnityLLMAPI.Common
{
    /// <summary>
    /// DownloadHandlerScript that streams decoded UTF-8 text chunks while also retaining the full response body.
    /// It can also parse server-sent event data when created via <see cref="ForServerSentEvents"/>.
    /// </summary>
    internal sealed class StreamingDownloadHandler : DownloadHandlerScript
    {
        private readonly Action<string> _onTextChunk;
        private readonly ServerSentEventParser _serverSentEventParser;
        private readonly MemoryStream _rawBytes;
        private readonly Decoder _decoder;
        private char[] _charBuffer;

        public StreamingDownloadHandler(Action<string> onTextChunk, int receiveBufferSize = 8192)
            : this(onTextChunk, null, receiveBufferSize)
        {
        }

        private StreamingDownloadHandler(
            Action<string> onTextChunk,
            Action<string> onServerSentEventData,
            int receiveBufferSize = 8192)
            : base(new byte[Math.Max(1024, receiveBufferSize)])
        {
            _onTextChunk = onTextChunk;
            _serverSentEventParser = onServerSentEventData == null ? null : new ServerSentEventParser(onServerSentEventData);
            _rawBytes = new MemoryStream(16 * 1024);
            _decoder = Encoding.UTF8.GetDecoder();
            _charBuffer = new char[8 * 1024];
        }

        public static StreamingDownloadHandler ForServerSentEvents(
            Action<string> onEventData,
            int receiveBufferSize = 8192)
        {
            return new StreamingDownloadHandler(
                onTextChunk: null,
                onServerSentEventData: onEventData,
                receiveBufferSize: receiveBufferSize);
        }

        protected override bool ReceiveData(byte[] data, int dataLength)
        {
            if (data == null || dataLength <= 0) return false;

            _rawBytes.Write(data, 0, dataLength);

            if (_onTextChunk != null || _serverSentEventParser != null)
            {
                var charCount = _decoder.GetCharCount(data, 0, dataLength);
                EnsureCharBufferCapacity(charCount);
                var decoded = _decoder.GetChars(data, 0, dataLength, _charBuffer, 0);
                if (decoded > 0)
                {
                    var chunk = new string(_charBuffer, 0, decoded);
                    _onTextChunk?.Invoke(chunk);
                    _serverSentEventParser?.Feed(chunk);
                }
            }

            return true;
        }

        protected override byte[] GetData() => _rawBytes.ToArray();

        protected override string GetText()
        {
            try
            {
                return Encoding.UTF8.GetString(_rawBytes.ToArray());
            }
            catch
            {
                return string.Empty;
            }
        }

        public void CompleteServerSentEvents()
        {
            _serverSentEventParser?.Complete();
        }

        public override void Dispose()
        {
            CompleteServerSentEvents();
            _rawBytes?.Dispose();
            base.Dispose();
        }

        private void EnsureCharBufferCapacity(int required)
        {
            if (required <= _charBuffer.Length) return;
            _charBuffer = new char[Math.Max(required, _charBuffer.Length * 2)];
        }

        /// <summary>
        /// Minimal parser for server-sent events.
        /// Feeds text chunks and emits each data payload on blank line boundaries.
        /// </summary>
        private sealed class ServerSentEventParser
        {
            private readonly Action<string> _onData;
            private readonly StringBuilder _lineBuffer = new StringBuilder();
            private readonly List<string> _dataLines = new List<string>();

            public ServerSentEventParser(Action<string> onData)
            {
                _onData = onData;
            }

            public void Feed(string chunk)
            {
                if (string.IsNullOrEmpty(chunk)) return;

                for (var i = 0; i < chunk.Length; i++)
                {
                    var c = chunk[i];
                    if (c == '\r') continue;

                    if (c != '\n')
                    {
                        _lineBuffer.Append(c);
                        continue;
                    }

                    var line = _lineBuffer.ToString();
                    _lineBuffer.Length = 0;
                    ProcessLine(line);
                }
            }

            public void Complete()
            {
                if (_lineBuffer.Length > 0)
                {
                    ProcessLine(_lineBuffer.ToString());
                    _lineBuffer.Length = 0;
                }

                DispatchEvent();
            }

            private void ProcessLine(string line)
            {
                if (string.IsNullOrEmpty(line))
                {
                    DispatchEvent();
                    return;
                }

                if (!line.StartsWith("data:", StringComparison.Ordinal)) return;

                var value = line.Length > 5 ? line.Substring(5) : string.Empty;
                if (value.StartsWith(" ", StringComparison.Ordinal)) value = value.Substring(1);
                _dataLines.Add(value);
            }

            private void DispatchEvent()
            {
                if (_dataLines.Count == 0) return;

                var payload = string.Join("\n", _dataLines);
                _dataLines.Clear();
                _onData?.Invoke(payload);
            }
        }
    }
}
