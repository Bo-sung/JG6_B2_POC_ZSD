using System;
using System.Collections.Generic;
using System.Text;

// 용도별 소비 횟수가 다른 난수 흐름에 영향을 주지 않게 분리한다.
public sealed class SessionRng
{
    // FNV-1a 표준 상수이며 게임 밸런스 수치가 아니다.
    const uint FNV_OFFSET = 2166136261;
    const uint FNV_PRIME = 16777619;
    readonly int seed;
    readonly Dictionary<string, Random> streams = new Dictionary<string, Random>(StringComparer.Ordinal);

    public SessionRng(int _seed)
    {
        seed = _seed;
    }

    public Random For(string _stream)
    {
        if (_stream == null)
        {
            throw new ArgumentNullException(nameof(_stream));
        }
        if (!streams.TryGetValue(_stream, out Random random))
        {
            uint hash = FNV_OFFSET;
            unchecked
            {
                foreach (byte value in Encoding.UTF8.GetBytes(_stream))
                {
                    hash = (hash ^ value) * FNV_PRIME;
                }
                hash = (hash ^ (uint)seed) * FNV_PRIME;
            }
            // Random은 부호 반전 시드가 같으므로 비음수 영역으로 제한한다.
            random = new Random((int)(hash & int.MaxValue));
            streams.Add(_stream, random);
        }
        return random;
    }
}

