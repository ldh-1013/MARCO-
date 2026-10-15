# 수영장 v1.1 블록아웃 검토용 프로젝트

현재 제작한 맵 원본을 팀원이 독립적으로 열어볼 수 있도록 공유합니다. 표시 전용 검토 도구이며 게임 로직이나 네트워크 기능은 없습니다.

## 열기
1. 이 브랜치를 내려받습니다.
2. Unity Hub > Add > Add project from disk에서 `Tools/PoolBlockoutPreview` 폴더를 선택합니다.
3. 원본 검증 버전 Unity 6000.3.10f1로 엽니다.
4. Assets/Scenes/IndoorPool_v1_1.unity를 엽니다.
5. Play 후 왼쪽 패널에서 1F / 2F / ALL을 선택합니다. Explore inside (fly)는 오른쪽 마우스 + WASD, Q/E로 이동합니다.

## 팀 프로젝트와의 관계
팀 루트 프로젝트는 Unity 6000.5.4f1 고정이며 현재 URP와 Input System 도입 전입니다. 이 폴더는 이전에 제작·검증한 URP 17.3 / Input System 1.18 기반 맵의 독립 검토본입니다. 팀 루트 Assets, Packages, ProjectSettings는 변경하지 않았습니다. 팀 에디터로 루트 프로젝트를 열면 이 폴더의 스크립트는 컴파일 대상이 아닙니다.

팀 게임에 직접 통합한 상태는 아닙니다. 통합 시 담당자가 팀의 Unity 버전에서 URP / Input System 도입을 확인하고 렌더러를 등록해야 합니다. 이 프로젝트의 ProjectSettings를 팀 프로젝트에 덮어쓰지 마세요. 맵 카메라는 UniversalRP의 렌더러 목록 1번을 사용합니다.

## 내용과 검증
64×62m, 21구역, 41개 재사용 Prefab, 밸브 5개, 출구 2개, 배수구 2개와 4인 시작 위치를 포함합니다. Assets/PoolV11/README.md에 상세 사용법과 임시값·한계가 있습니다.

Verification의 결과는 원본 프로젝트에서 저장·재열기·Play 모드로 검사한 기록입니다. 이 공유 폴더 자체를 Unity에서 재실행한 결과는 아닙니다. 테스트용 파이프 FBX는 포함하지 않아 해당 FBX 교체 샘플 검사는 재실행 시 SKIP 처리됩니다. Scene 생성·검증 도구는 Assets/Editor에 있습니다.

구현에는 OpenAI Codex를 사용했습니다. 팀 규칙에 따라 main 병합 전 다른 프로그래머의 검토가 필요합니다.
