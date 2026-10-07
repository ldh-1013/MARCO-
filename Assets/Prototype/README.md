# MARCO! 프로토타입 씬

## 에디터에서 배치 수정

Play 모드를 끈 상태에서 씬을 열면 Hierarchy에 Player, Main Camera, Prototype Ground, 테스트 Target, Directional Light가 보입니다. 선택한 뒤 Scene 뷰의 이동/회전/크기 도구 또는 Inspector의 Transform으로 직접 수정하고 씬을 저장하세요. 실행 시 배치를 다시 생성하거나 초기 위치로 덮어쓰지 않습니다.

카메라는 Player의 자식입니다. 카메라의 로컬 위치로 눈높이를 바꾸고, Camera 컴포넌트에서 FOV를 조절할 수 있습니다. Player의 이동속도/마우스 감도, Hunter의 태그 거리/쿨다운, Survivor의 상호작용 거리도 Inspector에서 수정할 수 있습니다. Play 중 이동한 위치는 종료하면 되돌아가므로 배치 수정은 Play 종료 후 저장하세요.

- `Prototype_Lobby`: 실제 TCP 방 생성/접속. Create room 후 IP 또는 주소 코드 복사 → 다른 인스턴스의 Join 입력칸에 붙여넣습니다. 로비 참가자 수를 동기화합니다.
- `Prototype_HunterTest`: WASD 이동과 좌클릭 태그 판정(사거리 1.2m, 전방 45도, 명중 2.5초/헛스윙 1.5초 쿨다운)을 확인합니다.
- `Prototype_SurvivorTest`: WASD 이동과 E 상호작용/치료 대상 테스트를 확인합니다.

## 마이크 시야 프로토타입

두 역할 테스트 씬은 기본적으로 완전 암전입니다. 마이크 RMS/dBFS가 Silence Db를 넘으면 중앙 원형 시야가 열리고, Full Vision Db에 가까워질수록 반경이 커집니다. 음량이 줄면 시야가 닫힙니다. Inspector에서 임계값/최대 반경/닫힘 속도를 조절합니다. `Left Alt`는 즉시 암전과 강제 뮤트입니다.

방 접속은 같은 PC(127.0.0.1), 같은 LAN 또는 VPN에서 연결 가능한 호스트 IP를 사용합니다. 자동 감지 주소가 잘못된 어댑터의 IP라면 Create room 전에 공유 주소를 수정하세요. 코드는 주소를 담고 있으며 인터넷 Relay나 게임플레이 동기화는 아직 구현하지 않았습니다. 검증 현황은 `docs/Status.md`를 참조하세요.

`F8` 전체 시야는 에디터와 Development Build에서만 컴파일되는 디버그 치트이며 Release 빌드에는 포함되지 않습니다.

두 역할 테스트는 멀티플레이 동기화 대신 각 씬 안의 독립 대상 오브젝트로 판정 흐름을 검증합니다.
