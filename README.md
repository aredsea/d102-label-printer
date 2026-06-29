# D102 라벨 인쇄 시스템

유비샵 `바코드인쇄`를 크롬 확장이 가로채 **D102 인쇄 프로그램**으로 보내고, 프로그램이
**ZPL로 Zebra에 직접 인쇄**한다(크롬 인쇄창 없음). 확장·프로그램 모두 **GitHub 자동업데이트**,
Win11 x64 **설치본 1개**로 자동설치.

- 기획서: [기획서-D102라벨인쇄시스템.md](기획서-D102라벨인쇄시스템.md)
- 확장(얇은 호출자): https://github.com/aredsea/ubishop-barcode-ext
- 다운로드(설치본): [Releases](https://github.com/aredsea/d102-label-printer/releases) → `D102LabelPrinter-win-Setup.exe`

## 구성

```
[유비샵]  바코드인쇄
   │  크롬 확장(얇은 호출자): 상품 데이터 수집 → POST 127.0.0.1:17600/print
   ▼
[D102 인쇄 프로그램]  .NET8 트레이 상주
   ├ 로컬서버(HttpListener) → ZPL 생성(바코드 네이티브 + 한글 Pretendard 비트맵)
   ├ Zebra raw 인쇄(Win32 스풀러)  ← 다이얼로그 없음
   ├ 설정창(WebView2): 위치·문구 편집(드래그/리사이즈/줌/텍스트/표시) → layout.json 파일저장
   └ Velopack 자동업데이트(GitHub 릴리스)
```

## 매장 PC 설치 (1회)

1. [Releases](https://github.com/aredsea/d102-label-printer/releases) 에서 **`D102LabelPrinter-win-Setup.exe`** 다운로드 → 실행.
   - 프로그램 설치 + 트레이 상주 + 바로가기. **확장은 자동 등록**(크롬 정책 force_installed).
   - (서명 미적용이라 SmartScreen 경고 시 "추가 정보 → 실행".)
2. **크롬 재시작** → 확장이 자동 설치됨(유비샵 페이지에서 동작).
3. 트레이 **D102 라벨 인쇄** 우클릭:
   - **프린터 선택** → Zebra GX430t (또는 기본 프린터로 두고 Windows 기본을 Zebra로).
   - **라벨 위치·문구 설정** → 위치/문구 맞추고 저장.
   - **테스트 인쇄** 로 한 장 확인.
4. 유비샵 상품검색 → 체크 → **바코드인쇄** → 다이얼로그 없이 즉시 인쇄.

## 자동 업데이트

- **프로그램**: 실행 시 GitHub 릴리스 확인 → 새 버전 자동 다운로드·적용(Velopack).
- **확장**: 크롬이 `update.xml`(force_installed)로 .crx 자동 갱신 + 로직은 라이브로더로 즉시 반영.

## 빌드 / 새 버전 배포 (개발자)

요구: .NET 8 SDK, `vpk`(=Velopack CLI), gh 로그인. WebView2 런타임은 Win11 기본 탑재.

```powershell
# 프로그램 새 버전(예: 0.1.1)
cd src/D102LabelPrinter
dotnet publish -c Release -r win-x64 --self-contained true -o publish
vpk pack --packId D102LabelPrinter --packVersion 0.1.1 --packDir publish --mainExe D102LabelPrinter.exe --packTitle "D102 라벨 인쇄" --outputDir ../../Releases
cd ../..
vpk upload github --repoUrl https://github.com/aredsea/d102-label-printer --publish --releaseName "0.1.1" --tag v0.1.1 --token <gh token>
# → 매장 PC가 다음 실행 때 자동 갱신
```

확장 코드 수정은 그냥 `ubishop-barcode-ext` 저장소에 push(라이브로더가 자동 반영).
확장 manifest/loader 가 바뀐 경우에만 .crx 재패킹 필요:
```powershell
chrome.exe --pack-extension="<clean ext dir>" --pack-extension-key="ubishop-barcode-ext.pem"
# → 새 .crx 를 ubishop-barcode-ext 저장소에 덮어쓰고 update.xml version 올림
```

> ⚠ **`ubishop-barcode-ext.pem`(확장 서명키)은 절대 공개 금지 + 반드시 백업.**
> 잃어버리면 확장 ID 가 바뀌어 기존 자동설치/업데이트가 끊긴다. (gitignore 처리됨)

## 검증 상태 (프린터 없이 Labelary 로)

- P1 ZPL 엔진(한글 비트맵+네이티브 바코드) · P2 프로그램(서버+ZPL+raw+트레이) ·
  P3 확장 얇은 호출자 · P4 설정 편집기(파일 완벽저장) · P5 Velopack 자동업데이트 ·
  P6 설치본+확장 자동등록 — **전부 구현·검증 완료**.
- 실제 종이 출력만 매장 Zebra 에서 최종 확인 필요.
