using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

public sealed class LogSession : IDisposable
{
    readonly StreamWriter writer;
    long sequence;
    public string SessionId { get; }
    public bool IsClosed { get; private set; }

    public LogSession(FileLogStore _store, string _sessionId = null)
    {
        if (_store == null)
        {
            throw new ArgumentNullException(nameof(_store));
        }
        SessionId = _sessionId ?? Guid.NewGuid().ToString("N");
        writer = _store.Open(SessionId);
    }

    public void Write(string _type, double _time, IDictionary<string, object> _payload)
    {
        if (IsClosed)
        {
            return;
        }
        var record = new Dictionary<string, object>
        {
            { "sessionId", SessionId },
            { "sequence", checked(sequence + 1) },
            { "t", _time },
            { "type", _type },
            { "payload", _payload }
        };
        var line = new StringBuilder();
        AppendJson(line, record);
        writer.WriteLine(line.ToString());
        sequence++;
    }

    public void Close()
    {
        if (IsClosed)
        {
            return;
        }
        IsClosed = true;
        writer.Dispose();
    }

    public void Dispose()
    {
        Close();
    }

    static void AppendJson(StringBuilder _output, object _value)
    {
        if (_value == null)
        {
            _output.Append("null");
        }
        else if (_value is string text)
        {
            AppendString(_output, text);
        }
        else if (_value is bool boolean)
        {
            _output.Append(boolean ? "true" : "false");
        }
        else if (_value is IDictionary<string, object> fields)
        {
            _output.Append('{');
            bool first = true;
            foreach (KeyValuePair<string, object> field in fields)
            {
                if (!first)
                {
                    _output.Append(',');
                }
                AppendString(_output, field.Key);
                _output.Append(':');
                AppendJson(_output, field.Value);
                first = false;
            }
            _output.Append('}');
        }
        else if (_value is IEnumerable items)
        {
            _output.Append('[');
            bool first = true;
            foreach (object item in items)
            {
                if (!first)
                {
                    _output.Append(',');
                }
                AppendJson(_output, item);
                first = false;
            }
            _output.Append(']');
        }
        else if (_value is float single)
        {
            if (float.IsNaN(single) || float.IsInfinity(single))
            {
                throw new ArgumentException("JSON 수치는 유한해야 합니다.");
            }
            _output.Append(single.ToString("R", CultureInfo.InvariantCulture));
        }
        else if (_value is double number)
        {
            if (double.IsNaN(number) || double.IsInfinity(number))
            {
                throw new ArgumentException("JSON 수치는 유한해야 합니다.");
            }
            _output.Append(number.ToString("R", CultureInfo.InvariantCulture));
        }
        else if (_value is byte || _value is sbyte || _value is short || _value is ushort || _value is int || _value is uint || _value is long || _value is ulong || _value is decimal)
        {
            _output.Append(Convert.ToString(_value, CultureInfo.InvariantCulture));
        }
        else
        {
            throw new ArgumentException("지원하지 않는 JSON 값입니다: " + _value.GetType().Name);
        }
    }

    static void AppendString(StringBuilder _output, string _text)
    {
        _output.Append('"');
        foreach (char value in _text)
        {
            switch (value)
            {
                case '"':
                    _output.Append("\\\"");
                    break;
                case '\\':
                    _output.Append("\\\\");
                    break;
                case '\b':
                    _output.Append("\\b");
                    break;
                case '\f':
                    _output.Append("\\f");
                    break;
                case '\n':
                    _output.Append("\\n");
                    break;
                case '\r':
                    _output.Append("\\r");
                    break;
                case '\t':
                    _output.Append("\\t");
                    break;
                default:
                    // 제어 문자와 서로게이트도 이스케이프해 UTF-8 손실을 막는다.
                    if (char.IsControl(value) || char.IsSurrogate(value))
                    {
                        _output.Append("\\u").Append(((int)value).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        _output.Append(value);
                    }
                    break;
            }
        }
        _output.Append('"');
    }
}

