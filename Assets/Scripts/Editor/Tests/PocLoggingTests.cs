using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

public sealed class PocLoggingTests
{
    string folder;
    FileLogStore store;

    [SetUp]
    public void SetUp()
    {
        folder = Path.Combine(Path.GetTempPath(), "PocLoggingTests", Guid.NewGuid().ToString("N"));
        store = new FileLogStore(folder);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, true);
        }
    }

    [Test]
    public void SequenceStartsAtOneAndEveryLineParses()
    {
        using (var session = new LogSession(store, "sequence-test"))
        {
            for (int index = 0; index < 3; index++)
            {
                session.Write("WaveStarted", index * 0.25, new Dictionary<string, object> { { "index", index } });
            }
        }
        string[] lines = File.ReadAllLines(store.PathFor("sequence-test"));
        Assert.That(lines.Length, Is.EqualTo(3));
        for (int index = 0; index < lines.Length; index++)
        {
            ParsedRecord record = JsonUtility.FromJson<ParsedRecord>(lines[index]);
            Assert.That(record.sequence, Is.EqualTo(index + 1));
            Assert.That(record.sessionId, Is.EqualTo("sequence-test"));
            Assert.That(record.t, Is.EqualTo(index * 0.25));
            Assert.That(record.type, Is.EqualTo("WaveStarted"));
            Assert.That(record.payload.index, Is.EqualTo(index));
        }
    }

    [Test]
    public void SpecialCharactersRoundTripWithoutBreakingJsonLines()
    {
        const string TEXT = "한국어 \"인용\" \\ 경로\n\r\t\b\f\0\u001f 😀";
        using (var session = new LogSession(store, "escape-test"))
        {
            session.Write(TEXT, 0, new Dictionary<string, object>
            {
                { "text", TEXT }, { "party", new[] { TEXT, "s2" } }, { "optional", null },
                { "flag", true }, { "position", new Dictionary<string, object> { { "x", 1.25f } } },
                { "quote\"\\\n", TEXT }
            });
        }
        string[] lines = File.ReadAllLines(store.PathFor("escape-test"));
        Assert.That(lines.Length, Is.EqualTo(1));
        // JsonUtility는 NUL을 잘라내므로 기존 설치된 파서로 제어 문자까지 검증한다.
        JObject parsed = JObject.Parse(lines[0]);
        Assert.That((string)parsed["type"], Is.EqualTo(TEXT));
        Assert.That((string)parsed["payload"]["text"], Is.EqualTo(TEXT));
        Assert.That(parsed["payload"]["party"].ToObject<string[]>(), Is.EqualTo(new[] { TEXT, "s2" }));
        Assert.That((bool)parsed["payload"]["flag"], Is.True);
        Assert.That((float)parsed["payload"]["position"]["x"], Is.EqualTo(1.25f));
        Assert.That(lines[0], Does.Contain("\\u0000").And.Contain("\\u001f").And.Contain("quote\\\"\\\\\\n"));
    }

    [Test]
    public void NumbersUseInvariantCultureAndFileHasNoBom()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            using (var session = new LogSession(store, "culture-test"))
            {
                session.Write("test", 1.25, new Dictionary<string, object> { { "amount", 2.5f } });
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
        string path = store.PathFor("culture-test");
        byte[] bytes = File.ReadAllBytes(path);
        Assert.That(bytes[0], Is.EqualTo((byte)'{'));
        string line = File.ReadAllLines(path)[0];
        ParsedRecord parsed = JsonUtility.FromJson<ParsedRecord>(line);
        Assert.That(parsed.t, Is.EqualTo(1.25));
        Assert.That(parsed.payload.amount, Is.EqualTo(2.5f));
        Assert.That(line, Does.Contain("\"amount\":2.5"));
    }

    [Test]
    public void CloseAndDisposeAreIdempotentAndIgnoreLaterWrites()
    {
        var session = new LogSession(store, "close-test");
        session.Write("before", 0, null);
        session.Close();
        session.Close();
        session.Dispose();
        Assert.DoesNotThrow(() => session.Write("after", double.NaN, null));
        Assert.That(session.IsClosed, Is.True);
        Assert.That(File.ReadAllLines(store.PathFor(session.SessionId)).Length, Is.EqualTo(1));
        using (var file = new FileStream(store.PathFor(session.SessionId), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.That(file.Length, Is.GreaterThan(0));
        }
    }

    [Test]
    public void InvalidJsonNumbersDoNotConsumeSequenceOrWritePartialLines()
    {
        using (var session = new LogSession(store, "invalid-test"))
        {
            Assert.Throws<ArgumentException>(() => session.Write("invalid", double.NaN, null));
            Assert.Throws<ArgumentException>(() => session.Write("invalid", 0, new Dictionary<string, object> { { "amount", float.PositiveInfinity } }));
            session.Write("valid", 0, null);
        }
        string[] lines = File.ReadAllLines(store.PathFor("invalid-test"));
        Assert.That(lines.Length, Is.EqualTo(1));
        Assert.That(JsonUtility.FromJson<ParsedRecord>(lines[0]).sequence, Is.EqualTo(1));
    }

    [Test]
    public void StoreRejectsPathTraversalAndDuplicateSessionFiles()
    {
        Assert.Throws<ArgumentException>(() => store.PathFor("../outside"));
        Assert.Throws<ArgumentException>(() => store.PathFor(".."));
        using (var session = new LogSession(store, "unique-test"))
        {
            Assert.Throws<IOException>(() => new LogSession(store, "unique-test"));
        }
        using (var first = new LogSession(store))
        using (var second = new LogSession(store))
        {
            Assert.That(first.SessionId, Is.Not.EqualTo(second.SessionId));
        }
    }

    [Serializable]
    public sealed class ParsedRecord
    {
        public string sessionId = null;
        public long sequence = 0;
        public double t = 0;
        public string type = null;
        public ParsedPayload payload = null;
    }

    [Serializable]
    public sealed class ParsedPayload
    {
        public int index = 0;
        public string text = null;
        public string[] party = null;
        public bool flag = false;
        public float amount = 0;
        public ParsedPosition position = null;
    }

    [Serializable]
    public sealed class ParsedPosition
    {
        public float x = 0;
    }
}
