using UnityEngine;

// 색과 설명 문자열의 단일 출처.
// 선택지 카드·정보 패널·슬롯 핍·장식 오브젝트가 모두 여기를 읽고, 개별 UI가 자기 색과 문구를 가지지 않는다.
// 설명 원문은 테이블의 description이 원본이고, 이 파일은 정렬·강조 같은 표기만 덧붙인다.
public enum EffectCategory { Offense, Mobility, Defense, Utility, BaseStat }

public static class Presentation
{
    // 범주 팔레트. 새 범주를 추가하면 여기 색과 Title을 함께 추가한다.
    public static Color Color(EffectCategory category) => category switch
    {
        EffectCategory.Offense => new Color(0.90f, 0.33f, 0.29f),
        EffectCategory.Mobility => new Color(0.33f, 0.58f, 0.95f),
        EffectCategory.Defense => new Color(0.35f, 0.76f, 0.44f),
        EffectCategory.BaseStat => new Color(0.96f, 0.78f, 0.27f),
        _ => new Color(0.72f, 0.74f, 0.80f),
    };

    public static string Title(EffectCategory category) => category switch
    {
        EffectCategory.Offense => "공격",
        EffectCategory.Mobility => "이동",
        EffectCategory.Defense => "방어",
        EffectCategory.BaseStat => "기본 능력치",
        _ => "유틸",
    };

    public static EffectCategory Category(UpgradeData upgrade) => GameTableRules.Category(upgrade == null ? null : upgrade.category);
    public static Color Color(UpgradeData upgrade) => Color(Category(upgrade));
    public static string Title(UpgradeData upgrade) => Title(Category(upgrade));

    public static string Name(UpgradeData upgrade) => upgrade == null ? "비어 있음"
        : string.IsNullOrEmpty(upgrade.displayName) ? upgrade.id : upgrade.displayName;

    // 설명은 테이블 원본. 비어 있으면 GameTableRules가 테이블 오류로 보고하므로 여기서 폴백 문구를 두지 않는다.
    public static string Describe(UpgradeData upgrade) => upgrade == null ? string.Empty : upgrade.description ?? string.Empty;

    // 같은 색에서 채도·명도만 낮춰 '기존 보유/약화' 표현을 만든다(팔레트를 새로 만들지 않는다).
    public static Color Shade(Color color, float saturation, float value)
    {
        UnityEngine.Color.RGBToHSV(color, out float hue, out float sat, out float val);
        return UnityEngine.Color.HSVToRGB(hue, sat * saturation, val * value);
    }

    // ── 리치 텍스트 표기 ──
    // 값 강조·Dim·제목 서식을 카드와 패널이 같이 쓴다. 서식 문자열을 개별 UI에 복사하지 않는다.
    const string ValueTag = "#FFE08A";
    const string DimTag = "#AEB6C2";
    public static string Accent(string value) => $"<color={ValueTag}>{value}</color>";
    public static string Dim(string value) => $"<color={DimTag}>{value}</color>";
    public static string Heading(string title, Color color) => $"<size=135%><b><color={Hex(color)}>{title}</color></b></size>";
    public static string Hex(Color color) => "#" + ColorUtility.ToHtmlStringRGB(color);

    // 테이블 항목 하나의 카드 본문(이름·범주·설명). 카드와 목록이 같은 출력을 쓴다.
    public static string Card(UpgradeData upgrade)
    {
        if (upgrade == null) return Dim("유효하지 않은 항목");
        return $"{Heading(Name(upgrade), Color(upgrade))}\n{Dim(Title(upgrade))}\n{Describe(upgrade)}";
    }
}
