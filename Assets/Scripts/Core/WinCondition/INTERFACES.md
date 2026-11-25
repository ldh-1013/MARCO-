# 밸브·승리조건 ↔ 팀원 파트 연동 가이드

준제(밸브·승리조건) 코드가 남경/재운/승민 파트와 만나는 지점은 전부 인터페이스로만 정의되어 있다.
실제 구현은 각 담당자가 만들어서 `ValveInteractionService`/`RoundState` 생성자에 넘겨주면 된다.
지금(6단계, 전체 완료 + v0.6 패치 반영)은 밸브 상태/소리/상호작용 + 라운드 진행·승패 판정 + 최후 생존자 페이즈·배수구까지 전부 있다.

파일 위치: `Assets/Scripts/Core/WinCondition/Core/`
- `IPlayerRoster.cs`, `PlayerRole.cs`
- `IValveSoundSink.cs`, `ValveSoundEvent.cs`
- `IRoundEventSink.cs`, `RoundResult.cs`
- `ValveInteractionService.cs` (밸브 잡기/놓기 진입점)
- `RoundState.cs` (탈출/태그/이탈/시간 판정 + 최후 생존자 페이즈 진입점)
- `LastSurvivorPhase.cs`, `DrainHatch.cs`, `DrainHatchId.cs`, `DrainHatchStatus.cs`, `DrainHatchActivation.cs`
- `DrainHatchInteractionService.cs` (배수구 작업 잡기/놓기 진입점)

테스트용 가짜 구현(`Fake*.cs`)은 `Assets/Scripts/Core/WinCondition/Tests/`에 있다. 테스트 전용(Editor)이라 게임 코드에서 참조하지 않는다. 실제 구현을 만들 때 참고만 하면 된다.

---

## 0. 빠른 시작 — 조립 순서와 매 틱 호출 순서 (서버에서만)

**라운드를 시작할 때 이 순서로 만든다.** 앞에서 만든 객체를 뒤에서 넘겨받기 때문에 순서가 바뀌면 안 된다.

```csharp
using Marco.Core.WinCondition;

var config = new WinConditionConfig();
var active = ValveActivation.SelectActiveValves(new System.Random().Next); // [B, E, 무작위 1개]
var board  = new ValveBoard(active, config);
var gate   = new Gate(config.RequiredOpenValveCount);                      // 항상 3
var valves = new ValveInteractionService(board, gate, roster, soundSink, eventSink);
var round  = new RoundState(config, roster, gate, valves, eventSink);
var drains = new DrainHatchInteractionService(round.LastSurvivorPhase, roster);
```

- `roster`(IPlayerRoster), `soundSink`(IValveSoundSink), `eventSink`(IRoundEventSink)는 각 담당자가 구현해서 넣는다(1~3번 섹션).
- `roster.TotalRunnerCount`는 `RoundState`를 만드는 순간 한 번만 읽는다. 역할 배정이 끝난 뒤에 만들어야 한다.

**매 서버 틱마다 반드시 이 순서로 두 개를 모두 호출한다.**

```csharp
board.Tick(dt);   // ① 밸브 진행도·감쇠·역류. 이걸 빼면 밸브가 전혀 움직이지 않는다
round.Tick(dt);   // ② 탈출·태그 처리, 최후 생존자 페이즈, 제한시간, 승패 판정
```

- `RoundState.Tick`은 밸브를 진행시키지 않는다. 밸브는 `ValveBoard.Tick`만 진행시킨다.
- 결과가 확정된 뒤(`round.Result != RoundResult.InProgress`)에는 두 호출을 멈춰도 된다.
- 클라이언트에서는 호출하지 않는다. 판정은 서버(호스트)만 한다.

---

## 1. IPlayerRoster — 재운(UI/UX·이동·플레이어 정보) 담당

플레이어의 역할/생존/탈출 상태를 밸브 쪽에서 물어볼 때 쓰는 창구. **밸브 쪽은 playerId만 가지고 있고, 역할은 항상 이걸 통해서 직접 조회한다** (요청 인자로 역할을 받지 않음).

| 멤버 | 반환 | 설명 | 언제 호출되나 |
|---|---|---|---|
| `GetRole(playerId)` | `PlayerRole` (Runner/Seeker/Echo) | 이 사람의 역할 | `ValveInteractionService.TryBeginWork`, `RoundState`의 모든 Notify 메서드 호출마다 |
| `IsAlive(playerId)` | `bool` | 생존 여부 | `DrainHatchInteractionService.TryBeginWork` 호출마다. (RoundState 자체는 "마지막 생존자" 여부를 자체 집계로 판단해서 이 값에 의존하지 않음) |
| `HasEscaped(playerId)` | `bool` | 탈출 여부 | 아직 미사용. 재운 쪽 UI에서 조회용으로 쓸 수 있음(밸브 쪽 로직은 자체 집계를 사용) |
| `AliveRunnerCount` | `int` | 현재 살아있는 Runner 수 | 아직 미사용(밸브/라운드 쪽은 쓰지 않음, 재운 UI 표시용으로 남겨둠) |
| `TotalRunnerCount` | `int` | 라운드 시작 시 전체 Runner 수 | **`RoundState` 생성 시점에 1번 읽어서** 탈출 요구 인원/제한시간을 결정함(인원 표 조회). 생성 이후 바뀌어도 다시 읽지 않음 |

**재운 쪽이 할 일**: 실제 플레이어 매니저에 이 인터페이스를 구현해서 `ValveInteractionService` 생성 시 넘겨주면 됨. 위치/이동/입력 같은 다른 정보는 요구하지 않음.

---

## 2. IValveSoundSink — 남경(소리·파문) 담당

밸브 관련 소리를 "언제 틀어야 하는지"만 알려줌. 반경, 파문 전파 같은 숫자는 전혀 넘기지 않음(그건 남경 파트 책임).

| 호출 | 뜻 | 언제 호출되나 |
|---|---|---|
| `NotifyValveSound(valveId, ValveSoundEvent.RotatingStarted)` | 밸브 회전 시작 | 그 밸브에 아무도 없다가 **첫 작업자**가 잡는 순간 (같은 밸브에 2번째, 3번째 사람이 추가돼도 다시 안 울림) |
| `NotifyValveSound(valveId, ValveSoundEvent.RotatingStopped)` | 밸브 회전 중지 | **마지막 작업자**가 손을 떼거나 탈락하는 순간, 또는 **밸브가 열리는(Open) 순간** (열리면 작업자 목록이 비워지므로 1번 울림. 그 뒤 `EndWork`가 와도 다시 안 울림) |
| `NotifyValveSound(valveId, ValveSoundEvent.ReflowRipple)` | 역류 물소리 파문 | 역류 30초 동안 균등 간격으로 3회 (TODO: 정확한 타이밍은 기획 확인 전) |

**남경 쪽이 할 일**: 이 인터페이스를 구현해서 `valveId` + `ValveSoundEvent`만 보고 실제 사운드/파문을 재생하면 됨.

---

## 3. IRoundEventSink — 승민(서버/네트워크) 수신 → 재운(UI) 쪽에 재전달

밸브 쪽에서 일어난 일을 서버가 받아서 클라이언트(UI)에 동기화할 때 쓰는 창구.

| 호출 | 뜻 | 언제 호출되나 |
|---|---|---|
| `OnValveStatusChanged(valveId, status)` | 밸브 상태 전환 | Closed/Rotating/Open/Reflowing 중 하나로 바뀔 때마다 |
| `OnValveProgressChanged(valveId, progress)` | 밸브 진행도(0.0~1.0) | 지금은 상태가 바뀔 때 같이 전달됨 (매 틱 연속 갱신은 아직 없음 — 게이지를 매끄럽게 움직이려면 승민 쪽에서 `Valve.Progress`를 직접 폴링하거나, 다음 단계에서 틱마다 보내는 기능을 추가해야 함) |
| `OnGateOpened()` | 게이트 개방 | 요구 개방 수 달성 시 **딱 1번만** 호출됨 (latch, 그 뒤로 다시 안 불림) |
| `OnRoundEnded(result)` | 라운드 결과 확정 (`RoundResult`: `RunnersWin`/`SeekersWin`/`Void`) | `RoundState`가 승패(또는 무효)를 확정하는 순간 **딱 1번만** 호출됨. 그 뒤로는 추가 입력이 전부 무시되므로 다시 호출되지 않음. `Void`는 v0.6에서 추가됨 — 술래가 라운드 중 나가면 승패 없이 이 값으로 끝남(아래 5번 섹션 참고) |
| `OnLastSurvivorPhaseEntered()` | 최후 생존자 페이즈 진입 | 살아있는(=아직 미해결) 도망자가 1명이 되는 순간 **딱 1번만** 호출됨 |
| `OnDrainHatchActivated(drainHatchId)` | 배수구 활성화 (`DrainHatchId`: `First`/`Second`) | 페이즈 진입 시점에 게이트가 안 열려있어서 배수구 2곳 중 하나가 골라졌을 때만 호출됨. **게이트가 이미 열려있으면 이 호출 자체가 없음** |

**승민 쪽이 할 일**: 이 인터페이스를 구현해서 네트워크로 클라이언트에 뿌리면 됨. 재운 쪽 UI는 승민이 받은 걸 다시 전달해주는 걸 구독하면 됨(밸브 쪽 코드를 직접 참조할 필요 없음).

---

## 4. ValveInteractionService — 준제(나)가 구현, 승민(서버)이 호출하는 입구

서버가 "누가 밸브를 잡았다/놨다"를 전달할 때 쓰는 유일한 진입점. **요청 인자에 역할을 받지 않고**, 내부에서 `IPlayerRoster`로 직접 확인한다. 생성할 때 위 세 인터페이스의 실제 구현(또는 Fake)과 `ValveBoard`, `Gate`를 넘겨주면, 밸브 이벤트(`StatusChanged`, `ReflowRippleTriggered`)와 게이트 개방을 자동으로 Sink에 연결해준다.

**활성 밸브 선택 (v0.6, `ValveBoard`를 만들기 전에 필요)**: 활성 밸브는 인원과 무관하게 **항상 3개**다. `ValveActivation.SelectActiveValves(nextIndex)`를 호출하면 `[B, E, 무작위 1개]`를 항상 이 순서로 돌려준다(B·E는 항상 포함, 나머지 1개만 A·C·D 중 무작위). 이 리스트를 그대로 `new ValveBoard(activeValveIds, config)`에 넘기면 됨. 요구 개방 수(`Gate`에 넘길 값)도 항상 3개(활성 밸브 전부)라서 `WinConditionConfig.RequiredOpenValveCount`를 그대로 쓰면 됨 — 더 이상 인원별로 다르지 않음.

| 메서드 | 설명 | 거부 조건 |
|---|---|---|
| `TryBeginWork(playerId, valveId)` | 밸브 잡기 시작. 성공하면 `true` | 활성 밸브가 아님 / Runner가 아님(Seeker, Echo) / 탈락한 Runner / **이미 열린(Open) 밸브** (거부 시 소리 없음) |
| `EndWork(playerId, valveId)` | 밸브 놓기 | 없음 (항상 적용, 등록 안 된 사람이면 조용히 무시) |
| `RemovePlayerFromAllValves(playerId)` | 탈락·연결 끊김 처리 | 모든 밸브의 작업자 목록에서 그 사람을 뺀다. 리셋이 아니라 "작업 중단"과 같은 취급(진행도는 유예 후 감쇠) |

- **밸브가 열리는 순간 작업자 목록이 비워진다.** E를 계속 누르고 있어도 그 사람은 더 이상 작업자가 아니다. 역류 중에 다시 돌리려면 `TryBeginWork`를 **새로** 호출해야 한다(E를 떼었다 다시 누르기).
- **역류 중 재작업**: 별도 진행도 `Valve.ReworkProgress`(0.0~1.0)를 0부터 채운다(1인 8초, 다인 배율 동일). 그동안 역류는 멈추지 않고 계속 닫힌다(`Valve.Progress`는 계속 줄어듦). 재작업이 먼저 끝나면 Open 복귀 + 180초 유예 리셋, 역류가 먼저 끝나면 Closed가 되고 재작업 진행도는 버린다. 재운 쪽 UI에서 재작업 게이지를 보여주려면 `ReworkProgress`를 폴링하면 됨. (TODO: 역류 계속 진행/재작업 진행도 폐기/손 뗐을 때 감쇠 여부는 기획 확인 전)

**승민 쪽이 할 일**: 입력(E 홀드 등, 재운 파트에서 받은 신호)을 받으면 `playerId`만 뽑아서 이 서비스의 `TryBeginWork`/`EndWork`를 호출. 플레이어가 죽거나 나가면 `RemovePlayerFromAllValves`를 호출.

---

## 5. RoundState — 준제(나)가 구현, 승민(서버)이 호출하는 입구

탈출/태그/이탈/제한시간을 집계해서 라운드 승패를 판정하는 진입점. **요청 인자에 역할을 받지 않고**, 내부에서 `IPlayerRoster`로 직접 확인한다. 생성할 때 `IPlayerRoster.TotalRunnerCount`를 한 번 읽어서 그 인원수에 맞는 탈출 요구/제한시간을 정한다(이후 바뀌어도 다시 안 읽음).

| 메서드 | 설명 | 거부/무시 조건 |
|---|---|---|
| `NotifyRunnerEscaped(playerId)` | 탈출 신고. 성공하면 `true` | 결과가 이미 확정됨 / 이미 처리된 사람 / Runner가 아님(Seeker, Echo) / **게이트가 안 열려있음** |
| `NotifyRunnerTagged(playerId)` | 태그(술래가 잡음) 신고. 성공하면 `true` | 결과가 이미 확정됨 / 이미 처리된 사람 / Runner가 아님 |
| `NotifyPlayerLeft(playerId)` | 탈락·연결 끊김 신고 (반환값 없음). v0.6: 역할에 따라 처리가 다름 — 아래 설명 참고 | 결과가 이미 확정되면 아무 것도 안 함 |
| `Tick(dt)` | 시간 진행 + 그동안 쌓인 탈출/태그를 **탈출 → 태그 → 시간 종료 → 판정** 순서로 한꺼번에 처리 | 결과가 이미 확정되면 아무 것도 안 함 |

- `NotifyRunnerTagged`/`NotifyPlayerLeft`로 처리된 사람은 자동으로 `ValveInteractionService.RemovePlayerFromAllValves`가 호출돼서 밸브 작업에서 빠진다.
- **라운드가 끝나는 경우 (기획서 10-01 개정)**: ① 도망자 전원 확정(탈출+태그) ② 마지막 생존자 탈출 ③ 최후 생존자 페이즈 시간 종료 ④ 제한시간 종료 ⑤ 술래 이탈(`Void`). **탈출 요구 인원을 채운 것만으로는 끝나지 않는다.**
- **판정**: 끝나는 순간 `탈출 인원 ≥ 탈출 요구`면 `RunnersWin`, 아니면 `SeekersWin`. 단 마지막 생존자 탈출(마지막 남은 도망자가 탈출로 확정)은 항상 `RunnersWin`.
- 같은 틱에 탈출과 태그가 동시에 들어오면(호출 순서와 무관하게) **탈출 쪽이 먼저 처리**된다. 그 틱에서 승부가 나면 나머지(태그, 시간 종료 확인)는 처리되지 않는다.
- 결과(`RoundState.Result`)는 한 번 확정되면 절대 안 바뀌고, 그 뒤 모든 Notify/Tick 호출은 조용히 무시된다.
- **`NotifyPlayerLeft`의 역할별 처리 (v0.6, 확정됨)**:
  - **술래(Seeker)가 나가면**: `Tick`을 기다리지 않고 **그 자리에서 즉시** `RoundResult.Void`로 확정된다(승패 없음, `OnRoundEnded(Void)` 호출).
  - **도망자(Runner)가 나가면**: `NotifyRunnerTagged`를 호출한 것과 **완전히 동일하게** 처리된다(메아리로 전환된 것으로 간주). 즉시 확정되지 않고 다른 태그와 똑같이 다음 `Tick(dt)`에서 "탈출 → 태그 → 시간 종료" 순서로 처리되며, 탈출 요구 인원 재계산 같은 특별 처리는 없음.
  - **메아리(Echo)가 나가면**: 특별 처리 없음(무시).
- 살아있는(=아직 탈출/태그/이탈로 안 끝난) 도망자가 1명이 되면 `RoundState`가 내부적으로 `LastSurvivorPhase`에 1회 진입시킨다. 자세한 내용은 아래 6번 섹션 참고.
- `RoundState` 생성자의 마지막 인자(`drainHatchRandomNextIndex`)는 선택값(기본 null → 진짜 `System.Random` 사용). 배수구 2곳 중 어느 쪽이 뽑히는지 테스트에서 고정하고 싶으면 `(max) => 0` 같은 함수를 넘기면 됨. 안 넘겨도 기존 호출 코드는 그대로 동작함.

**승민 쪽이 할 일**: 클라이언트에서 탈출 지점 도달/태그 판정이 일어나면 `playerId`만 뽑아서 `NotifyRunnerEscaped`/`NotifyRunnerTagged`를 호출. 매 서버 틱마다 `board.Tick(dt)`를 먼저, 그다음 `round.Tick(dt)`를 호출(0번 섹션). 플레이어가 나가면 `NotifyPlayerLeft`도 호출(밸브 쪽 `RemovePlayerFromAllValves`와는 별개로, 라운드 판정 쪽에도 알려줘야 함).

---

## 6. LastSurvivorPhase / DrainHatchInteractionService — 준제(나)가 구현, 승민(서버)이 호출하는 입구

살아있는 도망자가 1명 남으면 `RoundState`가 자동으로 만들어서 들고 있는 페이즈. **따로 생성할 필요 없음** — `roundState.LastSurvivorPhase`로 접근한다.

**진입 시점에 정해지는 것** (전부 `RoundState`가 자동으로 처리함):
- 제한시간: `min(90초, 그 순간 라운드 잔여시간)` — 라운드가 90초보다 적게 남았으면 그만큼만.
- 배수구 활성화: **게이트가 이미 열려있으면 배수구를 켜지 않는다**(정상 탈출 경로로 충분하니까). 안 열려있으면 `DrainHatchId.First`/`Second` 중 하나를 무작위로 골라서 켠다. 이때 배수구 작업 시간 `T = 11 − (그 순간 동시 개방 밸브 수 × 3)`초(최소 1초, v0.6)도 같이 정해짐.

| 멤버 | 설명 |
|---|---|
| `LastSurvivorPhase.IsActive` | 페이즈가 지금 진행 중인지 |
| `LastSurvivorPhase.ActiveDrainHatchId` | 활성화된 배수구 (`null`이면 게이트가 열려있어서 배수구가 없는 경우) |
| `LastSurvivorPhase.RemainingSeconds` | 페이즈 남은 시간 |
| `LastSurvivorPhase.GetDrainHatch(id)` | 특정 배수구(`DrainHatch`) 객체를 가져옴. `Progress`(0.0~1.0), `Status`(Inactive/InProgress/Completed) 확인 가능 |

**DrainHatchInteractionService** — `ValveInteractionService`와 똑같은 모양의 배수구 작업 진입점. `new DrainHatchInteractionService(roundState.LastSurvivorPhase, playerRoster)`로 만든다.

| 메서드 | 설명 | 거부 조건 |
|---|---|---|
| `TryBeginWork(playerId, drainHatchId)` | 배수구 작업 시작. 성공하면 `true` | 지금 활성화된 배수구가 아님 / Runner가 아님(Seeker, Echo) / 탈락한 Runner |
| `EndWork(playerId, drainHatchId)` | 배수구 작업 중단 | 없음(조용히 무시) |

- 배수구 진행도 규칙은 밸브와 같음(작업자 0명 되면 유예 3초 → 초당 0.10 감쇠). **다만 다인 배율과 역류는 없음** — 몇 명이 붙어도 속도는 그대로고, 완료되면 그걸로 끝(다시 안 닫힘).
- **배수구 작업이 끝나면(`Progress`가 1.0에 도달) 그 작업자는 게이트 상태와 무관하게 즉시 탈출이 인정된다.** `RoundState`가 내부적으로 감지해서 자동으로 처리하므로 승민 쪽에서 따로 호출할 필요 없음.
- 마지막 생존자가 (배수구든, 게이트가 이미 열려있어서 일반 경로든) 탈출하면 즉시 도망자 승리. 페이즈가 시간 초과되면 그때까지의 탈출 인원으로 판정(요구 인원 이상이면 도망자 승, 아니면 술래 승).

**승민 쪽이 할 일**: 입력(E 홀드 등)을 받으면 `playerId`만 뽑아서 `DrainHatchInteractionService.TryBeginWork`/`EndWork` 호출. 활성화된 배수구가 어디인지는 `IRoundEventSink.OnDrainHatchActivated`로 알려주므로 그걸로 UI/안내를 띄우면 됨. 배수구 완료로 인한 탈출은 `RoundState`가 알아서 처리하니 별도 호출 불필요.

---

## 호출 흐름 한눈에 보기

```
재운(입력/UI)  →  승민(서버)  →  ValveInteractionService.TryBeginWork(playerId, valveId)
                                        │
                                        ├─ IPlayerRoster로 역할/생존 확인 (재운 구현)
                                        │
                                        ▼
                                   ValveBoard / Valve (내 코드)
                                        │
                        ┌───────────────┼────────────────┐
                        ▼               ▼                ▼
              IValveSoundSink   IRoundEventSink        Gate(latch)
              (남경 구현)        (승민 구현 → 재운 UI)   (내 코드, 자동 재평가)
```

```
승민(서버)  →  RoundState.NotifyRunnerTagged / NotifyRunnerEscaped / NotifyPlayerLeft / Tick(dt)
                        │
                        ├─ NotifyPlayerLeft: 술래면 즉시 Void 확정, 도망자면 NotifyRunnerTagged와 동일 처리
                        │
                        ├─ 살아있는 도망자가 1명 남으면 → LastSurvivorPhase 자동 진입
                        │        │
                        │        ├─ 게이트 열림 → 배수구 없음 (정상 탈출 경로 그대로)
                        │        └─ 게이트 닫힘 → 배수구 1곳 무작위 활성 → IRoundEventSink.OnDrainHatchActivated
                        │                                  │
                        │                        DrainHatchInteractionService.TryBeginWork(playerId, drainHatchId)
                        │                                  │
                        │                        작업 완료 → RoundState가 자동으로 탈출 인정
                        ▼
              IRoundEventSink.OnRoundEnded(RunnersWin / SeekersWin / Void)  ← 딱 1번
```
