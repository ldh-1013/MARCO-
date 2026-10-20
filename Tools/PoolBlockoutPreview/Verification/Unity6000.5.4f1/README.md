# Unity 6000.5.4f1 변환 검증 (2026-10-20)

대상은 Tools/PoolBlockoutPreview 독립 검토 프로젝트입니다. 팀 루트 프로젝트와 원래 Gameproject는 변경하지 않았습니다.

- 목표 Editor: 6000.5.4f1 (d550df8bd089), 공식 Unity Hub로 설치.
- URP 17.3.0 -> 17.5.0, Input System 1.18.0 -> 1.19.0 및 관련 패키지를 Editor가 자동 갱신.
- V11Geometry.txt: v1.1 구조·좌표·충돌·수심·계단 등 440개 PASS.
- V12Geometry.txt: v1.2 구조·좌표·충돌·수심·계단·통로 등 487개 PASS.
- 파이프 테스트 FBX는 의도적으로 없으므로 샘플 모델 교체 검사는 SKIP입니다.
- EditorValidation.txt: 실제 렌더링 ALL / 1F / 2F, 셰이더 지원, 씬·Prefab·재질 목표 Editor 재직렬화 PASS.
- PlayMode.txt: 공유 프로젝트에서 실제 Play 진입·카메라·층 전환·수면 표시 PASS.
- Preservation.txt: 변환 전 백업 대비 기존 GUID179개, 씬·좌표 데이터4개 보존.

첫 배치 Play 실행에서 UnityEditor.Search.SearchDatabase 초기화 예외가 있었습니다. 로컬 UserSettings에 기본 Assets 검색 인덱스를 초기화한 뒤 같은 테스트를 재실행하여 오류 없이 통과했습니다. 맵 코드는 변경하지 않았으며 이 로컬 캐시는 Git에 포함하지 않습니다.

기존 Object 검색 API의 obsolete 컴파일 경고는 남아 있지만 컴파일/런타임 오류는 없습니다. 서버·캐릭터 이동·수영·상호작용·실제 플레이 밸런스 검증은 포함하지 않습니다.

재실행: 목표 Editor의 배치 executeMethod로 PoolPreviewUpgradeValidation.Run을 실행하고, 별도 실행에서 PoolV12PreviewSmoke.Start를 호출합니다. 첫 명령은 -quit을 사용하고 Play 검사는 자체 종료하므로 -quit을 사용하지 않습니다. 렌더링 검사를 위해 -nographics는 사용하지 않습니다.

렌더링 PNG는 로컬 검증 산출물이며 Git에서는 제외했습니다. 변환 전 ZIP 백업은 로컬 UnityProject/Backups에 보존했습니다. 과거 Verification/v1.2/UploadIntegrity.txt의 원본 일치 기록은 버전 변환 전 결과입니다.
