# 로비 CCTV 배경

표시 전용. `Assets/Scenes/Lobby/Lobby_CCTV.unity`는 수영장 맵의 표시용 메시를 복사해 만든 로비 전용 씬이다. 원본 맵과 머티리얼을 변경하지 않고 구도를 편집할 수 있다.

## 편집

- `Lobby CCTV display stage / Pool surveillance camera`: CCTV 위치, 회전, 화각.
- `Lobby CCTV display stage / CCTV subject`: 캐릭터 위치와 회전.
- 루트의 `LobbyCctvStage`: 카메라, 캐릭터, 대기 애니메이션 참조.
- `Assets/Scenes/Lobby/Materials`: 로비 전용 재질. `Brightness`는 배경 밝기이며 캐릭터는 발화 신호로 덮어쓴다.

씬은 배경 전용이므로 실제 로비 확인은 기존 시작 씬 `NetworkPool_v1_2`를 Play한다. 인트로가 끝나면 로비가 `Lobby_CCTV`를 추가 로드해 RenderTexture로 표시한다. 게임 시작 시 배경 카메라와 캐릭터 애니메이션은 비활성화된다. 편집 좌표는 원본 수영장과 같고 실행 중에만 루트가 Y=-10000으로 이동한다. 새 오브젝트를 추가할 때는 표시용 루트 아래에 두고 기존 오브젝트와 같은 레이어 31을 지정한다.

## 음성 반응

방 입장 전에는 로비가 마이크 레벨만 읽으며 녹음 파일을 만들거나 전송하지 않는다. 방 입장 후에는 기존 `PlayerVoice`의 발화 감지와 서버가 전달한 상대 음성에서 표시 신호를 받는다. 캐릭터 한 명이 로비에서 들리는 음성에 반응한다. 왼쪽 Alt 음소거는 기존 방식과 같다. 실제 인게임 음성 전파, 거리 판정, 시야 규칙은 변경하지 않았다.

## 검증 및 재생성

`MARCO > Lobby > Verify CCTV rendering`은 별도 미리보기 씬에서 조용한 상태와 밝아진 상태를 렌더링해 `Logs/LobbyCctv`에 이미지와 검사 결과를 남긴다. 현재 확인한 항목은 셰이더 컴파일, 캐릭터 표시, 밝기 변화, 시작 씬에서 추가 로드 및 로비 UI 표시이다. 실제 두 클라이언트의 음성 전달은 별도 플레이 테스트가 필요하다.

`MARCO > Lobby > Create CCTV scene`은 씬이 없을 때만 최초 복사본을 생성한다. 이미 편집한 로비 씬을 덮어쓰지 않는다.
