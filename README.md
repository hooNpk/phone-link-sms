# 단체문자발송

엑셀 명단을 불러와 **내 안드로이드 휴대폰 번호로** 한 명씩 자동으로 문자를 보내는 Windows 프로그램입니다.
Windows 기본 앱인 '휴대폰과 연결(Phone Link)'을 UI 자동화로 조작해 보냅니다.

- **소개 페이지:** https://hoonpk.github.io/phone-link-sms/
- **다운로드:** [최신 릴리스](https://github.com/hooNpk/phone-link-sms/releases/latest) — `단체문자발송.exe` 하나만 받아 실행하면 됩니다 (설치 불필요)

## 특징

- **문자 대행 비용 없음** — 내 휴대폰 요금제의 문자로 보냅니다 (장문·사진 문자는 요금제에 따라 과금될 수 있음)
- **1분에 약 6.6건** — 100명 기준 약 15분
- **명단을 어디에도 올리지 않음** — 서버·회원가입 없이 모든 처리를 내 PC에서 합니다
- `{이름}` 자동 치환, SMS 수신거부 자동 제외, 번호 오류·중복 검사, 테스트 발송 필수, 중단·재개, 발송 기록 저장

## 사용 환경

| 필요 | 지원 안 함 |
|---|---|
| Windows 10 / 11 (64비트) + '휴대폰과 연결' 앱 | 아이폰 |
| 안드로이드 휴대폰 + 'Windows와 연결' | Mac · 리눅스 |
| 엑셀 명단 (.xlsx) | 옛 엑셀 형식 (.xls) |

처음 실행할 때 'Windows의 PC 보호' 창이 뜨면 **[추가 정보] → [실행]**을 누르세요 (코드 서명이 없는 프로그램이라 처음 한 번 뜹니다).

## 사용 순서

0. **휴대폰 연결** — '휴대폰과 연결'과 휴대폰의 'Windows와 연결'이 켜져 있는지 확인
1. **명단 불러오기** — 이름·전화번호·수신여부 칸 자동 감지
2. **문구 작성** — 사진 첨부, `{이름}` 치환, 받는 사람 화면 미리보기
3. **발송 확인** — 내 번호로 테스트 발송 (성공해야 전체 발송 가능)
4. **발송** — 진행 상황·남은 시간 표시, 일시정지·중단·재개

발송 기록(성공·실패 목록, 실패 순간 화면 캡처)은 `%AppData%\PhoneLinkSms\logs`에 저장되며, 프로그램 상단의 **[발송 기록]** 버튼으로 언제든 열 수 있습니다.

## 개발

```
PhoneLinkSms.slnx
PhoneLinkSmsApp/            .NET 10 WPF 앱 (어셈블리 이름 PhoneLinkSms)
  Models/                   Recipient, SendSession
  Services/
    ExcelParser.cs          헤더 행·이름/전화/SMS 컬럼 자동 감지 (ClosedXML)
    PhoneNumberNormalizer   010-XXXX-XXXX 정규화
    RecipientRules.cs       수신거부 판정, 오류·중복 검사
    MessageTemplate.cs      {이름} 치환
    PhoneLinkAutomation.cs  UI 자동화 발송 — '휴대폰과 연결' 앱이 바뀌면 파일 상단 식별자부터 확인
    SendWorker.cs           전용 STA 스레드에서 순차 발송, 일시정지/중단
    SendLogger.cs           발송 기록 (CSV·문구·스크린샷)
  Settings/AppSettings.cs   %AppData%\PhoneLinkSms\settings.json
  Views/Step0~4             단계별 화면
PhoneLinkSmsApp.Tests/      xUnit (파서·정규화·검증 규칙·{이름} 치환)
tools/IconGen/              앱 아이콘 생성기
docs/                       소개 페이지 (GitHub Pages)
publish.ps1                 테스트 → dist\단체문자발송.exe 단일 파일 배포
```

```powershell
dotnet test                  # 단위 테스트
.\publish.ps1                # dist\단체문자발송.exe (자체 포함 단일 파일)
dotnet run --project tools\IconGen -- PhoneLinkSmsApp\Assets\app.ico   # 아이콘 다시 만들기
```

UI 자동화(`PhoneLinkAutomation`)는 STA 스레드에서만 호출해야 합니다 (`StaThread.Run`). `Task.Run`은 MTA라 쓰지 않습니다.
`SendMessage(..., dryRun: true)`로 보내기 버튼을 누르기 직전까지만 점검할 수 있습니다.
