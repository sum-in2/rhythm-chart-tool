# Changelog

이 파일은 [Keep a Changelog](https://keepachangelog.com/ko/1.1.0/) 형식을 따르고, 버전은 [유의적 버전](https://semver.org/lang/ko/)을 따릅니다.
1.0.0 전에는 호환되지 않는 변경이 있을 수 있습니다.

## [0.1.0] - 2026-11-03

첫 번째 버전.

### Added

- **오디오 분석**: `Python~/analyze.py`(librosa)가 BPM, 고정 그리드 offset, 박자(`beats`), onset(시각, 세기, 저/중/고 대역)을 분석 JSON 으로 출력합니다. 진행률(`PROGRESS`)과 종료 코드 규약, 선택 옵션 `--separate`(demucs 드럼 분리).
- **Unity 연동**: 설정 창(Python 경로, 프로젝트별 venv), venv 생성과 `pip install` 을 비동기로 실행하는 `Python 환경 설정`, 환경이 없을 때 설정을 권하는 확인창, `Analyze Selected Audio` 메뉴(진행률 표시, 취소, 한글·공백 경로, stderr 오류 출력), 분석 결과를 `ChartData` 에셋으로 저장. 재분석은 분석 데이터만 갱신하고 노트는 유지합니다.
- **난이도 자동 생성**: `ChartGenerator`(순수 C#)가 strength 상위 N% 선별, 그리드 스냅, 최소 간격, 동시치기, 롱노트, 대역 기반 레인 배치(같은 레인 연속 금지, 점프 제한, 계단/트릴 패턴)를 처리합니다. 자체 PRNG 로 같은 시드는 같은 결과입니다. 난이도 4개의 기본 프리셋(`DifficultyPreset`)과 레인 수(4~6) 설정.
- **차트 JSON**: `ChartJson` 입출력, `schemaVersion`, 마이그레이션 훅(`ChartJsonMigrations`), 잘못된 입력은 대상을 바꾸지 않고 거부.
- **차트 인스펙터**: 분석 요약, 레인 수/시드, 난이도별 생성, 프리셋 슬라이더와 프로젝트로 복사, JSON 내보내기/가져오기.
- **차트 에디터**: 파형(캐시), 눈금자, 비트 그리드와 스냅(1/4·1/8·1/16), 노트 추가/삭제/이동/롱노트 생성, 다중 선택, 도구 모드(Q/W/E)와 단축키, 재생 헤드와 미리듣기, 난이도 탭, Undo/Redo, 난이도별 통계 패널, **가로/세로 타임라인**.
- **재생**: `AudioSettings.dspTime` 기준 `ChartPlayer`, `SongClock`, 순수 C# 판정 `ChartRun`(Perfect/Good/Miss, 롱노트), Input System 키보드와 IMGUI 화면의 `ChartPlayerView`, 에디터의 "현재 위치부터 플레이 테스트".
- **샘플**: `Basic Playback`(직접 합성한 데모곡, 이 도구로 분석한 4개 난이도 차트, 씬).
- **테스트**: EditMode NUnit 110개(생성 규칙, 결정성, JSON, 마이그레이션, 편집 모델, 레이아웃, 파형, 판정, 시계).

### Known issues

- 검증은 Unity 6000.3, Windows 11, Python 3.13 에서만 했습니다. Unity 2022.3, macOS/Linux 는 확인하지 못했습니다.
- Python 3.12 이상이 필요합니다(고정한 librosa 1.0.0 계열의 요구).
- 사람이 직접 쳐 본 검증이 없고, 난이도 기본값은 실제 곡 1개의 노트 밀도만 보고 정했습니다.
- `--separate`(demucs)는 실행해 보지 못했습니다.
- 입력은 Input System 키보드만 지원합니다. 고정 BPM 만 지원합니다.
- 자세한 목록은 [README 의 알려진 한계](README.md#알려진-한계)를 보세요.
