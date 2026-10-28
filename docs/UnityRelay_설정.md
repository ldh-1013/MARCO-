# Unity Relay 방 연결 설정

## 적용된 연결 흐름

- FishNet은 게임 오브젝트와 플레이어 상태 동기화를 담당한다.
- FishyUnityTransport가 FishNet 트래픽을 Unity Relay로 전달한다.
- Unity Relay 참가 코드는 Unity Lobby 데이터 안에 멤버 전용으로 보관한다.
- 플레이어에게 표시하는 방 코드는 Unity Lobby가 발급한 짧은 영문·숫자 코드다. Unity Lobby 코드는 Unity Relay 연결 코드와 별개이며, 플레이어가 Unity Relay 코드를 직접 입력하지 않아도 된다.
- `Join` 화면은 참가 가능한 공개 방 목록을 보여 주고, 코드 직접 입력과 목록에서 선택하는 두 참가 경로를 제공한다.
- 방은 호스트 포함 최대 6명이다. 호스트는 Lobby가 방을 비활성 처리하지 않도록 15초마다 heartbeat를 보낸다.

## Unity Dashboard 준비

1. Unity Hub에서 이 프로젝트를 열고 프로젝트를 Unity Dashboard의 Cloud Project에 연결한다.
2. 프로젝트의 `Authentication`, `Lobby`, `Relay` 서비스를 활성화한다.
3. Authentication 설정에서 Anonymous sign-in을 허용한다. 현재 구현은 Unity 계정 입력 없이 익명 인증을 사용한다.
4. `Packages/manifest.json`의 패키지가 설치된 뒤 Editor 메뉴에서 `MARCO > Network > Migrate Scenes To Unity Relay`를 실행해 `NetworkTest`와 `NetworkPool_v1_2`의 FishNet 전송 구성을 바꾼다.
5. 두 빌드에서 `Create room`으로 방을 만들고, 생성된 Lobby 코드를 다른 빌드의 `Join` 화면에 입력하거나 참가 가능한 방 목록에서 선택한다.

Unity Cloud Project 연결은 계정과 프로젝트 소유권이 필요하므로 이 저장소만으로 자동 설정되지 않는다. 프로젝트 ID는 Unity Editor의 Services 설정에서 지정한다.

## 패키지

- `com.unity.services.multiplayer` 2.4.0 — Unity 6000.5용 Lobby/Relay SDK
- `com.alven.fishnet.unitytransport` Git tag `1.2.0` — FishNet용 Unity Transport 어댑터
- Unity Transport는 Unity 6000.5의 에디터 패키지로 제공된다.

Steamworks.NET과 FishySteamworks 패키지는 프로토타입의 다른 Steam API 경로에서 계속 참조하므로 패키지 목록에 남아 있다. 활성 네트워크 씬의 세션 전송에는 Unity Relay를 사용한다.
