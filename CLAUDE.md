# 프로젝트 지침

기획 기준은 `Docs/POC_CORE.md`, 착수 결정은 `Docs/DECISIONS.md`를 따른다.

## 씬

- 빌드 씬은 **Preload → Title → Game** 순서를 유지한다. Preload는 테이블·에셋 로드, Title은 진입·시작, Game은 본플레이다.

## 데이터

- 확정된 수치는 전부 테이블화한다. **SO 대신 JSON 테이블**을 우선한다(`Assets/Resources/Tables`, 직렬화 클래스, `TableManager` 로드·검증). SO는 장기 참조나 인스펙터 연결이 꼭 필요할 때만 쓴다.
- 코드에 수치를 하드코딩하지 않는다. 예외는 주석으로 이유를 남긴다.

## 아키텍처

- UI는 **MVP 패턴**으로 구성한다. View는 코드로 생성하지 않고 프리팹에 배치한다.
- 메시·오브젝트·레벨을 코드로 생성하지 않고 씬과 프리팹으로 제작한다. 런타임 생성은 게임플레이 스폰(적·탄환·파티클)과 플레이어가 설치한 시설만 허용한다.

## 로그

- 분석 기록은 `LogSession` → `FileLogStore` 한 경로로, `Application.persistentDataPath/Telemetry/{sessionId}.jsonl`에 남긴다.
- LogKit은 콘솔·오류 수집만 맡는다. 같은 사건을 양쪽에 기록하지 않고, 서버 주소·키를 저장소에 두지 않으며, 업로드 기본값은 꺼짐이다.

## 코딩 스타일

- 중괄호는 **Allman** 방식이다. 한 줄 if/for를 쓰지 않고, 중괄호를 생략하지 않으며, 본문은 새 줄에 쓴다.
- 클래스와 함수는 PascalCase, 변수와 멤버는 camelCase(가독성이 떨어지면 snake_case 허용)로 쓴다. 헝가리안 표기법은 금지한다.
- 파라미터는 **`_` 접두사** 필수다(`void SetDamage(int _damage)`).
- const와 static은 **전부 대문자 + 언더스코어**다(`const int MAX_LENGTH = 100;`).

## Unity 작업

- Unity 작업은 **Unity CLI(`unity`) 사용을 최우선으로 고려**한다. 컴파일 확인, 테스트 실행, Play Mode 제어, 씬·에셋 조작, 빌드, 콘솔 로그 확인 모두 `unity command ...`로 먼저 시도한다.
- CLI로 할 수 없을 때만 다른 수단(에디터 메뉴 스크립트, `-batchMode` 실행, 직접 파일 편집 등)을 쓰고, 그 이유를 작업 보고에 남긴다.

## Git

- **Git Flow** 전략을 사용한다. `master`는 릴리스, `develop`은 통합 브랜치이며, 작업은 `develop`에서 분기한 `feature/*`에서 한다. `release/*`, `hotfix/*`도 Git Flow 규칙을 따른다.
- 커밋 메시지에 `Co-Authored-By` 줄을 넣지 않는다.
- 커밋 메시지는 `[범위] 내용` 형식을 권장한다(예: `[wave] 적 편성 JSON 연동`).
- 커밋 전에 코드 정리(Visual Studio Ctrl+K, R)와 컴파일 경고를 확인한다.
