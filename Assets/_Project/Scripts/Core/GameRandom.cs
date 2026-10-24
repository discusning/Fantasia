using System;

namespace Fantasia.Core
{
    // 게임 결과용 난수 (주사위·판정·적 대상·전리품). 상태 4개를 저장 파일에 넣어 "확정 → 저장 → 표시"를 보장한다
    // (Save.md: 껐다 켜도 같은 결과). 연출·화면 흔들기 등은 UnityEngine.Random을 써도 이 흐름에 영향 없다.
    // xorshift128 — 같은 상태면 같은 수열.
    public static class GameRandom
    {
        private static uint _x = 123456789, _y = 362436069, _z = 521288629, _w = 88675123;

        public static void Seed(int seed)
        {
            uint s = (uint)seed;
            _x = 123456789u ^ s;
            _y = 362436069u ^ (s * 2654435761u);
            _z = 521288629u ^ (s * 40503u);
            _w = 88675123u ^ (s * 2246822519u);
            if ((_x | _y | _z | _w) == 0) _w = 1;
        }

        public static uint[] State
        {
            get => new[] { _x, _y, _z, _w };
            set
            {
                if (value == null || value.Length != 4 || (value[0] | value[1] | value[2] | value[3]) == 0) return;
                (_x, _y, _z, _w) = (value[0], value[1], value[2], value[3]);
            }
        }

        public static uint NextUInt()
        {
            uint t = _x ^ (_x << 11);
            _x = _y; _y = _z; _z = _w;
            return _w = _w ^ (_w >> 19) ^ t ^ (t >> 8);
        }

        // [0, 1)
        public static float Value => (NextUInt() >> 8) * (1f / 16777216f);

        // [min, maxExclusive)
        public static int Range(int min, int maxExclusive)
        {
            if (maxExclusive <= min) return min;
            return min + (int)(NextUInt() % (uint)(maxExclusive - min));
        }

        // 전리품 등 System.Random을 받는 규칙에 넘길 때 — 이 흐름에서 시드를 뽑아 같은 결과를 재현
        public static Random NewSystemRandom() => new Random((int)NextUInt());
    }
}
