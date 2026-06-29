# D102 라벨 인쇄 시스템 (Label Printer)

유비샵 `바코드인쇄`를 크롬 확장이 가로채, **자체 인쇄 프로그램**이 크롬 인쇄창 없이
Zebra로 ZPL 직접 인쇄한다. 확장·프로그램 모두 GitHub 자동업데이트, Win11 x64 설치본.

- 기획서: [기획서-D102라벨인쇄시스템.md](기획서-D102라벨인쇄시스템.md)
- 확장(별도 저장소): https://github.com/aredsea/ubishop-barcode-ext

## 구성 (예정)

```
d102-label-printer/
├── 기획서-D102라벨인쇄시스템.md   # PRD
├── poc/                           # ZPL 개념검증(프린터 없이 Labelary 검증)
│   ├── zpl_proto.ps1              # 레이아웃→ZPL(바코드 네이티브 + 한글 Pretendard 비트맵)
│   └── zpl_label_verified.png     # Labelary 렌더 결과(사진 라벨과 일치 확인)
├── src/                           # (예정) .NET 8 프로그램(트레이+로컬서버+ZPL+WebView2 설정)
└── installer/                     # (예정) Win11 x64 설치본(프로그램+확장 자동등록)
```

## 상태

- **P1 ZPL 엔진 코어 — 검증 완료**: 한글(Pretendard 비트맵 ^GF) + 네이티브 바코드(^BC)
  + mm 좌표 매핑. Labelary(12dpmm=300dpi)로 라벨 시각 검증, 사진 양식과 일치.
- 다음: P2 .NET 스캐폴드(로컬서버+raw인쇄+트레이).

## 인쇄 원리

- Zebra GX430t = 300dpi(12 dots/mm). mm×11.81 = dots. 라벨 60×10mm.
- 바코드: ZPL 네이티브 `^BCN`(Code128) → 선명·스캔 안정.
- 텍스트(한글 포함): Pretendard로 1-bit 비트맵 렌더 → `^GFA` 로 얹음(Zebra 한글 내장폰트 없음 우회).
- 전송: Win32 스풀러 raw 모드로 ZPL 직접 전송 → 크롬/다이얼로그 미경유.
