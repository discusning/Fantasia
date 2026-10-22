namespace Fantasia.Data
{
    // 8개 플레이어블 직업 (System.md "직업(플레이어블 캐릭터)"). 1차 구현 대상은
    // Detective·PlagueDoctor·Sniper 3명이며, 나머지 5개는 스키마만 존재하고
    // 값 입력은 보류 상태다.
    public enum JobId
    {
        Detective,      // 명탐정
        PlagueDoctor,   // 역병 의사
        Sniper,         // 저격수
        Inquisitor,     // 이단심문관
        Alchemist,      // 연금술사
        Conductor,      // 지휘자
        Assassin,       // 암살자
        Gambler,        // 도박가
    }
}
