# Wiplayer

범용 Windows 미디어 플레이어 - FFmpeg 기반으로 대부분의 영상 포맷을 지원합니다.

## 주요 기능

### MVP (1차)
- 파일 열기/드래그앤드롭
- 재생/일시정지/정지/시크
- 자막 지원 (SRT/ASS/SSA)
  - 자동 탐색 (동일 폴더/파일명 매칭)
  - 싱크 조절
  - 폰트/크기/테두리 설정
- 배속 재생 (0.2x ~ 4.0x)
- 구간 반복 (A-B)
- 오디오 트랙 선택
- 볼륨/음소거
- 전체화면/미니플레이어
- 이어보기 (마지막 위치 저장)
- 최근 재생 목록

### 2차 (예정)
- 네트워크 재생 (HTTP/HLS)
- 썸네일 탐색
- 오디오 이퀄라이저
- 화면 캡처

## 기술 스택

- **언어/프레임워크**: C# (.NET 8) + WPF
- **재생 엔진**: FFmpeg (FFmpeg.AutoGen)
- **비디오 렌더링**: WriteableBitmap (WPF)
- **오디오 출력**: NAudio (WASAPI)
- **하드웨어 가속**: D3D11VA / DXVA2 / NVDEC / QSV (자동 선택)

## 빌드 방법

### 요구사항

1. .NET 8 SDK
2. Visual Studio 2022 또는 VS Code
3. FFmpeg 바이너리 (아래 참고)

### FFmpeg 설치

[ffmpeg.zeranoe.com](https://github.com/BtbN/FFmpeg-Builds/releases) 또는 [gyan.dev](https://www.gyan.dev/ffmpeg/builds/)에서 Windows 빌드를 다운로드하세요.

필요한 DLL 파일:
- `avcodec-*.dll`
- `avformat-*.dll`
- `avutil-*.dll`
- `swresample-*.dll`
- `swscale-*.dll`

이 파일들을 `src/Wiplayer.FFmpeg/ffmpeg/` 폴더에 복사하세요.

### 빌드

```bash
# 솔루션 빌드
dotnet build Wiplayer.sln

# Release 빌드
dotnet build Wiplayer.sln -c Release

# 실행
dotnet run --project src/Wiplayer/Wiplayer.csproj
```

### 배포

```bash
# Self-contained 단일 파일 배포
dotnet publish src/Wiplayer/Wiplayer.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish

# FFmpeg DLL을 publish 폴더에 복사
copy src/Wiplayer.FFmpeg/ffmpeg/*.dll publish/
```

## 단축키

| 키 | 기능 |
|---|---|
| Space | 재생/일시정지 |
| S | 정지 |
| ← / → | 5초 뒤로/앞으로 |
| Ctrl+← / Ctrl+→ | 30초 뒤로/앞으로 |
| ↑ / ↓ | 볼륨 올리기/내리기 |
| M | 음소거 토글 |
| F / Enter | 전체화면 토글 |
| Escape | 전체화면 종료 |
| [ / ] | 배속 감소/증가 |
| Backspace | 배속 초기화 (1.0x) |
| V | 자막 토글 |
| Ctrl+[ / Ctrl+] | 자막 싱크 조절 |
| R | A-B 구간 반복 |
| , / . | 프레임 뒤로/앞으로 |
| Ctrl+O | 파일 열기 |

## 프로젝트 구조

```
Wiplayer.sln
├── src/
│   ├── Wiplayer/                 # WPF 애플리케이션
│   │   ├── Views/                # XAML 뷰
│   │   ├── ViewModels/           # MVVM ViewModels
│   │   ├── Services/             # 플레이어 서비스
│   │   ├── Converters/           # 값 변환기
│   │   └── Themes/               # 스타일/테마
│   │
│   ├── Wiplayer.Core/            # 핵심 비즈니스 로직
│   │   ├── Player/               # 플레이어 상태/인터페이스
│   │   ├── Playlist/             # 재생목록/최근파일
│   │   ├── Settings/             # 설정
│   │   └── Utils/                # 유틸리티
│   │
│   ├── Wiplayer.FFmpeg/          # FFmpeg 래퍼
│   │   ├── Demuxer/              # 디멀티플렉서
│   │   ├── Decoder/              # 비디오/오디오 디코더
│   │   └── ffmpeg/               # FFmpeg 바이너리 (DLL)
│   │
│   ├── Wiplayer.Renderer/        # 렌더링
│   │   ├── Video/                # 비디오 렌더러
│   │   └── Audio/                # 오디오 렌더러
│   │
│   └── Wiplayer.Subtitle/        # 자막 처리
│       └── Parsers/              # SRT/ASS 파서
```

## 라이선스

이 프로젝트는 MIT 라이선스입니다.

**주의**: FFmpeg는 LGPL/GPL 라이선스입니다. 배포 시 라이선스 조건을 확인하세요.

## 기여

이슈와 PR을 환영합니다!
